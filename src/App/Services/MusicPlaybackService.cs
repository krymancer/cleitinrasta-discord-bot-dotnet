using Lavalink4NET;
using Lavalink4NET.NetCord;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using Microsoft.Extensions.Options;
using NetCord.Services;

namespace App.Services;

/// <summary>Outcome of a music action: a message for the user and the player it acted on (if any).</summary>
public sealed record MusicResult(string Message, MusicPlayer? Player = null)
{
    public bool IsSuccess => Player is not null;
}

public sealed class MusicPlaybackService(IAudioService audioService, MusicQueryHistory history)
{
    public async Task<MusicResult> PlayAsync(IGuildContext context, ulong textChannelId, ulong userId, string query)
    {
        var result = await RetrieveAsync(context, textChannelId, join: true);
        if (!result.IsSuccess)
            return new MusicResult(GetErrorMessage(result.Status));

        var player = result.Player;
        player.TextChannelId = textChannelId;

        var tracksResult = await audioService.Tracks.LoadTracksAsync(query.Trim(), TrackSearchMode.YouTube);

        if (tracksResult.IsPlaylist)
        {
            if (tracksResult.Tracks.Length == 0)
                return new MusicResult("No tracks found.", player);

            var items = tracksResult.Tracks
                .Select(track => (ITrackQueueItem)new MusicQueueItem(new TrackReference(track), userId))
                .ToArray();

            await player.Queue.AddRangeAsync(items);

            if (player.CurrentTrack is null)
                await player.SkipAsync();

            history.Add(query, tracksResult.Tracks[0].Title, tracksResult.Tracks.Length);

            var name = tracksResult.Playlist?.Name is { Length: > 0 } playlistName
                ? $" from **{MusicMessageFactory.Escape(playlistName)}**"
                : string.Empty;

            return new MusicResult($"📃 Added {MusicMessageFactory.Tracks(tracksResult.Tracks.Length)}{name} to the queue.", player);
        }

        var track = tracksResult.Track;
        if (track is null)
        {
            if (tracksResult.IsFailed && query.Contains("spotify.com", StringComparison.OrdinalIgnoreCase))
            {
                return new MusicResult(
                    "Couldn't load that Spotify link. Spotify needs `SPOTIFY_CLIENT_ID`/`SPOTIFY_CLIENT_SECRET` set on Lavalink (see README).",
                    player);
            }

            return new MusicResult(tracksResult.IsFailed
                ? $"Couldn't load that: {tracksResult.Exception?.Message ?? "unknown error"}"
                : "No tracks found.", player);
        }

        var position = await player.PlayAsync(new MusicQueueItem(new TrackReference(track), userId));

        history.Add(query, track.Title, 1);

        var title = $"**{MusicMessageFactory.Escape(track.Title)}** `{MusicMessageFactory.FormatTime(track.Duration)}`";
        return new MusicResult(position == 0
            ? $"▶ Playing {title}"
            : $"➕ Queued {title} — position {position} in the queue", player);
    }

    public async Task<MusicResult> PauseAsync(IGuildContext context)
    {
        var (player, error) = await GetAsync(context);
        if (player is null) return error!;

        if (player.CurrentTrack is null) return new MusicResult("Nothing is playing.", player);
        if (player.State is PlayerState.Paused) return new MusicResult("Already paused.", player);

        await player.PauseAsync();
        return new MusicResult("⏸ Paused.", player);
    }

    public async Task<MusicResult> ResumeAsync(IGuildContext context)
    {
        var (player, error) = await GetAsync(context);
        if (player is null) return error!;

        // After /stop the queue is kept: resuming starts the next track.
        if (player.CurrentTrack is null)
        {
            if (player.Queue.IsEmpty)
                return new MusicResult("Nothing to resume — the queue is empty.", player);

            await player.SkipAsync();
            return new MusicResult("▶ Resumed the queue.", player);
        }

        if (player.State is not PlayerState.Paused) return new MusicResult("Not paused.", player);

        await player.ResumeAsync();
        return new MusicResult("▶ Resumed.", player);
    }

