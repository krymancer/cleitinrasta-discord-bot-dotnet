using NetCord;
using NetCord.Rest;

namespace App.Services;

public static class MusicMessageFactory
{
    private const string HistoryButtonPrefix = "music-history";
    public const string ClearButtonId = "music-clear";
    public const string SkipButtonId = "music-skip";
    public const string BackButtonId = "music-back";

    public static InteractionMessageProperties Plain(string content, bool ephemeral = false)
    {
        var message = new InteractionMessageProperties()
            .WithContent(content);

        if (ephemeral)
            message.WithFlags(MessageFlags.Ephemeral);

        return message;
    }

    public static InteractionMessageProperties History(IReadOnlyList<MusicQueryHistoryEntry> entries)
    {
        if (entries.Count == 0)
            return Plain("No play history yet.");

        var lines = entries.Select((entry, index) =>
        {
            var title = Truncate(Compact(entry.FirstTrackTitle), 90);
            var query = Truncate(Compact(entry.Query), 80);
            var count = entry.TrackCount == 1 ? "1 song" : $"{entry.TrackCount} songs";

            return $"**{index + 1}.** {title} ({count})\n`{query}`";
        });

        var buttons = entries.Select((entry, index) =>
        {
            var count = entry.TrackCount == 1 ? "1" : entry.TrackCount.ToString();
            var label = Truncate($"{index + 1}. {Compact(entry.FirstTrackTitle)} ({count})", 80);

            return new ActionRowProperties([
                new ButtonProperties($"{HistoryButtonPrefix}:{entry.Id}", label, ButtonStyle.Primary),
            ]);
        });

        return new InteractionMessageProperties()
            .AddEmbeds(new EmbedProperties()
                .WithTitle("Play history")
                .WithDescription(string.Join("\n\n", lines))
                .WithColor(new Color(0x4C9AFF)))
            .WithComponents(buttons);
    }

    public static InteractionMessageProperties Queue(MusicQueueView queue, string? status = null)
    {
        if (queue.ErrorMessage is not null)
            return Plain(queue.ErrorMessage);

        return new InteractionMessageProperties()
            .AddEmbeds(CreateQueueEmbed(queue, status))
            .AddComponents(CreateQueueControls(queue));
    }

    public static void ApplyQueue(MessageOptions options, MusicQueueView queue, string? status = null)
    {
        if (queue.ErrorMessage is not null)
        {
            options.Content = queue.ErrorMessage;
            options.Embeds = [];
            options.Components = [];
            return;
        }

        options.Content = string.Empty;
        options.Embeds = [CreateQueueEmbed(queue, status)];
        options.Components = [CreateQueueControls(queue)];
    }

    public static string HistoryButtonId(long id) => $"{HistoryButtonPrefix}:{id}";

    private static EmbedProperties CreateQueueEmbed(MusicQueueView queue, string? status)
    {
        var descriptionLines = new List<string>();

        if (!string.IsNullOrWhiteSpace(status))
            descriptionLines.Add($"**Status:** {status}");

        descriptionLines.Add(queue.CurrentTrackTitle is not null
            ? $"**Now playing:** {queue.CurrentTrackTitle}"
            : "**Now playing:** Nothing");

        if (queue.QueuedTrackTitles.Count == 0)
        {
            descriptionLines.Add("**Queue:** Empty");
        }
        else
        {
            descriptionLines.Add("**Queue:**");
            descriptionLines.AddRange(queue.QueuedTrackTitles
                .Take(10)
                .Select((title, index) => $"{index + 1}. {title}"));

            if (queue.QueuedTrackTitles.Count > 10)
                descriptionLines.Add($"...and {queue.QueuedTrackTitles.Count - 10} more.");
        }

        return new EmbedProperties()
            .WithTitle("Music queue")
            .WithDescription(string.Join("\n", descriptionLines))
            .WithColor(new Color(0x2ECC71));
    }

    private static ActionRowProperties CreateQueueControls(MusicQueueView queue) => new([
        new ButtonProperties(BackButtonId, "Back", ButtonStyle.Secondary).WithDisabled(!queue.CanBack),
        new ButtonProperties(SkipButtonId, "Skip", ButtonStyle.Primary).WithDisabled(!queue.CanSkip),
        new ButtonProperties(ClearButtonId, "Clear", ButtonStyle.Danger).WithDisabled(!queue.CanClear),
    ]);

    private static string Compact(string value) => value
        .Replace('\r', ' ')
        .Replace('\n', ' ')
        .Trim();

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        return value[..(maxLength - 3)] + "...";
    }
}
