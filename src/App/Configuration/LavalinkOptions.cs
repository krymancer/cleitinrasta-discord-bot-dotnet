namespace App.Configuration;

public class LavalinkOptions
{
    public const string SectionName = "Lavalink";
    
    public required string BaseAddress { get; set; }
    public required string Passphrase { get; set; }
}
