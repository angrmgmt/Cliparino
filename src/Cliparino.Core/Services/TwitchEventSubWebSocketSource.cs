using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

/// <summary>
///     Implements Twitch EventSub WebSocket connection for receiving real-time events.
/// </summary>
/// <remarks>
///     <para>
///         This class implements <see cref="ITwitchEventSource" /> using Twitch's EventSub WebSocket transport.
///         EventSub is Twitch's modern event system that replaces PubSub and provides more reliable event delivery.
///     </para>
///     <para>
///         <strong>EventSub WebSocket Flow:</strong><br />
///         1. Connect to wss://eventsub.wss.twitch.tv/ws<br />
///         2. Receive session_welcome with session_id<br />
///         3. Subscribe to events (channel.chat.message, channel.raid) using session_id<br />
///         4. Receive notification messages with event payloads<br />
///         5. Handle keepalive messages to maintain connection<br />
///         6. Handle reconnect messages to migrate to new session
///     </para>
///     <para>
///         <strong>Message Types:</strong><br />
///         - session_welcome: Initial connection handshake<br />
///         - notification: Event payload (chat message, raid, etc.)<br />
///         - session_keepalive: Heartbeat to maintain connection<br />
///         - session_reconnect: Server requests migration to new endpoint
///     </para>
///     <para>
///         Dependencies:
///         - <see cref="ITwitchAuthStore" /> - OAuth token for subscription authentication
///         - <see cref="IConfiguration" /> - Broadcaster user ID configuration
///         - <see cref="ILogger{TCategoryName}" /> - Structured logging
///     </para>
///     <para>
///         Thread-safety: Uses Channel for thread-safe event queuing. WebSocket messages are
///         received on a background task and queued for consumption.
///     </para>
///     <para>
///         Lifecycle: Registered as a singleton. Implements IAsyncDisposable for cleanup.
///     </para>
/// </remarks>
public class TwitchEventSubWebSocketSource : ITwitchEventSource {
    private readonly ITwitchAuthStore _authStore;
    private readonly IConfiguration _configuration;
    private readonly Channel<TwitchEvent> _eventChannel;
    private readonly ILogger<TwitchEventSubWebSocketSource> _logger;
    private CancellationTokenSource? _connectionCts;
    private string? _sessionId;

    private ClientWebSocket? _webSocket;

    public TwitchEventSubWebSocketSource(ITwitchAuthStore authStore,
        IConfiguration configuration,
        ILogger<TwitchEventSubWebSocketSource> logger) {
        _authStore = authStore;
        _configuration = configuration;
        _logger = logger;
        _eventChannel = Channel.CreateUnbounded<TwitchEvent>();
    }

