using Lavalink4NET;
using Lavalink4NET.Rest.Entities.Tracks;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace App.Services;

/// <summary>
/// Suggests songs while typing in /play: matching history first, then live YouTube results.
/// Discord gives autocomplete 3 seconds, so the search is cut off well before that.
/// </summary>
public sealed class PlayQueryAutocomplete(
    IAudioService audioService,
    MusicQueryHistory history,
    ILogger<PlayQueryAutocomplete> logger) : IAutocompleteProvider<AutocompleteInteractionContext>
{
    private const int MaxChoices = 25;
    private const int MaxSearchResults = 8;
    private const int MaxChoiceLength = 100;
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromMilliseconds(2200);

    public async ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option,
        AutocompleteInteractionContext context)
    {
        var input = option.Value?.Trim() ?? string.Empty;
        var choices = new List<ApplicationCommandOptionChoiceProperties>();
        var seenValues = new HashSet<string>(StringComparer.Ordinal);

        void Add(string name, string value)
        {
            if (value.Length > MaxChoiceLength || choices.Count >= MaxChoices || !seenValues.Add(value))
                return;

            choices.Add(new ApplicationCommandOptionChoiceProperties(
                MusicMessageFactory.Truncate(name, MaxChoiceLength), value));
        }

        // Links are played as-is; nothing useful to suggest.
        if (Uri.TryCreate(input, UriKind.Absolute, out _))
        {
            Add(input, input);
            return choices;
        }

        foreach (var entry in history.GetLatest())
        {
            if (input.Length == 0
                || entry.FirstTrackTitle.Contains(input, StringComparison.OrdinalIgnoreCase)
                || entry.Query.Contains(input, StringComparison.OrdinalIgnoreCase))
            {
                Add($"🕘 {entry.FirstTrackTitle}", entry.Query);
            }
        }

        if (input.Length < 2)
            return choices;

        // Keep what the user typed as the first option so Enter still searches it.
        choices.Insert(0, new ApplicationCommandOptionChoiceProperties(
            MusicMessageFactory.Truncate($"🔎 {input}", MaxChoiceLength),
            MusicMessageFactory.Truncate(input, MaxChoiceLength)));
        seenValues.Add(input);

        try
        {
            using var timeout = new CancellationTokenSource(SearchTimeout);
            var results = await audioService.Tracks.LoadTracksAsync(input, TrackSearchMode.YouTube, cancellationToken: timeout.Token);

            foreach (var track in results.Tracks.Take(MaxSearchResults))
            {
                if (track.Uri is null)
                    continue;

                var duration = track.IsLiveStream ? "LIVE" : MusicMessageFactory.FormatTime(track.Duration);
                Add($"{track.Title} — {track.Author} ({duration})", track.Uri.ToString());
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Autocomplete search for {Query} timed out", input);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Autocomplete search for {Query} failed", input);
        }

        return choices;
    }
}
