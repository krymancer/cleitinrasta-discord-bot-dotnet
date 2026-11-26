using Lavalink4NET.Players.Queued;
using NetCord;
using NetCord.Rest;

namespace App.Services;

public static class MusicMessageFactory
{
    private const string HistoryButtonPrefix = "music-history";
    public const string ClearButtonId = "music-clear";
    public const string SkipButtonId = "music-skip";
    public const string BackButtonId = "music-back";

    public const string PanelBackId = "np-back";
    public const string PanelPauseId = "np-pause";
    public const string PanelSkipId = "np-skip";
    public const string PanelStopId = "np-stop";
    public const string PanelLoopId = "np-loop";
    public const string PanelShuffleId = "np-shuffle";
    public const string PanelAutoplayId = "np-autoplay";
    public const string PanelClearId = "np-clear";

    private static readonly Color PlayingColor = new(0x1DB954);
    private static readonly Color PausedColor = new(0xF1C40F);
    private static readonly Color IdleColor = new(0x5865F2);

    public static InteractionMessageProperties Plain(string content, bool ephemeral = false)
    {
        var message = new InteractionMessageProperties()
            .WithContent(content)
            .WithAllowedMentions(AllowedMentionsProperties.None);

        if (ephemeral)
            message.WithFlags(MessageFlags.Ephemeral);

        return message;
    }

    // ---- Now Playing panel ------------------------------------------------------------

    public static void ApplyPanel(MessageProperties message, PanelView view)
    {
        message.Content = string.Empty;
        message.Embeds = [CreatePanelEmbed(view)];
        message.Components = CreatePanelControls(view);
    }

    public static void ApplyPanel(MessageOptions options, PanelView view)
    {
        options.Content = string.Empty;
        options.Embeds = [CreatePanelEmbed(view)];
        options.Components = CreatePanelControls(view);
    }

    private static EmbedProperties CreatePanelEmbed(PanelView view)
    {
        if (!view.HasTrack)
        {
            var idle = view.QueueCount > 0
                ? $"Stopped. {Tracks(view.QueueCount)} still in the queue — press **▶ Resume** or use `/resume`."
                : "Queue finished. Use `/play` to add more.";

            return new EmbedProperties()
                .WithAuthor(new EmbedAuthorProperties().WithName("⏹ Nothing playing"))
                .WithDescription(idle)
                .WithColor(IdleColor)
                .WithFooter(new EmbedFooterProperties().WithText(StatusLine(view)));
        }

        var lines = new List<string> { $"by **{Escape(view.Author ?? "Unknown")}**" };

        if (view.IsAutoplayPick)
            lines.Add("♾ Autoplay pick");
        else if (view.RequesterId is { } requester)
            lines.Add($"requested by <@{requester}>");

        lines.Add(string.Empty);
        lines.Add(view.IsLive
            ? "🔴 **LIVE**"
            : $"`{FormatTime(view.Position)}` {ProgressBar(view.Position, view.Duration)} `{FormatTime(view.Duration)}`");

        var embed = new EmbedProperties()
            .WithAuthor(new EmbedAuthorProperties().WithName(view.IsPaused ? "⏸ Paused" : "🎶 Now playing"))
            .WithTitle(Truncate(view.Title!, 256))
            .WithDescription(string.Join("\n", lines))
            .WithColor(view.IsPaused ? PausedColor : PlayingColor)
            .WithFooter(new EmbedFooterProperties().WithText(StatusLine(view)))
            .AddFields(new EmbedFieldProperties()
                .WithName(view.QueueCount > 0
                    ? $"Up next — {Tracks(view.QueueCount)}, {FormatTime(view.QueueDuration)}"
                    : "Up next")
                .WithValue(UpNext(view)));

        if (view.Uri is not null)
            embed.WithUrl(view.Uri.ToString());

        if (view.ArtworkUri is not null)
            embed.WithThumbnail(new EmbedThumbnailProperties(view.ArtworkUri.ToString()));

        return embed;
    }

