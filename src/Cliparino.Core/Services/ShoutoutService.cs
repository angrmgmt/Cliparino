using Cliparino.Core.Models;
using Microsoft.Extensions.Options;

namespace Cliparino.Core.Services;

/// <summary>
///     Executes Twitch shoutouts with random clip playback and configurable filters.
/// </summary>
/// <remarks>
///     <para>
///         This class implements <see cref="IShoutoutService" /> by selecting random clips from a target
///         broadcaster's channel with optional filtering (featured only, max duration, max age) and sending
///         Twitch's native <c>/shoutout</c> command.
///     </para>
///     <para>
///         Clip selection algorithm prioritizes featured clips when UseFeaturedClipsFirst is enabled,
///         then falls back to all clips if no featured clips match the filters.
///     </para>
///     <para>
///         Dependencies:
///         - <see cref="ITwitchHelixClient" /> - Fetch clips and send shoutouts
///         - <see cref="IPlaybackEngine" /> - Enqueue selected clip for playback
///         - <see cref="IConfiguration" /> - Shoutout settings (filters, fallbacks)
///         - <see cref="ILogger{TCategoryName}" /> - Structured logging
///     </para>
///     <para>
///         Lifecycle: Registered as a singleton.
///     </para>
/// </remarks>
public class ShoutoutService : IShoutoutService {
    private readonly ITwitchHelixClient _helixClient;
    private readonly ILogger<ShoutoutService> _logger;
    private readonly IPlaybackEngine _playbackEngine;
    private readonly IOptionsMonitor<ShoutoutOptions> _shoutoutOptions;

    public ShoutoutService(ITwitchHelixClient helixClient,
        IPlaybackEngine playbackEngine,
        IOptionsMonitor<ShoutoutOptions> shoutoutOptions,
        ILogger<ShoutoutService> logger) {
        _helixClient = helixClient;
        _playbackEngine = playbackEngine;
        _shoutoutOptions = shoutoutOptions;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ClipData?> SelectRandomClipAsync(string broadcasterName,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(broadcasterName)) {
            _logger.LogWarning("SelectRandomClipAsync called with empty broadcaster name");

            return null;
        }

        var broadcasterId = await _helixClient.GetBroadcasterIdByNameAsync(broadcasterName);

        if (string.IsNullOrEmpty(broadcasterId)) {
            _logger.LogWarning("Could not find broadcaster ID for: {BroadcasterName}", broadcasterName);

            return null;
        }

        var options = _shoutoutOptions.CurrentValue;
        var useFeaturedFirst = options.UseFeaturedClips;
        var maxLengthSeconds = options.MaxClipLength;
        var maxAgeDays = options.MaxClipAge;

        _logger.LogInformation(
            "Selecting clip for {BroadcasterName} - FeaturedFirst: {FeaturedFirst}, MaxLength: {MaxLength}s, MaxAge: {MaxAge} days",
            broadcasterName, useFeaturedFirst, maxLengthSeconds, maxAgeDays);

        var validPeriods = new[] { 1, 7, 30, 90, 365 };

        foreach (var period in validPeriods.Where(p => p >= maxAgeDays)) {
            var endDate = DateTimeOffset.UtcNow;
            var startDate = endDate.AddDays(-period);

            var clips = await _helixClient.GetClipsByBroadcasterAsync(broadcasterId,
                100,
                startDate,
                endDate);

            if (!clips.Any()) {
                _logger.LogDebug("No clips found for period {Period} days", period);

                continue;
            }

            _logger.LogDebug("Retrieved {Count} clips for period {Period} days", clips.Count, period);

            var selectedClip = SelectMatchingClip(clips, useFeaturedFirst, maxLengthSeconds);

            if (selectedClip != null) {
                _logger.LogInformation(
                    "Selected clip: '{Title}' ({Duration}s, {ViewCount} views, Featured: {IsFeatured})",
                    selectedClip.Title, selectedClip.DurationSeconds, selectedClip.ViewCount, selectedClip.IsFeatured);

                return selectedClip;
            }
        }

        _logger.LogWarning("No suitable clips found for {BroadcasterName} after checking all time periods",
            broadcasterName);

        return null;
    }

    /// <inheritdoc />
    public async Task<bool> ExecuteShoutoutAsync(string sourceBroadcasterId, string targetUsername,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(sourceBroadcasterId) || string.IsNullOrWhiteSpace(targetUsername)) {
            _logger.LogWarning("ExecuteShoutoutAsync called with empty parameters");

            return false;
        }

        var clip = await SelectRandomClipAsync(targetUsername, cancellationToken);

        if (clip == null) {
            _logger.LogWarning("No clip found for shoutout to {TargetUsername}", targetUsername);

            return false;
        }

        await _playbackEngine.PlayClipAsync(clip, cancellationToken);

        var options = _shoutoutOptions.CurrentValue;
        var shoutoutMessage = options.EnableMessage ? options.MessageTemplate : "";

        if (!string.IsNullOrWhiteSpace(shoutoutMessage)) {
            var (gameName, _) = await _helixClient.GetChannelInfoAsync(clip.Broadcaster.Id);

            var formattedMessage = shoutoutMessage
                .Replace("{channel}", clip.Broadcaster.DisplayName)
                .Replace("{game}", gameName ?? "Unknown")
                .Replace("{broadcaster}", clip.Broadcaster.DisplayName);

            var messageSent = await _helixClient.SendChatMessageAsync(sourceBroadcasterId, formattedMessage);

            if (!messageSent) _logger.LogWarning("Failed to send shoutout chat message");
        }

        var sendTwitchShoutout = options.SendTwitchShoutout;

        if (sendTwitchShoutout) {
            var shoutoutSent = await _helixClient.SendShoutoutAsync(sourceBroadcasterId, clip.Broadcaster.Id);

            if (!shoutoutSent) _logger.LogWarning("Failed to send Twitch /shoutout command");
        }

        _logger.LogInformation("Shoutout executed successfully for {TargetUsername}", targetUsername);

        return true;
    }

    private ClipData? SelectMatchingClip(IReadOnlyList<ClipData> clips, bool useFeaturedFirst, int maxLengthSeconds) {
        var matchingClips = FilterClips(clips, useFeaturedFirst, maxLengthSeconds);

        if (matchingClips.Any()) return SelectRandomFromList(matchingClips);

        if (!useFeaturedFirst) return null;

        _logger.LogDebug("No featured clips found, trying without featured filter");
        matchingClips = FilterClips(clips, false, maxLengthSeconds);

        return matchingClips.Any() ? SelectRandomFromList(matchingClips) : null;
    }

    private List<ClipData> FilterClips(IReadOnlyList<ClipData> clips, bool featuredOnly, int maxLengthSeconds) {
        return clips
            .Where(c => (!featuredOnly || c.IsFeatured) && c.DurationSeconds <= maxLengthSeconds)
            .ToList();
    }

    private ClipData SelectRandomFromList(List<ClipData> clips) {
        var random = new Random();

        return clips[random.Next(clips.Count)];
    }
}