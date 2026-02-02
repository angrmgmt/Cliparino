using Cliparino.Core.Models;

namespace Cliparino.Core.Services;

public class ShoutoutService : IShoutoutService {
    private readonly IConfiguration _configuration;
    private readonly ITwitchHelixClient _helixClient;
    private readonly ILogger<ShoutoutService> _logger;
    private readonly IPlaybackEngine _playbackEngine;

    public ShoutoutService(
        ITwitchHelixClient helixClient,
        IPlaybackEngine playbackEngine,
        IConfiguration configuration,
        ILogger<ShoutoutService> logger
    ) {
        _helixClient = helixClient;
        _playbackEngine = playbackEngine;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<ClipData?> SelectRandomClipAsync(
        string broadcasterName, CancellationToken cancellationToken = default
    ) {
        if (string.IsNullOrWhiteSpace(broadcasterName)) {
            _logger.LogWarning("SelectRandomClipAsync called with empty broadcaster name");

            return null;
        }

        var broadcasterId = await _helixClient.GetBroadcasterIdByNameAsync(broadcasterName);

        if (string.IsNullOrEmpty(broadcasterId)) {
            _logger.LogWarning("Could not find broadcaster ID for: {BroadcasterName}", broadcasterName);

            return null;
        }

        var useFeaturedFirst = _configuration.GetValue("Shoutout:UseFeaturedClipsFirst", true);
        var maxLengthSeconds = _configuration.GetValue("Shoutout:MaxClipLengthSeconds", 60);
        var maxAgeDays = _configuration.GetValue("Shoutout:MaxClipAgeDays", 365);

        _logger.LogInformation(
            "Selecting clip for {BroadcasterName} - FeaturedFirst: {FeaturedFirst}, MaxLength: {MaxLength}s, MaxAge: {MaxAge} days",
            broadcasterName, useFeaturedFirst, maxLengthSeconds, maxAgeDays
        );

        var validPeriods = new[] { 1, 7, 30, 90, 365 };

        foreach (var period in validPeriods.Where(p => p >= maxAgeDays)) {
            var endDate = DateTimeOffset.UtcNow;
            var startDate = endDate.AddDays(-period);

            var clips = await _helixClient.GetClipsByBroadcasterAsync(
                broadcasterId,
                100,
                startDate,
                endDate
            );

            if (!clips.Any()) {
                _logger.LogDebug("No clips found for period {Period} days", period);

                continue;
            }

            _logger.LogDebug("Retrieved {Count} clips for period {Period} days", clips.Count, period);

            var selectedClip = SelectMatchingClip(clips, useFeaturedFirst, maxLengthSeconds);

            if (selectedClip != null) {
                _logger.LogInformation(
                    "Selected clip: '{Title}' ({Duration}s, {ViewCount} views, Featured: {IsFeatured})",
                    selectedClip.Title, selectedClip.DurationSeconds, selectedClip.ViewCount, selectedClip.IsFeatured
                );

                return selectedClip;
            }
        }

        _logger.LogWarning(
            "No suitable clips found for {BroadcasterName} after checking all time periods", broadcasterName
        );

        return null;
    }

    public async Task<bool> ExecuteShoutoutAsync(
        string sourceBroadcasterId, string targetUsername, CancellationToken cancellationToken = default
    ) {
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

        var shoutoutMessage = _configuration.GetValue<string>("Shoutout:Message", "");

        if (!string.IsNullOrWhiteSpace(shoutoutMessage)) {
            var (gameName, displayName) = await _helixClient.GetChannelInfoAsync(clip.BroadcasterId);

            var formattedMessage = shoutoutMessage
                .Replace("{channel}", displayName ?? targetUsername)
                .Replace("{game}", gameName ?? "Unknown");

            var messageSent = await _helixClient.SendChatMessageAsync(sourceBroadcasterId, formattedMessage);

            if (!messageSent) _logger.LogWarning("Failed to send shoutout chat message");
        }

        var sendTwitchShoutout = _configuration.GetValue("Shoutout:SendTwitchShoutout", true);

        if (sendTwitchShoutout) {
            var shoutoutSent = await _helixClient.SendShoutoutAsync(sourceBroadcasterId, clip.BroadcasterId);

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