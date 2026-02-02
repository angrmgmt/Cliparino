using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

public interface IClipQueue {
    int Count { get; }

    ClipData? LastPlayed { get; }
    void Enqueue(ClipData clip);

    ClipData? Dequeue();

    ClipData? Peek();

    void Clear();

    void SetLastPlayed(ClipData clip);
}