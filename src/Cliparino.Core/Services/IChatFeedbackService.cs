namespace Cliparino.Core.Services;

/// <summary>
///     Provides rate-limited chat feedback for command execution results.
/// </summary>
/// <remarks>
///     <para>
///         This service handles sending feedback messages to Twitch chat when commands
///         fail or require user action. Messages are rate-limited to prevent spam.
///     </para>
///     <para>
///         Feedback is only sent to broadcaster/moderators via the channel (not whispers).
///         The service respects configuration settings for enabling/disabling feedback.
///     </para>
/// </remarks>
public interface IChatFeedbackService {
    /// <summary>
    ///     Sends feedback about an invalid clip URL/ID.
    /// </summary>
    /// <param name="username">The user who issued the command.</param>
    /// <param name="clipIdentifier">The invalid clip URL or ID.</param>
    Task SendClipNotFoundFeedbackAsync(string username, string clipIdentifier);

    /// <summary>
    ///     Sends feedback about a failed shoutout (no clips found).
    /// </summary>
    /// <param name="username">The user who issued the command.</param>
    /// <param name="targetChannel">The target channel for the shoutout.</param>
    Task SendShoutoutNoClipsFeedbackAsync(string username, string targetChannel);

    /// <summary>
    ///     Sends feedback about a clip search requiring approval.
    /// </summary>
    /// <param name="username">The user who issued the command.</param>
    /// <param name="broadcasterName">The broadcaster being searched.</param>
    /// <param name="searchTerms">The search terms used.</param>
    Task SendSearchAwaitingApprovalAsync(string username, string broadcasterName, string searchTerms);

    /// <summary>
    ///     Sends feedback about a clip search with no results.
    /// </summary>
    /// <param name="username">The user who issued the command.</param>
    /// <param name="broadcasterName">The broadcaster that was searched.</param>
    /// <param name="searchTerms">The search terms used.</param>
    Task SendSearchNoResultsFeedbackAsync(string username, string broadcasterName, string searchTerms);

    /// <summary>
    ///     Sends feedback about an approval timeout.
    /// </summary>
    /// <param name="username">The user whose request timed out.</param>
    Task SendApprovalTimeoutFeedbackAsync(string username);

    /// <summary>
    ///     Sends feedback about a denied approval request.
    /// </summary>
    /// <param name="username">The user whose request was denied.</param>
    Task SendApprovalDeniedFeedbackAsync(string username);

    /// <summary>
    ///     Sends a generic error feedback message.
    /// </summary>
    /// <param name="username">The user who issued the command.</param>
    /// <param name="errorMessage">A user-friendly error message.</param>
    Task SendErrorFeedbackAsync(string username, string errorMessage);
}