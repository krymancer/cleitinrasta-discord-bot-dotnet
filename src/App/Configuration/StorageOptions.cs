namespace App.Configuration;

public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>SQLite file. Relative paths resolve against the working directory (/app in Docker).</summary>
    public string DatabasePath { get; set; } = "data/bot.db";

    /// <summary>Restore the queue after a restart if the snapshot is at most this old.</summary>
    public int RestoreMaxAgeMinutes { get; set; } = 30;
}
