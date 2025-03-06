#region

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;
using Streamer.bot.Common.Events;
using Twitch.Common.Models.Api;

#endregion

public class HttpManager {
    private static readonly object ServerLock = new object();
    private static string _htmlInMemory;
    private readonly CPHLogger _logger;
    private readonly object _serverLock = new object();
    private CancellationTokenSource _cancellationTokenSource;
    private HttpClient _httpClient;
    private Task _listeningTask;
    private HttpListener _server;

    public HttpManager(CPHLogger logger) {
        _logger = logger;
    }

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

    public async Task<string> GetAsync(string url, Dictionary<string, string> headers = null) {
        try {
            _logger.Log(LogLevel.Debug, $"Sending GET request to: {url}");

            using (var request = new HttpRequestMessage(HttpMethod.Get, url)) {
                if (headers != null)
                    foreach (var header in headers)
                        request.Headers.Add(header.Key, header.Value);

                using (var response = await _httpClient.SendAsync(request)) {
                    if (response.IsSuccessStatusCode) return await response.Content.ReadAsStringAsync();

                    _logger.Log(LogLevel.Error, $"HTTP GET failed: {response.StatusCode} - {response.ReasonPhrase}");

                    return null;
                }
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"HTTP GET request failed: {ex.Message}", ex);

            return null;
        }
    }

    public async Task<string> PostAsync(string url, HttpContent content, Dictionary<string, string> headers = null) {
        try {
            _logger.Log(LogLevel.Debug, $"Sending POST request to: {url}");

            using (var request = new HttpRequestMessage(HttpMethod.Post, url)) {
                request.Content = content;

                if (headers != null)
                    foreach (var header in headers)
                        request.Headers.Add(header.Key, header.Value);

                using (var response = await _httpClient.SendAsync(request)) {
                    if (response.IsSuccessStatusCode) return await response.Content.ReadAsStringAsync();

                    _logger.Log(LogLevel.Error, $"HTTP POST failed: {response.StatusCode} - {response.ReasonPhrase}");

                    return null;
                }
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"HTTP POST request failed: {ex.Message}", ex);

            return null;
        }
    }