    public async Task<MusicResult> TogglePauseAsync(IGuildContext context)
    {
        var (player, error) = await GetAsync(context);
        if (player is null) return error!;

        return player.CurrentTrack is not null && player.State is not PlayerState.Paused
            ? await PauseAsync(context)
            : await ResumeAsync(context);
    }

    public async Task<MusicResult> SkipAsync(IGuildContext context)
    {
        var (player, error) = await GetAsync(context);
        if (player is null) return error!;

        if (player.CurrentTrack is null)
            return new MusicResult("Nothing is playing.", player);

        var skipped = player.CurrentTrack.Title;
        await player.SkipAsync();

        return new MusicResult($"⏭ Skipped **{MusicMessageFactory.Escape(skipped)}**.", player);
    }

    /// <summary>Stops the current track but keeps the queue (use resume to continue).</summary>
    public async Task<MusicResult> StopAsync(IGuildContext context)
    {
        var (player, error) = await GetAsync(context);
        if (player is null) return error!;

        if (player.CurrentTrack is null)
            return new MusicResult("Nothing is playing.", player);

        await player.StopAsync();

        return new MusicResult(player.Queue.IsEmpty
            ? "⏹ Stopped."
            : $"⏹ Stopped. {MusicMessageFactory.Tracks(player.Queue.Count)} still queued — `/resume` to continue.", player);
    }

    /// <summary>Clears the queue and stops playback.</summary>
    public async Task<MusicResult> ClearAsync(IGuildContext context)
    {
        var (player, error) = await GetAsync(context);
        if (player is null) return error!;

        var queuedTrackCount = player.Queue.Count;
        var hasCurrentTrack = player.CurrentTrack is not null;

        if (!hasCurrentTrack && queuedTrackCount == 0)
            return new MusicResult("Nothing playing and the queue is already empty.", player);

        await player.Queue.ClearAsync();
        if (hasCurrentTrack)
            await player.StopAsync();

        return new MusicResult(hasCurrentTrack
            ? $"🗑 Stopped playback and cleared {MusicMessageFactory.Tracks(queuedTrackCount)}."
            : $"🗑 Cleared {MusicMessageFactory.Tracks(queuedTrackCount)}.", player);
    }

    public async Task<MusicResult> BackAsync(IGuildContext context)
    {
        var (player, error) = await GetAsync(context);
        if (player is null) return error!;

        if (!player.Queue.HasHistory || player.Queue.History.Count == 0)
            return new MusicResult("No previous track in history.", player);

        var previousIndex = player.Queue.History.Count - 1;
        var previousItem = player.Queue.History[previousIndex];

        await player.Queue.History.RemoveAtAsync(previousIndex);

        if (player.CurrentItem is not null)
            await player.Queue.InsertAsync(0, player.CurrentItem);

        await player.PlayAsync(previousItem, enqueue: false);

        return new MusicResult(previousItem.Track is not null
            ? $"⏮ Playing previous track: **{MusicMessageFactory.Escape(previousItem.Track.Title)}**"
            : "⏮ Playing previous track.", player);
    }

    public async Task<MusicResult> SetLoopAsync(IGuildContext context, TrackRepeatMode? mode)
    {
        var (player, error) = await GetAsync(context);
        if (player is null) return error!;

        // No mode: cycle Off -> Track -> Queue -> Off.
        player.RepeatMode = mode ?? player.RepeatMode switch
        {
            TrackRepeatMode.None => TrackRepeatMode.Track,
            TrackRepeatMode.Track => TrackRepeatMode.Queue,
            _ => TrackRepeatMode.None,
        };

        return new MusicResult(player.RepeatMode switch
        {
            TrackRepeatMode.Track => "🔂 Looping the current track.",
            TrackRepeatMode.Queue => "🔁 Looping the whole queue.",
            _ => "Loop disabled.",
        }, player);
    }

    public async Task<MusicResult> ToggleShuffleAsync(IGuildContext context)
    {
        var (player, error) = await GetAsync(context);
        if (player is null) return error!;

        player.Shuffle = !player.Shuffle;
        return new MusicResult(player.Shuffle ? "🔀 Shuffle enabled." : "Shuffle disabled.", player);
    }

