/*  Cliparino is a clip player for Twitch.tv built to work with Streamer.bot.
    Copyright (C) 2024 Scott Mongrain - (angrmgmt@gmail.com)

    This library is free software; you can redistribute it and/or
    modify it under the terms of the GNU Lesser General Public
    License as published by the Free Software Foundation; either
    version 2.1 of the License, or (at your option) any later version.

    This library is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
    Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public
    License along with this library; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301
    USA
*/

#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Streamer.bot.Common.Events;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;
using Twitch.Common.Models.Api;

#endregion

/// <summary>
///     Manages HTTP server and client functionalities required for hosting and handling HTTP
///     interactions.
/// </summary>
public class HttpManager {
    #region Windows API Declarations for Browser Source Automation

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct Point {
        public int X;
        public int Y;
    }
    
    private static readonly Dictionary<string, uint> WinMsg = new Dictionary<string, uint> {
        { "WM_LBUTTONDOWN", CliparinoConstants.WindowsApi.WM_LBUTTONDOWN },
        { "WM_LBUTTONUP", CliparinoConstants.WindowsApi.WM_LBUTTONUP },
        { "WM_RBUTTONDOWN", CliparinoConstants.WindowsApi.WM_RBUTTONDOWN },
        { "WM_RBUTTONUP", CliparinoConstants.WindowsApi.WM_RBUTTONUP }
    };
    
    #endregion
    
    private const int NonceLength = CliparinoConstants.Http.NonceLength;
    private const int BasePort = CliparinoConstants.Http.BasePort;
    private const int MaxPortRetries = CliparinoConstants.Http.MaxPortRetries;
    private const string HelixApiUrl = CliparinoConstants.Http.HelixApiUrl;

    private const string CSSText = @"
    div {
        background-color: #0071c5;
        background-color: rgba(0,113,197,1);
        margin: 0 auto;
        overflow: hidden;
    }

