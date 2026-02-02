namespace Cliparino.Core.Models;

/// <summary>
///     Abstract base record for all Twitch events received from EventSub WebSocket or IRC.
/// </summary>
/// <remarks>
///     <para>
///         This record hierarchy represents the various events that Cliparino subscribes to and processes.
///         Events are created by <see cref="Services.TwitchEventSubWebSocketSource" /> or
///         <see cref="Services.TwitchIrcEventSource" /> and dispatched by <see cref="Services.TwitchEventCoordinator" />
///         to the appropriate handlers (primarily <see cref="Services.ICommandRouter" />).
///     </para>
///     <para>
///         Thread-safe due to immutability. Events can be safely passed between event sources,
///         coordinator, and handlers without synchronization.
///     </para>
/// </remarks>
public abstract record TwitchEvent;

/// <summary>
///     Event representing a chat message received from Twitch.
/// </summary>
/// <remarks>
///     <para>
///         This event is triggered whenever a user posts a message in the monitored Twitch channel.
///         It is the primary event for command parsing and execution.
///     </para>
///     <para>
///         Source: EventSub <c>channel.chat.message</c> subscription or IRC PRIVMSG.
///     </para>
/// </remarks>
/// <param name="Message">The chat message data including sender info, content, and permissions</param>
public record ChatMessageEvent(ChatMessage Message) : TwitchEvent;

/// <summary>
///     Event representing a raid on the monitored channel.
/// </summary>
/// <remarks>
///     <para>
///         This event is triggered when another broadcaster raids the monitored channel. It can be
///         configured to automatically trigger a shoutout command for the raider.
///     </para>
///     <para>
///         Source: EventSub <c>channel.raid</c> subscription.
///     </para>
/// </remarks>
/// <param name="RaiderUsername">The username of the broadcaster who initiated the raid</param>
/// <param name="RaiderId">The unique Twitch user ID of the raider</param>
/// <param name="ViewerCount">The number of viewers who participated in the raid</param>
public record RaidEvent(string RaiderUsername, string RaiderId, int ViewerCount) : TwitchEvent;