using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace App.Services;

/// <summary>Play history stored in SQLite so it survives restarts.</summary>
public sealed class MusicQueryHistory(BotDatabase database)
{
    private const int MaxStoredEntries = 500;

    public MusicQueryHistoryEntry Add(string query, string firstTrackTitle, int trackCount)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();

        // Replaying the same query moves it to the top instead of duplicating it.
        using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM play_history WHERE query = $query";
            delete.Parameters.AddWithValue("$query", query);
            delete.ExecuteNonQuery();
        }

        long id;
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO play_history (query, title, track_count, played_at)
                VALUES ($query, $title, $count, $playedAt)
                RETURNING id
                """;
            insert.Parameters.AddWithValue("$query", query);
            insert.Parameters.AddWithValue("$title", firstTrackTitle);
            insert.Parameters.AddWithValue("$count", trackCount);
            insert.Parameters.AddWithValue("$playedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            id = (long)insert.ExecuteScalar()!;
        }

        using (var trim = connection.CreateCommand())
        {
            trim.CommandText = """
                DELETE FROM play_history WHERE id NOT IN (
                    SELECT id FROM play_history ORDER BY id DESC LIMIT $max)
                """;
            trim.Parameters.AddWithValue("$max", MaxStoredEntries);
            trim.ExecuteNonQuery();
        }

        transaction.Commit();
        return new MusicQueryHistoryEntry(id, query, firstTrackTitle, trackCount);
    }

    /// <summary>Most recent entries first.</summary>
    public IReadOnlyList<MusicQueryHistoryEntry> GetLatest(int count = 5) =>
        Query("SELECT id, query, title, track_count FROM play_history ORDER BY id DESC LIMIT $limit",
            ("$limit", count));

    /// <summary>Recent entries whose title or query contains <paramref name="text"/>.</summary>
    public IReadOnlyList<MusicQueryHistoryEntry> Search(string text, int count = 5) =>
        Query("""
            SELECT id, query, title, track_count FROM play_history
            WHERE title LIKE $pattern ESCAPE '\' OR query LIKE $pattern ESCAPE '\'
            ORDER BY id DESC LIMIT $limit
            """,
            ("$pattern", "%" + EscapeLike(text) + "%"),
            ("$limit", count));

    public bool TryGet(long id, [NotNullWhen(true)] out MusicQueryHistoryEntry? entry)
    {
        entry = Query("SELECT id, query, title, track_count FROM play_history WHERE id = $id", ("$id", id))
            .FirstOrDefault();
        return entry is not null;
    }

    private IReadOnlyList<MusicQueryHistoryEntry> Query(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);

        using var reader = command.ExecuteReader();
        var entries = new List<MusicQueryHistoryEntry>();
        while (reader.Read())
            entries.Add(new MusicQueryHistoryEntry(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3)));

        return entries;
    }

    private static string EscapeLike(string text) => text
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");
}

public sealed record MusicQueryHistoryEntry(long Id, string Query, string FirstTrackTitle, int TrackCount);
