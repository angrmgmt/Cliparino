using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

/// <summary>
///     Defines the contract for managing moderator approval workflows for risky operations.
/// </summary>
/// <remarks>
///     <para>
///         This interface is implemented by <see cref="ApprovalService" /> and is used by
///         <see cref="CommandRouter" /> to gate clip search operations behind moderator approval.
///         This prevents non-moderators from playing arbitrary clips without oversight.
///     </para>
///     <para>
///         Key responsibilities:
///         - Determine if a user requires approval based on their permissions
///         - Manage pending approval requests with timeouts
///         - Process approval/denial responses from moderators
///         - Timeout expired approval requests automatically
///     </para>
///     <para>
///         <strong>Approval workflow:</strong><br />
///         1. User requests clip search via <c>!watch @broadcaster terms</c><br />
///         2. Service checks if user is moderator/broadcaster (via <see cref="IsApprovalRequired" />)<br />
///         3. If approval required, clip is found and approval request is created<br />
///         4. Moderators can respond with "yes", "yep", "sure", etc. to approve<br />
///         5. Moderators can respond with "no", "nope", "nah", etc. to deny<br />
///         6. Request times out after configured duration (default: 30 seconds)
///     </para>
///     <para>
///         Thread-safety: Implementations must be thread-safe as approval requests can be created
///         and responses can arrive concurrently from multiple chat messages.
///     </para>
/// </remarks>
public interface IApprovalService {
    /// <summary>
    ///     Requests moderator approval for a clip, waiting for a response or timeout.
    /// </summary>
    /// <param name="requester">The chat message from the user requesting the clip</param>
    /// <param name="clip">The clip that requires approval</param>
    /// <param name="timeout">The maximum time to wait for moderator response</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>
    ///     A task containing true if a moderator approved the clip, or false if denied or timed out.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         This method blocks until a moderator responds or the timeout elapses. The implementation
    ///         maintains internal state tracking the pending request and matching moderator responses
    ///         via <see cref="ProcessApprovalResponseAsync" />.
    ///     </para>
    ///     <para>
    ///         The method automatically cleans up the pending request when complete (approved, denied, or timed out).
    ///     </para>
    /// </remarks>
    Task<bool> RequestApprovalAsync(
        ChatMessage requester,
        ClipData clip,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Processes a chat message to determine if it's a response to a pending approval request.
    /// </summary>
    /// <param name="response">The chat message that might be an approval response</param>
    /// <returns>
    ///     A task containing true if the message was an approval response (approved or denied),
    ///     or false if the message was not related to any pending approval.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         This method is called by <see cref="CommandRouter.ProcessChatMessageAsync" /> for every
    ///         chat message to check if it's responding to a pending approval request.
    ///     </para>
    ///     <para>
    ///         Only messages from moderators or broadcasters are considered valid approval responses.
    ///         Approval phrases include: "yes", "yep", "yeah", "sure", "okay", "go ahead", etc.
    ///         Denial phrases include: "no", "nope", "nah", "not okay", etc.
    ///     </para>
    /// </remarks>
    Task<bool> ProcessApprovalResponseAsync(ChatMessage response);

    /// <summary>
    ///     Determines if a user requires moderator approval based on their permissions.
    /// </summary>
    /// <param name="requester">The chat message from the user to check</param>
    /// <returns>
    ///     True if the user requires approval (not a moderator/broadcaster), or false if the user
    ///     has sufficient permissions to bypass approval.
    /// </returns>
    /// <remarks>
    ///     Users who are moderators, VIPs, or the broadcaster bypass the approval requirement.
    ///     All other users require approval for clip search operations.
    /// </remarks>
    bool IsApprovalRequired(ChatMessage requester);
}