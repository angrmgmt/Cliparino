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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
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
    private const int NonceLength = 16;
    private const string ServerUrl = "http://localhost:8080/";
    private const string HelixApiUrl = "https://api.twitch.tv/helix/";

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
    </body>
    </html>
    ";

    private readonly CPHLogger _logger;
    private readonly TwitchApiManager _twitchApiManager;
    public readonly HttpClient Client;
    private ClipData _clipData;
    private HttpListener _listener;

    public HttpManager(CPHLogger logger, TwitchApiManager twitchApiManager) {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _twitchApiManager = twitchApiManager;
        Client = new HttpClient { BaseAddress = new Uri(HelixApiUrl) };
    }

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
    ///     (i.e. Disables caching of responses on client browsers.) 
    /// </summary>
    /// <returns>
    ///     A dictionary of headers configured for cache control.
    /// </returns>
    private static Dictionary<string, string> GenerateCacheControlHeaders() {
        return new Dictionary<string, string> {
            { "Cache-Control", "no-cache, no-store, must-revalidate" },
            { "Pragma", "no-cache" },
            { "Expires", "0" },
        };
    }

    /// <summary>
    ///     Starts an HTTP server and begins listening for incoming HTTP requests.
    /// </summary>
    /// <remarks>
    ///     This method initializes an <see cref="HttpListener" />, configures it with the server URL, and
    ///     starts listening for incoming HTTP requests. If the HTTP server is already running, the method
    ///     returns without performing any action. Logs messages to indicate the success or failure of the
    ///     server startup process.
    /// </remarks>
    /// <exception cref="System.Exception">
    ///     Throws an exception if there is an error while starting the server or initializing the HTTP
    ///     listener.
    /// </exception>
    public void StartServer() {
        try {
            if (_listener != null) return;

            _listener = new HttpListener();
            _listener.Prefixes.Add(ServerUrl);
            _listener.Start();
            _listener.BeginGetContext(HandleRequest, null);

            _logger.Log(LogLevel.Info, $"HTTP server started at {ServerUrl}");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Failed to start HTTP server.", ex);
        }
    }

    /// <summary>
    ///     Stops the hosting operation and shuts down any related resources such as the HTTP listener.
    /// </summary>
    /// <returns>
    ///     A Task representing the asynchronous operation of stopping the hosting process.
    /// </returns>
    public async Task StopHosting() {
        _logger.Log(LogLevel.Info, "Stopped hosting clip.");

        await Task.Run(() => {
                           Client.CancelPendingRequests();
                           _listener?.Close();
                       });
    }

    /// <summary>
    ///     Hosts provided Twitch clip.
    /// </summary>
    /// <param name="clipData">
    ///     The clip data containing details about the Twitch clip to be hosted.
    /// </param>
    public void HostClip(ClipData clipData) {
        try {
            _clipData = clipData ?? throw new ArgumentNullException(nameof(clipData));
            _logger.Log(LogLevel.Info, $"Hosting clip {clipData.Id}");
        } catch (ArgumentNullException argEx) {
            _logger.Log(LogLevel.Error, "Error while hosting clip.", argEx);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error while hosting clip.", ex);
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
    /// <returns>
    ///     A string containing the prepared HTML page with relevant clip and game data inserted.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when the nonce is null, empty, or consists only of whitespace.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when either clip data is null or the game name is null/empty.
    /// </exception>
    private async Task<string> PreparePage(string nonce) {
        if (string.IsNullOrWhiteSpace(nonce))
            throw new ArgumentNullException(nameof(nonce), "Nonce cannot be null or empty.");

        if (_clipData == null) throw new InvalidOperationException("Clip data cannot be null.");

        var gameName = (await _twitchApiManager.FetchGameById(_clipData.GameId)).Name;

        if (string.IsNullOrWhiteSpace(gameName))
            throw new InvalidOperationException("Game name cannot be null or empty.");

        _logger.Log(LogLevel.Error, $"Preparing page for clip '{_clipData.Id}'...");

        return HTMLText.Replace("[[clipId]]", _clipData.Id)
                       .Replace("[[nonce]]", nonce)
                       .Replace("[[streamerName]]", _clipData.BroadcasterName)
                       .Replace("[[gameName]]", gameName)
                       .Replace("[[clipTitle]]", _clipData.Title)
                       .Replace("[[curatorName]]", _clipData.CreatorName);
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
        var headers = new List<Dictionary<string, string>> {
            GenerateCORSHeaders(nonce),
            GenerateCacheControlHeaders(),
        }.SelectMany(dict => dict).ToDictionary(pair => pair.Key, pair => pair.Value);

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