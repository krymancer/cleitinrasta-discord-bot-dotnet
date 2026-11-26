using App.Services;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace App.Modules;

public class LiveModule(MeetingService meetingService) : ApplicationCommandModule<SlashCommandContext>
{
    [SlashCommand("live", "Create a video call room to share your screen")]
    public async Task LiveAsync()
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        var meeting = await meetingService.CreateAsync();
        var listeners = GetVoiceChannelMates();

        var message = new InteractionMessageProperties()
            .AddEmbeds(new EmbedProperties()
                .WithTitle("🔴 Live is on!")
                .WithDescription($"{Context.User} opened a **{meeting.Provider}** room to share their screen.\n{meeting.Url}")
                .WithColor(new Color(0xE53935)))
            .AddComponents(new ActionRowProperties([
                new LinkButtonProperties(meeting.Url, "Join the room"),
            ]))
            .WithAllowedMentions(new AllowedMentionsProperties
            {
                Everyone = false,
                AllowedRoles = [],
                AllowedUsers = listeners,
            });

        if (listeners.Count > 0)
            message.WithContent(string.Join(" ", listeners.Select(id => $"<@{id}>")));

        await Context.Interaction.SendFollowupMessageAsync(message);
    }

    /// <summary>Everyone (except bots and the caller) in the caller's voice channel.</summary>
    private List<ulong> GetVoiceChannelMates()
    {
        var guild = Context.Guild;
        if (guild is null || !guild.VoiceStates.TryGetValue(Context.User.Id, out var callerState) || callerState.ChannelId is null)
            return [];

        return guild.VoiceStates.Values
            .Where(state => state.ChannelId == callerState.ChannelId && state.UserId != Context.User.Id)
            .Where(state => !(guild.Users.TryGetValue(state.UserId, out var user) && user.IsBot))
            .Select(state => state.UserId)
            .ToList();
    }
}
