using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

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
    private string? _username;
    private StreamWriter? _writer;

    public bool IsConnected => _tcpClient?.Connected ?? false;
    public string SourceName => "IRC";

    public async Task ConnectAsync(CancellationToken cancellationToken = default) {
        if (IsConnected) {
            logger.LogWarning("IRC already connected");

            return;
        }

        var token = await authStore.GetAccessTokenAsync();

        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("Cannot connect to IRC: No access token available");

        _username = await GetUsernameAsync(token, cancellationToken);

        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(IrcServer, IrcPort, cancellationToken);

        var stream = _tcpClient.GetStream();
        _reader = new StreamReader(stream);
        _writer = new StreamWriter(stream) { AutoFlush = true };

        await _writer.WriteLineAsync($"PASS oauth:{token}");
        await _writer.WriteLineAsync($"NICK {_username}");

        await _writer.WriteLineAsync("CAP REQ :twitch.tv/tags twitch.tv/commands");

        await _writer.WriteLineAsync($"JOIN #{_username}");

        _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => ReceiveMessagesAsync(_connectionCts.Token), _connectionCts.Token);

        logger.LogInformation("IRC connected to #{Channel}", _username);
    }

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

    public async IAsyncEnumerable<TwitchEvent> StreamEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    ) {
        await foreach (var evt in _eventChannel.Reader.ReadAllAsync(cancellationToken)) yield return evt;
    }

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

        var badges = new HashSet<string>(
            badgesStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(b => b.Split('/')[0])
        );

        var chatMessage = new ChatMessage(
            username,
            displayName,
            channel,
            userId,
            channelId,
            messageText,
            badges.Contains("moderator"),
            badges.Contains("vip"),
            badges.Contains("broadcaster"),
            badges.Contains("subscriber")
        );

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

    [GeneratedRegex(@":(.+)!.+@.+ PRIVMSG #(\w+) :(.+)")]
    private static partial Regex MyRegex();
}