    #region HTML and CSS Constants

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
        font: normal 400 1.5em/1 'OpenDyslexic', 'Open Sans', sans-serif;
    }

    .line3 {
        font: italic 100 1em/1 'OpenDyslexic', 'Open Sans', sans-serif;
    }
    ";

    /// <summary>
    ///     HTML template for the Cliparino browser source. The placeholders ([[clipId]], [[nonce]], etc.)
    ///     will be replaced dynamically at runtime.
    /// </summary>
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
    </body>
    </html>
    ";

    #endregion

    #region Server & Local Hosting

    /// <summary>
    ///     Semaphore for synchronizing server-related operations.
    /// </summary>
    private static readonly SemaphoreSlim ServerLockSemaphore = new SemaphoreSlim(1, 1);

    /// <summary>
    ///     Semaphore for ensuring single-threaded access to server setup logic.
    /// </summary>
    private static readonly SemaphoreSlim ServerSemaphore = new SemaphoreSlim(1, 1);

    /// <summary>
    ///     Semaphore for token-related operations.
    /// </summary>
    private static readonly SemaphoreSlim TokenSemaphore = new SemaphoreSlim(1, 1);

    /// <summary>
    ///     Configures and starts the local HTTP server. Ensures proper cleanup and resource management.
    /// </summary>
    /// <returns>
    ///     A task that resolves to <c>true</c> if the server setup is successful; <c>
    ///         false
    ///     </c> otherwise.
    /// </returns>
    private async Task<bool> ConfigureAndServe() {
        try {
            // Ensure any previous server instances are properly cleaned up.
            await CleanupServer();

            // Validate port availability. If the port is already bound, throw an exception.
            ValidatePortAvailability(8080);

            // Use a semaphore to ensure thread-safety for the setup process.
            if (!await ExecuteWithSemaphore(ServerSemaphore, nameof(ServerSemaphore), SetupServerAndTokens))
                return false;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Configuration failed: {ex.Message}");
            await CleanupServer();

            return false;
        }

        _logger.Log(LogLevel.Debug, $"{nameof(ConfigureAndServe)} executed successfully: Server setup complete.");

        return true;
    }

    /// <summary>
    ///     Handles the complete server initialization process, including token setup and starting the
    ///     listener.
    /// </summary>
    private async Task SetupServerAndTokens() {
        using (new CPHInline.ScopedSemaphore(ServerLockSemaphore, _logger)) {
            // Initialize HTTP server if it hasn't been set up.
            InitializeServer();

            // Setup token-related resources such as cancellation tokens.
            if (!await SetupTokenSemaphore()) return;

            // Start HTTP listener and configure the browser source for use.
            _listeningTask = StartListening(_server, _cancellationTokenSource.Token);
            ConfigureBrowserSource("Cliparino", "Player", "http://localhost:8080/index.htm");
        }
    }

    /// <summary>
    ///     Checks whether the specified port is available. If it is unavailable, throws an exception.
    /// </summary>
    /// <param name="port">
    ///     The port to validate.
    /// </param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the port is already in use.
    /// </exception>
    private void ValidatePortAvailability(int port) {
        if (!IsPortAvailable(port)) throw new InvalidOperationException($"Port {port} is already in use.");
    }

    /// <summary>
    ///     Initializes the HTTP server and starts listening on the specified port.
    /// </summary>
    private void InitializeServer() {
        if (_server != null) return;

        _server = new HttpListener();
        _server.Prefixes.Add("http://localhost:8080/");
        _server.Start();
        _logger.Log(LogLevel.Info, "Server initialized.");
    }

    /// <summary>
    ///     Sets up a cancellation token semaphore for resource management. Uses a semaphore to ensure
    ///     thread-safety during the setup.
    /// </summary>
    /// <returns>
    ///     Returns <c>true</c> if the semaphore was successfully set up, <c>
    ///         false
    ///     </c> otherwise.
    /// </returns>
    private async Task<bool> SetupTokenSemaphore() {
        return await ExecuteWithSemaphore(TokenSemaphore,
                                          nameof(TokenSemaphore),
                                          async () => {
                                              _cancellationTokenSource = new CancellationTokenSource();
                                              await Task.CompletedTask; // To align with async signature.
                                          });
    }

    /// <summary>
    ///     Configures the browser source with the provided parameters. Updates the browser source URL and
    ///     refreshes it.
    /// </summary>
    /// <param name="name">
    ///     The name of the browser source.
    /// </param>
    /// <param name="player">
    ///     The player name associated with the source.
    /// </param>
    /// <param name="url">
    ///     The URL to set for the browser source.
    /// </param>
    private void ConfigureBrowserSource(string name, string player, string url) {
        UpdateBrowserSource(name, player, url);
        RefreshBrowserSource();
        _logger.Log(LogLevel.Info, "Browser source configured.");
    }

    #endregion

    #region Server and Local Hosting

    /// <summary>
    ///     Starts the HTTP server, serving HTML and handling incoming requests.
    /// </summary>
    public void StartServer() {
        lock (ServerLock) {
            if (_server != null) {
                _logger.Log(LogLevel.Warn, "Server is already running.");

                return;
            }

            try {
                _server = new HttpListener();
                _server.Prefixes.Add("http://localhost:8080/"); // Bind to localhost on port 8080
                _server.Start();
                _logger.Log(LogLevel.Info, "HTTP server started at http://localhost:8080/.");

                // Launch the listening task to handle requests
                _listeningTask = Task.Run(async () => {
                                              while (_server.IsListening) {
                                                  var context = await _server.GetContextAsync();
                                                  _ = HandleHTTPRequest(context); // Fire-and-forget handling of requests
                                              }
                                          });
            } catch (Exception ex) {
                _logger.Log(LogLevel.Error, $"Could not start server: {ex.Message}");
            }
        }
    }

    /// <summary>
    ///     Stops the HTTP server and cleans up resources.
    /// </summary>
    public void StopServer() {
        lock (ServerLock) {
            if (_server == null || !_server.IsListening) {
                _logger.Log(LogLevel.Warn, "Server is not running.");

                return;
            }

            try {
                _server.Stop();
                _server.Close();
                _server = null;
                _listeningTask = null;
                _logger.Log(LogLevel.Info, "HTTP server stopped.");
            } catch (Exception ex) {
                _logger.Log(LogLevel.Error, $"Could not stop server: {ex.Message}");
            }
        }
    }

    /// <summary>
    ///     Handles the incoming HTTP request asynchronously, serving content or logging errors.
    /// </summary>
    /// <param name="context">
    ///     The HTTP context for the incoming request.
    /// </param>
    private async Task HandleHTTPRequest(HttpListenerContext context) {
        try {
            var requestPath = context.Request.Url.AbsolutePath;
            _logger.Log(LogLevel.Debug, $"Received request for {requestPath}.");

            if (requestPath == "/index.css") {
                // Serve the CSS content
                await ServeCSS(context);
            } else if (requestPath == "/index.html" || requestPath == "/") {
                // Serve the dynamically generated HTML with placeholder replacements
                await ServeHTML(context);
            } else {
                // Handle 404 Not Found
                _logger.Log(LogLevel.Warn, $"Request for unknown resource: {requestPath}");
                context.Response.StatusCode = 404;
                context.Response.Close();
            }
        } catch (Exception ex) {
            // Log and respond with an error
            _logger.Log(LogLevel.Error, $"Error handling HTTP request: {ex.Message}");
            context.Response.StatusCode = 500;
            context.Response.Close();
        }
    }

    /// <summary>
    ///     Serves the CSS content for the Cliparino interface.
    /// </summary>
    /// <param name="context">
    ///     HTTP request/response context.
    /// </param>
    /// <returns>
    ///     A Task representing the asynchronous operation.
    /// </returns>
    private async Task ServeCSS(HttpListenerContext context) {
        context.Response.ContentType = "text/css";
        var buffer = Encoding.UTF8.GetBytes(CSSText); // Use the inline CSS constant
        await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        context.Response.Close();
    }

    /// <summary>
    ///     Serves the dynamically generated HTML content with placeholders replaced.
    /// </summary>
    /// <param name="context">
    ///     HTTP request/response context.
    /// </param>
    /// <returns>
    ///     A Task representing the asynchronous operation.
    /// </returns>
    private async Task ServeHTML(HttpListenerContext context) {
        // Generate a nonce for Content-Security-Policy and placeholder replacement
        var nonce = Guid.NewGuid().ToString("N");

        // Generate the HTML with required dynamic data
        var htmlContent = GenerateHTML("clipId-placeholder", // Replace with actual clipId if available
                                       nonce,
                                       "streamer_placeholder",
                                       "game_placeholder",
                                       "clip_placeholder",
                                       "curator_placeholder");

        // Prepare response headers, including CORS
        var corsHeaders = GenerateCORSHeaders(nonce);

        foreach (var header in corsHeaders) context.Response.Headers[header.Key] = header.Value;

        // Serve the HTML content
        context.Response.ContentType = "text/html";
        var buffer = Encoding.UTF8.GetBytes(htmlContent);
        await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        context.Response.Close();
    }

    private async Task HandleHTTPRequest(HttpListenerContext context) {
        try {
            var requestPath = context.Request.Url.AbsolutePath;
            LogRequest(requestPath); // Log the incoming request using CPHLogger

            switch (requestPath) {
                case "/index.css": await ServeCSS(context); break;
                case "/index.html":
                case "/":
                    await ServeHTML(context);

                    break;
                default:
                    // Unknown path, return 404 error
                    HandleError(context, 404, $"Unknown resource requested: {requestPath}");

                    break;
            }
        } catch (Exception ex) {
            // Handle internal server error with detailed logging
            HandleError(context, 500, "Error while handling request.", ex);
        }
    }

    private async Task ServeHTML(HttpListenerContext context) {
        var nonce = Guid.NewGuid().ToString("N");

        // Apply response headers, including CORS and CSP policies
        ApplyCommonResponseHeaders(context.Response, nonce);

        var htmlContent = GenerateHTML(GetQueryParameter(context.Request.QueryString, "clipId") ?? "defaultClip",
                                       nonce,
                                       "streamer_placeholder",
                                       "game_placeholder",
                                       "clip_placeholder",
                                       "author_placeholder");

        var buffer = Encoding.UTF8.GetBytes(htmlContent);
        context.Response.ContentType = "text/html";
        await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        context.Response.Close();
    }

    #endregion

    #region Request Handling

    private const string HTMLErrorPage = "<h1>Error Generating HTML Content</h1>";
    private const string NotFoundResponse = "404 Not Found";

    /// <summary>
    ///     Starts listening for incoming HTTP requests on the given server. Handles requests in a loop
    ///     until the server stops or cancellation is requested.
    /// </summary>
    /// <param name="server">
    ///     The <see cref="HttpListener" /> instance to handle requests.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token to monitor for request cancellation.
    /// </param>
    private async Task StartListening(HttpListener server, CancellationToken cancellationToken) {
        _logger.Log(LogLevel.Info, "HTTP server started on http://localhost:8080");

        while (server.IsListening && !cancellationToken.IsCancellationRequested) {
            HttpListenerContext context = null;

            try {
                context = await server.GetContextAsync();
                await HandleRequest(context);
            } catch (HttpListenerException ex) {
                _logger.Log(LogLevel.Warn, $"Listener exception: {ex.Message}");
            } catch (Exception ex) {
                _logger.Log(LogLevel.Error, $"Unexpected error: {ex.Message}\n{ex.StackTrace}");
            } finally {
                context?.Response.Close();
            }
        }
    }

    //TODO: Sort this tf out, what a mess.
