using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

public interface IApprovalService {
    Task<bool> RequestApprovalAsync(
        ChatMessage requester,
        ClipData clip,
        TimeSpan timeout,
        CancellationToken cancellationToken = default
    );

    Task<bool> ProcessApprovalResponseAsync(ChatMessage response);

    bool IsApprovalRequired(ChatMessage requester);
}