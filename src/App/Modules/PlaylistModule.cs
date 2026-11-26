using App.Services;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace App.Modules;

[SlashCommand("playlist", "Saved server playlists")]
public class PlaylistModule(
    PlaylistStore playlists,
    MusicPlaybackService playbackService,
    NowPlayingPanel panel) : ApplicationCommandModule<SlashCommandContext>
{
    private ulong GuildId => Context.Guild?.Id ?? Context.Interaction.GuildId ?? 0;

    [SubSlashCommand("save", "Save the current song + queue as a playlist (overwrites the same name)")]
    public async Task SaveAsync(
        [SlashCommandParameter(Description = "Playlist name", MaxLength = PlaylistStore.MaxNameLength,
            AutocompleteProviderType = typeof(PlaylistNameAutocomplete))]
        string name)
    {
        name = name.Trim();
        var (tracks, error) = await playbackService.GetAllTracksAsync(Context);
        if (error is not null || tracks.Count == 0)
        {
            await RespondAsync(error?.Message ?? "Nothing to save — the queue is empty.", ephemeral: true);
            return;
        }

        playlists.Save(GuildId, name, Context.User.Id, tracks);
        await RespondAsync($"💾 Saved **{MusicMessageFactory.Escape(name)}** ({MusicMessageFactory.Tracks(tracks.Count)}).");
    }

    [SubSlashCommand("load", "Add a saved playlist to the queue")]
    public async Task LoadAsync(
        [SlashCommandParameter(Description = "Playlist name", AutocompleteProviderType = typeof(PlaylistNameAutocomplete))]
        string name,
        [SlashCommandParameter(Description = "Shuffle the songs before adding them")]
        bool shuffle = false)
    {
        var tracks = playlists.Load(GuildId, name.Trim());
        if (tracks is null)
        {
            await RespondAsync($"There's no playlist called **{MusicMessageFactory.Escape(name)}**. See `/playlist list`.", ephemeral: true);
            return;
        }

        if (tracks.Count == 0)
        {
            await RespondAsync("That playlist is empty.", ephemeral: true);
            return;
        }

        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        var result = await playbackService.EnqueueAsync(Context, Context.Channel.Id, Context.User.Id, tracks, shuffle);
        if (result.Player is not null)
            await panel.RefreshAsync(result.Player);

        var message = result.IsSuccess
            ? $"📃 Loaded **{MusicMessageFactory.Escape(name.Trim())}** — {MusicMessageFactory.Tracks(tracks.Count)}{(shuffle ? ", shuffled" : string.Empty)}."
            : result.Message;

        await Context.Interaction.SendFollowupMessageAsync(MusicMessageFactory.Plain(message));
    }

    [SubSlashCommand("list", "Show saved playlists")]
    public Task ListAsync()
    {
        var saved = playlists.List(GuildId);
        if (saved.Count == 0)
            return RespondAsync("No saved playlists yet. Queue some songs and use `/playlist save`.");

        var lines = saved.Select(playlist =>
            $"**{MusicMessageFactory.Escape(playlist.Name)}** — {MusicMessageFactory.Tracks(playlist.TrackCount)}, by <@{playlist.CreatedBy}>, <t:{playlist.UpdatedAt.ToUnixTimeSeconds()}:R>");

        return Context.Interaction.SendResponseAsync(InteractionCallback.Message(new InteractionMessageProperties()
            .AddEmbeds(new EmbedProperties()
                .WithTitle("Saved playlists")
                .WithDescription(string.Join("\n", lines))
                .WithColor(new Color(0x4C9AFF)))
            .WithAllowedMentions(AllowedMentionsProperties.None)));
    }

    [SubSlashCommand("delete", "Delete a saved playlist")]
    public Task DeleteAsync(
        [SlashCommandParameter(Description = "Playlist name", AutocompleteProviderType = typeof(PlaylistNameAutocomplete))]
        string name)
    {
        return playlists.Delete(GuildId, name.Trim())
            ? RespondAsync($"🗑 Deleted **{MusicMessageFactory.Escape(name.Trim())}**.")
            : RespondAsync($"There's no playlist called **{MusicMessageFactory.Escape(name)}**.", ephemeral: true);
    }

    private Task RespondAsync(string message, bool ephemeral = false) =>
        Context.Interaction.SendResponseAsync(InteractionCallback.Message(MusicMessageFactory.Plain(message, ephemeral)));
}

public sealed class PlaylistNameAutocomplete(PlaylistStore playlists) : IAutocompleteProvider<AutocompleteInteractionContext>
{
    public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option,
        AutocompleteInteractionContext context)
    {
        var guildId = context.Interaction.GuildId ?? 0;
        var choices = playlists.List(guildId, option.Value?.Trim())
            .Select(playlist => new ApplicationCommandOptionChoiceProperties(
                MusicMessageFactory.Truncate($"{playlist.Name} ({MusicMessageFactory.Tracks(playlist.TrackCount)})", 100),
                playlist.Name));

        return ValueTask.FromResult<IEnumerable<ApplicationCommandOptionChoiceProperties>?>(choices);
    }
}
