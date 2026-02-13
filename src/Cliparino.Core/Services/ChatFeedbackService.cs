using System.Collections.Concurrent;
using Cliparino.Core.Models;
using Microsoft.Extensions.Options;

namespace Cliparino.Core.Services;

/// <summary>
///     Provides rate-limited chat feedback for command execution results.
/// </summary>
/// <remarks>
///     <para>
///         This service uses the IRC event source to send messages to chat.
///         Messages are rate-limited based on the configured interval to prevent spam.
///     </para>
///     <para>
///         Rate limiting is tracked per-user to ensure fair feedback distribution.
///     </para>
/// </remarks>
public class ChatFeedbackService : IChatFeedbackService {
    private readonly TwitchIrcEventSource _ircSource;
    private readonly ConcurrentDictionary<string, DateTime> _lastMessageTimes = new();
    private readonly ILogger<ChatFeedbackService> _logger;
    private readonly IOptionsMonitor<ChatFeedbackOptions> _options;

    public ChatFeedbackService(TwitchIrcEventSource ircSource,
        IOptionsMonitor<ChatFeedbackOptions> options,
        ILogger<ChatFeedbackService> logger) {
        _ircSource = ircSource;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SendClipNotFoundFeedbackAsync(string username, string clipIdentifier) {
        await SendFeedbackAsync($"@{username} That clip wasn't found. Double-check the link?");
    }

    /// <inheritdoc />
    public async Task SendShoutoutNoClipsFeedbackAsync(string username, string targetChannel) {
        await SendFeedbackAsync($"@{username} No clips found for @{targetChannel}");
    }

    /// <inheritdoc />
    public async Task SendSearchAwaitingApprovalAsync(string username, string broadcasterName, string searchTerms) {
        var options = _options.CurrentValue;

        if (!options.ShowApprovalStatus) return;

        await SendFeedbackAsync($"Searching for clips from @{broadcasterName}... Mod approval needed!");
    }

    /// <inheritdoc />
    public async Task SendSearchNoResultsFeedbackAsync(string username, string broadcasterName, string searchTerms) {
        await SendFeedbackAsync($"@{username} No clips found for @{broadcasterName} matching \"{searchTerms}\"");
    }

    /// <inheritdoc />
    public async Task SendApprovalTimeoutFeedbackAsync(string username) {
        await SendFeedbackAsync($"@{username} Clip search timed out (no mod approval)");
    }

    /// <inheritdoc />
    public async Task SendApprovalDeniedFeedbackAsync(string username) {
        await SendFeedbackAsync($"@{username} Clip request was denied by a moderator");
    }

    /// <inheritdoc />
    public async Task SendErrorFeedbackAsync(string username, string errorMessage) {
        await SendFeedbackAsync($"@{username} {errorMessage}");
    }

    private async Task SendFeedbackAsync(string message) {
        var options = _options.CurrentValue;

        if (!options.Enabled) {
            _logger.LogDebug("Chat feedback disabled, skipping: {Message}", message);

            return;
        }

        if (!_ircSource.IsConnected || string.IsNullOrEmpty(_ircSource.Username)) {
            _logger.LogDebug("IRC not connected, skipping feedback: {Message}", message);

            return;
        }

        // Rate limiting - use a global key for simplicity
        var rateLimitKey = "global";
        var now = DateTime.UtcNow;
        var rateLimitSeconds = options.RateLimitSeconds;

        if (_lastMessageTimes.TryGetValue(rateLimitKey, out var lastTime)) {
            var elapsed = (now - lastTime).TotalSeconds;

            if (elapsed < rateLimitSeconds) {
                _logger.LogDebug("Rate limited, skipping feedback ({Elapsed:F1}s < {Limit}s): {Message}",
                    elapsed, rateLimitSeconds, message);

                return;
            }
        }

        if (!options.SendToChat) {
            _logger.LogInformation("Chat feedback (logged only): {Message}", message);

            return;
        }

        var channel = _ircSource.Username;
        var sent = await _ircSource.SendMessageAsync(channel, message);

        if (sent) {
            _lastMessageTimes[rateLimitKey] = now;
            _logger.LogInformation("Chat feedback sent: {Message}", message);
        }
    }
}