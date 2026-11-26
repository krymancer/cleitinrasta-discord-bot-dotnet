using Lavalink4NET.Extensions;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Protocol.Payloads.Events;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace App.Services;

public sealed record MusicPlayerOptions : QueuedLavalinkPlayerOptions
{
    public ulong TextChannelId { get; init; }
}

/// <summary>
/// A queue item that remembers who asked for it. <see cref="RequesterId"/> is <c>null</c>
/// for tracks the bot added on its own (autoplay).
/// </summary>
public sealed record MusicQueueItem(TrackReference Reference, ulong? RequesterId) : ITrackQueueItem
{
    public LavalinkTrack? Track => Reference.Track;
}

/// <summary>
/// Queued player that keeps the Now Playing panel in sync and tops the queue up
/// with related tracks when autoplay is on.
/// </summary>
public sealed class MusicPlayer : QueuedLavalinkPlayer
{
    public static PlayerFactory<MusicPlayer, MusicPlayerOptions> Factory { get; } =
        PlayerFactory.Create<MusicPlayer, MusicPlayerOptions>(properties => new MusicPlayer(properties));

    public static MusicPlayerOptions CreateOptions(ulong textChannelId) => new()
    {
        TextChannelId = textChannelId,
        SelfDeaf = true,
        // /stop keeps the queue; /clear empties it.
        ClearQueueOnStop = false,
        // Loop/shuffle are settings, not per-track state: keep them across stops.
        ResetTrackRepeatOnStop = false,
        ResetShuffleOnStop = false,
        HistoryCapacity = 50,
    };

    private readonly NowPlayingPanel? _panel;
    private readonly PlayerSessionService? _sessions;
    private readonly AutoplayService? _autoplay;
    private readonly AlertService? _alerts;
    private readonly ILogger _logger;

    private DateTimeOffset _trackStartedAt;
    private bool _seekedThisTrack;
    private bool _nextStartIsMidTrack;
    private string? _lastExceptionMessage;

    public MusicPlayer(IPlayerProperties<MusicPlayer, MusicPlayerOptions> properties)
        : base(properties)
    {
        TextChannelId = properties.Options.Value.TextChannelId;
        _panel = properties.ServiceProvider?.GetService<NowPlayingPanel>();
        _autoplay = properties.ServiceProvider?.GetService<AutoplayService>();
        _sessions = properties.ServiceProvider?.GetService<PlayerSessionService>();
        _alerts = properties.ServiceProvider?.GetService<AlertService>();
        _logger = properties.Logger;
    }

    /// <summary>Channel the Now Playing panel is posted in. Follows the last /play.</summary>
    public ulong TextChannelId { get; set; }

    /// <summary>Keep playing related tracks once the queue runs out.</summary>
    public bool Autoplay { get; set; }

    protected override async ValueTask NotifyTrackStartedAsync(ITrackQueueItem track, CancellationToken cancellationToken = default)
    {
        await base.NotifyTrackStartedAsync(track, cancellationToken);

        _trackStartedAt = DateTimeOffset.UtcNow;
        _seekedThisTrack = _nextStartIsMidTrack;
        _nextStartIsMidTrack = false;
        _lastExceptionMessage = null;

        if (_panel is not null)
            await RunSafeAsync(() => _panel.PostAsync(this), "post the Now Playing panel");
    }

    /// <summary>The next track starts part-way through (restore after restart), so don't flag a short play time.</summary>
    public void MarkNextStartAsMidTrack() => _nextStartIsMidTrack = true;

    public override async ValueTask SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
    {
        _seekedThisTrack = true;
        await base.SeekAsync(position, cancellationToken);
    }

    protected override async ValueTask NotifyTrackExceptionAsync(ITrackQueueItem track, TrackException exception, CancellationToken cancellationToken = default)
    {
        await base.NotifyTrackExceptionAsync(track, exception, cancellationToken);
        _lastExceptionMessage = exception.Cause ?? exception.Message;
    }

