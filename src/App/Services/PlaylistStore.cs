using System.Globalization;
using Lavalink4NET.Tracks;

namespace App.Services;

public sealed record PlaylistSummary(string Name, int TrackCount, ulong CreatedBy, DateTimeOffset UpdatedAt);

/// <summary>Named playlists saved per guild. Tracks are stored encoded, so loading needs no searching.</summary>
public sealed class PlaylistStore(BotDatabase database)
{
    public const int MaxNameLength = 50;

    /// <summary>Creates or replaces a playlist.</summary>
    public void Save(ulong guildId, string name, ulong userId, IReadOnlyList<LavalinkTrack> tracks)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        Execute(connection, "DELETE FROM playlist_tracks WHERE guild_id = $guild AND name = $name", guildId, name);

        using (var upsert = connection.CreateCommand())
        {
            upsert.CommandText = """
                INSERT INTO playlists (guild_id, name, created_by, updated_at)
                VALUES ($guild, $name, $user, $updatedAt)
                ON CONFLICT (guild_id, name) DO UPDATE SET updated_at = excluded.updated_at
                """;
            upsert.Parameters.AddWithValue("$guild", (long)guildId);
            upsert.Parameters.AddWithValue("$name", name);
            upsert.Parameters.AddWithValue("$user", (long)userId);
            upsert.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            upsert.ExecuteNonQuery();
        }

        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO playlist_tracks (guild_id, name, position, encoded, title)
                VALUES ($guild, $name, $position, $encoded, $title)
                """;
            var guild = insert.Parameters.AddWithValue("$guild", (long)guildId);
            var playlist = insert.Parameters.AddWithValue("$name", name);
            var position = insert.Parameters.Add("$position", Microsoft.Data.Sqlite.SqliteType.Integer);
            var encoded = insert.Parameters.Add("$encoded", Microsoft.Data.Sqlite.SqliteType.Text);
            var title = insert.Parameters.Add("$title", Microsoft.Data.Sqlite.SqliteType.Text);

            for (var i = 0; i < tracks.Count; i++)
            {
                position.Value = i;
                encoded.Value = tracks[i].ToString();
                title.Value = tracks[i].Title;
                insert.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    public IReadOnlyList<LavalinkTrack>? Load(ulong guildId, string name)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.encoded FROM playlist_tracks t
            WHERE t.guild_id = $guild AND t.name = $name
            ORDER BY t.position
            """;
        command.Parameters.AddWithValue("$guild", (long)guildId);
        command.Parameters.AddWithValue("$name", name);

        using var reader = command.ExecuteReader();
        var tracks = new List<LavalinkTrack>();
        while (reader.Read())
        {
            if (LavalinkTrack.TryParse(reader.GetString(0), CultureInfo.InvariantCulture, out var track))
                tracks.Add(track);
        }

        return tracks.Count > 0 || Exists(connection, guildId, name) ? tracks : null;
    }

    public bool Delete(ulong guildId, string name)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        Execute(connection, "DELETE FROM playlist_tracks WHERE guild_id = $guild AND name = $name", guildId, name);
        var deleted = Execute(connection, "DELETE FROM playlists WHERE guild_id = $guild AND name = $name", guildId, name);
        transaction.Commit();
        return deleted > 0;
    }

    public IReadOnlyList<PlaylistSummary> List(ulong guildId, string? filter = null, int limit = 25)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.name, p.created_by, p.updated_at,
                   (SELECT COUNT(*) FROM playlist_tracks t WHERE t.guild_id = p.guild_id AND t.name = p.name)
            FROM playlists p
            WHERE p.guild_id = $guild AND ($filter = '' OR instr(lower(p.name), lower($filter)) > 0)
            ORDER BY p.updated_at DESC
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$guild", (long)guildId);
        command.Parameters.AddWithValue("$filter", filter ?? string.Empty);
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var playlists = new List<PlaylistSummary>();
        while (reader.Read())
        {
            playlists.Add(new PlaylistSummary(
                reader.GetString(0),
                reader.GetInt32(3),
                (ulong)reader.GetInt64(1),
                DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture)));
        }

        return playlists;
    }

    private static bool Exists(Microsoft.Data.Sqlite.SqliteConnection connection, ulong guildId, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM playlists WHERE guild_id = $guild AND name = $name";
        command.Parameters.AddWithValue("$guild", (long)guildId);
        command.Parameters.AddWithValue("$name", name);
        return command.ExecuteScalar() is not null;
    }

    private static int Execute(Microsoft.Data.Sqlite.SqliteConnection connection, string sql, ulong guildId, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$guild", (long)guildId);
        command.Parameters.AddWithValue("$name", name);
        return command.ExecuteNonQuery();
    }
}
