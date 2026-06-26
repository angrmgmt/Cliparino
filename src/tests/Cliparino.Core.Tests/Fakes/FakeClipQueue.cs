using Cliparino.Core.Models;
using Cliparino.Core.Services;

namespace Cliparino.Core.Tests.Fakes;

public sealed class FakeClipQueue : IClipQueue {
    private readonly Queue<ClipData> _queue = new();

    public int Count => _queue.Count;
    public ClipData? LastPlayed { get; private set; }

    public void Enqueue(ClipData clip) {
        ArgumentNullException.ThrowIfNull(clip);
        _queue.Enqueue(clip);
    }

    public ClipData? Dequeue() {
        return _queue.Count > 0 ? _queue.Dequeue() : null;
    }

    public ClipData? Peek() {
        return _queue.Count > 0 ? _queue.Peek() : null;
    }

    public void Clear() {
        _queue.Clear();
    }

    public void SetLastPlayed(ClipData clip) {
        LastPlayed = clip;
    }
}