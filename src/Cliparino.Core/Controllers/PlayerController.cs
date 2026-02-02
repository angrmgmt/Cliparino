using Cliparino.Core.Models;
using Cliparino.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cliparino.Core.Controllers;

[ApiController]
[Route("api")]
public class PlayerController : ControllerBase {
    private readonly IClipQueue _clipQueue;
    private readonly ITwitchHelixClient? _helixClient;
    private readonly ILogger<PlayerController> _logger;
    private readonly IPlaybackEngine _playbackEngine;

    public PlayerController(
        IPlaybackEngine playbackEngine,
        IClipQueue clipQueue,
        ILogger<PlayerController> logger,
        ITwitchHelixClient? helixClient = null
    ) {
        _playbackEngine = playbackEngine;
        _clipQueue = clipQueue;
        _logger = logger;
        _helixClient = helixClient;
    }

    [HttpGet("status")]
    public IActionResult GetStatus() {
        return Ok(
            new {
                state = _playbackEngine.CurrentState.ToString(),
                currentClip = _playbackEngine.CurrentClip,
                queueSize = _clipQueue.Count
            }
        );
    }

    [HttpPost("play")]
    public async Task<IActionResult> PlayClip([FromBody] PlayClipRequest request) {
        if (string.IsNullOrWhiteSpace(request.ClipId)) return BadRequest("ClipId is required");

        ClipData? clipData;

        if (_helixClient != null) {
            try {
                clipData = await _helixClient.GetClipByIdAsync(request.ClipId) ??
                           await _helixClient.GetClipByUrlAsync(request.ClipId);

                if (clipData == null) {
                    _logger.LogWarning("Clip not found in Twitch API: {ClipId}", request.ClipId);

                    return NotFound(new { error = "Clip not found", clipId = request.ClipId });
                }

                _logger.LogInformation("Validated clip from Twitch: {ClipId} - {Title}", clipData.Id, clipData.Title);
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to validate clip with Twitch API, using fallback");
                clipData = CreateFallbackClipData(request);
            }
        } else {
            _logger.LogWarning("Twitch API client not available, using fallback clip data");
            clipData = CreateFallbackClipData(request);
        }

        await _playbackEngine.PlayClipAsync(clipData);

        _logger.LogInformation("Clip enqueued via API: {ClipId}", clipData.Id);

        return Ok(new { message = "Clip enqueued", clipData });
    }

    private static ClipData CreateFallbackClipData(PlayClipRequest request) {
        return new ClipData(
            request.ClipId,
            $"https://clips.twitch.tv/{request.ClipId}",
            request.Title ?? "Unknown Clip",
            request.CreatorName ?? "Unknown",
            request.BroadcasterName ?? "Unknown",
            "unknown",
            request.GameName ?? "Unknown",
            request.DurationSeconds > 0 ? request.DurationSeconds : 30,
            DateTime.UtcNow,
            0,
            false
        );
    }

    [HttpPost("replay")]
    public async Task<IActionResult> Replay() {
        await _playbackEngine.ReplayAsync();

        return Ok(new { message = "Replaying last clip" });
    }

    [HttpPost("stop")]
    public async Task<IActionResult> Stop() {
        await _playbackEngine.StopPlaybackAsync();

        return Ok(new { message = "Playback stopped" });
    }

    [HttpPost("content-warning")]
    public IActionResult ContentWarningDetected([FromBody] ContentWarningRequest request) {
        _logger.LogWarning("Content warning detected via {Method}", request.DetectionMethod);

        return Ok(new { obsAutomation = false });
    }
}

public record PlayClipRequest(
    string ClipId,
    string? Title = null,
    string? CreatorName = null,
    string? BroadcasterName = null,
    string? GameName = null,
    int DurationSeconds = 30
);

public record ContentWarningRequest(
    string DetectionMethod,
    string Timestamp
);