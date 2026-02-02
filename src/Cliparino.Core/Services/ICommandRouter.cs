using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

public interface ICommandRouter {
    ChatCommand? ParseCommand(ChatMessage message);

    Task ExecuteCommandAsync(ChatCommand command, CancellationToken cancellationToken = default);

    Task ProcessChatMessageAsync(ChatMessage message, CancellationToken cancellationToken = default);
}