    private static string UpNext(PanelView view)
    {
        if (view.UpNext.Count == 0)
            return view.Autoplay ? "♾ Autoplay will pick something similar." : "Nothing — add more with `/play`.";

        var lines = view.UpNext
            .Select((track, index) => $"`{index + 1}.` {Truncate(Escape(track.Title), 70)} `{FormatTime(track.Duration)}`")
            .ToList();

        if (view.QueueCount > view.UpNext.Count)
            lines.Add($"…and {view.QueueCount - view.UpNext.Count} more (`/queue`)");

        return string.Join("\n", lines);
    }

    private static string StatusLine(PanelView view)
    {
        var loop = view.RepeatMode switch
        {
            TrackRepeatMode.Track => "Track",
            TrackRepeatMode.Queue => "Queue",
            _ => "Off",
        };

        return $"Loop: {loop}  •  Shuffle: {OnOff(view.Shuffle)}  •  Autoplay: {OnOff(view.Autoplay)}  •  Volume: {view.VolumePercent}%";
    }

    private static IMessageComponentProperties[] CreatePanelControls(PanelView view)
    {
        if (!view.HasTrack && view.QueueCount == 0)
            return [];

        var loopLabel = view.RepeatMode switch
        {
            TrackRepeatMode.Track => "🔂 Loop: Track",
            TrackRepeatMode.Queue => "🔁 Loop: Queue",
            _ => "🔁 Loop: Off",
        };

        return
        [
            new ActionRowProperties([
                new ButtonProperties(PanelBackId, "⏮", ButtonStyle.Secondary).WithDisabled(!view.CanGoBack),
                view.HasTrack && !view.IsPaused
                    ? new ButtonProperties(PanelPauseId, "⏸ Pause", ButtonStyle.Primary)
                    : new ButtonProperties(PanelPauseId, "▶ Resume", ButtonStyle.Success),
                new ButtonProperties(PanelSkipId, "⏭ Skip", ButtonStyle.Primary).WithDisabled(!view.HasTrack),
                new ButtonProperties(PanelStopId, "⏹ Stop", ButtonStyle.Secondary).WithDisabled(!view.HasTrack),
            ]),
            new ActionRowProperties([
                new ButtonProperties(PanelLoopId, loopLabel, Toggle(view.RepeatMode is not TrackRepeatMode.None)),
                new ButtonProperties(PanelShuffleId, "🔀 Shuffle", Toggle(view.Shuffle)),
                new ButtonProperties(PanelAutoplayId, "♾ Autoplay", Toggle(view.Autoplay)),
                new ButtonProperties(PanelClearId, "🗑 Clear", ButtonStyle.Danger),
            ]),
        ];
    }

    private static ButtonStyle Toggle(bool on) => on ? ButtonStyle.Success : ButtonStyle.Secondary;

    private static string ProgressBar(TimeSpan position, TimeSpan duration, int width = 16)
    {
        var ratio = duration <= TimeSpan.Zero ? 0 : Math.Clamp(position / duration, 0, 1);
        var knob = (int)Math.Round(ratio * (width - 1));

        return string.Concat(Enumerable.Range(0, width).Select(i => i == knob ? "🔘" : "▬"));
    }

    // ---- Lyrics -----------------------------------------------------------------------

