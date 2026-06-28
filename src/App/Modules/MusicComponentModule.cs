using App.Services;
using NetCord.Rest;
using NetCord.Services.ComponentInteractions;

namespace App.Modules;

public class MusicComponentModule(MusicPlaybackService playbackService, MusicQueryHistory history)
    : ComponentInteractionModule<ButtonInteractionContext>
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

        var message = await playbackService.PlayAsync(Context, entry.Query);

        await Context.Interaction.SendFollowupMessageAsync(MusicMessageFactory.Plain(message));
    }

    [ComponentInteraction(MusicMessageFactory.BackButtonId)]
    public Task BackAsync() => UpdateQueueAsync(context => playbackService.BackAsync(context));

    [ComponentInteraction(MusicMessageFactory.SkipButtonId)]
    public Task SkipAsync() => UpdateQueueAsync(context => playbackService.SkipAsync(context));

    [ComponentInteraction(MusicMessageFactory.ClearButtonId)]
    public Task ClearAsync() => UpdateQueueAsync(context => playbackService.ClearAsync(context));

    private async Task UpdateQueueAsync(Func<ButtonInteractionContext, Task<string>> action)
    {
        var status = await action(Context);
        var queue = await playbackService.GetQueueAsync(Context);

        await Context.Interaction.SendResponseAsync(InteractionCallback.ModifyMessage(
            options => MusicMessageFactory.ApplyQueue(options, queue, status)));
    }
}
