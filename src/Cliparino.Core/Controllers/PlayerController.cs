using Cliparino.Core.Models;
using Cliparino.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Cliparino.Core.Controllers;

/// <summary>
///     Exposes a minimal HTTP API for controlling clip playback and querying playback state.
/// </summary>
/// <remarks>
///     <para>
///         This controller is intended for local control (for example, UI actions or integrations that can POST a clip
///         ID/URL).
///         The underlying playback work is delegated to <see cref="IPlaybackEngine" /> and queued via
///         <see cref="IClipQueue" />.
///     </para>
///     <para>
///         Routing: this controller is rooted at <c>/api</c> (for example <c>GET /api/status</c>).
///     </para>
///     <para>
///         Threading: action methods run on the ASP.NET Core request pipeline. Playback actions are asynchronous and
///         enqueue work
///         into background services; they should return quickly and not block on clip playback.
///     </para>
/// </remarks>
[ApiController]
[Route("api")]
public class PlayerController : ControllerBase {
    private readonly IClipQueue _clipQueue;
    private readonly ITwitchHelixClient? _helixClient;
    private readonly ILogger<PlayerController> _logger;
    private readonly IPlaybackEngine _playbackEngine;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PlayerController" /> class.
    /// </summary>
    /// <param name="playbackEngine">Playback engine responsible for state management and clip execution.</param>
    /// <param name="clipQueue">Queue used to track pending clips.</param>
    /// <param name="logger">Logger instance for structured diagnostics.</param>
    /// <param name="helixClient">
    ///     Optional Twitch Helix client used to validate clip identifiers and enrich fallback metadata.
    ///     When <see langword="null" />, the API will enqueue clips using best-effort fallback data.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="playbackEngine" />, <paramref name="clipQueue" />, or <paramref name="logger" /> is
    ///     <see langword="null" />.
    /// </exception>
    public PlayerController(
        IPlaybackEngine playbackEngine,
        IClipQueue clipQueue,
        ILogger<PlayerController> logger,
        ITwitchHelixClient? helixClient = null
    ) {
        _playbackEngine = playbackEngine ?? throw new ArgumentNullException(nameof(playbackEngine));
        _clipQueue = clipQueue ?? throw new ArgumentNullException(nameof(clipQueue));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _helixClient = helixClient;
    }

    /// <summary>
    ///     Returns the current player state, the currently playing clip (if any), and the queue size.
    /// </summary>
    /// <returns>An <see cref="IActionResult" /> containing the current playback status snapshot.</returns>
    [HttpGet("status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetStatus() {
        return Ok(
            new {
                state = _playbackEngine.CurrentState.ToString(),
                currentClip = _playbackEngine.CurrentClip,
                queueSize = _clipQueue.Count
            }
        );
    }

    /// <summary>
    ///     Enqueues a clip for playback.
    /// </summary>
    /// <param name="request">
    ///     The play request. <see cref="PlayClipRequest.ClipId" /> may be either a Twitch clip ID or a clip URL.
    ///     Optional metadata fields are used only when the Twitch API is unavailable or validation fails.
    /// </param>
    /// <returns>
    ///     <para><c>200 OK</c> when the clip is accepted for playback.</para>
    ///     <para><c>400 Bad Request</c> when <see cref="PlayClipRequest.ClipId" /> is missing/blank.</para>
    ///     <para><c>404 Not Found</c> when the Twitch API is available and the clip cannot be resolved.</para>
    /// </returns>
    /// <remarks>
    ///     If <see cref="ITwitchHelixClient" /> is available, this endpoint attempts to resolve the clip via Helix.
    ///     If Helix resolution fails (or is unavailable), the request is still enqueued using fallback metadata.
    /// </remarks>
    [HttpPost("play")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
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

    /// <summary>
    ///     Requests a replay of the most recently played clip.
    /// </summary>
    /// <returns><c>200 OK</c> when the replay request is accepted.</returns>
    /// <remarks>
    ///     The replay behavior is defined by <see cref="IPlaybackEngine.ReplayAsync" /> and may be a no-op if no clip has
    ///     played yet.
    /// </remarks>
    [HttpPost("replay")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Replay() {
        await _playbackEngine.ReplayAsync();

        return Ok(new { message = "Replaying last clip" });
    }

    /// <summary>
    ///     Stops playback and advances the engine to an idle/cooldown state (implementation dependent).
    /// </summary>
    /// <returns><c>200 OK</c> when the stop request is accepted.</returns>
    [HttpPost("stop")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Stop() {
        await _playbackEngine.StopPlaybackAsync();

        return Ok(new { message = "Playback stopped" });
    }

    /// <summary>
    ///     Accepts an external content-warning signal (for example from browser automation) and records it in logs.
    /// </summary>
    /// <param name="request">Information about how the warning was detected and the relevant timestamp.</param>
    /// <returns>
    ///     <c>200 OK</c> with a response payload indicating whether any OBS automation was performed.
    /// </returns>
    /// <remarks>
    ///     This endpoint currently does not trigger automation; it exists to support future safety workflows.
    /// </remarks>
    [HttpPost("content-warning")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult ContentWarningDetected([FromBody] ContentWarningRequest request) {
        _logger.LogWarning("Content warning detected via {Method}", request.DetectionMethod);

        return Ok(new { obsAutomation = false });
    }
}

/// <summary>
///     Request payload for <c>POST /api/play</c>.
/// </summary>
/// <param name="ClipId">
///     The Twitch clip identifier or a full Twitch clip URL. This value is required.
/// </param>
/// <param name="Title">
///     Optional clip title used only when Helix validation is unavailable.
/// </param>
/// <param name="CreatorName">
///     Optional creator/curator name used only when Helix validation is unavailable.
/// </param>
/// <param name="BroadcasterName">
///     Optional broadcaster name used only when Helix validation is unavailable.
/// </param>
/// <param name="GameName">
///     Optional game name used only when Helix validation is unavailable.
/// </param>
/// <param name="DurationSeconds">
///     Optional clip duration in seconds. Values less than or equal to zero fall back to a default duration.
/// </param>
public record PlayClipRequest(
    string ClipId,
    string? Title = null,
    string? CreatorName = null,
    string? BroadcasterName = null,
    string? GameName = null,
    int DurationSeconds = 30
);

/// <summary>
///     Request payload for <c>POST /api/content-warning</c>.
/// </summary>
/// <param name="DetectionMethod">A short identifier describing how the warning was detected.</param>
/// <param name="Timestamp">Timestamp string associated with the warning (format determined by the caller).</param>
public record ContentWarningRequest(
    string DetectionMethod,
    string Timestamp
);