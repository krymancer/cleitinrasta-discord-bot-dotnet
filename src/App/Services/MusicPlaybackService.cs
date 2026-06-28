using Lavalink4NET;
using Lavalink4NET.NetCord;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using NetCord.Services;

namespace App.Services;

public sealed class MusicPlaybackService(IAudioService audioService, MusicQueryHistory history)
{
    public async Task<string> PlayAsync(IGuildContext context, string query)
    {
        var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: PlayerChannelBehavior.Join);

        var result = await audioService.Players
            .RetrieveAsync(context, playerFactory: PlayerFactory.Queued, retrieveOptions);

        if (!result.IsSuccess)
            return GetErrorMessage(result.Status);

        var player = result.Player;

        var tracksResult = await audioService.Tracks
            .LoadTracksAsync(query, TrackSearchMode.YouTube);

        if (tracksResult.IsPlaylist)
        {
            if (tracksResult.Tracks.Length == 0)
                return "No tracks found.";

            foreach (var playlistTrack in tracksResult.Tracks)
            {
                await player.Queue.AddAsync(new TrackQueueItem(playlistTrack));
            }

            if (player.CurrentTrack is null)
                await player.SkipAsync();

            history.Add(query, tracksResult.Tracks[0].Title, tracksResult.Tracks.Length);

            return $"Added {tracksResult.Tracks.Length} tracks to the queue.";
        }

        var track = tracksResult.Track;
        if (track is null)
            return "No tracks found.";

        var position = await player.PlayAsync(track);

        history.Add(query, track.Title, 1);

        return position == 0
            ? $"Playing: {track.Title}"
            : $"Added to queue: {track.Title}";
    }

    public async Task<string> ClearAsync(IGuildContext context)
    {
        var result = await RetrievePlayerAsync(context);
        if (!result.IsSuccess)
            return GetErrorMessage(result.Status);

        var player = result.Player;
        var queuedTrackCount = player.Queue.Count;
        var hasCurrentTrack = player.CurrentTrack is not null;

        if (!hasCurrentTrack && queuedTrackCount == 0)
            return "Nothing playing and queue is already empty.";

        await player.Queue.ClearAsync();
        await player.StopAsync();

        return hasCurrentTrack
            ? $"Stopped playback and cleared {queuedTrackCount} queued track(s)."
            : $"Cleared {queuedTrackCount} queued track(s).";
    }

    public async Task<string> SkipAsync(IGuildContext context)
    {
        var result = await RetrievePlayerAsync(context);
        if (!result.IsSuccess)
            return GetErrorMessage(result.Status);

        var player = result.Player;

        if (player.CurrentTrack is null)
            return "Nothing playing!";

        await player.SkipAsync();

        var track = player.CurrentTrack;

        return track is not null
            ? $"Skipped. Now playing: {track.Title}"
            : "Skipped. Stopped playing because the queue is now empty.";
    }

    public async Task<string> BackAsync(IGuildContext context)
    {
        var result = await RetrievePlayerAsync(context);
        if (!result.IsSuccess)
            return GetErrorMessage(result.Status);

        var player = result.Player;

        if (!player.Queue.HasHistory || player.Queue.History.Count == 0)
            return "No previous track in history.";

        var previousIndex = player.Queue.History.Count - 1;
        var previousItem = player.Queue.History[previousIndex];

        await player.Queue.History.RemoveAtAsync(previousIndex);

        if (player.CurrentTrack is not null)
            await player.Queue.InsertAsync(0, new TrackQueueItem(player.CurrentTrack));

        await player.PlayAsync(previousItem, enqueue: false);

        return previousItem.Track is not null
            ? $"Playing previous track: {previousItem.Track.Title}"
            : "Playing previous track.";
    }

    public async Task<MusicQueueView> GetQueueAsync(IGuildContext context)
    {
        var result = await RetrievePlayerAsync(context);
        if (!result.IsSuccess)
            return MusicQueueView.FromError(GetErrorMessage(result.Status));

        var player = result.Player;
        var queue = player.Queue
            .Select(item => item.Track?.Title ?? item.Identifier)
            .ToArray();

        return new MusicQueueView(
            ErrorMessage: null,
            CurrentTrackTitle: player.CurrentTrack?.Title,
            QueuedTrackTitles: queue,
            CanBack: player.Queue.HasHistory && player.Queue.History.Count > 0,
            CanSkip: player.CurrentTrack is not null,
            CanClear: player.CurrentTrack is not null || queue.Length > 0);
    }

    private ValueTask<PlayerResult<QueuedLavalinkPlayer>> RetrievePlayerAsync(IGuildContext context)
    {
        var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: PlayerChannelBehavior.None);

        return audioService.Players
            .RetrieveAsync(context, playerFactory: PlayerFactory.Queued, retrieveOptions);
    }

    private static string GetErrorMessage(PlayerRetrieveStatus retrieveStatus) => retrieveStatus switch
    {
        PlayerRetrieveStatus.UserNotInVoiceChannel => "You are not connected to a voice channel.",
        PlayerRetrieveStatus.BotNotConnected => "The bot is currently not connected.",
        _ => "Unknown error.",
    };
}

public sealed record MusicQueueView(
    string? ErrorMessage,
    string? CurrentTrackTitle,
    IReadOnlyList<string> QueuedTrackTitles,
    bool CanBack,
    bool CanSkip,
    bool CanClear)
{
    public static MusicQueueView FromError(string errorMessage) => new(
        ErrorMessage: errorMessage,
        CurrentTrackTitle: null,
        QueuedTrackTitles: [],
        CanBack: false,
        CanSkip: false,
        CanClear: false);
}
