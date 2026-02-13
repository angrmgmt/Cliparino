using System.Text.RegularExpressions;
using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

/// <summary>
///     Parses and executes chat commands from Twitch messages, orchestrating clip playback and shoutouts.
/// </summary>
/// <remarks>
///     <para>
///         This class implements <see cref="ICommandRouter" /> and serves as the central dispatcher for all
///         chat-triggered actions in Cliparino. It receives chat messages from <see cref="TwitchEventCoordinator" />,
///         parses them into strongly-typed command objects, and executes them by coordinating with multiple services.
///     </para>
///     <para>
///         <strong>Command parsing strategy:</strong><br />
///         - Uses regex for flexible clip URL matching (multiple Twitch URL formats)
///         - Pattern matching for @username prefix detection in search commands
///         - Case-insensitive command matching (!watch, !WATCH, !Watch all work)
///         - Whitespace-tolerant parsing
///     </para>
///     <para>
///         <strong>Command execution flow:</strong><br />
///         1. <c>!watch &lt;url&gt;</c> → Fetch clip from Twitch API → Enqueue in playback engine<br />
///         2. <c>!watch @user terms</c> → Search clips → Request moderator approval → Enqueue<br />
///         3. <c>!stop</c> → Stop playback engine<br />
///         4. <c>!replay</c> → Replay last clip<br />
///         5. <c>!so @user</c> → Execute shoutout with random clip
///     </para>
///     <para>
///         Dependencies:
///         - <see cref="IPlaybackEngine" /> - Clip playback control
///         - <see cref="ITwitchHelixClient" /> - Twitch API for clip fetching
///         - <see cref="IShoutoutService" /> - Shoutout execution
///         - <see cref="IClipSearchService" /> - Fuzzy clip search
///         - <see cref="IApprovalService" /> - Moderator approval workflow
///         - <see cref="IConfiguration" /> - Application settings (approval timeout, etc.)
///         - <see cref="ILogger{TCategoryName}" /> - Structured logging
///     </para>
///     <para>
///         Thread-safety: All methods are async and stateless (except for injected dependencies). Safe to
///         call from multiple threads concurrently. The approval service maintains internal state for
///         pending approval requests.
///     </para>
///     <para>
///         Lifecycle: Registered as a singleton. A single instance exists for the lifetime of the application.
///     </para>
/// </remarks>
public partial class CommandRouter : ICommandRouter {
    /// <summary>
    ///     Regex pattern for matching Twitch clip URLs and extracting the clip ID (slug).
    ///     Supports both https://clips.twitch.tv/SlugHere and https://www.twitch.tv/broadcaster/clip/SlugHere formats.
    /// </summary>
    private static readonly Regex ClipUrlRegex = MyRegex();

    private readonly IApprovalService _approvalService;
    private readonly IChatFeedbackService _chatFeedback;
    private readonly IClipSearchService _clipSearchService;
    private readonly IConfiguration _configuration;
    private readonly ITwitchHelixClient _helixClient;
    private readonly ILogger<CommandRouter> _logger;
    private readonly IPlaybackEngine _playbackEngine;
    private readonly IShoutoutService _shoutoutService;

