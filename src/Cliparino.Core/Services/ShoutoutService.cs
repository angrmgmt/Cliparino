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
public class ShoutoutService(ITwitchHelixClient helixClient,
    IPlaybackEngine playbackEngine,
    IOptionsMonitor<ShoutoutOptions> shoutoutOptions,
    ILogger<ShoutoutService> logger)
    : IShoutoutService {
    /// <inheritdoc />
    public async Task<ClipData?> SelectRandomClipAsync(string broadcasterName,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(broadcasterName)) {
            logger.LogWarning("SelectRandomClipAsync called with empty broadcaster name");

            return null;
        }

        var broadcasterId = await helixClient.GetBroadcasterIdByNameAsync(broadcasterName);

        if (string.IsNullOrEmpty(broadcasterId)) {
            logger.LogWarning("Could not find broadcaster ID for: {BroadcasterName}", broadcasterName);

            return null;
        }

        var options = shoutoutOptions.CurrentValue;
        var useFeaturedFirst = options.UseFeaturedClips;
        var maxLengthSeconds = options.MaxClipLength;
        var maxAgeDays = options.MaxClipAge;

        logger.LogInformation(
            "Selecting clip for {BroadcasterName} - FeaturedFirst: {FeaturedFirst}, MaxLength: {MaxLength}s, MaxAge: {MaxAge} days",
            broadcasterName, useFeaturedFirst, maxLengthSeconds, maxAgeDays);

        var validPeriods = new[] { 1, 7, 30, 90, 365 };

        foreach (var period in validPeriods.Where(p => p >= maxAgeDays)) {
            var endDate = DateTimeOffset.UtcNow;
            var startDate = endDate.AddDays(-period);

            var clips = await helixClient.GetClipsByBroadcasterAsync(broadcasterId,
                100,
                startDate,
                endDate);

            if (!clips.Any()) {
                logger.LogDebug("No clips found for period {Period} days", period);

                continue;
            }

            logger.LogDebug("Retrieved {Count} clips for period {Period} days", clips.Count, period);

            var selectedClip = SelectMatchingClip(clips, useFeaturedFirst, maxLengthSeconds);

            if (selectedClip == null) continue;
            logger.LogInformation("Selected clip: '{Title}' ({Duration}s, {ViewCount} views, Featured: {IsFeatured})",
                selectedClip.Title, selectedClip.DurationSeconds, selectedClip.ViewCount, selectedClip.IsFeatured);

            return selectedClip;
        }

        logger.LogWarning("No suitable clips found for {BroadcasterName} after checking all time periods",
            broadcasterName);

        return null;
    }

    /// <inheritdoc />
    public async Task<bool> ExecuteShoutoutAsync(string sourceBroadcasterId, string targetUsername,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(sourceBroadcasterId) || string.IsNullOrWhiteSpace(targetUsername)) {
            logger.LogWarning("ExecuteShoutoutAsync called with empty parameters");

            return false;
        }

        var clip = await SelectRandomClipAsync(targetUsername, cancellationToken);

        if (clip == null) {
            logger.LogWarning("No clip found for shoutout to {TargetUsername}", targetUsername);

            return false;
        }

        await playbackEngine.PlayClipAsync(clip, cancellationToken);

        var options = shoutoutOptions.CurrentValue;
        var shoutoutMessage = options.EnableMessage ? options.MessageTemplate : "";

        if (!string.IsNullOrWhiteSpace(shoutoutMessage)) {
            var (gameName, _) = await helixClient.GetChannelInfoAsync(clip.Broadcaster.Id);

            var formattedMessage = shoutoutMessage
                .Replace("{channel}", clip.Broadcaster.DisplayName)
                .Replace("{game}", gameName ?? "Unknown")
                .Replace("{broadcaster}", clip.Broadcaster.DisplayName);

            var messageSent = await helixClient.SendChatMessageAsync(sourceBroadcasterId, formattedMessage);

            if (!messageSent) logger.LogWarning("Failed to send shoutout chat message");
        }

        var sendTwitchShoutout = options.SendTwitchShoutout;

        if (sendTwitchShoutout) {
            var shoutoutSent = await helixClient.SendShoutoutAsync(sourceBroadcasterId, clip.Broadcaster.Id);

            if (!shoutoutSent) logger.LogWarning("Failed to send Twitch /shoutout command");
        }

        logger.LogInformation("Shoutout executed successfully for {TargetUsername}", targetUsername);

        return true;
    }

    private ClipData? SelectMatchingClip(IReadOnlyList<ClipData> clips, bool useFeaturedFirst, int maxLengthSeconds) {
        var matchingClips = FilterClips(clips, useFeaturedFirst, maxLengthSeconds);

        if (matchingClips.Count != 0) return SelectRandomFromList(matchingClips);

        if (!useFeaturedFirst) return null;

        logger.LogDebug("No featured clips found, trying without featured filter");
        matchingClips = FilterClips(clips, false, maxLengthSeconds);

        return matchingClips.Count != 0 ? SelectRandomFromList(matchingClips) : null;
    }

    private static List<ClipData> FilterClips(IReadOnlyList<ClipData> clips, bool featuredOnly, int maxLengthSeconds) {
        return clips
            .Where(c => (!featuredOnly || c.IsFeatured) && c.DurationSeconds <= maxLengthSeconds)
            .ToList();
    }

    private static ClipData SelectRandomFromList(List<ClipData> clips) {
        var random = new Random();

        return clips[random.Next(clips.Count)];
    }
}