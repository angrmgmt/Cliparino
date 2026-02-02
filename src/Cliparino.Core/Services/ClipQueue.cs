using System.Collections.Concurrent;
using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

public class ClipQueue : IClipQueue {
    private readonly ConcurrentQueue<ClipData> _queue = new();

    public void Enqueue(ClipData clip) {
        ArgumentNullException.ThrowIfNull(clip);
        _queue.Enqueue(clip);
    }

    public ClipData? Dequeue() {
        _queue.TryDequeue(out var clip);

        return clip;
    }

    public ClipData? Peek() {
        _queue.TryPeek(out var clip);

        return clip;
    }

    public void Clear() {
        _queue.Clear();
    }

    public int Count => _queue.Count;

    public ClipData? LastPlayed { get; private set; }

    public void SetLastPlayed(ClipData clip) {
        ArgumentNullException.ThrowIfNull(clip);
        LastPlayed = clip;
    }
}