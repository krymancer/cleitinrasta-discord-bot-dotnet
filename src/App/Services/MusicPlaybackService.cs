using Lavalink4NET;
using Lavalink4NET.NetCord;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.Options;
using NetCord.Services;

namespace App.Services;

/// <summary>Outcome of a music action: a message for the user and the player it acted on (if any).</summary>
public sealed record MusicResult(string Message, MusicPlayer? Player = null)
{
    public bool IsSuccess => Player is not null;
}

public sealed class MusicPlaybackService(
    IAudioService audioService,
    MusicQueryHistory history,
    AlertService alerts)
{
    public async Task<MusicResult> PlayAsync(IGuildContext context, ulong textChannelId, ulong userId, string query, bool next = false)
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

            if (next)
                await player.Queue.InsertRangeAsync(0, items);
            else
                await player.Queue.AddRangeAsync(items);

            if (player.CurrentTrack is null)
                await player.SkipAsync();

            history.Add(query, tracksResult.Tracks[0].Title, tracksResult.Tracks.Length);

            var name = tracksResult.Playlist?.Name is { Length: > 0 } playlistName
                ? $" from **{MusicMessageFactory.Escape(playlistName)}**"
                : string.Empty;

            return new MusicResult(next
                ? $"📃 Added {MusicMessageFactory.Tracks(tracksResult.Tracks.Length)}{name} to the front of the queue."
                : $"📃 Added {MusicMessageFactory.Tracks(tracksResult.Tracks.Length)}{name} to the queue.", player);
        }

        var track = tracksResult.Track;

        if (tracksResult.IsFailed)
        {
            await alerts.ReportAsync("search-failed", "Loading a song failed",
                $"`/play {MusicMessageFactory.Truncate(query, 200)}`\n\n`{MusicMessageFactory.Truncate(tracksResult.Exception?.Cause ?? tracksResult.Exception?.Message ?? "unknown error", 900)}`");
        }

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

        var item = new MusicQueueItem(new TrackReference(track), userId);
        int position;
        if (next && player.CurrentTrack is not null)
        {
            await player.Queue.InsertAsync(0, item);
            position = 1;
        }
        else
        {
            position = await player.PlayAsync(item);
        }

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

    public async Task<MusicResult> RemoveAsync(IGuildContext context, int position)
    {
        var (player, error) = await GetAsync(context);
        if (player is null) return error!;

        if (position < 1 || position > player.Queue.Count)
            return new MusicResult(QueueRangeError(player, "remove"), player);

        var item = player.Queue[position - 1];
        await player.Queue.RemoveAtAsync(position - 1);

        return new MusicResult($"🗑 Removed **{MusicMessageFactory.Escape(Title(item))}** from the queue.", player);
    }

    public async Task<MusicResult> MoveAsync(IGuildContext context, int from, int to)
    {
        var (player, error) = await GetAsync(context);
        if (player is null) return error!;

        var count = player.Queue.Count;
        if (from < 1 || from > count || to < 1 || to > count)
            return new MusicResult(QueueRangeError(player, "move"), player);

        var item = player.Queue[from - 1];
        await player.Queue.RemoveAtAsync(from - 1);
        await player.Queue.InsertAsync(to - 1, item);

        return new MusicResult($"↕ Moved **{MusicMessageFactory.Escape(Title(item))}** to position {to}.", player);
    }

    public async Task<MusicResult> SeekAsync(IGuildContext context, string time)
    {
        var (player, error) = await GetAsync(context);
        if (player is null) return error!;

        var track = player.CurrentTrack;
        if (track is null) return new MusicResult("Nothing is playing.", player);
        if (!track.IsSeekable) return new MusicResult("This track can't be seeked (live stream?).", player);

        if (!TryParseTime(time, out var position))
            return new MusicResult("Use a time like `1:30`, `90` or `1:02:03`.", player);

        if (position >= track.Duration)
            return new MusicResult($"That's past the end of the song ({MusicMessageFactory.FormatTime(track.Duration)}).", player);

        await player.SeekAsync(position);
        return new MusicResult($"⏩ Jumped to {MusicMessageFactory.FormatTime(position)}.", player);
    }

    public async Task<MusicResult> SetVolumeAsync(IGuildContext context, int? percent)
    {
        var (player, error) = await GetAsync(context);
        if (player is null) return error!;

        if (percent is null)
            return new MusicResult($"🔊 Volume is {Math.Round(player.Volume * 100)}%.", player);

        var clamped = Math.Clamp(percent.Value, 0, 200);
        await player.SetVolumeAsync(clamped / 100f);
        return new MusicResult($"🔊 Volume set to {clamped}%.", player);
    }

    /// <summary>Queues already-resolved tracks (saved playlists).</summary>
    public async Task<MusicResult> EnqueueAsync(IGuildContext context, ulong textChannelId, ulong userId, IReadOnlyList<LavalinkTrack> tracks, bool shuffle)
    {
        var result = await RetrieveAsync(context, textChannelId, join: true);
        if (!result.IsSuccess)
            return new MusicResult(GetErrorMessage(result.Status));

        var player = result.Player;
        player.TextChannelId = textChannelId;

        var items = tracks
            .Select(track => (ITrackQueueItem)new MusicQueueItem(new TrackReference(track), userId))
            .ToList();

        if (shuffle)
            items = items.OrderBy(_ => Random.Shared.Next()).ToList();

        await player.Queue.AddRangeAsync(items);

        if (player.CurrentTrack is null)
            await player.SkipAsync();

        return new MusicResult($"📃 Added {MusicMessageFactory.Tracks(items.Count)} to the queue.", player);
    }

    /// <summary>The current track followed by the queue, for saving as a playlist.</summary>
    public async Task<(IReadOnlyList<LavalinkTrack> Tracks, MusicResult? Error)> GetAllTracksAsync(IGuildContext context)
    {
        var (player, error) = await GetAsync(context);
        if (player is null) return ([], error);

        var tracks = new List<LavalinkTrack>();
        if (player.CurrentTrack is not null)
            tracks.Add(player.CurrentTrack);

        tracks.AddRange(player.Queue.Select(item => item.Track).OfType<LavalinkTrack>());
        return (tracks, null);
    }

    public static bool TryParseTime(string input, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        var parts = input.Trim().Split(':');
        if (parts.Length is < 1 or > 3)
            return false;

        var seconds = 0;
        foreach (var part in parts)
        {
            if (!int.TryParse(part, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value))
                return false;
            seconds = seconds * 60 + value;
        }

        time = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private static string QueueRangeError(MusicPlayer player, string verb) => player.Queue.IsEmpty
        ? "The queue is empty."
        : $"Pick a position between 1 and {player.Queue.Count} to {verb} (see `/queue`).";

    private static string Title(ITrackQueueItem item) => item.Track?.Title ?? item.Identifier;

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

        var options = Options.Create(MusicPlayer.CreateOptions(textChannelId));

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
