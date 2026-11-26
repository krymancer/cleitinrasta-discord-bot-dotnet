using App.Configuration;
using Lavalink4NET;
using Lavalink4NET.Events;
using Lavalink4NET.Rest.Entities.Tracks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Services;

public sealed record HealthCheckResult(DateTimeOffset At, bool Healthy, string Details);

/// <summary>
/// Watches the Lavalink connection and periodically checks that YouTube still answers,
/// reporting only when the state changes (broken → alert, fixed → all clear).
/// </summary>
public sealed class HealthMonitor : BackgroundService
{
    private const string ProbeSearch = "rick astley never gonna give you up";
    private const string ProbeVideo = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

    private readonly IAudioService _audioService;
    private readonly AlertService _alerts;
    private readonly AlertOptions _options;
    private readonly ILogger<HealthMonitor> _logger;
    private bool _connectionLost;
    private bool _youTubeBroken;

    public HealthMonitor(IAudioService audioService, AlertService alerts, IOptions<AlertOptions> options, ILogger<HealthMonitor> logger)
    {
        _audioService = audioService;
        _alerts = alerts;
        _options = options.Value;
        _logger = logger;

        _audioService.ConnectionClosed += OnConnectionClosedAsync;
        _audioService.ConnectionReady += OnConnectionReadyAsync;
    }

    public HealthCheckResult? LastCheck { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.HealthCheckMinutes <= 0)
            return;

        try
        {
            await _audioService.WaitForReadyAsync(stoppingToken);
            // Let Lavalink warm up (plugins, PoToken service) before the first probe.
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.HealthCheckMinutes));
            do
            {
                await CheckYouTubeAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public async Task<HealthCheckResult> CheckYouTubeAsync(CancellationToken cancellationToken = default)
    {
        string details;
        bool healthy;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));

            var search = await _audioService.Tracks.LoadTracksAsync(ProbeSearch, TrackSearchMode.YouTube, cancellationToken: timeout.Token);
            var video = await _audioService.Tracks.LoadTracksAsync(ProbeVideo, TrackSearchMode.None, cancellationToken: timeout.Token);

            healthy = search.Count > 0 && video.Track is not null;
            details = healthy
                ? $"Search returned {search.Count} results; video lookup OK."
                : $"Search: {Describe(search)}\nVideo lookup: {Describe(video)}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            healthy = false;
            details = $"{ex.GetType().Name}: {ex.Message}";
        }

        var result = new HealthCheckResult(DateTimeOffset.UtcNow, healthy, details);
        LastCheck = result;

        if (!healthy && !_youTubeBroken)
        {
            _youTubeBroken = true;
            await _alerts.ReportAsync("youtube-health", "YouTube check failed",
                $"The background check couldn't load YouTube.\n\n{details}\n\nSee docs/youtube-playback.md for the usual fixes.");
        }
        else if (healthy && _youTubeBroken)
        {
            _youTubeBroken = false;
            await _alerts.ReportAsync("youtube-health", "YouTube is working again", details, resolved: true);
        }

        _logger.LogInformation("YouTube health check: {Healthy} ({Details})", healthy ? "OK" : "FAILED", details);
        return result;
    }

    private static string Describe(TrackLoadResult result) => result.IsFailed
        ? $"failed — {result.Exception?.Cause ?? result.Exception?.Message ?? "unknown error"}"
        : result.Count == 0 ? "no results" : $"{result.Count} results";

    private async Task OnConnectionClosedAsync(object sender, ConnectionClosedEventArgs eventArgs)
    {
        if (_connectionLost)
            return;

        _connectionLost = true;
        await _alerts.ReportAsync("lavalink-connection", "Lost connection to Lavalink",
            $"Close status: {eventArgs.CloseStatus?.ToString() ?? "none"} {eventArgs.CloseStatusDescription}\n" +
            $"{eventArgs.Exception?.Message}\nReconnecting automatically; music is paused until it's back.");
    }

    private async Task OnConnectionReadyAsync(object sender, ConnectionReadyEventArgs eventArgs)
    {
        if (!_connectionLost)
            return;

        _connectionLost = false;
        await _alerts.ReportAsync("lavalink-connection", "Lavalink is back", "Reconnected to Lavalink.", resolved: true);
    }

    public override void Dispose()
    {
        _audioService.ConnectionClosed -= OnConnectionClosedAsync;
        _audioService.ConnectionReady -= OnConnectionReadyAsync;
        base.Dispose();
    }
}
