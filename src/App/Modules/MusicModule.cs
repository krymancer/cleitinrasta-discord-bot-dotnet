using App.Services;
using Lavalink4NET.Players.Queued;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace App.Modules;

public class MusicModule(
    MusicPlaybackService playbackService,
    MusicQueryHistory history,
    NowPlayingPanel panel) : ApplicationCommandModule<SlashCommandContext>
{
    [SlashCommand("play", "Play a song or playlist (YouTube, Spotify, SoundCloud links or a search)")]
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