    protected override async ValueTask NotifyTrackStuckAsync(ITrackQueueItem track, TimeSpan threshold, CancellationToken cancellationToken = default)
    {
        await base.NotifyTrackStuckAsync(track, threshold, cancellationToken);

        if (_alerts is not null && track.Track is { } stuck)
        {
            await RunSafeAsync(() => _alerts.ReportAsync("track-stuck", "Track got stuck",
                $"**{stuck.Title}** ({stuck.SourceName}) produced no audio for {threshold.TotalSeconds:0}s.\n{stuck.Uri}"),
                "report a stuck track");
        }
    }

    protected override async ValueTask NotifyTrackEndedAsync(ITrackQueueItem queueItem, TrackEndReason endReason, CancellationToken cancellationToken = default)
    {
        if (endReason is TrackEndReason.LoadFailed && queueItem.Track is { } failed)
            await HandleFailedTrackAsync(failed);
        else if (endReason is TrackEndReason.Finished && queueItem.Track is { } finished)
            await CheckEndedEarlyAsync(finished);

        if (Autoplay && _autoplay is not null && endReason.MayStartNext()
            && Queue.IsEmpty && RepeatMode is TrackRepeatMode.None && queueItem.Track is not null)
        {
            await RunSafeAsync(async () =>
            {
                var next = await _autoplay.FindNextAsync(queueItem.Track, RecentIdentifiers(), cancellationToken);
                if (next is not null)
                    await Queue.AddAsync(new MusicQueueItem(new TrackReference(next), RequesterId: null), cancellationToken);
            }, "find an autoplay track");
        }

        await base.NotifyTrackEndedAsync(queueItem, endReason, cancellationToken);
    }

    public override async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        await base.StopAsync(cancellationToken);

        if (_panel is not null)
            await RunSafeAsync(() => _panel.RefreshAsync(this), "refresh the Now Playing panel");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_panel is not null)
            await RunSafeAsync(() => _panel.RemoveAsync(GuildId), "remove the Now Playing panel");

        _sessions?.Forget(GuildId);

        await base.DisposeAsyncCore();
    }

    /// <summary>A track failed to load or play: report it (the queue moves on by itself).</summary>
    private async Task HandleFailedTrackAsync(LavalinkTrack failed)
    {
        if (_alerts is null)
            return;

        var reason = _lastExceptionMessage ?? "unknown error";
        await RunSafeAsync(() => _alerts.ReportAsync($"load-failed:{failed.SourceName}", $"Couldn't play a {failed.SourceName} track",
            $"**{failed.Title}**\n{failed.Uri}\n\n`{MusicMessageFactory.Truncate(reason, 900)}`\n\nSkipped to the next song."),
            "report a failed track");
    }

    /// <summary>
    /// Catches the "music stops ~60s in" class of bug: Lavalink says the track finished
    /// although it played for much less than its length.
    /// </summary>
    private async Task CheckEndedEarlyAsync(LavalinkTrack track)
    {
        if (_alerts is null || _seekedThisTrack || track.IsLiveStream || track.Duration < TimeSpan.FromSeconds(90))
            return;

        var played = DateTimeOffset.UtcNow - _trackStartedAt;
        if (played >= track.Duration * 0.9 - TimeSpan.FromSeconds(5))
            return;

        await RunSafeAsync(() => _alerts.ReportAsync($"ended-early:{track.SourceName}", "A song stopped early",
            $"**{track.Title}** ({track.SourceName}) ended after {MusicMessageFactory.FormatTime(played)} " +
            $"of {MusicMessageFactory.FormatTime(track.Duration)}. This is what the YouTube SABR breakage looked like " +
            $"(see docs/youtube-playback.md).\n{track.Uri}"),
            "report an early end");
    }

    private HashSet<string> RecentIdentifiers()
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);

        if (Queue.HasHistory)
        {
            foreach (var item in Queue.History)
            {
                if (item.Track is not null)
                    identifiers.Add(item.Track.Identifier);
            }
        }

        if (CurrentTrack is not null)
            identifiers.Add(CurrentTrack.Identifier);

        return identifiers;
    }

    private async Task RunSafeAsync(Func<Task> action, string description)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to {Action} in guild {GuildId}", description, GuildId);
        }
    }
}
