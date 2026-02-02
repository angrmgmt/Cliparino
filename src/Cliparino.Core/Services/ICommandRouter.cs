using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

/// <summary>
///     Defines the contract for parsing and executing chat commands from Twitch messages.
/// </summary>
/// <remarks>
///     <para>
///         This interface is implemented by <see cref="CommandRouter" /> and is used by
///         <see cref="TwitchEventCoordinator" /> to process incoming chat messages from EventSub or IRC.
///     </para>
///     <para>
///         Key responsibilities:
///         - Parse raw chat messages into strongly-typed command objects
///         - Route commands to appropriate execution handlers
///         - Orchestrate command execution with dependencies (Twitch API, approval service, playback engine)
///         - Handle approval workflows for risky operations (e.g., clip search)
///     </para>
///     <para>
///         Supported commands:
///         - <c>!watch &lt;url&gt;</c> - Play clip by URL/ID
///         - <c>!watch @broadcaster search terms</c> - Search and play clip (requires approval)
///         - <c>!stop</c> - Stop current playback
///         - <c>!replay</c> - Replay last clip
///         - <c>!so @username</c> / <c>!shoutout @username</c> - Perform shoutout with random clip
///     </para>
///     <para>
///         Thread-safety: All methods are async and thread-safe. Multiple messages can be processed
///         concurrently, though approval workflows maintain internal state for pending requests.
///     </para>
/// </remarks>
public interface ICommandRouter {
    /// <summary>
    ///     Parses a chat message to extract a command, if present.
    /// </summary>
    /// <param name="message">The chat message to parse</param>
    /// <returns>
    ///     A <see cref="ChatCommand" /> if the message starts with a recognized command prefix (!),
    ///     or null if the message is not a command or the command is not recognized.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         This method performs pattern matching against known command formats. It does not execute
    ///         the command, validate parameters against external systems, or check permissions. Those
    ///         operations occur during <see cref="ExecuteCommandAsync" />.
    ///     </para>
    ///     <para>
    ///         Commands are case-insensitive. For example, <c>!WATCH</c>, <c>!Watch</c>, and <c>!watch</c>
    ///         are all recognized.
    ///     </para>
    /// </remarks>
    ChatCommand? ParseCommand(ChatMessage message);

    /// <summary>
    ///     Executes a parsed command asynchronously.
    /// </summary>
    /// <param name="command">The command to execute</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>A task representing the async command execution</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="command" /> is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when canceled via <paramref name="cancellationToken" /></exception>
    /// <remarks>
    ///     <para>
    ///         This method routes the command to the appropriate handler based on the command type:
    ///         - <see cref="WatchClipCommand" /> → Fetch clip from Twitch API → Enqueue in playback engine
    ///         - <see cref="WatchSearchCommand" /> → Search clips → Request approval → Enqueue
    ///         - <see cref="StopCommand" /> → Stop playback engine
    ///         - <see cref="ReplayCommand" /> → Replay last clip via playback engine
    ///         - <see cref="ShoutoutCommand" /> → Execute shoutout via <see cref="IShoutoutService" />
    ///     </para>
    ///     <para>
    ///         Errors during execution (e.g., clip not found, API failures) are logged but do not throw
    ///         exceptions to the caller. This prevents one failed command from disrupting the event stream.
    ///     </para>
    /// </remarks>
    Task ExecuteCommandAsync(ChatCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Processes a chat message, handling both approval responses and new commands.
    /// </summary>
    /// <param name="message">The chat message to process</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>A task representing the async processing operation</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message" /> is null</exception>
    /// <exception cref="OperationCanceledException">Thrown when canceled via <paramref name="cancellationToken" /></exception>
    /// <remarks>
    ///     <para>
    ///         This is the primary entry point for chat message processing. It performs two operations:
    ///         1. Checks if the message is a response to a pending approval request (via <see cref="IApprovalService" />)
    ///         2. If not an approval response, parses and executes the message as a command
    ///     </para>
    ///     <para>
    ///         This two-phase approach allows moderators to approve or deny clip search requests by simply
    ///         responding with "yes" or "no" in chat, without requiring special command syntax.
    ///     </para>
    /// </remarks>
    Task ProcessChatMessageAsync(ChatMessage message, CancellationToken cancellationToken = default);
}