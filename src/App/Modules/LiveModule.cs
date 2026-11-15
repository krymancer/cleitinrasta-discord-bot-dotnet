using App.Services;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace App.Modules;

public class LiveModule(MeetingService meetingService) : ApplicationCommandModule<SlashCommandContext>
{
    [SlashCommand("live", "Cria uma sala de videochamada para compartilhar a tela")]
    public async Task LiveAsync()
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        var meeting = await meetingService.CreateAsync();

        var message = new InteractionMessageProperties()
            .AddEmbeds(new EmbedProperties()
                .WithTitle("🔴 Live aberta!")
                .WithDescription($"{Context.User} abriu uma sala no **{meeting.Provider}** pra compartilhar a tela.\n{meeting.Url}")
                .WithColor(new Color(0xE53935)))
            .AddComponents(new ActionRowProperties([
                new LinkButtonProperties(meeting.Url, "Entrar na sala"),
            ]));

        await Context.Interaction.SendFollowupMessageAsync(message);
    }
}
