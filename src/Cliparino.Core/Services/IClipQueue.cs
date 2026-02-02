using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

/// <summary>
///     Defines the contract for managing the FIFO queue of clips awaiting playback.
/// </summary>
/// <remarks>
///     <para>
///         This interface is implemented by <see cref="ClipQueue" /> and is used by
///         <see cref="IPlaybackEngine" /> for sequential clip playback and by controllers for queue inspection.
///     </para>
///     <para>
///         Key responsibilities:
///         - Maintain FIFO queue of clips to be played
///         - Track the last played clip for replay functionality
///         - Provide thread-safe enqueue/dequeue operations
///         - Report queue size for monitoring and diagnostics
///     </para>
///     <para>
///         Thread-safety: All operations must be thread-safe as the queue is accessed from multiple
///         sources (command router enqueuing, playback engine dequeuing, HTTP controllers inspecting).
///     </para>
/// </remarks>
public interface IClipQueue {
    /// <summary>
    ///     Gets the number of clips currently in the queue awaiting playback.
    /// </summary>
    /// <value>The count of queued clips. Returns 0 if the queue is empty.</value>
    int Count { get; }

    /// <summary>
    ///     Gets the most recently played clip, or null if no clip has been played yet.
    /// </summary>
    /// <value>
    ///     The <see cref="ClipData" /> of the last played clip, or null if no clips have been played
    ///     since the application started. Used by the !replay command to re-enqueue the last clip.
    /// </value>
    ClipData? LastPlayed { get; }

    /// <summary>
    ///     Adds a clip to the end of the playback queue.
    /// </summary>
    /// <param name="clip">The clip to enqueue for playback</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="clip" /> is null</exception>
    void Enqueue(ClipData clip);

    /// <summary>
    ///     Removes and returns the clip at the front of the queue.
    /// </summary>
    /// <returns>
    ///     The next <see cref="ClipData" /> to be played, or null if the queue is empty.
    /// </returns>
    ClipData? Dequeue();

    /// <summary>
    ///     Returns the clip at the front of the queue without removing it.
    /// </summary>
    /// <returns>
    ///     The next <see cref="ClipData" /> to be played, or null if the queue is empty.
    /// </returns>
    ClipData? Peek();

    /// <summary>
    ///     Removes all clips from the queue.
    /// </summary>
    /// <remarks>
    ///     This operation does not affect the <see cref="LastPlayed" /> clip tracking.
    /// </remarks>
    void Clear();

    /// <summary>
    ///     Updates the last played clip reference for replay functionality.
    /// </summary>
    /// <param name="clip">The clip that was just played</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="clip" /> is null</exception>
    /// <remarks>
    ///     This method is called by <see cref="PlaybackEngine" /> after a clip successfully plays
    ///     to completion. It enables the !replay command to re-enqueue the same clip.
    /// </remarks>
    void SetLastPlayed(ClipData clip);
}