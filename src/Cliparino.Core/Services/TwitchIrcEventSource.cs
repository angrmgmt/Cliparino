using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

/// <summary>
///     Provides an IRC-based event source for Twitch chat messages and notifications.
/// </summary>
/// <remarks>
///     <para>Used as a fallback mechanism when the primary EventSub WebSocket connection is unavailable or fails.</para>
///     <para>
///         Dependencies: <see cref="ITwitchAuthStore" /> for access tokens and
///         <see cref="ILogger{TwitchIrcEventSource}" /> for logging.
///     </para>
///     <para>
///         Thread-safety: Internal state is managed via <see cref="CancellationTokenSource" /> and a
///         <see cref="Channel{TwitchEvent}" /> to ensure safe event streaming.
///     </para>
///     <para>Lifecycle: Managed by <see cref="TwitchEventCoordinator" /> as an <see cref="ITwitchEventSource" />.</para>
/// </remarks>
public partial class TwitchIrcEventSource(
    ITwitchAuthStore authStore,
    ILogger<TwitchIrcEventSource> logger)
    : ITwitchEventSource {
    private const string IrcServer = "irc.chat.twitch.tv";
    private const int IrcPort = 6667;
    private readonly Channel<TwitchEvent> _eventChannel = Channel.CreateUnbounded<TwitchEvent>();
    private CancellationTokenSource? _connectionCts;
    private StreamReader? _reader;

    private TcpClient? _tcpClient;
    private StreamWriter? _writer;

    /// <summary>
    ///     Gets the username of the currently authenticated user, or null if not connected.
    /// </summary>
    public string? Username { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether the IRC client is currently connected.
    /// </summary>
    public bool IsConnected => _tcpClient?.Connected ?? false;

    /// <summary>
    ///     Gets the name of the event source.
    /// </summary>
    public string SourceName => "IRC";

    /// <summary>
    ///     Establishes a connection to the Twitch IRC server and joins the broadcaster's channel.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no access token is available.</exception>
    public async Task ConnectAsync(CancellationToken cancellationToken = default) {
        if (IsConnected) {
            logger.LogWarning("IRC already connected");

            return;
        }

        var token = await authStore.GetAccessTokenAsync();

        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("Cannot connect to IRC: No access token available");

        Username = await GetUsernameAsync(token, cancellationToken);

        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(IrcServer, IrcPort, cancellationToken);

        var stream = _tcpClient.GetStream();
        _reader = new StreamReader(stream);
        _writer = new StreamWriter(stream) { AutoFlush = true };

        await _writer.WriteLineAsync($"PASS oauth:{token}");
        await _writer.WriteLineAsync($"NICK {Username}");

        await _writer.WriteLineAsync("CAP REQ :twitch.tv/tags twitch.tv/commands");

        await _writer.WriteLineAsync($"JOIN #{Username}");

        _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => ReceiveMessagesAsync(_connectionCts.Token), _connectionCts.Token);

        logger.LogInformation("IRC connected to #{Channel}", Username);
    }

    /// <summary>
    ///     Disconnects from the Twitch IRC server and cleans up resources.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default) {
        if (_connectionCts != null) {
            await _connectionCts.CancelAsync();
            _connectionCts.Dispose();
            _connectionCts = null;
        }

        if (_writer != null) {
            if (_tcpClient?.Connected == true) await _writer.WriteLineAsync("QUIT");

            await _writer.DisposeAsync();
            _writer = null;
        }

        if (_reader != null) {
            _reader.Dispose();
            _reader = null;
        }

        if (_tcpClient != null) {
            _tcpClient.Dispose();
            _tcpClient = null;
        }

        logger.LogInformation("IRC disconnected");
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
        try {
            while (!cancellationToken.IsCancellationRequested && _reader != null) {
                var line = await _reader.ReadLineAsync(cancellationToken);

                if (line == null) break;

                if (line.StartsWith("PING")) {
                    var pongResponse = line.Replace("PING", "PONG");
                    await _writer!.WriteLineAsync(pongResponse);
                    logger.LogDebug("IRC PONG sent");

                    continue;
                }

                ProcessIrcMessage(line);
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            logger.LogError(ex, "Error receiving IRC messages");
        }
    }

    private void ProcessIrcMessage(string message) {
        try {
            if (message.Contains("PRIVMSG"))
                HandlePrivMsg(message);
            else if (message.Contains("USERNOTICE")) HandleUserNotice(message);
        } catch (Exception ex) {
            logger.LogError(ex, "Error processing IRC message: {Message}", message);
        }
    }

    private void HandlePrivMsg(string message) {
        var match = MyRegex().Match(message);

        if (!match.Success) return;

        var tags = ParseTags(message);

        var username = match.Groups[1].Value;
        var channel = match.Groups[2].Value;

        if (string.IsNullOrEmpty(username)) username = channel;
        if (string.IsNullOrEmpty(channel)) channel = username;

        var messageText = match.Groups[3].Value;

        var displayName = tags.GetValueOrDefault("display-name", username);
        var userId = tags.GetValueOrDefault("user-id", "");
        var badgesStr = tags.GetValueOrDefault("badges", "");
        var channelId = tags.GetValueOrDefault("room-id", "");

        var badges = new HashSet<string>(badgesStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(b => b.Split('/')[0]));

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
        logger.LogDebug("IRC chat message from {User}: {Message}", displayName, messageText);
    }

    private void HandleUserNotice(string message) {
        var tags = ParseTags(message);
        var msgId = tags.GetValueOrDefault("msg-id", "");

        if (msgId != "raid") return;

        var raiderUsername = tags.GetValueOrDefault("login", "");
        var raiderId = tags.GetValueOrDefault("user-id", "");
        var viewerCountStr = tags.GetValueOrDefault("msg-param-viewerCount", "0");

        if (!int.TryParse(viewerCountStr, out var viewerCount)) return;

        _eventChannel.Writer.TryWrite(new RaidEvent(raiderUsername, raiderId, viewerCount));
        logger.LogInformation("IRC raid from {Raider} with {Viewers} viewers", raiderUsername, viewerCount);
    }

    private static Dictionary<string, string> ParseTags(string message) {
        var tags = new Dictionary<string, string>();

        if (!message.StartsWith('@')) return tags;

        var tagsEnd = message.IndexOf(' ');

        if (tagsEnd == -1) return tags;

        var tagsString = message[1..tagsEnd];
        var tagPairs = tagsString.Split(';');

        foreach (var tagPair in tagPairs) {
            var parts = tagPair.Split('=', 2);
            if (parts.Length == 2) tags[parts[0]] = parts[1];
        }

        return tags;
    }

    private static async Task<string> GetUsernameAsync(string token, CancellationToken cancellationToken) {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var response = await httpClient.GetAsync("https://id.twitch.tv/oauth2/validate", cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.GetProperty("login").GetString()!;
    }

    /// <summary>
    ///     Sends a chat message to the specified channel.
    /// </summary>
    /// <param name="channel">The channel to send the message to (without the # prefix).</param>
    /// <param name="message">The message to send.</param>
    /// <returns>True if the message was sent successfully, false otherwise.</returns>
    public async Task<bool> SendMessageAsync(string channel, string message) {
        if (_writer == null || !IsConnected) {
            logger.LogWarning("Cannot send message: IRC not connected");

            return false;
        }

        try {
            await _writer.WriteLineAsync($"PRIVMSG #{channel} :{message}");
            logger.LogDebug("IRC message sent to #{Channel}: {Message}", channel, message);

            return true;
        } catch (Exception ex) {
            logger.LogError(ex, "Failed to send IRC message to #{Channel}", channel);

            return false;
        }
    }

    [GeneratedRegex(@":(.+)!.+@.+ PRIVMSG #(\w+) :(.+)")]
    private static partial Regex MyRegex();
}