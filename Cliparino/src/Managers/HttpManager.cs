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
        font: normal 400 1.5em/1 'OpenDyslexic', 'Open Sans', sans-serif;
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
    private string _currentClipEmbedUrl;
    private HttpListener _listener;

    public HttpManager(CPHLogger logger, TwitchApiManager twitchApiManager) {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _twitchApiManager = twitchApiManager;
        Client = new HttpClient { BaseAddress = new Uri(HelixApiUrl) };
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

    public void StopHosting() {
        _currentClipEmbedUrl = null;
        _logger.Log(LogLevel.Info, "Stopped hosting clip.");
    }

    public void HostClip(ClipData clipData) {
        try {
            _clipData = clipData ?? throw new ArgumentNullException(nameof(clipData));
            _currentClipEmbedUrl = $"https://clips.twitch.tv/embed?clip={clipData.Id}&parent=localhost";
            _logger.Log(LogLevel.Info, $"Hosting clip {clipData.Id}");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error while hosting clip.", ex);
        }
    }

    private void HandleRequest(IAsyncResult result) {
        if (_listener == null || !_listener.IsListening) return;

        var context = _listener.EndGetContext(result);

        _listener.BeginGetContext(HandleRequest, null);

        try {
            var requestPath = context.Request.Url.AbsolutePath;
            _logger.Log(LogLevel.Debug, $"Received HTTP request: {requestPath}");

            switch (requestPath) {
                case "/index.css": Task.Run(() => ServeCSS(context)); break;
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

    private static async Task ServeCSS(HttpListenerContext context) {
        context.Response.ContentType = "text/css";

        var buffer = Encoding.UTF8.GetBytes(CSSText);

        await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);

        context.Response.Close();
    }

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

    private async Task<(string responseString, HttpListenerContext context)> SetUpSite(HttpListenerContext context) {
        var nonce = CreateNonce();

        context = ReadyHeaders(nonce, context);

        return (await PreparePage(nonce), context);
    }

    private async Task<string> PreparePage(string nonce) {
        var gameName = (await _twitchApiManager.FetchGameById(_clipData.GameId)).Name;
        var page = HTMLText.Replace("[[clipId]]", _currentClipEmbedUrl.Split('/').Last())
                           .Replace("[[nonce]]", nonce)
                           .Replace("[[streamerName]]", _clipData.BroadcasterName)
                           .Replace("[[gameName]]", gameName)
                           .Replace("[[clipTitle]]", _clipData.Title)
                           .Replace("[[curatorName]]", _clipData.CreatorName);

        return page;
    }

    private static HttpListenerContext ReadyHeaders(string nonce, HttpListenerContext context) {
        var headers = GenerateCORSHeaders(nonce);

        foreach (var header in headers) context.Response.Headers[header.Key] = header.Value;

        return context;
    }

    private static string CreateNonce() {
        var guidBytes = Guid.NewGuid().ToByteArray();
        var base64Nonce = Convert.ToBase64String(guidBytes);

        return SanitizeNonce(base64Nonce).Substring(0, NonceLength);
    }

    private static string SanitizeNonce(string nonce) {
        return nonce.Replace("+", "-").Replace("/", "_").Replace("=", "_");
    }

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