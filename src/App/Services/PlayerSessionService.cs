using System.Globalization;
using System.Text.Json;
using App.Configuration;
using Lavalink4NET;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord.Gateway;
using NetCord.Rest;

namespace App.Services;

public sealed record SnapshotTrack(string Encoded, ulong? RequesterId);

public sealed record PlayerSnapshot(
    ulong GuildId,
    ulong VoiceChannelId,
    ulong TextChannelId,
    SnapshotTrack? Current,
    long PositionMs,
    bool Paused,
    IReadOnlyList<SnapshotTrack> Queue,
    TrackRepeatMode RepeatMode,
    bool Shuffle,
    bool Autoplay,
    float Volume);

/// <summary>
/// Saves every guild's queue to SQLite every few seconds and on shutdown, and puts the
/// music back after the bot restarts (deploys, crashes) if people are still in the channel.
/// </summary>
public sealed class PlayerSessionService : BackgroundService
{
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(10);

    private readonly IAudioService _audioService;
    private readonly GatewayClient _client;
    private readonly BotDatabase _database;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly StorageOptions _options;
    private readonly ILogger<PlayerSessionService> _logger;
    private readonly TaskCompletionSource _gatewayReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PlayerSessionService(
        IAudioService audioService,
        GatewayClient client,
        BotDatabase database,
        IHostApplicationLifetime lifetime,
        IOptions<StorageOptions> options,
        ILogger<PlayerSessionService> logger)
    {
        _audioService = audioService;
        _client = client;
        _database = database;
        _lifetime = lifetime;
        _options = options.Value;
        _logger = logger;

        _client.Ready += _ =>
        {
            _gatewayReady.TrySetResult();
            return default;
        };

        // Runs before the players are torn down, so this is the most up-to-date snapshot.
        _lifetime.ApplicationStopping.Register(SaveAll);
    }

