using App.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Services;

/// <summary>
/// Small SQLite database for everything that should survive a restart:
/// play history, saved playlists and the last queue of each guild.
/// </summary>
public sealed class BotDatabase
{
    private readonly string _connectionString;

    public BotDatabase(IOptions<StorageOptions> options, ILogger<BotDatabase> logger)
    {
        var path = Path.GetFullPath(options.Value.DatabasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        }.ToString();

        Migrate();
        logger.LogInformation("Using database at {Path}", path);
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void Migrate()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS play_history (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                query        TEXT    NOT NULL,
                title        TEXT    NOT NULL,
                track_count  INTEGER NOT NULL,
                played_at    TEXT    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_play_history_played_at ON play_history (played_at DESC);

            CREATE TABLE IF NOT EXISTS playlists (
                guild_id    INTEGER NOT NULL,
                name        TEXT    NOT NULL COLLATE NOCASE,
                created_by  INTEGER NOT NULL,
                updated_at  TEXT    NOT NULL,
                PRIMARY KEY (guild_id, name)
            );

            CREATE TABLE IF NOT EXISTS playlist_tracks (
                guild_id   INTEGER NOT NULL,
                name       TEXT    NOT NULL COLLATE NOCASE,
                position   INTEGER NOT NULL,
                encoded    TEXT    NOT NULL,
                title      TEXT    NOT NULL,
                PRIMARY KEY (guild_id, name, position)
            );

            CREATE TABLE IF NOT EXISTS player_snapshots (
                guild_id   INTEGER PRIMARY KEY,
                saved_at   TEXT    NOT NULL,
                json       TEXT    NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }
}
