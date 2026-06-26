using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cliparino.Core.Services;

/// <summary>
///     Uses the Chrome DevTools Protocol to dispatch a trusted mouse click into the Cliparino
///     browser source, giving the page sticky user activation before the Twitch embed loads.
/// </summary>
/// <remarks>
///     Requires OBS to be launched with <c>--chromium-flags="... --remote-debugging-port=PORT"</c>.
///     All failures are swallowed at Debug level — the clip still plays, just potentially muted
///     if the activation trick doesn't work.
/// </remarks>
public class CefClickService(
    IHttpClientFactory httpClientFactory,
    ILogger<CefClickService> logger,
    IConfiguration configuration) : ICefClickService {
    private static readonly JsonSerializerOptions CdpJson = new() { PropertyNameCaseInsensitive = true };

    private int Port => configuration.GetValue("OBS:CefDebuggingPort", 9222);
    private string PlayerUrl => configuration["Player:Url"] ?? "http://localhost:5291";

    public async Task SimulateClickAsync(CancellationToken cancellationToken = default) {
        try {
            var targets = await GetAllTargetDebuggerUrlsAsync(cancellationToken);

            if (targets.Count == 0) {
                logger.LogWarning(
                    "No CDP targets found — OBS may not have been restarted with the debugging flag yet, or the player page is not loaded.");

                return;
            }

            logger.LogInformation("Found {Count} CDP targets in OBS CEF. Attempting to unmute...", targets.Count);

            foreach (var wsUrl in targets)
                try {
                    using var ws = new ClientWebSocket();
                    await ws.ConnectAsync(new Uri(wsUrl), cancellationToken);

                    await ws.SendAsync(
                        Encoding.UTF8.GetBytes(
                            JsonSerializer.Serialize(new { id = 100, method = "Page.bringToFront" })),
                        WebSocketMessageType.Text, true, cancellationToken);

                    // 1. Click in a safe area (top-leftish) to give focus/activation without toggling play/pause
                    await SendMouseEventAsync(ws, 1, "mousePressed", 1, 10.0, 10.0, cancellationToken);
                    await SendMouseEventAsync(ws, 2, "mouseReleased", 0, 10.0, 10.0, cancellationToken);

                    // 2. Explicitly unmute and set volume via JS evaluation (idempotent, unlike 'M' key)
                    // We target all video elements and common player controls
                    const string unmuteScript = @"
                        (function() {
                            const videos = document.querySelectorAll('video');
                            videos.forEach(v => {
                                v.muted = false;
                                v.volume = 1.0;
                                if (v.paused) v.play().catch(() => {});
                            });
                            document.querySelector('button[aria-label=""Unmute (m)""]')?.click();
                            document.querySelector('.video-player__container')?.focus();
                        })();";

                    await EvaluateJsAsync(ws, 20, unmuteScript, cancellationToken);

                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cancellationToken);
                    logger.LogInformation("Interaction dispatched to target: {Url}", wsUrl);
                } catch (Exception ex) {
                    logger.LogDebug(ex, "Failed to dispatch interaction to target {Url}", wsUrl);
                }

            logger.LogDebug("CEF interactions completed on port {Port}", Port);
        } catch (Exception ex) {
            logger.LogDebug(ex, "CEF interaction simulation failed (non-fatal)");
        }
    }

    private static async Task EvaluateJsAsync(ClientWebSocket ws, int id, string expression, CancellationToken ct) {
        var msg = JsonSerializer.Serialize(new {
            id, method = "Runtime.evaluate", @params = new { expression, userGesture = true, awaitPromise = false }
        });
        await ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, ct);

        var buf = new byte[1024];
        await ws.ReceiveAsync(buf, ct);
    }

    private async Task<List<string>> GetAllTargetDebuggerUrlsAsync(CancellationToken ct) {
        try {
            var http = httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(2);

            var json = await http.GetStringAsync($"http://localhost:{Port}/json/list", ct);
            var targets = JsonSerializer.Deserialize<CdpTarget[]>(json, CdpJson);
            var playerHost = new Uri(PlayerUrl).Authority; // e.g. "localhost:5291"

            return targets?
                .Where(t =>
                    t.WebSocketDebuggerUrl != null &&
                    (t.Url?.Contains(playerHost, StringComparison.OrdinalIgnoreCase) == true ||
                     t.Url?.Contains("twitch.tv", StringComparison.OrdinalIgnoreCase) == true))
                .Select(t => t.WebSocketDebuggerUrl!)
                .ToList() ?? [];
        } catch {
            return [];
        }
    }

    private static async Task SendMouseEventAsync(ClientWebSocket ws, int id, string type, int buttons, double x,
        double y, CancellationToken ct) {
        var msg = JsonSerializer.Serialize(new {
            id,
            method = "Input.dispatchMouseEvent",
            @params = new {
                type,
                x,
                y,
                button = "left",
                clickCount = 1,
                modifiers = 0,
                buttons,
                pointerType = "mouse"
            }
        });
        await ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, ct);

        // Drain the CDP acknowledgement response
        var buf = new byte[512];
        await ws.ReceiveAsync(buf, ct);
    }

    private record CdpTarget(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("webSocketDebuggerUrl")]
        string? WebSocketDebuggerUrl);
}