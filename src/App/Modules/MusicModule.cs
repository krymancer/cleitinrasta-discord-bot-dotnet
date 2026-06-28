using App.Services;
using Lavalink4NET;
using Lavalink4NET.NetCord;
using Lavalink4NET.Players;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace App.Modules;

public class MusicModule(
    IAudioService audioService,
    MusicPlaybackService playbackService,
    MusicQueryHistory history) : ApplicationCommandModule<SlashCommandContext>
{
    [SlashCommand("play", "Plays a track!")]
    public async Task PlayAsync([SlashCommandParameter(Description = "The query to search for")] string query)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        var message = await playbackService.PlayAsync(Context, query);

        await Context.Interaction.SendFollowupMessageAsync(MusicMessageFactory.Plain(message));
    }

    [SlashCommand("clear", description: "Clears the queue and stops playback")]
    public async Task<string> Clear()
    {
        return await playbackService.ClearAsync(Context);
    }

    [SlashCommand("history", description: "Shows the last played queries")]
    public InteractionMessageProperties History()
    {
        var entries = history.GetLatest();

        return MusicMessageFactory.History(entries);
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
        return await playbackService.SkipAsync(Context);
    }

    [SlashCommand("queue", description: "Show the queue")]
    public async Task<InteractionMessageProperties> Queue()
    {
        var queue = await playbackService.GetQueueAsync(Context);

        return MusicMessageFactory.Queue(queue);
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
}
