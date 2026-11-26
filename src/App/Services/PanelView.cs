using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;

namespace App.Services;

public sealed record PanelTrack(string Title, Uri? Uri, TimeSpan Duration);

/// <summary>Snapshot of a player, taken so rendering never races with the queue.</summary>
public sealed record PanelView(
    string? Title,
    Uri? Uri,
    string? Author,
    Uri? ArtworkUri,
    TimeSpan Duration,
    TimeSpan Position,
    bool IsLive,
    bool IsPaused,
    ulong? RequesterId,
    bool IsAutoplayPick,
    IReadOnlyList<PanelTrack> UpNext,
    int QueueCount,
    TimeSpan QueueDuration,
    TrackRepeatMode RepeatMode,
    bool Shuffle,
    bool Autoplay,
    bool CanGoBack)
{
    public bool HasTrack => Title is not null;

    public static PanelView From(MusicPlayer player)
    {
        var track = player.CurrentTrack;
        var queue = player.Queue.ToArray();

        return new PanelView(
            Title: track?.Title,
            Uri: track?.Uri,
            Author: track?.Author,
            ArtworkUri: track?.ArtworkUri,
            Duration: track?.Duration ?? TimeSpan.Zero,
            Position: player.Position?.Position ?? TimeSpan.Zero,
            IsLive: track?.IsLiveStream ?? false,
            IsPaused: player.State is PlayerState.Paused,
            RequesterId: (player.CurrentItem as MusicQueueItem)?.RequesterId,
            IsAutoplayPick: player.CurrentItem is MusicQueueItem { RequesterId: null },
            UpNext: queue
                .Take(3)
                .Select(item => new PanelTrack(item.Track?.Title ?? item.Identifier, item.Track?.Uri, item.Track?.Duration ?? TimeSpan.Zero))
                .ToArray(),
            QueueCount: queue.Length,
            QueueDuration: TimeSpan.FromTicks(queue.Sum(item => item.Track?.Duration.Ticks ?? 0)),
            RepeatMode: player.RepeatMode,
            Shuffle: player.Shuffle,
            Autoplay: player.Autoplay,
            CanGoBack: player.Queue.HasHistory && player.Queue.History.Count > 0);
    }
}
