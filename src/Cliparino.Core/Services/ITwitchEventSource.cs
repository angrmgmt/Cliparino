using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

/// <summary>
///     Defines the contract for streaming Twitch events from a connection source (EventSub WebSocket or IRC).
/// </summary>
/// <remarks>
///     <para>
///         This interface is implemented by <see cref="TwitchEventSubWebSocketSource" /> and
///         <see cref="TwitchIrcEventSource" /> and is used by <see cref="TwitchEventCoordinator" />
///         to receive events from Twitch with automatic failover between sources.
///     </para>
///     <para>
///         Key responsibilities:
///         - Establish and maintain connection to Twitch event source
///         - Stream events asynchronously as they arrive
///         - Handle connection lifecycle (connect, disconnect, reconnect)
///         - Report connection status for health monitoring
///         - Clean up resources on disposal
///     </para>
///     <para>
///         <strong>Event Source Hierarchy:</strong><br />
///         Primary: <see cref="TwitchEventSubWebSocketSource" /> (EventSub WebSocket)<br />
///         Fallback: <see cref="TwitchIrcEventSource" /> (IRC PRIVMSG)<br />
///     </para>
///     <para>
///         <strong>Event Streaming Pattern:</strong><br />
///         Events are streamed via <see cref="StreamEventsAsync" /> using IAsyncEnumerable.
///         The coordinator consumes events with <c>await foreach</c> and dispatches them
///         to the command router. If the stream terminates (error, disconnection), the
///         coordinator automatically fails over to the backup source.
///     </para>
///     <para>
///         Thread-safety: All methods are async. StreamEventsAsync produces events on a single
///         thread; consumers may process events concurrently.
///     </para>
/// </remarks>
public interface ITwitchEventSource : IAsyncDisposable {
    /// <summary>
    ///     Gets a value indicating whether the event source is currently connected and streaming events.
    /// </summary>
    /// <value>
    ///     True if connected and ready to stream events; false if disconnected or connecting.
    /// </value>
    bool IsConnected { get; }

    /// <summary>
    ///     Gets the descriptive name of this event source for logging and diagnostics.
    /// </summary>
    /// <value>
    ///     A human-readable name like "EventSub WebSocket" or "IRC" identifying the source type.
    /// </value>
    string SourceName { get; }

    /// <summary>
    ///     Streams Twitch events asynchronously as they arrive from the connection.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to stop streaming events</param>
    /// <returns>
    ///     An async enumerable stream of <see cref="TwitchEvent" /> instances. The stream continues
    ///     until disconnection, error, or cancellation.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         This method uses the async stream pattern (IAsyncEnumerable). Consumers typically
    ///         use <c>await foreach</c> to process events:
    ///         <code>
    /// await foreach (var twitchEvent in source.StreamEventsAsync(cancellationToken)) {
    ///     // Process event
    /// }
    /// </code>
    ///     </para>
    ///     <para>
    ///         The stream terminates when:
    ///         - The connection is lost or closed
    ///         - An unrecoverable error occurs
    ///         - The <paramref name="cancellationToken" /> is cancelled
    ///         - <see cref="DisconnectAsync" /> is called
    ///     </para>
    ///     <para>
    ///         Event types produced:
    ///         - <see cref="ChatMessageEvent" /> - User posted a chat message
    ///         - <see cref="RaidEvent" /> - Channel received a raid
    ///     </para>
    ///     <para>
    ///         Errors during event streaming are typically logged and may terminate the stream,
    ///         triggering failover to the backup event source in <see cref="TwitchEventCoordinator" />.
    ///     </para>
    /// </remarks>
    IAsyncEnumerable<TwitchEvent> StreamEventsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Establishes a connection to the Twitch event source.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the connection attempt</param>
    /// <returns>A task representing the async connection operation</returns>
    /// <exception cref="OperationCanceledException">
    ///     Thrown when cancelled via <paramref name="cancellationToken" />
    /// </exception>
    /// <remarks>
    ///     <para>
    ///         This method initiates the connection process but does not wait for events to arrive.
    ///         After connecting successfully, <see cref="IsConnected" /> will be true and
    ///         <see cref="StreamEventsAsync" /> can be called to begin receiving events.
    ///     </para>
    ///     <para>
    ///         Connection process varies by implementation:
    ///         - EventSub: Open WebSocket, authenticate, subscribe to events
    ///         - IRC: Connect to IRC server, authenticate, join channel
    ///     </para>
    ///     <para>
    ///         If connection fails, implementations should log the error and leave <see cref="IsConnected" />
    ///         as false. The coordinator will retry with exponential backoff or failover to the backup source.
    ///     </para>
    /// </remarks>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Closes the connection to the Twitch event source gracefully.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the disconnection operation</param>
    /// <returns>A task representing the async disconnection operation</returns>
    /// <exception cref="OperationCanceledException">
    ///     Thrown when cancelled via <paramref name="cancellationToken" />
    /// </exception>
    /// <remarks>
    ///     <para>
    ///         This method terminates the event stream and closes the underlying connection.
    ///         After disconnecting, <see cref="IsConnected" /> will be false and
    ///         <see cref="StreamEventsAsync" /> will complete (stop yielding events).
    ///     </para>
    ///     <para>
    ///         Graceful shutdown includes:
    ///         - Sending goodbye/quit messages to the server (if applicable)
    ///         - Flushing pending events
    ///         - Closing network connections
    ///         - Cleaning up subscriptions
    ///     </para>
    ///     <para>
    ///         Calling this method when already disconnected has no effect.
    ///     </para>
    /// </remarks>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}