//     /// <summary>
//     ///     Handles an incoming HTTP request, providing appropriate responses based on requested paths.
//     ///     Configures CORS headers and responds with either HTML, CSS, or a 404 error.
//     /// </summary>
//     /// <param name="context">
//     ///     The <see cref="HttpListenerContext" /> representing the incoming request.
//     /// </param>
//     private async Task HandleRequest(HttpListenerContext context) {
//         try {
//             var requestPath = context.Request.Url?.AbsolutePath ?? string.Empty;
//             var nonce = ApplyCORSHeaders(context.Response);
//             string responseText;
//             string contentType;
//             HttpStatusCode statusCode;
//
//             switch (requestPath) {
//                 case "/index.css":
//                     (responseText, contentType, statusCode) = (CSSText, "text/css; charset=utf-8", HttpStatusCode.OK);
// /// Handles an incoming HTTP request to serve the Cliparino HTML content.
// /// </summary>
// /// <param name="context">HTTP context for the request.</param>
// private async Task HandleHTTPRequest(HttpListenerContext context) {
//     try {
//         // Generate a new nonce for this request
//         var nonce = Guid.NewGuid().ToString("N");
//
//                     break;
//                 case "/":
//                 case "/index.htm":
//                     (responseText, contentType, statusCode) = (GetHtmlInMemorySafe().Replace("[[nonce]]", nonce),
//                                                                "text/html; charset=utf-8", HttpStatusCode.OK);
//
//                     break;
//                 default:
//                     (responseText, contentType, statusCode) =
//                         (NotFoundResponse, "text/plain; charset=utf-8", HttpStatusCode.NotFound);
//
//                     break;
//             }
//         // Prepare the headers (including the nonce for CORS).
//         var corsHeaders = GenerateCORSHeaders(nonce);
//         foreach (var header in corsHeaders) {
//             context.Response.Headers[header.Key] = header.Value;
//         }
//
//             context.Response.StatusCode = (int)statusCode;
//         // Replace dynamic placeholders in the HTML template
//         var htmlContent = GenerateHTML(
//             context.Request.QueryString["clipId"] ?? "defaultClipId",
//             nonce,
//             "streamer_name",
//             "game_name",
//             "clip_title",
//             "curator_name"
//         );
//
//             await WriteResponse(responseText, context.Response, contentType);
//         } catch (Exception ex) {
//             _logger.Log(LogLevel.Error, $"Error handling request: {ex.Message}\n{ex.StackTrace}");
//         }
//     }
//         // Write the HTML content to the response
//         var buffer = Encoding.UTF8.GetBytes(htmlContent);
//         context.Response.ContentType = "text/html";
//         context.Response.OutputStream.Write(buffer, 0, buffer.Length);
//         context.Response.OutputStream.Close();
//     } catch (Exception ex) {
//         _logger.Log(LogLevel.Error, $"Error handling HTTP request: {ex.Message}");
//         context.Response.StatusCode = 500;
//         context.Response.Close();
//     }
// }

