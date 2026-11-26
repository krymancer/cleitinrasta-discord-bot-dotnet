using System.Collections.Concurrent;
using App.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;

namespace App.Services;

public sealed record AlertRecord(DateTimeOffset At, string Title, string Details);

/// <summary>
/// Reports playback problems to a channel and/or a DM so a broken YouTube setup is noticed
/// before anyone complains. Repeats of the same problem are throttled.
/// </summary>
public sealed class AlertService(GatewayClient client, IOptions<AlertOptions> options, ILogger<AlertService> logger)
{
    private const int MaxRecent = 10;

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSent = new();
    private readonly ConcurrentQueue<AlertRecord> _recent = new();

    /// <summary>Most recent problems, newest first (also the ones that were throttled).</summary>
    public IReadOnlyList<AlertRecord> Recent => _recent.Reverse().ToArray();

    /// <param name="key">Identifies "the same problem" for throttling, e.g. "load-failed".</param>
    public async Task ReportAsync(string key, string title, string details, bool resolved = false)
    {
        logger.Log(resolved ? LogLevel.Information : LogLevel.Warning, "{Title}: {Details}", title, details);

        if (!resolved)
        {
            _recent.Enqueue(new AlertRecord(DateTimeOffset.UtcNow, title, details));
            while (_recent.Count > MaxRecent && _recent.TryDequeue(out _)) { }
        }

        var settings = options.Value;
        if (settings.ChannelId == 0 && settings.UserId == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        if (!resolved && _lastSent.TryGetValue(key, out var last) && now - last < TimeSpan.FromMinutes(settings.CooldownMinutes))
            return;

        _lastSent[key] = now;

        var embed = new EmbedProperties()
            .WithTitle(MusicMessageFactory.Truncate((resolved ? "✅ " : "⚠️ ") + title, 256))
            .WithDescription(MusicMessageFactory.Truncate(details, 4000))
            .WithColor(new Color(resolved ? 0x2ECC71 : 0xE67E22))
            .WithTimestamp(now);

        if (settings.ChannelId != 0)
            await TrySendAsync(() => client.Rest.SendMessageAsync(settings.ChannelId, Message(embed)), "alert channel");

        if (settings.UserId != 0)
        {
            await TrySendAsync(async () =>
            {
                var dm = await client.Rest.GetDMChannelAsync(settings.UserId);
                return await dm.SendMessageAsync(Message(embed));
            }, "alert DM");
        }
    }

    private static MessageProperties Message(EmbedProperties embed) => new()
    {
        Embeds = [embed],
        AllowedMentions = AllowedMentionsProperties.None,
    };

    private async Task TrySendAsync(Func<Task<RestMessage>> send, string target)
    {
        try
        {
            await send();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not deliver alert to {Target}", target);
        }
    }
}
