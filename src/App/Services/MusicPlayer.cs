using Lavalink4NET.Extensions;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Protocol.Payloads.Events;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace App.Services;

public sealed record MusicPlayerOptions : QueuedLavalinkPlayerOptions
{
    public ulong TextChannelId { get; init; }
}

/// <summary>
/// A queue item that remembers who asked for it. <see cref="RequesterId"/> is <c>null</c>
/// for tracks the bot added on its own (autoplay).
/// </summary>
public sealed record MusicQueueItem(TrackReference Reference, ulong? RequesterId) : ITrackQueueItem
{
    public LavalinkTrack? Track => Reference.Track;
}

/// <summary>
/// Queued player that keeps the Now Playing panel in sync and tops the queue up
/// with related tracks when autoplay is on.
/// </summary>
public sealed class MusicPlayer : QueuedLavalinkPlayer
{
    public static PlayerFactory<MusicPlayer, MusicPlayerOptions> Factory { get; } =
        PlayerFactory.Create<MusicPlayer, MusicPlayerOptions>(properties => new MusicPlayer(properties));

    public static MusicPlayerOptions CreateOptions(ulong textChannelId) => new()
    {
        TextChannelId = textChannelId,
        SelfDeaf = true,
        // /stop keeps the queue; /clear empties it.
        ClearQueueOnStop = false,
        // Loop/shuffle are settings, not per-track state: keep them across stops.
        ResetTrackRepeatOnStop = false,
        ResetShuffleOnStop = false,
        HistoryCapacity = 50,
    };

    private readonly NowPlayingPanel? _panel;
    private readonly PlayerSessionService? _sessions;
    private readonly AutoplayService? _autoplay;
    private readonly ILogger _logger;

    public MusicPlayer(IPlayerProperties<MusicPlayer, MusicPlayerOptions> properties)
        : base(properties)
    {
        TextChannelId = properties.Options.Value.TextChannelId;
        _panel = properties.ServiceProvider?.GetService<NowPlayingPanel>();
        _autoplay = properties.ServiceProvider?.GetService<AutoplayService>();
        _sessions = properties.ServiceProvider?.GetService<PlayerSessionService>();
        _logger = properties.Logger;
    }

    /// <summary>Channel the Now Playing panel is posted in. Follows the last /play.</summary>
    public ulong TextChannelId { get; set; }

    /// <summary>Keep playing related tracks once the queue runs out.</summary>
    public bool Autoplay { get; set; }

    protected override async ValueTask NotifyTrackStartedAsync(ITrackQueueItem track, CancellationToken cancellationToken = default)
    {
        await base.NotifyTrackStartedAsync(track, cancellationToken);

        if (_panel is not null)
            await RunSafeAsync(() => _panel.PostAsync(this), "post the Now Playing panel");
    }

    protected override async ValueTask NotifyTrackEndedAsync(ITrackQueueItem queueItem, TrackEndReason endReason, CancellationToken cancellationToken = default)
    {
        if (Autoplay && _autoplay is not null && endReason.MayStartNext()
            && Queue.IsEmpty && RepeatMode is TrackRepeatMode.None && queueItem.Track is not null)
        {
            await RunSafeAsync(async () =>
            {
                var next = await _autoplay.FindNextAsync(queueItem.Track, RecentIdentifiers(), cancellationToken);
                if (next is not null)
                    await Queue.AddAsync(new MusicQueueItem(new TrackReference(next), RequesterId: null), cancellationToken);
            }, "find an autoplay track");
        }

        await base.NotifyTrackEndedAsync(queueItem, endReason, cancellationToken);
    }

    public override async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        await base.StopAsync(cancellationToken);

        if (_panel is not null)
            await RunSafeAsync(() => _panel.RefreshAsync(this), "refresh the Now Playing panel");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_panel is not null)
            await RunSafeAsync(() => _panel.RemoveAsync(GuildId), "remove the Now Playing panel");

        _sessions?.Forget(GuildId);

        await base.DisposeAsyncCore();
    }

    private HashSet<string> RecentIdentifiers()
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);

        if (Queue.HasHistory)
        {
            foreach (var item in Queue.History)
            {
                if (item.Track is not null)
                    identifiers.Add(item.Track.Identifier);
            }
        }

        if (CurrentTrack is not null)
            identifiers.Add(CurrentTrack.Identifier);

        return identifiers;
    }

    private async Task RunSafeAsync(Func<Task> action, string description)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to {Action} in guild {GuildId}", description, GuildId);
        }
    }
}
