using Lavalink4NET;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.Logging;

namespace App.Services;

/// <summary>
/// Picks the next track when the queue runs dry, using YouTube's "Mix" playlist
/// for the track that just finished.
/// </summary>
public sealed class AutoplayService(IAudioService audioService, ILogger<AutoplayService> logger)
{
    private const int CandidatePoolSize = 10;

    public async Task<LavalinkTrack?> FindNextAsync(
        LavalinkTrack seed,
        IReadOnlySet<string> recentIdentifiers,
        CancellationToken cancellationToken = default)
    {
        var videoId = await ResolveYouTubeIdAsync(seed, cancellationToken);
        if (videoId is null)
        {
            logger.LogInformation("Autoplay: no YouTube match for {Title}", seed.Title);
            return null;
        }

        var mixUrl = $"https://www.youtube.com/watch?v={videoId}&list=RD{videoId}";
        var mix = await audioService.Tracks.LoadTracksAsync(mixUrl, TrackSearchMode.None, cancellationToken: cancellationToken);

        var candidates = mix.Tracks
            .Where(track => track.Identifier != videoId
                && !recentIdentifiers.Contains(track.Identifier)
                && !track.IsLiveStream
                && track.Duration < TimeSpan.FromMinutes(15)
                && !IsSameSong(track, seed))
            .Take(CandidatePoolSize)
            .ToArray();

        if (candidates.Length == 0)
        {
            logger.LogInformation("Autoplay: mix for {VideoId} had no new tracks", videoId);
            return null;
        }

        return candidates[Random.Shared.Next(candidates.Length)];
    }

    private async Task<string?> ResolveYouTubeIdAsync(LavalinkTrack seed, CancellationToken cancellationToken)
    {
        if (string.Equals(seed.SourceName, "youtube", StringComparison.OrdinalIgnoreCase))
            return seed.Identifier;

        // Spotify and other mirrored sources: find the same song on YouTube first.
        var match = await audioService.Tracks.LoadTrackAsync(
            $"{seed.Author} {seed.Title}", TrackSearchMode.YouTube, cancellationToken: cancellationToken);

        return match?.Identifier;
    }

    // Mixes often contain other uploads of the same song (lyric videos, live versions).
    private static bool IsSameSong(LavalinkTrack candidate, LavalinkTrack seed) =>
        candidate.Title.Contains(seed.Title, StringComparison.OrdinalIgnoreCase)
        || seed.Title.Contains(candidate.Title, StringComparison.OrdinalIgnoreCase);
}
