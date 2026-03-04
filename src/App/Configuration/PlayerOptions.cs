namespace App.Configuration;

public class PlayerOptions
{
    public const string SectionName = "Player";
    
    public int InactivityTimeoutMinutes { get; set; } = 5;
}
