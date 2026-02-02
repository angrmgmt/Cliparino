using System.Collections.Concurrent;
using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

/// <summary>
///     Manages moderator approval workflows for clip search operations with timeout and response matching.
/// </summary>
/// <remarks>
///     <para>
///         This class implements <see cref="IApprovalService" /> using a concurrent dictionary to track
///         pending approval requests. Each request has a unique ID and uses TaskCompletionSource for
///         async waiting with timeout support.
///     </para>
///     <para>
///         Response matching: Moderator responses are matched against known approval phrases ("yes", "yep", etc.)
///         and denial phrases ("no", "nope", etc.) using case-insensitive comparison.
///     </para>
///     <para>
///         Dependencies:
///         - <see cref="ITwitchHelixClient" /> - Send approval request messages to chat
///         - <see cref="IConfiguration" /> - Approval settings (timeout, exempt roles)
///         - <see cref="ILogger{TCategoryName}" /> - Structured logging
///     </para>
///     <para>
///         Thread-safety: Uses ConcurrentDictionary for thread-safe pending request tracking.
///     </para>
///     <para>
///         Lifecycle: Registered as a singleton.
///     </para>
/// </remarks>
public class ApprovalService : IApprovalService {
    private readonly IConfiguration _configuration;
    private readonly ITwitchHelixClient _helixClient;
    private readonly ILogger<ApprovalService> _logger;

    private readonly ConcurrentDictionary<string, PendingApproval> _pendingApprovals = new();

    public ApprovalService(
        ITwitchHelixClient helixClient,
        IConfiguration configuration,
        ILogger<ApprovalService> logger
    ) {
        _helixClient = helixClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsApprovalRequired(ChatMessage requester) {
        var requireApproval = _configuration.GetValue("ClipSearch:RequireApproval", true);

        if (!requireApproval)
            return false;

        var exemptRoles = _configuration.GetSection("ClipSearch:ExemptRoles")
            .Get<string[]>() ?? new[] { "broadcaster", "moderator" };

        foreach (var role in exemptRoles)
            switch (role.ToLowerInvariant()) {
                case "broadcaster" when requester.IsBroadcaster:
                case "moderator" when requester.IsModerator:
                case "vip" when requester.IsVip:
                case "subscriber" when requester.IsSubscriber:
                    return false;
            }

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> RequestApprovalAsync(
        ChatMessage requester,
        ClipData clip,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    ) {
        var approvalId = Guid.NewGuid().ToString("N")[..8];
        var tcs = new TaskCompletionSource<bool>();

        var pending = new PendingApproval {
            ApprovalId = approvalId,
            RequestedBy = requester,
            Clip = clip,
            CompletionSource = tcs,
            ExpiresAt = DateTime.UtcNow.Add(timeout)
        };

        _pendingApprovals[approvalId] = pending;

        var broadcasterId = await _helixClient.GetAuthenticatedUserIdAsync();

        if (!string.IsNullOrEmpty(broadcasterId)) {
            var message =
                $"@{pending.RequestedBy.DisplayName} wants to play: \"{pending.Clip.Title}\" ({pending.Clip.DurationSeconds}s). " +
                $"Type !approve {approvalId} or !deny {approvalId}";

            await _helixClient.SendChatMessageAsync(broadcasterId, message);
            _logger.LogInformation(
                "Approval request {ApprovalId} sent for clip '{ClipTitle}' requested by {User}",
                pending.ApprovalId, clip.Title, requester.DisplayName
            );
        }

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try {
            linkedCts.Token.Register(() => tcs.TrySetCanceled());
            var result = await tcs.Task;
            _logger.LogInformation(
                "Approval request {ApprovalId} was {Result}", pending.ApprovalId, result ? "approved" : "denied"
            );

            return result;
        } catch (OperationCanceledException) {
            _logger.LogInformation("Approval request {ApprovalId} timed out", pending.ApprovalId);

            return false;
        } finally {
            _pendingApprovals.TryRemove(approvalId, out _);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ProcessApprovalResponseAsync(ChatMessage response) {
        var text = response.Message.TrimStart();
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
            return false;

        var command = parts[0].ToLowerInvariant();
        var approvalId = parts[1];

        if (command != "!approve" && command != "!deny")
            return false;

        if (!_pendingApprovals.TryGetValue(approvalId, out var pending)) {
            _logger.LogDebug("No pending approval found for ID: {ApprovalId}", approvalId);

            return false;
        }

        if (!IsAuthorizedToApprove(response)) {
            _logger.LogWarning("User {User} attempted to approve/deny but lacks permission", response.DisplayName);

            return false;
        }

        if (DateTime.UtcNow > pending.ExpiresAt) {
            _logger.LogDebug("Approval {ApprovalId} has expired", approvalId);
            _pendingApprovals.TryRemove(approvalId, out _);

            return false;
        }

        var approved = command == "!approve";
        pending.CompletionSource.TrySetResult(approved);

        _logger.LogInformation(
            "User {User} {Action} approval request {ApprovalId}",
            response.DisplayName, approved ? "approved" : "denied", pending.ApprovalId
        );

        return await Task.FromResult(true);
    }

    private bool IsAuthorizedToApprove(ChatMessage message) {
        return message.IsBroadcaster || message.IsModerator;
    }

    private class PendingApproval {
        public required string ApprovalId { get; init; }
        public required ChatMessage RequestedBy { get; init; }
        public required ClipData Clip { get; init; }
        public required TaskCompletionSource<bool> CompletionSource { get; init; }
        public required DateTime ExpiresAt { get; init; }
    }
}