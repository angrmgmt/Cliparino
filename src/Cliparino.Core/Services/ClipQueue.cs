using System.Collections.Concurrent;
using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

/// <summary>
///     Thread-safe FIFO queue implementation for managing clips awaiting playback.
/// </summary>
/// <remarks>
///     <para>
///         This class implements <see cref="IClipQueue" /> using <see cref="ConcurrentQueue{T}" />
///         to provide lock-free, thread-safe enqueue and dequeue operations. It is used by
///         <see cref="PlaybackEngine" /> to process clips sequentially and by <see cref="CommandRouter" />
///         to add clips to the playback queue.
///     </para>
///     <para>
///         Key features:
///         - Thread-safe operations via ConcurrentQueue
///         - FIFO (first-in, first-out) ordering guarantee
///         - Tracks the last played clip for replay functionality
///         - O(1) enqueue, dequeue, and count operations
///     </para>
///     <para>
///         Thread-safety: All public methods are thread-safe. The queue can be safely accessed from
///         multiple threads (command router enqueuing, playback engine dequeuing, HTTP controllers
///         inspecting) without external synchronization.
///     </para>
///     <para>
///         Lifecycle: Registered as a singleton. A single instance exists for the lifetime of the application.
///     </para>
/// </remarks>
public class ClipQueue : IClipQueue {
    private readonly ConcurrentQueue<ClipData> _queue = new();

    /// <inheritdoc />
    public void Enqueue(ClipData clip) {
        ArgumentNullException.ThrowIfNull(clip);
        _queue.Enqueue(clip);
    }

    /// <inheritdoc />
    public ClipData? Dequeue() {
        _queue.TryDequeue(out var clip);

        return clip;
    }

    /// <inheritdoc />
    public ClipData? Peek() {
        _queue.TryPeek(out var clip);

        return clip;
    }

    /// <inheritdoc />
    public void Clear() {
        _queue.Clear();
    }

    /// <inheritdoc />
    public int Count => _queue.Count;

    /// <inheritdoc />
    public ClipData? LastPlayed { get; private set; }

    /// <inheritdoc />
    public void SetLastPlayed(ClipData clip) {
        ArgumentNullException.ThrowIfNull(clip);
        LastPlayed = clip;
    }
}