    public async Task<MusicResult> ToggleAutoplayAsync(IGuildContext context)
    {
        var (player, error) = await GetAsync(context);
        if (player is null) return error!;

        player.Autoplay = !player.Autoplay;
        return new MusicResult(player.Autoplay
            ? "♾ Autoplay on — when the queue ends I'll keep playing similar songs."
            : "Autoplay off.", player);
    }

    public async Task<MusicResult> LeaveAsync(IGuildContext context)
    {
        var (player, error) = await GetAsync(context);
        if (player is null) return error!;

        await player.DisconnectAsync();
        return new MusicResult("👋 Disconnected from the voice channel.", player);
    }

    public async Task<MusicResult> GetPlayerAsync(IGuildContext context)
    {
        var (player, error) = await GetAsync(context);
        return player is null ? error! : new MusicResult(string.Empty, player);
    }

    public async Task<MusicQueueView> GetQueueAsync(IGuildContext context)
    {
        var (player, error) = await GetAsync(context);
        if (player is null)
            return MusicQueueView.FromError(error!.Message);

        var queue = player.Queue.ToArray();

        return new MusicQueueView(
            ErrorMessage: null,
            CurrentTrackTitle: player.CurrentTrack?.Title,
            QueuedTrackTitles: queue.Select(item => item.Track?.Title ?? item.Identifier).ToArray(),
            TotalDuration: TimeSpan.FromTicks(queue.Sum(item => item.Track?.Duration.Ticks ?? 0)),
            CanBack: player.Queue.HasHistory && player.Queue.History.Count > 0,
            CanSkip: player.CurrentTrack is not null,
            CanClear: player.CurrentTrack is not null || queue.Length > 0);
    }

    private async Task<(MusicPlayer? Player, MusicResult? Error)> GetAsync(IGuildContext context)
    {
        var result = await RetrieveAsync(context, textChannelId: 0, join: false);
        return result.IsSuccess
            ? (result.Player, null)
            : (null, new MusicResult(GetErrorMessage(result.Status)));
    }

    private ValueTask<PlayerResult<MusicPlayer>> RetrieveAsync(IGuildContext context, ulong textChannelId, bool join)
    {
        var retrieveOptions = new PlayerRetrieveOptions(
            ChannelBehavior: join ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None);

        var options = Options.Create(new MusicPlayerOptions
        {
            TextChannelId = textChannelId,
            SelfDeaf = true,
            // /stop keeps the queue; /clear empties it.
            ClearQueueOnStop = false,
            // Loop/shuffle are settings, not per-track state: keep them across stops.
            ResetTrackRepeatOnStop = false,
            ResetShuffleOnStop = false,
            HistoryCapacity = 50,
        });

        return audioService.Players.RetrieveAsync(context, MusicPlayer.Factory, options, retrieveOptions);
    }

    public static string GetErrorMessage(PlayerRetrieveStatus retrieveStatus) => retrieveStatus switch
    {
        PlayerRetrieveStatus.UserNotInVoiceChannel => "You need to be in a voice channel first.",
        PlayerRetrieveStatus.BotNotConnected => "I'm not in a voice channel. Use `/play` to start.",
        PlayerRetrieveStatus.VoiceChannelMismatch => "You need to be in the same voice channel as me.",
        _ => "Something went wrong talking to the music server.",
    };
}

public sealed record MusicQueueView(
    string? ErrorMessage,
    string? CurrentTrackTitle,
    IReadOnlyList<string> QueuedTrackTitles,
    TimeSpan TotalDuration,
    bool CanBack,
    bool CanSkip,
    bool CanClear)
{
    public static MusicQueueView FromError(string errorMessage) => new(
        ErrorMessage: errorMessage,
        CurrentTrackTitle: null,
        QueuedTrackTitles: [],
        TotalDuration: TimeSpan.Zero,
        CanBack: false,
        CanSkip: false,
        CanClear: false);
}
