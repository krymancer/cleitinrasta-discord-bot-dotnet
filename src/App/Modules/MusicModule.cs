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

public class MusicModule(IAudioService audioService) : ApplicationCommandModule<SlashCommandContext>
{
    [SlashCommand("play", "Plays a track!")]
    public async Task PlayAsync([SlashCommandParameter(Description = "The query to search for")] string query)
    {
        // Defer the response immediately to avoid timeout
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        // Run the music logic off the gateway thread to avoid deadlock
        await Task.Run(async () =>
        {
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
                foreach (var playlistTrack in tracksResult.Tracks)
                {
                    await player.Queue.AddAsync(new TrackQueueItem(playlistTrack));
                }

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

            await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties { Content = $"Playing: {player.CurrentTrack?.Title}" });
        });
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
        var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: PlayerChannelBehavior.Join);

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
        var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: PlayerChannelBehavior.Join);

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
        var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: PlayerChannelBehavior.Join);

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
        var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: PlayerChannelBehavior.Join);

        var result = await audioService.Players
            .RetrieveAsync(Context, playerFactory: PlayerFactory.Queued, retrieveOptions);

        if (!result.IsSuccess) return GetErrorMessage(result.Status);

        var player = result.Player;

        if (player.CurrentTrack is null) return "Nothing playing!";

        await player.SkipAsync();

        var track = player.CurrentTrack;

        return track is not null
            ? $"Skipped. Now playing: {track.Uri}"
            : "Skipped. Stopped playing because the queue is now empty.";
    }

    [SlashCommand("queue", description: "Show the queue")]
    public async Task<string> Queue()
    {
        var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: PlayerChannelBehavior.Join);

        var result = await audioService.Players
            .RetrieveAsync(Context, playerFactory: PlayerFactory.Queued, retrieveOptions);

        if (!result.IsSuccess) return GetErrorMessage(result.Status);

        var player = result.Player;

        var queue = player.Queue.ToList();

        if (queue.Count > 0)
        {
            return string.Join("\n", queue.Select(x => x.Track?.Title));
        }

        return "No queue";
    }

    [SlashCommand("shuffle", "shuffle the queue")]
    public async Task<string> Shuffle()
    {
        var retrieveOptions = new PlayerRetrieveOptions(ChannelBehavior: PlayerChannelBehavior.Join);

        var result = await audioService.Players
            .RetrieveAsync(Context, playerFactory: PlayerFactory.Queued, retrieveOptions);

        if (!result.IsSuccess) return GetErrorMessage(result.Status);

        var player = result.Player;

        player.Shuffle = true;

        return "Done!";
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