    #twitch-embed {
        display: block;
    }

    .iframe-container {
        height: 1080px;
        position: relative;
        width: 1920px;
    }

    #clip-iframe {
        height: 100%;
        left: 0;
        position: absolute;
        top: 0;
        width: 100%;
    }

    #overlay-text {
        background-color: #042239;
        background-color: rgba(4,34,57,0.7071);
        border-radius: 5px;
        color: #ffb809;
        left: 5%;
        opacity: 0.5;
        padding: 10px;
        position: absolute;
        top: 80%;
    }

    .line1, .line2, .line3 {
        font-family: 'Open Sans', sans-serif;
        font-size: 2em;
    }

    .line1 {
        font: normal 600 2em/1.2 'OpenDyslexic', 'Open Sans', sans-serif;
    }

    .line2 {
        font: normal 400 1.5em/1.2 'OpenDyslexic', 'Open Sans', sans-serif;
    }

    .line3 {
        font: italic 100 1em/1 'OpenDyslexic', 'Open Sans', sans-serif;
    }
    ";

    private const string HTMLText = @"
    <!DOCTYPE html>
    <html lang=""en"">
    <head>
    <meta charset=""utf-8"">
    <link href=""/index.css"" rel=""stylesheet"" type=""text/css"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Cliparino</title>
    </head>
    <body>
    <div id=""twitch-embed"">
        <div class=""iframe-container"">
            <iframe allowfullscreen autoplay=""true"" controls=""false"" height=""1080"" id=""clip-iframe"" mute=""false"" preload=""auto"" 
                src=""https://clips.twitch.tv/embed?clip=[[clipId]]&nonce=[[nonce]]&autoplay=true&parent=localhost"" title=""Cliparino"" width=""1920"">
            </iframe>
            <div class=""overlay-text"" id=""overlay-text"">
                <div class=""line1"">[[streamerName]] doin' a heckin' [[gameName]] stream</div>
                <div class=""line2"">[[clipTitle]]</div>
                <div class=""line3"">by [[curatorName]]</div>
            </div>
        </div>
    </div>
    <script>
        let contentWarningDetected = false;
        let obsIntegrationAttempted = false;

        // Enhanced content warning detection with server-side automation
        const handleContentWarning = async (detectionMethod = 'unknown') => {
            if (contentWarningDetected) return;
            
            contentWarningDetected = true;
            console.log(`[Cliparino] Content warning detected via ${detectionMethod}`);
            
            // Notify the main application about content warning (server handles automation)
            try {
                const response = await fetch('/content-warning-detected', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ 
                        clipId: '[[clipId]]',
                        detectionMethod: detectionMethod,
                        timestamp: new Date().toISOString()
                    })
                });
                
                const result = await response.json();
                
                if (result && result.obsAutomation === true) {
                    showContentWarningNotification('OBS automation active');
                } else {
                    showContentWarningNotification('Manual interaction required');
                }
            } catch (error) {
                console.log('[Cliparino] Content warning notification failed:', error);
                showContentWarningNotification('Manual interaction required');
            }
        };

        const showContentWarningNotification = (method = 'detected') => {
            const notification = document.createElement('div');
            
            notification.id = 'content-warning-notification';
            notification.style.cssText = `
                position: absolute;
                top: 10%;
                right: 5%;
                background: rgba(255, 184, 9, 0.9);
                color: #042239;
                padding: 15px;
                border-radius: 5px;
                font-family: 'Open Sans', sans-serif;
                font-size: 1.1em;
                z-index: 1000;
                max-width: 320px;
                box-shadow: 0 4px 8px rgba(0,0,0,0.3);
                text-align: center;
            `;
            
            if (method === 'OBS automation active') {
                notification.innerHTML = `
                    <strong>✓ Content Warning Handled</strong><br>
                    <small>OBS automation is processing...</small>
                `;
                notification.style.background = 'rgba(76, 175, 80, 0.9)';
                notification.style.color = 'white';
            } else {
                notification.innerHTML = `
                    <strong>⚠ Content Warning</strong><br>
                    Right-click Browser Source in OBS<br>
                    → Select ""Interact""<br>
                    → Click through warning<br>
                    <small>Usually only needed once per session</small>
                `;
            }
            
            document.body.appendChild(notification);
            
            // Auto-hide after 12 seconds
            setTimeout(() => {
                if (notification.parentNode) {
                    notification.parentNode.removeChild(notification);
                }
            }, 12000);
        };

        // Detection strategies
        const detectContentWarning = () => {
            const iframe = document.getElementById('clip-iframe');
            
            if (!iframe) return;

            // Method 1: Try direct DOM access (will fail but worth trying)
            try {
                const iframeDoc = iframe.contentWindow.document;
                const warningSelectors = [
                    '.content-warning-overlay',
                    '.content-classification-gate-overlay', 
                    '.content-gate-overlay',
                    '.mature-content-overlay',
                    '[data-a-target=""content-classification-gate-overlay""]'
                ];
                
                for (const selector of warningSelectors) {
                    const element = iframeDoc.querySelector(selector);
                    
                    if (element) {
                        handleContentWarning('DOM-access');
                        
                        return;
                    }
                }
            } catch (e) {
                // Expected cross-origin error
            }

            // Method 2: Check iframe loading patterns
            if (iframe.src.includes('error=') || iframe.src.includes('warning=')) {
                handleContentWarning('URL-parameter');
                
                return;
            }

            // Method 3: Check for loading delays (content warnings often cause delays)
            const checkDelay = () => {
                if (!iframe.contentWindow || iframe.contentWindow.location.href === 'about:blank') {
                    handleContentWarning('loading-delay');
                }
            };
            
            setTimeout(checkDelay, 4000); // Check after 4 seconds
        };

        // Set up detection
        const iframe = document.getElementById('clip-iframe');
        
        // Listen for iframe load events
        iframe.addEventListener('load', () => {
            setTimeout(detectContentWarning, 1000);
            setTimeout(detectContentWarning, 3000);
        });

        // OBS Browser Source detection (informational only)
        if (window.obsstudio) {
            console.log('[Cliparino] OBS Browser Source environment detected');
        }

        // PostMessage listener for future Twitch integration
        window.addEventListener('message', (event) => {
            if (event.origin !== 'https://clips.twitch.tv') return;
            
            if (event.data && (event.data.type === 'mature-content-gate' || event.data.type === 'content-warning')) {
                handleContentWarning('postMessage');
            }
        });

        // Initial check
        setTimeout(detectContentWarning, 2000);
    </script>
    </body>
    </html>
    ";

    private readonly IInlineInvokeProxy _cph;
    private readonly CPHLogger _logger;
    private readonly TwitchApiManager _twitchApiManager;
    public readonly HttpClient Client;
    private ClipData _clipData;
    private int _currentPort = BasePort;
    private HttpListener _listener;

    public HttpManager(IInlineInvokeProxy cph, CPHLogger logger, TwitchApiManager twitchApiManager) {
        _cph = cph ?? throw new ArgumentNullException(nameof(cph));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _twitchApiManager = twitchApiManager;
        Client = new HttpClient { BaseAddress = new Uri(HelixApiUrl) };
    }

    /// <summary>
    ///     Gets the current server URL being used by the HTTP server.
    /// </summary>
    public string ServerUrl { get; private set; } = $"http://localhost:{BasePort}/";

    /// <summary>
    ///     Gets a value indicating whether the HTTP server is currently running.
    /// </summary>
    private bool IsServerRunning => _listener?.IsListening == true;

    /// <summary>
    ///     Generates a dictionary containing headers for Cross-Origin Resource Sharing (CORS)
    ///     configuration.
    /// </summary>
    /// <param name="nonce">
    ///     A unique string used for script security to prevent unauthorized execution of scripts.
    /// </param>
    /// <returns>
    ///     A dictionary of headers configured for CORS.
    /// </returns>
    private static Dictionary<string, string> GenerateCORSHeaders(string nonce) {
        return new Dictionary<string, string> {
            { "Access-Control-Allow-Origin", "*" },
            { "Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS" },
            { "Access-Control-Allow-Headers", "*" }, {
                "Content-Security-Policy",
                $"script-src 'nonce-{nonce}' 'strict-dynamic'; object-src 'none'; base-uri 'none'; frame-ancestors 'self' https://clips.twitch.tv;"
            }
        };
    }

    /// <summary>
    ///     Generates a dictionary containing headers for cache control configuration.
    ///     (i.e., Disables caching of responses on client browsers.)
    /// </summary>
    /// <returns>
    ///     A dictionary of headers configured for cache control.
    /// </returns>
    private static Dictionary<string, string> GenerateCacheControlHeaders() {
        return new Dictionary<string, string> {
            { "Cache-Control", "no-cache, no-store, must-revalidate" }, { "Pragma", "no-cache" }, { "Expires", "0" }
        };
    }

    /// <summary>
    ///     Attempts to automate content warning handling using Streamer.bot's OBS integration.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private async Task<bool> AutomateContentWarningHandling() {
        try {
            _logger.Log(LogLevel.Info, "Attempting to automate content warning handling via OBS...");

            // Strategy 1: Try full automation with Windows API (most comprehensive)
            var clickResult = await AutomateContentWarningClick();
            
            if (clickResult) {
                _logger.Log(LogLevel.Info, "Content warning automated successfully via Windows API");
                
                return true;
            }

            // Strategy 2: Try to refresh the browser source to potentially bypass the warning
            var refreshResult = await RefreshBrowserSource();
            
            if (refreshResult) {
                _logger.Log(LogLevel.Info, "Browser source refreshed successfully");
                
                return true;
            }

            // Strategy 3: Try to simulate interaction with the browser source
            var interactResult = await TriggerBrowserSourceInteract();
            
            if (interactResult) {
                _logger.Log(LogLevel.Info, "Browser source interaction triggered successfully");
                
                return true;
            }

            // Strategy 4: Try to toggle browser source visibility (sometimes helps with loading issues)
            var toggleResult = await ToggleBrowserSourceVisibility();
            
            if (toggleResult) {
                _logger.Log(LogLevel.Info, "Browser source visibility toggled successfully");
                
                return true;
            }

            _logger.Log(LogLevel.Debug, "All OBS automation strategies failed or unavailable");
            
            return false;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Warn, "Error during OBS automation attempt", ex);
            
            return false;
        }
    }

    /// <summary>
    ///     Attempts to refresh the browser source to potentially bypass content warnings.
    /// </summary>
    /// <returns>True if refresh was successful, false otherwise.</returns>
    private Task<bool> RefreshBrowserSource() {
        try {
            // Get the current scene first
            var currentScene = _cph.ObsGetCurrentScene();
            
            if (string.IsNullOrEmpty(currentScene)) {
                _logger.Log(LogLevel.Debug, "Could not get current OBS scene");
                
                return Task.FromResult(false);
            }

            // Try to refresh the browser source using OBS WebSocket
            var refreshPayload = new {
                sourceName = "Player" // This matches the actual browser source name in ObsSceneManager
            };

            var result = _cph.ObsSendRaw("RefreshBrowserSource", JsonConvert.SerializeObject(refreshPayload));

            if (result == null) return Task.FromResult(false);

            _logger.Log(LogLevel.Debug, "Browser source refresh command sent successfully");
            
            return Task.FromResult(true);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Debug, "Error refreshing browser source", ex);
            
            return Task.FromResult(false);
        }
    }

    /// <summary>
    ///     Attempts to trigger browser source interaction programmatically.
    /// </summary>
    /// <returns>True if interaction was triggered, false otherwise.</returns>
    private Task<bool> TriggerBrowserSourceInteract() {
        try {
            // Note: This is experimental - OBS WebSocket may not support programmatic interaction
            var interactPayload = new {
                sourceName = "Player",
                interact = true
            };

            var result = _cph.ObsSendRaw("TriggerBrowserSourceInteract", JsonConvert.SerializeObject(interactPayload));

            if (result == null) return Task.FromResult(false);

            _logger.Log(LogLevel.Debug, "Browser source interaction trigger sent");
            
            return Task.FromResult(true);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Debug, "Error triggering browser source interaction", ex);
            
            return Task.FromResult(false);
        }
    }

    /// <summary>
    ///     Attempts to toggle browser source visibility to potentially reset its state.
    /// </summary>
    /// <returns>True if the toggle was successful, false otherwise.</returns>
    private async Task<bool> ToggleBrowserSourceVisibility() {
        try {
            var currentScene = _cph.ObsGetCurrentScene();
            
            if (string.IsNullOrEmpty(currentScene)) {
                return false;
            }

            const string cliparinoSourceName = "Cliparino";
            const string playerSourceName = "Player";
            
            // Toggle visibility (hide then show after a short delay)
            _cph.ObsSetSourceVisibility(cliparinoSourceName, playerSourceName, false);
            await Task.Delay(500); // Short delay
            _cph.ObsSetSourceVisibility(cliparinoSourceName, playerSourceName, true);
            _logger.Log(LogLevel.Debug, "Browser source visibility toggled");
            
            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Debug, "Error toggling browser source visibility", ex);
            
            return false;
        }
    }

    /// <summary>
    ///     Attempts to fully automate the content warning interaction by opening OBS interact mode
    ///     and simulating the click to bypass the warning.
    /// </summary>
    /// <returns>True if automation was successful, false otherwise.</returns>
    private async Task<bool> AutomateContentWarningClick() {
        try {
            _logger.Log(LogLevel.Info, "Attempting full automation of content warning click...");

            // Step 1: Try to trigger OBS interact mode programmatically
            var interactTriggered = await TriggerObsInteractMode();
            if (!interactTriggered) {
                _logger.Log(LogLevel.Debug, "Could not trigger OBS interact mode programmatically");
                return false;
            }

            // Step 2: Wait for an interact window to open
            await Task.Delay(2000);

            // Step 3: Find and click the content warning button
            var clickSuccessful = await ClickContentWarningButton();
            if (!clickSuccessful) {
                _logger.Log(LogLevel.Debug, "Could not locate or click content warning button");
                return false;
            }

            // Step 4: Close the Interact window
            await CloseObsInteractWindow();

            _logger.Log(LogLevel.Info, "Content warning automation completed successfully!");
            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error during content warning automation", ex);
            return false;
        }
    }

    /// <summary>
    ///     Attempts to trigger OBS interact mode for the browser source.
    /// </summary>
    /// <returns>True if interact mode was triggered, false otherwise.</returns>
    private async Task<bool> TriggerObsInteractMode() {
        try {
            // Method 1: Try using OBS WebSocket if available
            var obsPayload = new {
                sourceName = "Player",
                action = "interact"
            };
            
            var result = _cph.ObsSendRaw("TriggerInteract", JsonConvert.SerializeObject(obsPayload));
            if (result != null) {
                _logger.Log(LogLevel.Debug, "OBS interact mode triggered via WebSocket");
                return true;
            }

            // Method 2: Try Windows automation to right-click on the browser source
            var obsWindow = FindObsWindow();

            if (obsWindow == IntPtr.Zero) return false;

            var browserSourceCoordinates = await FindBrowserSourceCoordinates(obsWindow);

            if (!browserSourceCoordinates.HasValue) return false;

            // Right-click on the browser source
            await SimulateRightClick(obsWindow, browserSourceCoordinates.Value);
            await Task.Delay(200);
                    
            // Click on the "Interact" menu item (approximate position)
            var interactPosition = new Point {
                X = browserSourceCoordinates.Value.X + 50,
                Y = browserSourceCoordinates.Value.Y + 100
            };
            
            await SimulateLeftClick(obsWindow, interactPosition);
            _logger.Log(LogLevel.Debug, "OBS interact mode triggered via Windows automation");
                    
            return true;

        } catch (Exception ex) {
            _logger.Log(LogLevel.Debug, "Error triggering OBS interact mode", ex);
            
            return false;
        }
    }

    /// <summary>
    ///     Attempts to find and click the content warning button in the Interact window.
    /// </summary>
    /// <returns>True if the click was successful, false otherwise.</returns>
    private async Task<bool> ClickContentWarningButton() {
        try {
            // Look for an OBS Interact window
            var interactWindow = FindWindow(null, "Interact");
            if (interactWindow == IntPtr.Zero) {
                // Try alternate window title
                interactWindow = FindWindow(null, "Browser Source - Interact");
            }

            if (interactWindow == IntPtr.Zero) {
                _logger.Log(LogLevel.Debug, "Could not find OBS interact window");
                
                return false;
            }

            // Get window dimensions
            if (!GetWindowRect(interactWindow, out var windowRect)) {
                return false;
            }

            // Content warning buttons are typically in the center-bottom area
            // We'll try multiple common positions where these buttons appear
            var buttonPositions = new[] {
                new Point { X = (windowRect.Right - windowRect.Left) / 2, Y = (windowRect.Bottom - windowRect.Top) * 3 / 4 },
                new Point { X = (windowRect.Right - windowRect.Left) / 2, Y = (windowRect.Bottom - windowRect.Top) * 2 / 3 },
                new Point { X = (windowRect.Right - windowRect.Left) / 2 + 100, Y = (windowRect.Bottom - windowRect.Top) * 3 / 4 },
                new Point { X = (windowRect.Right - windowRect.Left) / 2 - 100, Y = (windowRect.Bottom - windowRect.Top) * 3 / 4 }
            };

            foreach (var position in buttonPositions) {
                await SimulateLeftClick(interactWindow, position);
                await Task.Delay(500);
                
                // Check if the click was successful (the window might close or change)
                if (GetWindowRect(interactWindow, out _)) continue;

                _logger.Log(LogLevel.Debug, $"Content warning button clicked successfully at position {position.X}, {position.Y}");
                
                return true;
            }

            return false;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Debug, "Error clicking content warning button", ex);
            
            return false;
        }
    }

    /// <summary>
    ///     Simulates a left mouse click at the specified coordinates within a window.
    /// </summary>
    /// <param name="hWnd">Handle to the target window.</param>
    /// <param name="position">The position to click.</param>
    private static async Task SimulateLeftClick(IntPtr hWnd, Point position) {
        var lParam = (IntPtr)((position.Y << 16) | position.X);
        
        PostMessage(hWnd, WinMsg["WM_LBUTTONDOWN"], IntPtr.Zero, lParam);
        await Task.Delay(50);
        PostMessage(hWnd, WinMsg["WM_LBUTTONUP"], IntPtr.Zero, lParam);
    }

    /// <summary>
    ///     Simulates a right mouse click at the specified coordinates within a window.
    /// </summary>
    /// <param name="hWnd">Handle to the target window.</param>
    /// <param name="position">The position to click.</param>
    private static async Task SimulateRightClick(IntPtr hWnd, Point position) {
        var lParam = (IntPtr)((position.Y << 16) | position.X);
        
        PostMessage(hWnd, WinMsg["WM_RBUTTONDOWN"], IntPtr.Zero, lParam);
        await Task.Delay(50);
        PostMessage(hWnd, WinMsg["WM_RBUTTONUP"], IntPtr.Zero, lParam);
    }

    /// <summary>
    ///     Finds the OBS Studio window handle.
    /// </summary>
    /// <returns>Window handle or IntPtr.Zero if not found.</returns>
    private IntPtr FindObsWindow() {
        var obsWindow = FindWindow(null, "OBS Studio");
        if (obsWindow == IntPtr.Zero) {
            // Try alternate window titles
            obsWindow = FindWindow(null, "OBS");
        }
        return obsWindow;
    }

    /// <summary>
    ///     Attempts to find the coordinates of the browser source within the OBS window.
    /// </summary>
    /// <param name="obsWindow">Handle to the OBS window.</param>
    /// <returns>Coordinates of the browser source if found.</returns>
    private Task<Point?> FindBrowserSourceCoordinates(IntPtr obsWindow) {
        try {
            if (!GetWindowRect(obsWindow, out var obsRect)) {
                return Task.FromResult<Point?>(null);
            }

            // Browser sources are typically in the preview area
            // This is an approximation - you may need to adjust based on OBS layout
            var estimatedPosition = new Point {
                X = (obsRect.Right - obsRect.Left) / 2,
                Y = (obsRect.Bottom - obsRect.Top) / 3
            };

            return Task.FromResult<Point?>(estimatedPosition);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Debug, "Error finding browser source coordinates", ex);
            return Task.FromResult<Point?>(null);
        }
    }

    /// <summary>
    ///     Attempts to close the OBS interact window.
    /// </summary>
    private async Task CloseObsInteractWindow() {
        try {
            var interactWindow = FindWindow(null, "Interact");
            if (interactWindow == IntPtr.Zero) {
                interactWindow = FindWindow(null, "Browser Source - Interact");
            }

            if (interactWindow != IntPtr.Zero) {
                // Send Alt+F4 to close the window
                PostMessage(interactWindow, 0x0104, (IntPtr)0x73, IntPtr.Zero); // WM_SYSKEYDOWN, VK_F4
                await Task.Delay(50);
                PostMessage(interactWindow, 0x0105, (IntPtr)0x73, IntPtr.Zero); // WM_SYSKEYUP, VK_F4
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Debug, "Error closing OBS interact window", ex);
        }
    }

    /// <summary>
    ///     Starts an HTTP server and begins listening for incoming HTTP requests.
    /// </summary>
    /// <remarks>
    ///     This method initializes an <see cref="HttpListener" />, configures it with the server URL, and
    ///     starts listening for incoming HTTP requests. If the HTTP server is already running, the method
    ///     returns without performing any action. If the default port is in use, it will try alternative ports.
    ///     Logs messages to indicate the success or failure of the server startup process.
    /// </remarks>
    /// <exception cref="System.Exception">
    ///     Throws an exception if there is an error while starting the server or initializing the HTTP
    ///     listener after trying all available ports.
    /// </exception>
    public void StartServer() {
        try {
            // Check if the server is already running
            if (IsServerRunning) {
                _logger.Log(LogLevel.Info, $"HTTP server is already running at {ServerUrl}");

                return;
            }

            // Clean up any existing listener that isn't running
            if (_listener != null) {
                try {
                    _listener.Close();
                } catch {
                    // Ignore cleanup errors
                }

                _listener = null;
            }

            var portAttempts = 0;
            _currentPort = BasePort;

            while (portAttempts < MaxPortRetries) {
                try {
                    ServerUrl = $"http://localhost:{_currentPort}/";
                    _listener = new HttpListener();
                    _listener.Prefixes.Add(ServerUrl);
                    _listener.Start();
                    _listener.BeginGetContext(HandleRequest, null);

                    _logger.Log(LogLevel.Info, $"HTTP server started successfully at {ServerUrl}");

                    return;
                } catch (HttpListenerException ex) when (IsPortInUseError(ex)) {
                    // Port is in use, try the next port
                    CleanupListener();
                    _currentPort++;
                    portAttempts++;

                    if (portAttempts < MaxPortRetries)
                        _logger.Log(LogLevel.Warn,
                                    $"Port {_currentPort - 1} is in use (Error: {ex.ErrorCode}), trying port {_currentPort}...");
                } catch (Exception ex) {
                    CleanupListener();
                    _logger.Log(LogLevel.Error,
                                $"Unexpected error starting HTTP server on port {_currentPort}: {ex.Message}",
                                ex);

                    throw;
                }
            }

            var errorMessage =
                $"Failed to start HTTP server after trying ports {BasePort} to {_currentPort - 1}. All ports are in use.";
            _logger.Log(LogLevel.Error, errorMessage);

            throw new InvalidOperationException(errorMessage);
        } catch (Exception ex) when (!(ex is InvalidOperationException)) {
            _logger.Log(LogLevel.Error, "Failed to start HTTP server.", ex);

            throw;
        }
    }

    /// <summary>
    ///     Determines if the given HttpListenerException indicates a port is in use.
    /// </summary>
    /// <param name="ex">
    ///     The HttpListenerException to check.
    /// </param>
    /// <returns>
    ///     True if the error indicates the port is in use, false otherwise.
    /// </returns>
    private static bool IsPortInUseError(HttpListenerException ex) {
        // Common error codes for port in use:
        // 32 = ERROR_SHARING_VIOLATION
        //   (The process cannot access the file because it is being used by another process)
        // 183 = ERROR_ALREADY_EXISTS
        //   (Cannot create a file when that file already exists)
        // 10048 = WSAEADDRINUSE
        //   (Address already in use)
        return ex.ErrorCode == 32 || ex.ErrorCode == 183 || ex.ErrorCode == 10048;
    }

    /// <summary>
    ///     Safely cleans up the HTTP listener.
    /// </summary>
    private void CleanupListener() {
        if (_listener == null) return;

        try {
            _listener.Close();
        } catch {
            // Ignore cleanup errors
        }

        _listener = null;
    }

    /// <summary>
    ///     Tests if the HTTP server is responding properly by making a simple request.
    /// </summary>
    /// <returns>
    ///     True if the server is responding, false otherwise.
    /// </returns>
    private bool TestServerResponse() {
        if (!IsServerRunning) {
            _logger.Log(LogLevel.Warn, "TestServerResponse: Server is not running.");

            return false;
        }

        try {
            using (var client = new HttpClient()) {
                client.Timeout = TimeSpan.FromSeconds(5);
                var response = client.GetAsync(ServerUrl).Result;
                var isSuccess = response.IsSuccessStatusCode;

                if (isSuccess)
                    _logger.Log(LogLevel.Debug, $"Server response test successful: {response.StatusCode}");
                else
                    _logger.Log(LogLevel.Warn, $"Server response test failed: {response.StatusCode}");

                return isSuccess;
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Server response test failed with exception: {ex.Message}");

            return false;
        }
    }

    /// <summary>
    ///     Validates that the HTTP server is properly configured and ready to serve clips.
    /// </summary>
    /// <returns>
    ///     True if the server is ready, false otherwise.
    /// </returns>
    public bool ValidateServerReadiness() {
        try {
            // Check if the server is running
            if (!IsServerRunning) {
                _logger.Log(LogLevel.Error, "Server readiness check failed: HTTP server is not running.");

                return false;
            }

            // Test if the server responds to requests
            if (!TestServerResponse()) {
                _logger.Log(LogLevel.Error,
                            "Server readiness check failed: HTTP server is not responding to requests.");

                return false;
            }

            // Check if clip data is available
            if (_clipData == null) {
                _logger.Log(LogLevel.Warn, "Server readiness check: No clip data loaded, but server is ready.");

                return true; // Server is ready, just no clip loaded yet
            }

            _logger.Log(LogLevel.Info, $"Server readiness check passed: Ready to serve clips at {ServerUrl}");

            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Server readiness check failed with exception.", ex);

            return false;
        }
    }

    /// <summary>
    ///     Stops the hosting operation and shuts down any related resources such as the HTTP listener.
    /// </summary>
    /// <returns>
    ///     A Task representing the asynchronous operation of stopping the hosting process.
    /// </returns>
    public async Task StopHosting() {
        _logger.Log(LogLevel.Info, "Stopping clip hosting and HTTP server...");

        await Task.Run(() => {
                           try {
                               Client.CancelPendingRequests();
                               CleanupListener();
                               _logger.Log(LogLevel.Info, "HTTP server stopped successfully.");
                           } catch (Exception ex) {
                               _logger.Log(LogLevel.Warn, "Error occurred while stopping HTTP server.", ex);
                           }
                       });
    }

    /// <summary>
    ///     Hosts provided Twitch clip.
    /// </summary>
    /// <param name="clipData">
    ///     The clip data containing details about the Twitch clip to be hosted.
    /// </param>
    /// <returns>
    ///     True if the clip was successfully prepared for hosting, otherwise false.
    /// </returns>
    public bool HostClip(ClipData clipData) {
        try {
            if (clipData == null) {
                _logger.Log(LogLevel.Error, "Cannot host clip: clip data is null.");

                return false;
            }

            if (string.IsNullOrWhiteSpace(clipData.Id)) {
                _logger.Log(LogLevel.Error, "Cannot host clip: clip ID is null or empty.");

                return false;
            }

            // Critical validation: Ensure the HTTP server is running before hosting
            if (!IsServerRunning) {
                _logger.Log(LogLevel.Error,
                            "Cannot host clip: HTTP server is not running. Attempting to start server...");

                try {
                    StartServer();

                    if (!IsServerRunning) {
                        _logger.Log(LogLevel.Error, "Cannot host clip: Failed to start HTTP server.");

                        return false;
                    }
                } catch (Exception ex) {
                    _logger.Log(LogLevel.Error, "Cannot host clip: Failed to start HTTP server.", ex);

                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(clipData.Title))
                _logger.Log(LogLevel.Warn, "Clip title is null or empty, but proceeding with hosting.");

            _clipData = clipData;
            _logger.Log(LogLevel.Info,
                        $"Successfully prepared clip '{clipData.Title}' (ID: {clipData.Id}) for hosting at {ServerUrl}");

            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error while preparing clip for hosting.", ex);

            return false;
        }
    }

    /// <summary>
    ///     Handles incoming HTTP requests asynchronously.
    /// </summary>
    /// <param name="result">
    ///     The IAsyncResult associated with the ongoing asynchronous operation.
    /// </param>
    private void HandleRequest(IAsyncResult result) {
        if (_listener == null || !_listener.IsListening) return;

        var context = _listener.EndGetContext(result);

        _listener.BeginGetContext(HandleRequest, null);

        try {
            var requestPath = context.Request.Url.AbsolutePath;
            
            _logger.Log(LogLevel.Debug, $"Received HTTP request: {requestPath}");

            switch (requestPath) {
                case "/index.css": Task.Run(() => ServeCSS(context)); break;
                case "/favicon.ico": context.Response.StatusCode = 204; break;
                case "/content-warning-detected":
                    if (context.Request.HttpMethod == "POST") {
                        Task.Run(() => HandleContentWarningNotification(context));
                    } else {
                        context.Response.StatusCode = 405; // Method not allowed
                    }
                    break;
                case "/index.html":
                case "/":
                    Task.Run(() => ServeHTML(context));

                    break;
                default: HandleError(context, 404, $"Unknown resource requested: {requestPath}"); break;
            }
        } catch (Exception ex) {
            HandleError(context, 500, "Error while handling request.", ex);
        }
    }

    /// <summary>
    ///     Handles content warning notifications from the browser and attempts OBS automation.
    /// </summary>
    /// <param name="context">The HTTP listener context.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    private async Task HandleContentWarningNotification(HttpListenerContext context) {
        try {
            // Read the request body
            string requestBody;
            using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding)) {
                requestBody = await reader.ReadToEndAsync();
            }

            // Parse the JSON request
            var warningData = JsonConvert.DeserializeObject<dynamic>(requestBody);
            var clipId = warningData?.clipId?.ToString();
            var detectionMethod = warningData?.detectionMethod?.ToString();

            _logger.Log(LogLevel.Info, $"Content warning detected for clip {clipId} via {detectionMethod}");

            // Attempt OBS automation
            var obsResult = await AutomateContentWarningHandling();
            
            if (obsResult) {
                _logger.Log(LogLevel.Info, "OBS automation successful for content warning");
            } else {
                _logger.Log(LogLevel.Info, "=== CONTENT WARNING DETECTED ===");
                _logger.Log(LogLevel.Info, $"Clip: {clipId}");
                _logger.Log(LogLevel.Info, "To continue:");
                _logger.Log(LogLevel.Info, "1. In OBS, right-click the Browser Source");
                _logger.Log(LogLevel.Info, "2. Select 'Interact'");
                _logger.Log(LogLevel.Info, "3. Click through the age verification");
                _logger.Log(LogLevel.Info, "4. Close the interact window");
                _logger.Log(LogLevel.Info, "===============================");
            }

            // Send response
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            
            var responseData = JsonConvert.SerializeObject(new { success = true, obsAutomation = obsResult });
            var buffer = Encoding.UTF8.GetBytes(responseData);
            
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error handling content warning notification", ex);
            context.Response.StatusCode = 500;
        } finally {
            context.Response.Close();
        }
    }

    /// <summary>
    ///     Serves a CSS response to the HTTP client.
    /// </summary>
    /// <param name="context">
    ///     The HTTP listener context which contains both the request and the response objects.
    /// </param>
    /// <returns>
    ///     A Task representing the asynchronous operation of serving the CSS content.
    /// </returns>
    private static async Task ServeCSS(HttpListenerContext context) {
        context.Response.ContentType = "text/css";

        var buffer = Encoding.UTF8.GetBytes(CSSText);

        await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        context.Response.Close();
    }

    /// <summary>
    ///     Serves an HTML response for an incoming HTTP request.
    /// </summary>
    /// <param name="context">
    ///     The HTTP listener context, which represents the state of an HTTP request and response.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation of serving an HTML response.
    /// </returns>
    private async Task ServeHTML(HttpListenerContext context) {
        try {
            string responseString;

            (responseString, context) = await SetUpSite(context);

            var buffer = Encoding.UTF8.GetBytes(responseString);

            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
            context.Response.Close();
        } catch (Exception ex) {
            HandleError(context, 500, "Error while serving HTML.", ex);
        }
    }

    /// <summary>
    ///     Sets up the site by generating a nonce, preparing the headers, and returning the prepared HTML
    ///     and updated context.
    /// </summary>
    /// <param name="context">
    ///     The HTTP listener context that represents the current HTTP request and response being handled.
    /// </param>
    /// <returns>
    ///     A tuple containing the response string (HTML content of the page) and the updated HTTP listener
    ///     context.
    /// </returns>
    private async Task<(string responseString, HttpListenerContext context)> SetUpSite(HttpListenerContext context) {
        var nonce = CreateNonce();

        context = ReadyHeaders(nonce, context);

        return (await PreparePage(nonce), context);
    }

    /// <summary>
    ///     Prepares an HTML page by inserting clip and game data into placeholders.
    /// </summary>
    /// <param name="nonce">
    ///     A unique string used to ensure secure communication in the prepared page.
    /// </param>
    /// <param name="clipData">
    ///     An object containing information about the clip to be displayed on the page.
    /// </param>
    /// <returns>
    ///     A string containing the prepared HTML page with relevant clip and game data inserted.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when the nonce is null, empty, or consists only of whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when either clip data is null or the game name is null/empty.
    /// </exception>
    private async Task<string> PreparePage(string nonce = null, ClipData clipData = null) {
        try {
            // Validate input based on which mode we're in
            if (clipData != null)
                _clipData = clipData; // Store for consistency with existing implementation
            else if (_clipData == null) throw new InvalidOperationException("Clip data cannot be null.");

            if (string.IsNullOrWhiteSpace(nonce))
                throw new ArgumentNullException(nameof(nonce), "Nonce cannot be null or empty.");

            // Fetch game data with fallback handling
            var gameData = await _twitchApiManager.GetGameDataAsync(_clipData.GameId);
            var gameName = gameData?.Name;

            if (string.IsNullOrWhiteSpace(gameName)) {
                gameName = "Unknown Game";
                _logger.Log(LogLevel.Warn, $"Could not fetch game name for game ID {_clipData.GameId}");
            }

            _logger.Log(LogLevel.Debug, $"Preparing page for clip '{_clipData.Id}'...");

            return HTMLText.Replace("[[clipId]]", _clipData.Id)
                           .Replace("[[nonce]]", nonce)
                           .Replace("[[streamerName]]", _clipData.BroadcasterName)
                           .Replace("[[gameName]]", gameName)
                           .Replace("[[clipTitle]]", _clipData.Title)
                           .Replace("[[curatorName]]", _clipData.CreatorName);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error while preparing page", ex);

            throw; // Maintain the exception chain for higher-level handling
        }
    }

    /// <summary>
    ///     Sets up the necessary headers to configure the HTTP response with proper CORS headers.
    /// </summary>
    /// <param name="nonce">
    ///     A unique string used for security purposes in headers such as the Content-Security-Policy.
    /// </param>
    /// <param name="context">
    ///     The <see cref="HttpListenerContext" /> object representing the current HTTP request and
    ///     response context.
    /// </param>
    /// <returns>
    ///     The updated <see cref="HttpListenerContext" /> object with the necessary headers applied.
    /// </returns>
    private static HttpListenerContext ReadyHeaders(string nonce, HttpListenerContext context) {
        var headers = new List<Dictionary<string, string>> { GenerateCORSHeaders(nonce), GenerateCacheControlHeaders() }
                      .SelectMany(dict => dict)
                      .ToDictionary(pair => pair.Key, pair => pair.Value);

        foreach (var header in headers) context.Response.Headers[header.Key] = header.Value;

        return context;
    }

    /// <summary>
    ///     Generates a nonce value, a unique identifier, for security purposes. The nonce is a sanitized,
    ///     Base64-encoded string derived from a GUID and truncated to a specific length.
    /// </summary>
    /// <returns>
    ///     A sanitized, truncated, Base64-encoded string used as a nonce.
    /// </returns>
    private static string CreateNonce() {
        var guidBytes = Guid.NewGuid().ToByteArray();
        var base64Nonce = Convert.ToBase64String(guidBytes);

        return SanitizeNonce(base64Nonce).Substring(0, NonceLength);
    }

    /// <summary>
    ///     Sanitizes a nonce by replacing certain characters to make it URL-friendly.
    /// </summary>
    /// <param name="nonce">
    ///     The original nonce string to be sanitized.
    /// </param>
    /// <returns>
    ///     A sanitized, URL-friendly nonce string.
    /// </returns>
    private static string SanitizeNonce(string nonce) {
        return nonce.Replace("+", "-").Replace("/", "_").Replace("=", "_");
    }

    /// <summary>
    ///     Handles sending an error response to the HTTP client and logs the error details.
    /// </summary>
    /// <param name="context">
    ///     The HTTP listener context that contains the request and response objects.
    /// </param>
    /// <param name="statusCode">
    ///     The HTTP status code to send in the response.
    /// </param>
    /// <param name="errorMessage">
    ///     The error message to log and send to the client.
    /// </param>
    /// <param name="exception">
    ///     An optional exception object that provides additional details about the error.
    /// </param>
    private void HandleError(HttpListenerContext context,
                             int statusCode,
                             string errorMessage,
                             Exception exception = null) {
        try {
            _logger.Log(LogLevel.Error, errorMessage, exception);
            context.Response.StatusCode = statusCode;
            context.Response.Close();
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Failed to send HTTP error response.", ex);
        }
    }
}