    /// <summary>Called when a player goes away. Keeps the snapshot if the bot itself is shutting down.</summary>
    public void Forget(ulong guildId)
    {
        if (_lifetime.ApplicationStopping.IsCancellationRequested)
            return;

        Delete(guildId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _audioService.WaitForReadyAsync(stoppingToken);
            await _gatewayReady.Task.WaitAsync(stoppingToken);
            // Guild data (voice states) arrives right after READY.
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            await RestoreAllAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restoring players after restart failed");
        }

        using var timer = new PeriodicTimer(SaveInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                SaveAll();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private void SaveAll()
    {
        foreach (var player in _audioService.Players.GetPlayers<MusicPlayer>())
        {
            try
            {
                var snapshot = Capture(player);
                if (snapshot is null)
                    Delete(player.GuildId);
                else
                    Save(snapshot);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not save player snapshot for guild {GuildId}", player.GuildId);
            }
        }
    }

    private static PlayerSnapshot? Capture(MusicPlayer player)
    {
        var queue = player.Queue
            .Where(item => item.Track is not null)
            .Select(item => new SnapshotTrack(item.Track!.ToString(), (item as MusicQueueItem)?.RequesterId))
            .ToArray();

        var current = player.CurrentItem?.Track is { } track
            ? new SnapshotTrack(track.ToString(), (player.CurrentItem as MusicQueueItem)?.RequesterId)
            : null;

        if (current is null && queue.Length == 0)
            return null;

        return new PlayerSnapshot(
            player.GuildId,
            player.VoiceChannelId,
            player.TextChannelId,
            current,
            (long)(player.Position?.Position.TotalMilliseconds ?? 0),
            player.State is PlayerState.Paused,
            queue,
            player.RepeatMode,
            player.Shuffle,
            player.Autoplay,
            player.Volume);
    }

    private async Task RestoreAllAsync(CancellationToken cancellationToken)
    {
        foreach (var (snapshot, savedAt) in LoadAll())
        {
            if (DateTimeOffset.UtcNow - savedAt > TimeSpan.FromMinutes(_options.RestoreMaxAgeMinutes))
            {
                _logger.LogInformation("Skipping stale snapshot for guild {GuildId} from {SavedAt}", snapshot.GuildId, savedAt);
                Delete(snapshot.GuildId);
                continue;
            }

            if (!HasListeners(snapshot))
            {
                _logger.LogInformation("Nobody is in the voice channel of guild {GuildId}; not restoring", snapshot.GuildId);
                Delete(snapshot.GuildId);
                continue;
            }

            try
            {
                await RestoreAsync(snapshot, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not restore player for guild {GuildId}", snapshot.GuildId);
            }
        }
    }

    private async Task RestoreAsync(PlayerSnapshot snapshot, CancellationToken cancellationToken)
    {
        var player = await _audioService.Players.JoinAsync(
            snapshot.GuildId,
            snapshot.VoiceChannelId,
            MusicPlayer.Factory,
            Options.Create(MusicPlayer.CreateOptions(snapshot.TextChannelId)),
            cancellationToken);

        player.RepeatMode = snapshot.RepeatMode;
        player.Shuffle = snapshot.Shuffle;
        player.Autoplay = snapshot.Autoplay;

        if (Math.Abs(snapshot.Volume - player.Volume) > 0.001f)
            await player.SetVolumeAsync(snapshot.Volume, cancellationToken);

        var queue = snapshot.Queue
            .Select(ToQueueItem)
            .OfType<ITrackQueueItem>()
            .ToArray();

        if (queue.Length > 0)
            await player.Queue.AddRangeAsync(queue, cancellationToken);

        if (snapshot.Current is not null && ToQueueItem(snapshot.Current) is { } current)
        {
            var position = TimeSpan.FromMilliseconds(snapshot.PositionMs);
            var startAt = current.Track!.IsSeekable && position < current.Track.Duration ? position : (TimeSpan?)null;
            await player.PlayAsync(current, enqueue: false, new TrackPlayProperties(StartPosition: startAt), cancellationToken);

            if (snapshot.Paused)
                await player.PauseAsync(cancellationToken);
        }
        // No current track: it was stopped with songs queued. Leave it stopped; /resume continues.

        _logger.LogInformation("Restored player in guild {GuildId}: {Count} queued", snapshot.GuildId, queue.Length);

        if (snapshot.TextChannelId != 0)
        {
            await _client.Rest.SendMessageAsync(snapshot.TextChannelId, new MessageProperties
            {
                Content = "♻️ I restarted — picked the music back up where it was.",
                AllowedMentions = AllowedMentionsProperties.None,
            }, cancellationToken: cancellationToken);
        }
    }

    private static MusicQueueItem? ToQueueItem(SnapshotTrack track) =>
        LavalinkTrack.TryParse(track.Encoded, CultureInfo.InvariantCulture, out var parsed)
            ? new MusicQueueItem(new TrackReference(parsed), track.RequesterId)
            : null;

    private bool HasListeners(PlayerSnapshot snapshot)
    {
        if (!_client.Cache.Guilds.TryGetValue(snapshot.GuildId, out var guild))
            return false;

        return guild.VoiceStates.Values.Any(state =>
            state.ChannelId == snapshot.VoiceChannelId
            && !(guild.Users.TryGetValue(state.UserId, out var user) && user.IsBot));
    }

    // ---- storage ----

    private void Save(PlayerSnapshot snapshot)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO player_snapshots (guild_id, saved_at, json) VALUES ($guild, $savedAt, $json)
            ON CONFLICT (guild_id) DO UPDATE SET saved_at = excluded.saved_at, json = excluded.json
            """;
        command.Parameters.AddWithValue("$guild", (long)snapshot.GuildId);
        command.Parameters.AddWithValue("$savedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(snapshot));
        command.ExecuteNonQuery();
    }

    private void Delete(ulong guildId)
    {
        try
        {
            using var connection = _database.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM player_snapshots WHERE guild_id = $guild";
            command.Parameters.AddWithValue("$guild", (long)guildId);
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete player snapshot for guild {GuildId}", guildId);
        }
    }

    private List<(PlayerSnapshot Snapshot, DateTimeOffset SavedAt)> LoadAll()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT saved_at, json FROM player_snapshots";

        using var reader = command.ExecuteReader();
        var snapshots = new List<(PlayerSnapshot, DateTimeOffset)>();
        while (reader.Read())
        {
            var snapshot = JsonSerializer.Deserialize<PlayerSnapshot>(reader.GetString(1));
            if (snapshot is not null)
                snapshots.Add((snapshot, DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture)));
        }

        return snapshots;
    }
}