    public static InteractionMessageProperties Lyrics(Lyrics lyrics)
    {
        // Embed descriptions max out at 4096 characters and a message at 6000 in total.
        const int PageSize = 4000;
        const int TotalBudget = 5600;

        var text = lyrics.Text.Length > TotalBudget
            ? lyrics.Text[..TotalBudget].TrimEnd() + "\n…"
            : lyrics.Text;

        var pages = new List<string>();
        var remaining = text;
        while (remaining.Length > 0)
        {
            var take = remaining.Length <= PageSize ? remaining.Length : remaining.LastIndexOf('\n', PageSize);
            if (take <= 0) take = Math.Min(PageSize, remaining.Length);
            pages.Add(remaining[..take]);
            remaining = remaining[take..].TrimStart('\n');
        }

        var embeds = pages.Select((page, index) =>
        {
            var embed = new EmbedProperties().WithDescription(page).WithColor(new Color(0xE91E63));
            if (index == 0)
                embed.WithTitle(Truncate($"🎤 {lyrics.TrackName} — {lyrics.ArtistName}", 256));
            if (index == pages.Count - 1)
                embed.WithFooter(new EmbedFooterProperties().WithText("Lyrics from lrclib.net"));
            return embed;
        });

        return new InteractionMessageProperties()
            .AddEmbeds(embeds)
            .WithAllowedMentions(AllowedMentionsProperties.None);
    }

    // ---- History ----------------------------------------------------------------------

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
                new ButtonProperties(HistoryButtonId(entry.Id), label, ButtonStyle.Primary),
            ]);
        });

        return new InteractionMessageProperties()
            .AddEmbeds(new EmbedProperties()
                .WithTitle("Play history")
                .WithDescription(string.Join("\n\n", lines))
                .WithColor(new Color(0x4C9AFF)))
            .WithComponents(buttons);
    }

    public static string HistoryButtonId(long id) => $"{HistoryButtonPrefix}:{id}";

    // ---- Queue ------------------------------------------------------------------------

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

    private static EmbedProperties CreateQueueEmbed(MusicQueueView queue, string? status)
    {
        var descriptionLines = new List<string>();

        if (!string.IsNullOrWhiteSpace(status))
            descriptionLines.Add($"**Status:** {status}");

        descriptionLines.Add(queue.CurrentTrackTitle is not null
            ? $"**Now playing:** {Escape(queue.CurrentTrackTitle)}"
            : "**Now playing:** Nothing");

        if (queue.QueuedTrackTitles.Count == 0)
        {
            descriptionLines.Add("**Queue:** Empty");
        }
        else
        {
            descriptionLines.Add($"**Queue:** {Tracks(queue.QueuedTrackTitles.Count)}, {FormatTime(queue.TotalDuration)}");
            descriptionLines.AddRange(queue.QueuedTrackTitles
                .Take(15)
                .Select((title, index) => $"`{index + 1}.` {Truncate(Escape(title), 80)}"));

            if (queue.QueuedTrackTitles.Count > 15)
                descriptionLines.Add($"…and {queue.QueuedTrackTitles.Count - 15} more.");
        }

        return new EmbedProperties()
            .WithTitle("Music queue")
            .WithDescription(string.Join("\n", descriptionLines))
            .WithColor(new Color(0x2ECC71));
    }

    private static ActionRowProperties CreateQueueControls(MusicQueueView queue) => new([
        new ButtonProperties(BackButtonId, "⏮ Back", ButtonStyle.Secondary).WithDisabled(!queue.CanBack),
        new ButtonProperties(SkipButtonId, "⏭ Skip", ButtonStyle.Primary).WithDisabled(!queue.CanSkip),
        new ButtonProperties(ClearButtonId, "🗑 Clear", ButtonStyle.Danger).WithDisabled(!queue.CanClear),
    ]);

    // ---- Helpers ----------------------------------------------------------------------

    public static string FormatTime(TimeSpan time) => time.TotalHours >= 1
        ? time.ToString(@"h\:mm\:ss")
        : time.ToString(@"m\:ss");

    public static string Tracks(int count) => count == 1 ? "1 track" : $"{count} tracks";

    private static string OnOff(bool value) => value ? "On" : "Off";

    public static string Escape(string value) => value
        .Replace("*", "\\*")
        .Replace("_", "\\_")
        .Replace("`", "'")
        .Replace("~", "\\~")
        .Replace("|", "\\|");

    private static string Compact(string value) => value
        .Replace('\r', ' ')
        .Replace('\n', ' ')
        .Trim();

    public static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        return value[..(maxLength - 1)] + "…";
    }
}
