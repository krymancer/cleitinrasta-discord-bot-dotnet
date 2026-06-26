namespace App.Services;

public sealed class MusicQueryHistory
{
    private const int MaxEntries = 5;
    private readonly List<MusicQueryHistoryEntry> _entries = new();
    private readonly object _syncRoot = new();

    public void Add(string query, string firstTrackTitle, int trackCount)
    {
        var entry = new MusicQueryHistoryEntry(query, firstTrackTitle, trackCount);

        lock (_syncRoot)
        {
            _entries.Insert(0, entry);

            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        }
    }

    public IReadOnlyList<MusicQueryHistoryEntry> GetLatest()
    {
        lock (_syncRoot)
        {
            return _entries.ToArray();
        }
    }
}

public sealed record MusicQueryHistoryEntry(string Query, string FirstTrackTitle, int TrackCount);