    public CommandRouter(IPlaybackEngine playbackEngine,
        ITwitchHelixClient helixClient,
        IShoutoutService shoutoutService,
        IClipSearchService clipSearchService,
        IApprovalService approvalService,
        IChatFeedbackService chatFeedback,
        IConfiguration configuration,
        ILogger<CommandRouter> logger) {
        _playbackEngine = playbackEngine;
        _helixClient = helixClient;
        _shoutoutService = shoutoutService;
        _clipSearchService = clipSearchService;
        _approvalService = approvalService;
        _chatFeedback = chatFeedback;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public ChatCommand? ParseCommand(ChatMessage message) {
        var text = message.Message.TrimStart();

        if (!text.StartsWith('!'))
            return null;

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
            return null;

        var command = parts[0].ToLowerInvariant();

        return command switch {
            "!watch" => ParseWatchCommand(message, parts),
            "!stop" => new StopCommand(message),
            "!replay" => new ReplayCommand(message),
            "!so" or "!shoutout" => ParseShoutoutCommand(message, parts),
            _ => null
        };
    }

    /// <inheritdoc />
    public async Task ExecuteCommandAsync(ChatCommand command, CancellationToken cancellationToken = default) {
        try {
            switch (command) {
                case WatchClipCommand watch:
                    await ExecuteWatchClipAsync(watch, cancellationToken);

                    break;

                case WatchSearchCommand search:
                    await ExecuteWatchSearchAsync(search, cancellationToken);

                    break;

                case StopCommand:
                    await ExecuteStopAsync(cancellationToken);

                    break;

                case ReplayCommand:
                    await ExecuteReplayAsync(cancellationToken);

                    break;

                case ShoutoutCommand shoutout:
                    await ExecuteShoutoutAsync(shoutout, cancellationToken);

                    break;

                default:
                    _logger.LogWarning("Unknown command type: {CommandType}", command.GetType().Name);

                    break;
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "Error executing command: {Command}", command);
        }
    }

    /// <inheritdoc />
    public async Task ProcessChatMessageAsync(ChatMessage message, CancellationToken cancellationToken = default) {
        await _approvalService.ProcessApprovalResponseAsync(message);

        var command = ParseCommand(message);
        if (command != null) await ExecuteCommandAsync(command, cancellationToken);
    }

    private ChatCommand? ParseWatchCommand(ChatMessage message, string[] parts) {
        if (parts.Length < 2)
            return null;

        var clipUrlMatch = ClipUrlRegex.Match(message.Message);

        if (clipUrlMatch.Success) return new WatchClipCommand(message, clipUrlMatch.Groups[1].Value);

        if (parts[1].StartsWith('@')) {
            var broadcasterName = parts[1].TrimStart('@');
            var searchTerms = string.Join(' ', parts.Skip(2));

            return string.IsNullOrWhiteSpace(searchTerms)
                ? null
                : new WatchSearchCommand(message, broadcasterName, searchTerms);
        }

        return new WatchClipCommand(message, parts[1]);
    }

    private static ChatCommand? ParseShoutoutCommand(ChatMessage message, string[] parts) {
        if (parts.Length < 2)
            return null;

        var targetUsername = parts[1].TrimStart('@');

        return new ShoutoutCommand(message, targetUsername);
    }

    private async Task ExecuteWatchClipAsync(WatchClipCommand command, CancellationToken cancellationToken) {
        _logger.LogInformation("Executing watch clip command: {ClipId} (requested by {User})",
            command.ClipIdentifier, command.Source.DisplayName);

        var clipData = await _helixClient.GetClipByIdAsync(command.ClipIdentifier) ??
                       await _helixClient.GetClipByUrlAsync(command.ClipIdentifier);

        if (clipData == null) {
            _logger.LogWarning("Clip not found: {ClipId}", command.ClipIdentifier);
            await _chatFeedback.SendClipNotFoundFeedbackAsync(command.Source.DisplayName, command.ClipIdentifier);

            return;
        }

        await _playbackEngine.PlayClipAsync(clipData, cancellationToken);
        _logger.LogInformation("Clip enqueued: {ClipTitle} by {Broadcaster}",
            clipData.Title, clipData.Broadcaster.DisplayName);
    }

    private async Task ExecuteWatchSearchAsync(WatchSearchCommand command, CancellationToken cancellationToken) {
        _logger.LogInformation("Executing watch search command: @{Broadcaster} {SearchTerms} (requested by {User})",
            command.BroadcasterName, command.SearchTerms, command.Source.DisplayName);

        var clip = await _clipSearchService.SearchClipAsync(command.BroadcasterName,
            command.SearchTerms,
            cancellationToken);

        if (clip == null) {
            _logger.LogWarning("No clips found for search: @{Broadcaster} {SearchTerms}",
                command.BroadcasterName, command.SearchTerms);
            await _chatFeedback.SendSearchNoResultsFeedbackAsync(command.Source.DisplayName,
                command.BroadcasterName,
                command.SearchTerms);

            return;
        }

        if (_approvalService.IsApprovalRequired(command.Source)) {
            _logger.LogInformation("Approval required for clip search by {User}", command.Source.DisplayName);

            await _chatFeedback.SendSearchAwaitingApprovalAsync(command.Source.DisplayName,
                command.BroadcasterName,
                command.SearchTerms);

            var approvalTimeout = TimeSpan.FromSeconds(_configuration.GetValue("ClipSearch:ApprovalTimeoutSeconds",
                30));

            var approved = await _approvalService.RequestApprovalAsync(command.Source,
                clip,
                approvalTimeout,
                cancellationToken);

            if (!approved) {
                _logger.LogInformation("Clip search request denied or timed out for {User}",
                    command.Source.DisplayName);
                await _chatFeedback.SendApprovalTimeoutFeedbackAsync(command.Source.DisplayName);

                return;
            }
        }

        await _playbackEngine.PlayClipAsync(clip, cancellationToken);
        _logger.LogInformation("Clip enqueued from search: {ClipTitle} by {Broadcaster}",
            clip.Title, clip.Broadcaster.DisplayName);
    }

    private async Task ExecuteStopAsync(CancellationToken cancellationToken) {
        _logger.LogInformation("Executing stop command");
        await _playbackEngine.StopPlaybackAsync(cancellationToken);
    }

    private async Task ExecuteReplayAsync(CancellationToken cancellationToken) {
        _logger.LogInformation("Executing replay command");
        await _playbackEngine.ReplayAsync(cancellationToken);
    }

    private async Task ExecuteShoutoutAsync(ShoutoutCommand command, CancellationToken cancellationToken) {
        _logger.LogInformation("Executing shoutout command for @{TargetUser} (requested by {User})",
            command.TargetUsername, command.Source.DisplayName);

        var sourceBroadcasterId = await _helixClient.GetAuthenticatedUserIdAsync();

        if (string.IsNullOrEmpty(sourceBroadcasterId)) {
            _logger.LogError("Could not get authenticated user ID for shoutout");

            return;
        }

        var success = await _shoutoutService.ExecuteShoutoutAsync(sourceBroadcasterId,
            command.TargetUsername,
            cancellationToken);

        if (!success) {
            _logger.LogWarning("Shoutout execution failed for @{TargetUser}", command.TargetUsername);
            await _chatFeedback.SendShoutoutNoClipsFeedbackAsync(command.Source.DisplayName, command.TargetUsername);
        }
    }

    [GeneratedRegex(@"(?:https?://)?(?:www\.)?(?:clips\.twitch\.tv/|twitch\.tv/\w+/clip/)([a-zA-Z0-9_-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-US")]
    private static partial Regex MyRegex();
}