/// <summary>
///     Writes a response to the HTTP response stream. Handles errors during response writing and
///     ensures proper encoding.
/// </summary>
/// <param name="responseText">
///     The text to write to the response stream.
/// </param>
/// <param name="response">
///     The <see cref="HttpListenerResponse" /> object to write to.
/// </param>
/// <param name="contentType">
///     The MIME type of the response content.
/// </param>
private async Task WriteResponse(string responseText, HttpListenerResponse response, string contentType) {
        try {
            response.ContentType = contentType;

            var buffer = Encoding.UTF8.GetBytes(responseText);

            response.ContentLength64 = buffer.Length;

            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error writing response: {ex.Message}");
        }
    }

    private class ClipInfo {
        public readonly ClipData ClipData = new ClipData();

        public readonly string ClipTitle = "";

        public readonly string CuratorName = "";

        public readonly string StreamerName = "";
        public string ClipUrl = "";
    }

    /// <summary>
    ///     Writes the generated HTML content to a file and configures the server to serve it.
    /// </summary>
    /// <param name="clipData">
    ///     Data representing the clip to display in the browser.
    /// </param>
    /// <param name="token">
    ///     A cancellation token to handle cancellation of the HTML generation process.
    /// </param>
    private async Task CreateAndHostClipPageAsync(ClipData clipData, CancellationToken token) {
        try {
            _logger.Log(LogLevel.Debug, $"{nameof(CreateAndHostClipPageAsync)} called for clip ID: {clipData.Id}");

            if (token.IsCancellationRequested) return;

            // Gather clip info and related details for HTML generation.
            var clipInfo = GetClipInfo(clipData);
            var gameName = await FetchGameNameAsync(clipData.GameId, token) ?? "Unknown Game";

            if (token.IsCancellationRequested) return;

            _htmlInMemory = GenerateHtmlContent(clipInfo, gameName);
            _logger.Log(LogLevel.Debug, "Generated HTML content stored in memory.");

            if (token.IsCancellationRequested) return;

            await Task.Run(() => File.WriteAllText(Path.Combine(PathBuddy.GetCliparinoFolderPath(), "index.htm"),
                                                   _htmlInMemory),
                           token);

            if (token.IsCancellationRequested) return;

            _logger.LogHtmlContent(_htmlInMemory);

            var isConfigured = await ConfigureAndServe();

            await EnsureCliparinoInSceneAsync(CPH.ObsGetCurrentScene(), token);

            if (!await EnsureSourceExistsAndIsVisibleAsync("Cliparino", "Player"))
                _logger.Log(LogLevel.Warn, "'Player' source did not exist and failed to be added to 'Cliparino'.");

            await SetBrowserSourceAsync("http://localhost:8080/index.htm");

            // Ensure OBS registers source change, allow for cancellation requests.
            await Task.Delay(500, token);

            RefreshBrowserSource();

            if (isConfigured)
                _logger.Log(LogLevel.Info, "Server configured and ready to serve HTML content.");
            else
                _logger.Log(LogLevel.Error, "Failed to configure server.");
        } catch (OperationCanceledException) {
            _logger.Log(LogLevel.Warn, "Clip hosting operation was cancelled.");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error occurred in {nameof(CreateAndHostClipPageAsync)}: {ex.Message}");
            _logger.Log(LogLevel.Debug, ex.StackTrace);
        } finally {
            // Ensure cleanup does not block execution and accepts cancellation requests.
            await Task.Run(() => CleanupServer(), token);

            _logger.Log(LogLevel.Debug, $"{nameof(CreateAndHostClipPageAsync)} execution finished.");
        }
    }

    private static class PathBuddy {
        private static readonly string AppDataFolder = Path.Combine(GetFolderPath(ApplicationData), "Cliparino");
        private static LogDelegate _log;

        public static string GetCliparinoFolderPath() {
            return AppDataFolder;
        }

        public static void SetLogger(CPHLogger log) {
            _log = log;
        }

        public static void EnsureCliparinoFolderExists() {
            if (!Directory.Exists(AppDataFolder)) {
                Directory.CreateDirectory(AppDataFolder);
                _log(LogLevel.Debug, $"Created Cliparino folder at {AppDataFolder}.");
            } else {
                _log(LogLevel.Info, $"Cliparino folder exists at {AppDataFolder}.");
            }
        }
    }

    private void LogHtmlContent(string htmlContent) {
        var cliparinoPagePath = Path.Combine(PathBuddy.GetCliparinoFolderPath(), "cliparino.html");

        File.WriteAllText(cliparinoPagePath, htmlContent);
        _logger.Log(LogLevel.Info, $"Generated HTML for clip playback, written to {cliparinoPagePath}");
    }

    private string GenerateHtmlContent(ClipInfo clipInfo, string gameName) {
        // ReSharper disable once StringLiteralTypo - hash innit
        const string defaultClipID = "SoftHilariousNigiriPipeHype-2itiu1ZeL77SAPRM";
        var safeClipId = clipInfo.ClipData.Id ?? defaultClipID;
        var safeStreamerName = WebUtility.HtmlEncode(clipInfo.StreamerName) ?? "Unknown Streamer";
        var safeGameName = WebUtility.HtmlEncode(gameName) ?? "Unknown Game";
        var safeClipTitle = WebUtility.HtmlEncode(clipInfo.ClipTitle) ?? "Untitled Clip";
        var safeCuratorName = WebUtility.HtmlEncode(clipInfo.CuratorName) ?? "Anonymous";

        _logger.Log(LogLevel.Debug, $"Generating HTML content for clip ID: {safeClipId}");

        return HTMLText.Replace("[[clipId]]", safeClipId)
                       .Replace("[[streamerName]]", safeStreamerName)
                       .Replace("[[gameName]]", safeGameName)
                       .Replace("[[clipTitle]]", safeClipTitle)
                       .Replace("[[curatorName]]", safeCuratorName);
    }

    /// <summary>
    ///     Safely retrieves an HTML template from in-memory storage. If unavailable, returns a default
    ///     error page.
    /// </summary>
    /// <returns>
    ///     HTML content stored in memory or a default error page.
    /// </returns>
    private string GetHtmlInMemorySafe() {
        _logger.Log(LogLevel.Debug, "Attempting to retrieve HTML template from memory.");

        using (new CPHInline.ScopedSemaphore(ServerSemaphore, Log)) {
            if (string.IsNullOrWhiteSpace(_htmlInMemory)) {
                _logger.Log(LogLevel.Warn, "_htmlInMemory is null or empty. Returning default error response.");

                return HTMLErrorPage;
            }

            _logger.Log(LogLevel.Info, "Successfully retrieved HTML template from memory.");

            return _htmlInMemory;
        }
    }

    /// <summary>
    ///     Writes the generated HTML content to a file and configures the server to serve it.
    /// </summary>
    /// <param name="clipData">
    ///     Data representing the clip to display in the browser.
    /// </param>
    /// <param name="token">
    ///     A cancellation token to handle cancellation of the HTML generation process.
    /// </param>
    private async Task CreateAndHostClipPageAsync(ClipData clipData, CancellationToken token) {
        try {
            _logger.Log(LogLevel.Debug, $"{nameof(CreateAndHostClipPageAsync)} called for clip ID: {clipData.Id}");

            if (token.IsCancellationRequested) return;

            // Gather clip info and related details for HTML generation.
            var clipInfo = GetClipInfo(clipData);
            var gameName = await FetchGameNameAsync(clipData.GameId, token) ?? "Unknown Game";

            if (token.IsCancellationRequested) return;

            // Generate and store HTML content in memory.
            _htmlInMemory = GenerateHtmlContent(clipInfo, gameName);
            _logger.Log(LogLevel.Debug, "Generated HTML content stored in memory.");

            if (token.IsCancellationRequested) return;
        } catch (OperationCanceledException) {
            _logger.Log(LogLevel.Warn, "Clip hosting operation was cancelled.");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error occurred in {nameof(CreateAndHostClipPageAsync)}: {ex.Message}");
        }
    }

    #endregion

    #region Request Handling Utilities

    /// <summary>
    ///     Handles incoming HTTP errors by logging and notifying the client.
    /// </summary>
    /// <param name="context">
    ///     The HTTP request context.
    /// </param>
    /// <param name="statusCode">
    ///     The HTTP status code to return.
    /// </param>
    /// <param name="errorMessage">
    ///     The developer-facing error message to log.
    /// </param>
    /// <param name="exception">
    ///     Optional exception to include in error logs.
    /// </param>
    private void HandleError(HttpListenerContext context,
                             int statusCode,
                             string errorMessage,
                             Exception exception = null) {
        try {
            _logger.Log(LogLevel.Error, errorMessage, exception); // Leverage CPHLogger here

            context.Response.StatusCode = statusCode;
            context.Response.Close();
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Failed to send HTTP error response: {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Logs the incoming request with its path.
    /// </summary>
    /// <param name="requestPath">
    ///     The path of the incoming HTTP request.
    /// </param>
    private void LogRequest(string requestPath) {
        _logger.Log(LogLevel.Debug, $"Received HTTP request for path: {requestPath}");
    }

    /// <summary>
    ///     Centralizes CORS and header management for all HTTP responses.
    /// </summary>
    /// <param name="response">
    ///     The HTTP response to modify.
    /// </param>
    /// <param name="nonce">
    ///     The nonce generated for this response (if applicable).
    /// </param>
    private void ApplyCommonResponseHeaders(HttpListenerResponse response, string nonce = null) {
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "*";

        if (!string.IsNullOrEmpty(nonce))
            response.Headers["Content-Security-Policy"] =
                $"script-src 'nonce-{nonce}' 'strict-dynamic'; object-src 'none'; base-uri 'none'; frame-ancestors 'self' https://clips.twitch.tv;";
    }

    /// <summary>
    ///     Reads query parameters from HTTP requests.
    /// </summary>
    /// <param name="queryString">
    ///     The query string received in the request.
    /// </param>
    /// <param name="paramName">
    ///     The specific parameter name to retrieve.
    /// </param>
    /// <returns>
    ///     The parameter value if found; otherwise, null.
    /// </returns>
    private static string GetQueryParameter(NameValueCollection queryString, string paramName) {
        var value = queryString[paramName];

        return string.IsNullOrEmpty(value) ? null : value;
    }

    #endregion
}