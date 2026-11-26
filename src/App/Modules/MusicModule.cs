using App.Services;
using Lavalink4NET.Players.Queued;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace App.Modules;

public class MusicModule(
    MusicPlaybackService playbackService,
    MusicQueryHistory history,
    NowPlayingPanel panel,
    LyricsService lyricsService) : ApplicationCommandModule<SlashCommandContext>
{
    [SlashCommand("play", "Play a song or playlist (search, YouTube or Spotify link)")]
    public async Task PlayAsync(
        [SlashCommandParameter(Description = "Song name or link", AutocompleteProviderType = typeof(PlayQueryAutocomplete))]
        string query)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        var result = await playbackService.PlayAsync(Context, Context.Channel.Id, Context.User.Id, query);
        if (result.Player is not null)
            await panel.RefreshAsync(result.Player);

        await Context.Interaction.SendFollowupMessageAsync(MusicMessageFactory.Plain(result.Message));
    }

    [SlashCommand("playnext", "Put a song or playlist at the front of the queue")]
    public async Task PlayNextAsync(
        [SlashCommandParameter(Description = "Song name or link", AutocompleteProviderType = typeof(PlayQueryAutocomplete))]
        string query)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        var result = await playbackService.PlayAsync(Context, Context.Channel.Id, Context.User.Id, query, next: true);
        if (result.Player is not null)
            await panel.RefreshAsync(result.Player);

        await Context.Interaction.SendFollowupMessageAsync(MusicMessageFactory.Plain(result.Message));
    }

    [SlashCommand("remove", "Remove a song from the queue")]
    public Task RemoveAsync(
        [SlashCommandParameter(Description = "Position in /queue", MinValue = 1)] int position) =>
        RunAsync(playbackService.RemoveAsync(Context, position));

    [SlashCommand("move", "Move a song to another position in the queue")]
    public Task MoveAsync(
        [SlashCommandParameter(Description = "Current position in /queue", MinValue = 1)] int from,
        [SlashCommandParameter(Description = "New position (1 = next)", MinValue = 1)] int to) =>
        RunAsync(playbackService.MoveAsync(Context, from, to));

    [SlashCommand("seek", "Jump to a time in the current song")]
    public Task SeekAsync(
        [SlashCommandParameter(Description = "Like 1:30, 90 or 1:02:03")] string time) =>
        RunAsync(playbackService.SeekAsync(Context, time));

    [SlashCommand("volume", "Show or set the volume")]
    public Task VolumeAsync(
        [SlashCommandParameter(Description = "0-200 (100 = normal)", MinValue = 0, MaxValue = 200)] int? percent = null) =>
        RunAsync(playbackService.SetVolumeAsync(Context, percent));

    [SlashCommand("lyrics", "Show the lyrics of the current song (or search)")]
    public async Task LyricsAsync(
        [SlashCommandParameter(Description = "Song to look up (default: what's playing)")] string? song = null)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        string title;
        string? author = null;
        TimeSpan? duration = null;

        if (!string.IsNullOrWhiteSpace(song))
        {
            title = song;
        }
        else
        {
            var current = (await playbackService.GetPlayerAsync(Context)).Player?.CurrentTrack;
            if (current is null)
            {
                await Context.Interaction.SendFollowupMessageAsync(
                    MusicMessageFactory.Plain("Nothing is playing. Use `/lyrics song:<name>` to search."));
                return;
            }

            (title, author, duration) = (current.Title, current.Author, current.Duration);
        }

        var lyrics = await lyricsService.FindAsync(title, author, duration);
        await Context.Interaction.SendFollowupMessageAsync(lyrics is null
            ? MusicMessageFactory.Plain($"No lyrics found for **{MusicMessageFactory.Escape(LyricsService.CleanTitle(title))}**.")
            : MusicMessageFactory.Lyrics(lyrics));
    }

    [SlashCommand("nowplaying", "Show the Now Playing panel at the bottom of the chat")]
    public async Task NowPlayingAsync()
    {
        var result = await playbackService.GetPlayerAsync(Context);
        if (result.Player is null)
        {
            await RespondAsync(result.Message, ephemeral: true);
            return;
        }

        result.Player.TextChannelId = Context.Channel.Id;
        await RespondAsync("🎶 Here you go:", ephemeral: true);
        await panel.PostAsync(result.Player);
    }

    [SlashCommand("pause", "Pause the current song")]
    public Task PauseAsync() => RunAsync(playbackService.PauseAsync(Context));

    [SlashCommand("resume", "Resume playback (also restarts the queue after /stop)")]
    public Task ResumeAsync() => RunAsync(playbackService.ResumeAsync(Context));

    [SlashCommand("skip", "Skip the current song")]
    public Task SkipAsync() => RunAsync(playbackService.SkipAsync(Context), refreshPanel: false);

    [SlashCommand("stop", "Stop the current song but keep the queue")]
    public Task StopAsync() => RunAsync(playbackService.StopAsync(Context));

    [SlashCommand("clear", "Clear the queue and stop playback")]
    public Task ClearAsync() => RunAsync(playbackService.ClearAsync(Context));

    [SlashCommand("loop", "Loop the current song or the whole queue")]
    public Task LoopAsync(
        [SlashCommandParameter(Description = "What to loop (default: cycle Off → Track → Queue)")] LoopMode? mode = null)
    {
        TrackRepeatMode? repeatMode = mode switch
        {
            LoopMode.Track => TrackRepeatMode.Track,
            LoopMode.Queue => TrackRepeatMode.Queue,
            LoopMode.Off => TrackRepeatMode.None,
            _ => null,
        };

        return RunAsync(playbackService.SetLoopAsync(Context, repeatMode));
    }

    public enum LoopMode
    {
        Track,
        Queue,
        Off,
    }

    [SlashCommand("shuffle", "Toggle shuffle")]
    public Task ShuffleAsync() => RunAsync(playbackService.ToggleShuffleAsync(Context));

    [SlashCommand("autoplay", "Toggle autoplay: keep playing similar songs when the queue ends")]
    public Task AutoplayAsync() => RunAsync(playbackService.ToggleAutoplayAsync(Context));

    [SlashCommand("queue", "Show the queue")]
    public async Task<InteractionMessageProperties> QueueAsync()
    {
        var queue = await playbackService.GetQueueAsync(Context);
        return MusicMessageFactory.Queue(queue);
    }

    [SlashCommand("history", "Show recently played searches")]
    public InteractionMessageProperties History() => MusicMessageFactory.History(history.GetLatest());

    [SlashCommand("leave", "Disconnect the bot from the voice channel")]
    public Task LeaveAsync() => RunAsync(playbackService.LeaveAsync(Context), refreshPanel: false);

    private async Task RunAsync(Task<MusicResult> action, bool refreshPanel = true)
    {
        var result = await action;

        // Answer first: Discord only waits 3 seconds for the interaction response.
        await RespondAsync(result.Message, ephemeral: !result.IsSuccess);

        if (refreshPanel && result.Player is not null)
            await panel.RefreshAsync(result.Player);
    }

    private Task RespondAsync(string message, bool ephemeral = false) =>
        Context.Interaction.SendResponseAsync(InteractionCallback.Message(MusicMessageFactory.Plain(message, ephemeral)));
}
