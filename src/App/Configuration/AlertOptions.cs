namespace App.Configuration;

public class AlertOptions
{
    public const string SectionName = "Alerts";

    /// <summary>Text channel for problem reports (e.g. #logs). 0 = don't post to a channel.</summary>
    public ulong ChannelId { get; set; }

    /// <summary>User to DM about problems (e.g. you). 0 = no DMs.</summary>
    public ulong UserId { get; set; }

    /// <summary>The same problem is reported at most once per this many minutes.</summary>
    public int CooldownMinutes { get; set; } = 30;

    /// <summary>How often to test-search YouTube in the background. 0 disables the check.</summary>
    public int HealthCheckMinutes { get; set; } = 30;
}
