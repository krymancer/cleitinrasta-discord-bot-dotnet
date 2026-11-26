using System.Collections.Concurrent;
using Lavalink4NET;
using Lavalink4NET.Players;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Rest;

namespace App.Services;

/// <summary>
/// Keeps one "Now Playing" message per guild. A new message is posted (and the old one
/// deleted) when a track starts, so the panel stays near the bottom of the channel;
/// otherwise the same message is edited in place.
/// </summary>
public sealed class NowPlayingPanel(GatewayClient client, ILogger<NowPlayingPanel> logger)
{
    private readonly ConcurrentDictionary<ulong, PanelMessage> _messages = new();
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _locks = new();

    public IEnumerable<ulong> GuildsWithPanel => _messages.Keys;

    /// <summary>Posts a fresh panel at the bottom of the player's text channel.</summary>
    public async Task PostAsync(MusicPlayer player)
    {
        if (player.TextChannelId == 0)
            return;

        var gate = GetLock(player.GuildId);
        await gate.WaitAsync();
        try
        {
            if (_messages.TryRemove(player.GuildId, out var old))
                await TryDeleteAsync(old);

            var message = new MessageProperties { AllowedMentions = AllowedMentionsProperties.None };
            MusicMessageFactory.ApplyPanel(message, PanelView.From(player));

            var sent = await client.Rest.SendMessageAsync(player.TextChannelId, message);
            _messages[player.GuildId] = new PanelMessage(sent.ChannelId, sent.Id);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Edits the existing panel in place. Does nothing if there is no panel.</summary>
    public async Task RefreshAsync(MusicPlayer player)
    {
        if (!_messages.ContainsKey(player.GuildId))
            return;

        var gate = GetLock(player.GuildId);
        await gate.WaitAsync();
        try
        {
            if (!_messages.TryGetValue(player.GuildId, out var panel))
                return;

            var view = PanelView.From(player);
            try
            {
                await client.Rest.ModifyMessageAsync(panel.ChannelId, panel.MessageId,
                    options => MusicMessageFactory.ApplyPanel(options, view));
            }
            catch (RestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Someone deleted the panel by hand; stop tracking it.
                _messages.TryRemove(player.GuildId, out _);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Remembers that <paramref name="messageId"/> is now the guild's panel (used by buttons).</summary>
    public void Track(ulong guildId, ulong channelId, ulong messageId) =>
        _messages[guildId] = new PanelMessage(channelId, messageId);

    public bool IsPanel(ulong guildId, ulong messageId) =>
        _messages.TryGetValue(guildId, out var panel) && panel.MessageId == messageId;

    public async Task RemoveAsync(ulong guildId)
    {
        var gate = GetLock(guildId);
        await gate.WaitAsync();
        try
        {
            if (_messages.TryRemove(guildId, out var panel))
                await TryDeleteAsync(panel);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task TryDeleteAsync(PanelMessage panel)
    {
        try
        {
            await client.Rest.DeleteMessageAsync(panel.ChannelId, panel.MessageId);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not delete old Now Playing panel {MessageId}", panel.MessageId);
        }
    }

    private SemaphoreSlim GetLock(ulong guildId) => _locks.GetOrAdd(guildId, _ => new SemaphoreSlim(1, 1));

    private sealed record PanelMessage(ulong ChannelId, ulong MessageId);
}

/// <summary>Moves the progress bar on every panel while music is playing.</summary>
public sealed class NowPlayingRefresher(IAudioService audioService, NowPlayingPanel panel, ILogger<NowPlayingRefresher> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var guildId in panel.GuildsWithPanel.ToArray())
            {
                if (!audioService.Players.TryGetPlayer<MusicPlayer>(guildId, out var player) || player is null)
                    continue;

                if (player.State is not PlayerState.Playing)
                    continue;

                try
                {
                    await panel.RefreshAsync(player);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Periodic panel refresh failed for guild {GuildId}", guildId);
                }
            }
        }
    }
}
