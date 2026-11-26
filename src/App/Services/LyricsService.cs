using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace App.Services;

public sealed record Lyrics(string TrackName, string ArtistName, string Text);

/// <summary>Looks lyrics up on LRCLIB (https://lrclib.net), a free lyrics database with no API key.</summary>
public sealed partial class LyricsService(ILogger<LyricsService> logger) : IDisposable
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://lrclib.net/"),
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders = { { "User-Agent", "discord-music-bot/1.0" } },
    };

    public async Task<Lyrics?> FindAsync(string title, string? author, TimeSpan? duration, CancellationToken cancellationToken = default)
    {
        var cleanTitle = CleanTitle(title);
        var artist = CleanAuthor(author);

        // Try "title artist" first, then the title alone (YouTube titles often already contain the artist).
        string[] queries = artist is null || cleanTitle.Contains(artist, StringComparison.OrdinalIgnoreCase)
            ? [cleanTitle]
            : [$"{cleanTitle} {artist}", cleanTitle];

        foreach (var query in queries)
        {
            var results = await SearchAsync(query, cancellationToken);
            var best = results
                .Where(result => !result.Instrumental && !string.IsNullOrWhiteSpace(result.PlainLyrics))
                // Prefer a version with about the same length as what is playing.
                .OrderBy(result => duration is { } d ? Math.Abs(result.Duration - d.TotalSeconds) : 0)
                .FirstOrDefault(result => duration is not { } d || Math.Abs(result.Duration - d.TotalSeconds) < 30
                    || results.Length == 1);

            best ??= results.FirstOrDefault(result => !result.Instrumental && !string.IsNullOrWhiteSpace(result.PlainLyrics));

            if (best is not null)
                return new Lyrics(best.TrackName, best.ArtistName, best.PlainLyrics!.Trim());
        }

        return null;
    }

    private async Task<LrcLibResult[]> SearchAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            return await _http.GetFromJsonAsync<LrcLibResult[]>(
                $"api/search?q={Uri.EscapeDataString(query)}", cancellationToken) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "LRCLIB search failed for {Query}", query);
            return [];
        }
    }

    /// <summary>"Artist - Song (Official Video) [4K]" → "Artist - Song".</summary>
    public static string CleanTitle(string title)
    {
        var cleaned = BracketsRegex().Replace(title, " ");
        cleaned = NoiseRegex().Replace(cleaned, " ");
        cleaned = WhitespaceRegex().Replace(cleaned, " ").Trim(' ', '-', '|');
        return cleaned.Length > 0 ? cleaned : title;
    }

    private static string? CleanAuthor(string? author)
    {
        if (string.IsNullOrWhiteSpace(author))
            return null;

        var cleaned = AuthorNoiseRegex().Replace(author, string.Empty).Trim();
        return cleaned.Length > 0 ? cleaned : null;
    }

    [GeneratedRegex(@"[\(\[\{][^\)\]\}]*[\)\]\}]")]
    private static partial Regex BracketsRegex();

    [GeneratedRegex(@"\b(official|video|audio|lyrics?|clipe|oficial|hd|4k|visualizer|music video|ft\.?|feat\.?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseRegex();

    [GeneratedRegex(@"\s*-\s*Topic$|VEVO$|\s+Official$", RegexOptions.IgnoreCase)]
    private static partial Regex AuthorNoiseRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public void Dispose() => _http.Dispose();

    private sealed record LrcLibResult(
        [property: JsonPropertyName("trackName")] string TrackName,
        [property: JsonPropertyName("artistName")] string ArtistName,
        [property: JsonPropertyName("duration")] double Duration,
        [property: JsonPropertyName("instrumental")] bool Instrumental,
        [property: JsonPropertyName("plainLyrics")] string? PlainLyrics);
}
