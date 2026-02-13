namespace Cliparino.Core.Models;

/// <summary>
///     Configuration options for chat feedback on command failures.
/// </summary>
public class ChatFeedbackOptions {
    /// <summary>
    ///     Whether to send feedback messages to chat when commands fail.
    ///     Default is true for moderators and broadcaster.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Minimum interval between feedback messages in seconds to prevent spam.
    ///     Default is 10 seconds.
    /// </summary>
    public int RateLimitSeconds { get; set; } = 10;

    /// <summary>
    ///     Whether to send feedback publicly in chat or only log it.
    ///     When false, feedback is logged but not sent to chat.
    /// </summary>
    public bool SendToChat { get; set; } = true;

    /// <summary>
    ///     Whether to include approval status messages in chat.
    /// </summary>
    public bool ShowApprovalStatus { get; set; } = true;
}