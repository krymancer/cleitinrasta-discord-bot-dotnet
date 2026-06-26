using App.Services;
using Lavalink4NET;
using Lavalink4NET.Extensions;
using Lavalink4NET.NetCord;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace App.Modules;

public class MusicModule(IAudioService audioService, MusicQueryHistory history) : ApplicationCommandModule<SlashCommandContext>
{
    [SlashCommand("play", "Plays a track!")]
    public async Task PlayAsync([SlashCommandParameter(Description = "The query to search for")] string query)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: PlayerChannelBehavior.Join);

        var result = await audioService.Players
            .RetrieveAsync(Context, playerFactory: PlayerFactory.Queued, retrieveOptions);

        if (!result.IsSuccess)
        {
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties { Content = GetErrorMessage(result.Status) });
            return;
        }

        var player = result.Player;

        var tracksResult = await audioService.Tracks
            .LoadTracksAsync(query, TrackSearchMode.YouTube);

        if (tracksResult.IsPlaylist)
        {
            if (tracksResult.Tracks.Length == 0)
            {
                await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties { Content = "No tracks found." });
                return;
            }

            foreach (var playlistTrack in tracksResult.Tracks)
            {
                await player.Queue.AddAsync(new TrackQueueItem(playlistTrack));
            }

            if (player.CurrentTrack is null)
                await player.SkipAsync();

            history.Add(query, tracksResult.Tracks[0].Title, tracksResult.Tracks.Length);

            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties { Content = $"Added {tracksResult.Tracks.Length} tracks to the queue" });
            return;
        }

        var track = tracksResult.Track;
        if (track is null)
        {
            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties { Content = "No tracks found." });
            return;
        }

        await player.PlayAsync(track);

        history.Add(query, track.Title, 1);

        await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties { Content = $"Playing: {player.CurrentTrack?.Title}" });
    }

    [SlashCommand("clear", description: "Clears the queue and stops playback")]
    public async Task<string> Clear()
    {
        var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: PlayerChannelBehavior.None);

        var result = await audioService.Players
            .RetrieveAsync(Context, playerFactory: PlayerFactory.Queued, retrieveOptions);

        if (!result.IsSuccess) return GetErrorMessage(result.Status);

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

    [SlashCommand("history", description: "Shows the last played queries")]
    public string History()
    {
        var entries = history.GetLatest();

        if (entries.Count == 0)
            return "No play history yet.";

        var lines = entries
            .Select((entry, index) => FormatHistoryEntry(index + 1, entry));

        return string.Join("\n", lines);
    }

    [SlashCommand("stop", description: "Stops the current track")]
    public async Task<string> Stop()
    {
        var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: PlayerChannelBehavior.None);

        var result = await audioService.Players
            .RetrieveAsync(Context, playerFactory: PlayerFactory.Queued, retrieveOptions);

        if (!result.IsSuccess) return GetErrorMessage(result.Status);

        var player = result.Player;

        if (player.CurrentTrack is null) return ("Nothing playing!");

        await player.StopAsync();
        return "Stopped playing.";
    }

    [SlashCommand("position", description: "Shows the track position")]
    public async Task<string> Position()
    {
        var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: PlayerChannelBehavior.None);

        var result = await audioService.Players
            .RetrieveAsync(Context, playerFactory: PlayerFactory.Queued, retrieveOptions);

        if (!result.IsSuccess) return GetErrorMessage(result.Status);

        var player = result.Player;

        if (player.CurrentTrack is null) return "Nothing playing!";

        return $"Position: {player.Position?.Position} / {player.CurrentTrack.Duration}.";
    }

    [SlashCommand("pause", description: "Pauses the player.")]
    public async Task<string> PauseAsync()
    {
        var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: PlayerChannelBehavior.None);

        var result = await audioService.Players
            .RetrieveAsync(Context, playerFactory: PlayerFactory.Queued, retrieveOptions);

        if (!result.IsSuccess) return GetErrorMessage(result.Status);

        var player = result.Player;

        if (player.State is PlayerState.Paused)
        {
            return "Player is already paused.";
        }

        await player.PauseAsync();
        return "Paused.";
    }

    [SlashCommand("resume", description: "Resumes the player.")]
    public async Task<string> ResumeAsync()
    {
        var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: PlayerChannelBehavior.None);

        var result = await audioService.Players
            .RetrieveAsync(Context, playerFactory: PlayerFactory.Queued, retrieveOptions);

        if (!result.IsSuccess) return GetErrorMessage(result.Status);

        var player = result.Player;

        if (player.State is not PlayerState.Paused) return "Player is not paused.";

        await player.ResumeAsync();
        return "Resumed.";
    }


    [SlashCommand("skip", description: "Skips the current track")]
    public async Task<string> Skip()
    {
        var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: PlayerChannelBehavior.None);

        var result = await audioService.Players
            .RetrieveAsync(Context, playerFactory: PlayerFactory.Queued, retrieveOptions);

        if (!result.IsSuccess) return GetErrorMessage(result.Status);

        var player = result.Player;

        if (player.CurrentTrack is null) return "Nothing playing!";

        await player.SkipAsync();

        var track = player.CurrentTrack;

        return track is not null
            ? $"Skipped. Now playing: {track.Title}"
            : "Skipped. Stopped playing because the queue is now empty.";
    }

    [SlashCommand("queue", description: "Show the queue")]
    public async Task<string> Queue()
    {
        var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: PlayerChannelBehavior.None);

        var result = await audioService.Players
            .RetrieveAsync(Context, playerFactory: PlayerFactory.Queued, retrieveOptions);

        if (!result.IsSuccess) return GetErrorMessage(result.Status);

        var player = result.Player;

        var lines = new List<string>();

        if (player.CurrentTrack is not null)
            lines.Add($"Now playing: {player.CurrentTrack.Title}");

        var queue = player.Queue.ToList();
        for (var i = 0; i < queue.Count; i++)
            lines.Add($"{i + 1}. {queue[i].Track?.Title}");

        return lines.Count > 0 ? string.Join("\n", lines) : "Nothing playing and queue is empty.";
    }

    [SlashCommand("shuffle", "Toggles shuffle mode")]
    public async Task<string> Shuffle()
    {
        var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: PlayerChannelBehavior.None);

        var result = await audioService.Players
            .RetrieveAsync(Context, playerFactory: PlayerFactory.Queued, retrieveOptions);

        if (!result.IsSuccess) return GetErrorMessage(result.Status);

        var player = result.Player;

        player.Shuffle = !player.Shuffle;

        return player.Shuffle ? "Shuffle enabled." : "Shuffle disabled.";
    }

    [SlashCommand("leave", description: "Disconnects the bot from the voice channel")]
    public async Task<string> LeaveAsync()
    {
        var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: PlayerChannelBehavior.None);

        var result = await audioService.Players
            .RetrieveAsync(Context, playerFactory: PlayerFactory.Queued, retrieveOptions);

        if (!result.IsSuccess) return GetErrorMessage(result.Status);

        var player = result.Player;

        // Disconnect and dispose the player
        await player.DisconnectAsync();

        return "Disconnected from voice channel.";
    }

    private static string GetErrorMessage(PlayerRetrieveStatus retrieveStatus) => retrieveStatus switch
    {
        PlayerRetrieveStatus.UserNotInVoiceChannel => "You are not connected to a voice channel.",
        PlayerRetrieveStatus.BotNotConnected => "The bot is currently not connected.",
        _ => "Unknown error.",
    };

    private static string FormatHistoryEntry(int index, MusicQueryHistoryEntry entry)
    {
        var firstTrackTitle = Truncate(Compact(entry.FirstTrackTitle), 90);
        var query = Truncate(Compact(entry.Query), 80);
        var count = entry.TrackCount == 1 ? "1 song" : $"{entry.TrackCount} songs";

        return $"{index}. {firstTrackTitle} ({count}) - query: {query}";
    }

    private static string Compact(string value) => value
        .Replace('\r', ' ')
        .Replace('\n', ' ')
        .Trim();

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        return value[..(maxLength - 3)] + "...";
    }
}
