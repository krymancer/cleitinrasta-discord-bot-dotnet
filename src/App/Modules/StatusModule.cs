using System.Diagnostics;
using App.Services;
using Lavalink4NET;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace App.Modules;

public class StatusModule(IAudioService audioService, HealthMonitor health, AlertService alerts)
    : ApplicationCommandModule<SlashCommandContext>
{
    [SlashCommand("status", "Bot and music server health")]
    public async Task StatusAsync(
        [SlashCommandParameter(Description = "Run a fresh YouTube check now (takes a few seconds)")] bool check = false)
    {
        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        var fields = new List<EmbedFieldProperties>();
        var healthy = true;

        // Lavalink
        try
        {
            var client = await audioService.ApiClientProvider.GetClientAsync();
            var info = await client.RetrieveServerInformationAsync();
            var stats = await client.RetrieveStatisticsAsync();

            var plugins = info.Plugins.Length == 0
                ? "none"
                : string.Join("\n", info.Plugins.Select(plugin => $"`{plugin.Name}` {ShortVersion(plugin.Version)}"));

            fields.Add(Field("Lavalink", $"🟢 v{info.Version.Version}{(info.Version.PreRelease is { } pre ? "-" + pre : "")}, up {FormatUptime(stats.Uptime)}\n" +
                $"{stats.PlayingPlayers}/{stats.ConnectedPlayers} players playing, " +
                $"{stats.MemoryUsage.UsedMemory / 1024 / 1024} MiB used, CPU {stats.ProcessorUsage.LavalinkLoad:P0}"));
            fields.Add(Field("Plugins", plugins));
            fields.Add(Field("Sources", string.Join(", ", info.AvailableSourceManagers)));
        }
        catch (Exception ex)
        {
            healthy = false;
            fields.Add(Field("Lavalink", $"🔴 unreachable — {ex.Message}"));
        }

        // YouTube
        var result = check ? await health.CheckYouTubeAsync() : health.LastCheck;
        if (result is null)
        {
            fields.Add(Field("YouTube", "⚪ not checked yet — use `/status check:True`"));
        }
        else
        {
            healthy &= result.Healthy;
            fields.Add(Field("YouTube", $"{(result.Healthy ? "🟢" : "🔴")} {result.Details}\nchecked <t:{result.At.ToUnixTimeSeconds()}:R>"));
        }

        // Bot
        var process = Process.GetCurrentProcess();
        var botUptime = DateTime.Now - process.StartTime;
        fields.Add(Field("Bot", $"up {FormatUptime(botUptime)}, {process.WorkingSet64 / 1024 / 1024} MiB"));

        // Recent problems
        var recent = alerts.Recent.Take(5).ToArray();
        fields.Add(Field("Recent problems", recent.Length == 0
            ? "None 🎉"
            : string.Join("\n", recent.Select(alert => $"<t:{alert.At.ToUnixTimeSeconds()}:R> {MusicMessageFactory.Truncate(alert.Title, 80)}"))));

        var embed = new EmbedProperties()
            .WithTitle(healthy ? "✅ Everything looks fine" : "⚠️ Something is wrong")
            .WithColor(new Color(healthy ? 0x2ECC71 : 0xE67E22))
            .WithFields(fields);

        await Context.Interaction.SendFollowupMessageAsync(new InteractionMessageProperties()
            .AddEmbeds(embed)
            .WithFlags(MessageFlags.Ephemeral));
    }

    private static EmbedFieldProperties Field(string name, string value) =>
        new EmbedFieldProperties().WithName(name).WithValue(MusicMessageFactory.Truncate(value, 1024));

    // Plugin versions can be 40-character commit hashes.
    private static string ShortVersion(string version) => version.Length > 12 ? version[..7] : version;

    private static string FormatUptime(TimeSpan uptime) => uptime.TotalDays >= 1
        ? $"{(int)uptime.TotalDays}d {uptime.Hours}h"
        : uptime.TotalHours >= 1 ? $"{(int)uptime.TotalHours}h {uptime.Minutes}m" : $"{uptime.Minutes}m";
}
