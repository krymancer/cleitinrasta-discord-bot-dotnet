using System.Diagnostics.CodeAnalysis;

namespace App.Services;

public sealed class MusicQueryHistory
{
    private const int MaxEntries = 5;
    private readonly List<MusicQueryHistoryEntry> _entries = new();
    private readonly object _syncRoot = new();
    private long _nextId;

    public MusicQueryHistoryEntry Add(string query, string firstTrackTitle, int trackCount)
    {
        lock (_syncRoot)
        {
            var entry = new MusicQueryHistoryEntry(++_nextId, query, firstTrackTitle, trackCount);

            _entries.Insert(0, entry);

            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);

            return entry;
        }
    }

    public IReadOnlyList<MusicQueryHistoryEntry> GetLatest()
    {
        lock (_syncRoot)
        {
            return _entries.ToArray();
        }
    }

    public bool TryGet(long id, [NotNullWhen(true)] out MusicQueryHistoryEntry? entry)
    {
        lock (_syncRoot)
        {
            entry = _entries.FirstOrDefault(entry => entry.Id == id);
            return entry is not null;
        }
    }
}

public sealed record MusicQueryHistoryEntry(long Id, string Query, string FirstTrackTitle, int TrackCount);
