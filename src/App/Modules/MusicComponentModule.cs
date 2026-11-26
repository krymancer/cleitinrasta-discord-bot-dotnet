using App.Services;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace App.Modules;

public class MusicComponentModule(
    MusicPlaybackService playbackService,
    MusicQueryHistory history,
    NowPlayingPanel panel) : ComponentInteractionModule<ButtonInteractionContext>
{
    [ComponentInteraction("music-history")]
    public async Task PlayHistoryAsync(long id)
    {
        if (!history.TryGet(id, out var entry))
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message(
                MusicMessageFactory.Plain("That history item is no longer available.", ephemeral: true)));
            return;
        }

        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        var result = await playbackService.PlayAsync(Context, Context.Channel.Id, Context.User.Id, entry.Query);
        if (result.Player is not null)
            await panel.RefreshAsync(result.Player);

        await Context.Interaction.SendFollowupMessageAsync(MusicMessageFactory.Plain(result.Message));
    }

    // ---- /queue message buttons ----

    [ComponentInteraction(MusicMessageFactory.BackButtonId)]
    public Task QueueBackAsync() => UpdateQueueAsync(playbackService.BackAsync);

    [ComponentInteraction(MusicMessageFactory.SkipButtonId)]
    public Task QueueSkipAsync() => UpdateQueueAsync(playbackService.SkipAsync);

    [ComponentInteraction(MusicMessageFactory.ClearButtonId)]
    public Task QueueClearAsync() => UpdateQueueAsync(playbackService.ClearAsync);

    private async Task UpdateQueueAsync(Func<ButtonInteractionContext, Task<MusicResult>> action)
    {
        var result = await action(Context);
        var queue = await playbackService.GetQueueAsync(Context);

        await Context.Interaction.SendResponseAsync(InteractionCallback.ModifyMessage(
            options => MusicMessageFactory.ApplyQueue(options, queue, result.Message)));

        if (result.Player is not null)
            await panel.RefreshAsync(result.Player);
    }

    // ---- Now Playing panel buttons ----

    [ComponentInteraction(MusicMessageFactory.PanelBackId)]
    public Task PanelBackAsync() => PanelActionAsync(playbackService.BackAsync);

    [ComponentInteraction(MusicMessageFactory.PanelPauseId)]
    public Task PanelPauseAsync() => PanelActionAsync(playbackService.TogglePauseAsync);

    [ComponentInteraction(MusicMessageFactory.PanelSkipId)]
    public Task PanelSkipAsync() => PanelActionAsync(playbackService.SkipAsync);

    [ComponentInteraction(MusicMessageFactory.PanelStopId)]
    public Task PanelStopAsync() => PanelActionAsync(playbackService.StopAsync);

    [ComponentInteraction(MusicMessageFactory.PanelLoopId)]
    public Task PanelLoopAsync() => PanelActionAsync(context => playbackService.SetLoopAsync(context, mode: null));

    [ComponentInteraction(MusicMessageFactory.PanelShuffleId)]
    public Task PanelShuffleAsync() => PanelActionAsync(playbackService.ToggleShuffleAsync);

    [ComponentInteraction(MusicMessageFactory.PanelAutoplayId)]
    public Task PanelAutoplayAsync() => PanelActionAsync(playbackService.ToggleAutoplayAsync);

    [ComponentInteraction(MusicMessageFactory.PanelClearId)]
    public Task PanelClearAsync() => PanelActionAsync(playbackService.ClearAsync);

    /// <summary>
    /// Acknowledges the click without changing the message, runs the action, then lets the
    /// panel re-render itself. Skip/back post a fresh panel through the track-started hook,
    /// so editing the clicked message directly would race with its deletion.
    /// </summary>
    private async Task PanelActionAsync(Func<ButtonInteractionContext, Task<MusicResult>> action)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredModifyMessage);

        var guildId = Context.Interaction.GuildId ?? 0;
        var message = Context.Interaction.Message;
        if (!panel.IsPanel(guildId, message.Id))
            panel.Track(guildId, message.ChannelId, message.Id);

        var result = await action(Context);

        if (result.Player is null)
        {
            await Context.Interaction.SendFollowupMessageAsync(MusicMessageFactory.Plain(result.Message, ephemeral: true));
            return;
        }

        await panel.RefreshAsync(result.Player);
    }
}