    public bool IsConnected => _webSocket?.State == WebSocketState.Open;
    public string SourceName => "EventSub WebSocket";

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default) {
        if (IsConnected) {
            _logger.LogWarning("EventSub WebSocket already connected");

            return;
        }

        var token = await _authStore.GetAccessTokenAsync();

        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("Cannot connect to EventSub: No access token available");

        _webSocket = new ClientWebSocket();
        _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _logger.LogInformation("Connecting to Twitch EventSub WebSocket...");

        await _webSocket.ConnectAsync(new Uri("wss://eventsub.wss.twitch.tv/ws"),
            cancellationToken);

        _logger.LogInformation("EventSub WebSocket connected, waiting for welcome message");

        _ = Task.Run(() => ReceiveMessagesAsync(_connectionCts.Token), _connectionCts.Token);
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken cancellationToken = default) {
        if (_webSocket == null) return;

        if (_connectionCts != null) {
            await _connectionCts.CancelAsync();
            _connectionCts.Dispose();
            _connectionCts = null;
        }

        if (_webSocket.State == WebSocketState.Open)
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", cancellationToken);

        _webSocket.Dispose();
        _webSocket = null;
        _sessionId = null;

        _logger.LogInformation("EventSub WebSocket disconnected");
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TwitchEvent> StreamEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        await foreach (var evt in _eventChannel.Reader.ReadAllAsync(cancellationToken)) yield return evt;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() {
        await DisconnectAsync();
        GC.SuppressFinalize(this);
    }

    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken) {
        var buffer = new byte[4096];
        var messageBuilder = new StringBuilder();

        try {
            while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open) {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close) {
                    _logger.LogWarning("EventSub WebSocket closed by server");

                    break;
                }

                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (result.EndOfMessage) {
                    var message = messageBuilder.ToString();
                    messageBuilder.Clear();

                    await ProcessMessageAsync(message, cancellationToken);
                }
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            _logger.LogError(ex, "Error receiving EventSub messages");
        }
    }

    private async Task ProcessMessageAsync(string message, CancellationToken cancellationToken) {
        try {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            var metadata = root.GetProperty("metadata");
            var messageType = metadata.GetProperty("message_type").GetString();

            switch (messageType) {
                case "session_welcome":
                    await HandleWelcomeAsync(root, cancellationToken);

                    break;

                case "notification":
                    await HandleNotificationAsync(root);

                    break;

                case "session_keepalive":
                    _logger.LogDebug("EventSub keepalive received");

                    break;

                case "session_reconnect":
                    _logger.LogWarning("EventSub reconnect requested");

                    break;

                default:
                    _logger.LogWarning("Unknown EventSub message type: {MessageType}", messageType);

                    break;
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "Error processing EventSub message: {Message}", message);

            // Subscription failures (403 Forbidden) should trigger disconnect and IRC fallback
            // Rethrow to signal connection failure to the coordinator
            throw;
        }
    }

    private async Task HandleWelcomeAsync(JsonElement root, CancellationToken cancellationToken) {
        var payload = root.GetProperty("payload");
        var session = payload.GetProperty("session");
        _sessionId = session.GetProperty("id").GetString();

        _logger.LogInformation("EventSub session established: {SessionId}", _sessionId);

        await SubscribeToEventsAsync(cancellationToken);
    }

    private async Task SubscribeToEventsAsync(CancellationToken cancellationToken) {
        var token = await _authStore.GetAccessTokenAsync();
        var clientId = _configuration["Twitch:ClientId"];

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
        httpClient.DefaultRequestHeaders.Add("Client-Id", clientId);

        var userId = await GetUserIdAsync(httpClient, cancellationToken);

        await SubscribeToChannelChatMessageAsync(httpClient, userId, cancellationToken);
        await SubscribeToChannelRaidAsync(httpClient, userId, cancellationToken);

        _logger.LogInformation("EventSub subscriptions created successfully");
    }

    private async Task<string> GetUserIdAsync(HttpClient httpClient, CancellationToken cancellationToken) {
        var response = await httpClient.GetAsync("https://api.twitch.tv/helix/users", cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.GetProperty("data")[0].GetProperty("id").GetString()!;
    }

    private async Task SubscribeToChannelChatMessageAsync(HttpClient httpClient, string userId,
        CancellationToken cancellationToken) {
        var subscriptionPayload = new {
            type = "channel.chat.message",
            version = "1",
            condition = new { broadcaster_user_id = userId, user_id = userId },
            transport = new { method = "websocket", session_id = _sessionId }
        };

        var content = new StringContent(JsonSerializer.Serialize(subscriptionPayload),
            Encoding.UTF8,
            "application/json");

        var response = await httpClient.PostAsync("https://api.twitch.tv/helix/eventsub/subscriptions",
            content,
            cancellationToken);

        if (!response.IsSuccessStatusCode) {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to subscribe to channel.chat.message: {Error}", error);

            throw new Exception($"EventSub subscription failed: {error}");
        }

        _logger.LogInformation("Subscribed to channel.chat.message events");
    }

    private async Task SubscribeToChannelRaidAsync(HttpClient httpClient, string userId,
        CancellationToken cancellationToken) {
        var subscriptionPayload = new {
            type = "channel.raid",
            version = "1",
            condition = new { to_broadcaster_user_id = userId },
            transport = new { method = "websocket", session_id = _sessionId }
        };

        var content = new StringContent(JsonSerializer.Serialize(subscriptionPayload),
            Encoding.UTF8,
            "application/json");

        var response = await httpClient.PostAsync("https://api.twitch.tv/helix/eventsub/subscriptions",
            content,
            cancellationToken);

        if (!response.IsSuccessStatusCode) {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Failed to subscribe to channel.raid (non-critical): {Error}", error);
        } else {
            _logger.LogInformation("Subscribed to channel.raid events");
        }
    }

    private async Task HandleNotificationAsync(JsonElement root) {
        var payload = root.GetProperty("payload");
        var subscription = payload.GetProperty("subscription");
        var subscriptionType = subscription.GetProperty("type").GetString();
        var eventData = payload.GetProperty("event");

        switch (subscriptionType) {
            case "channel.chat.message":
                HandleChatMessage(eventData);

                break;

            case "channel.raid":
                HandleRaid(eventData);

                break;

            default:
                _logger.LogDebug("Unhandled EventSub notification type: {Type}", subscriptionType);

                break;
        }

        await Task.CompletedTask;
    }

    private void HandleChatMessage(JsonElement eventData) {
        var username = eventData.GetProperty("chatter_user_login").GetString()!;
        var displayName = eventData.GetProperty("chatter_user_name").GetString()!;
        var userId = eventData.GetProperty("chatter_user_id").GetString()!;
        var channelId = eventData.GetProperty("broadcaster_user_id").GetString()!;
        var channel = eventData.GetProperty("broadcaster_user_login").GetString()!;
        var messageText = eventData.GetProperty("message").GetProperty("text").GetString()!;

        var badges = new HashSet<string>();

        if (eventData.TryGetProperty("badges", out var badgesArray))
            foreach (var badge in badgesArray.EnumerateArray()) {
                var setId = badge.GetProperty("set_id").GetString();
                if (setId != null) badges.Add(setId);
            }

        var chatMessage = new ChatMessage(username,
            displayName,
            channel,
            userId,
            channelId,
            messageText,
            badges.Contains("moderator"),
            badges.Contains("vip"),
            badges.Contains("broadcaster"),
            badges.Contains("subscriber"));

        _eventChannel.Writer.TryWrite(new ChatMessageEvent(chatMessage));
        _logger.LogDebug("Chat message from {User}: {Message}", displayName, messageText);
    }

    private void HandleRaid(JsonElement eventData) {
        var raiderUsername = eventData.GetProperty("from_broadcaster_user_login").GetString()!;
        var raiderId = eventData.GetProperty("from_broadcaster_user_id").GetString()!;
        var viewerCount = eventData.GetProperty("viewers").GetInt32();

        _eventChannel.Writer.TryWrite(new RaidEvent(raiderUsername, raiderId, viewerCount));
        _logger.LogInformation("Raid from {Raider} with {Viewers} viewers", raiderUsername, viewerCount);
    }
}