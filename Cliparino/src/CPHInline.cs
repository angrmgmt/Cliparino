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
using System.Threading.Tasks;
using Streamer.bot.Common.Events;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;

#endregion

// ReSharper disable once UnusedType.Global
// ReSharper disable once ClassNeverInstantiated.Global
/// <summary>
///     The main class for handling Twitch clip operations including watch, shoutout, and replay
///     commands. Interacts with Twitch and OBS APIs to stream or display clip content.
/// </summary>
public class CPHInline : CPHInlineBase {
    private static HttpManager _httpManager;
    public static Dimensions Dimensions;
    private ClipManager _clipManager;
    private bool _initialized;
    private CPHLogger _logger;
    private bool _loggingEnabled;
    private ObsSceneManager _obsSceneManager;
    private TwitchApiManager _twitchApiManager;

    // ReSharper disable once UnusedMember.Global
    public bool Execute() {
        InitializeComponents();

        if (_logger == null) {
            CPH.LogDebug("Logger is null. Attempting to reinitialize.");

            try {
                _logger = new CPHLogger(CPH, false);
                _logger.Log(LogLevel.Debug, "Logger reinitialized successfully.");
            } catch (Exception ex) {
                CPH.LogError($"Logger failed to reinitialize. {ex.Message ?? "Unknown error"}\n{ex.StackTrace}");

                return false;
            }
        }

        if (_obsSceneManager == null || _clipManager == null || _twitchApiManager == null || _httpManager == null) {
            _logger.Log(LogLevel.Error, "One or more dependencies are null after initialization.");

            return false;
        }

        _logger.Log(LogLevel.Debug, "Execute started.");

        try {
            var command = GetArgument(CPH, "command", "");

            if (string.IsNullOrWhiteSpace(command)) {
                _logger.Log(LogLevel.Error, "Command argument is missing.");

                return false;
            }

            _logger.Log(LogLevel.Info, $"Executing command: {command}");

            switch (command.ToLower()) {
                case "!watch": return HandleWatchCommand(GetArgument(CPH, "input0", "")).GetAwaiter().GetResult();
                case "!so": return HandleShoutoutCommand(GetArgument(CPH, "input0", "")).GetAwaiter().GetResult();
                case "!replay": return HandleReplayCommand().GetAwaiter().GetResult();
                case "!stop": return HandleStopCommand().GetAwaiter().GetResult();
                default:
                    _logger.Log(LogLevel.Warn, $"Unknown command received: {command}");

                    return false;
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Critical failure during execution.", ex);

            return false;
        } finally {
            _logger.Log(LogLevel.Debug, "Execute completed.");
        }
    }

    private void InitializeComponents() {
        if (_initialized) return;

        CPH.LogInfo("Cliparino :: InitializeComponents :: Initializing Cliparino components...");
        _loggingEnabled = GetArgument(CPH, "logging", false);

        var height = GetArgument(CPH, "height", 1080);
        var width = GetArgument(CPH, "width", 1920);

        Dimensions = new Dimensions(height, width);

        try {
            _logger = new CPHLogger(CPH, _loggingEnabled);
            _logger?.Log(LogLevel.Debug, "Logger initialized successfully.");
            _twitchApiManager = new TwitchApiManager(CPH, _logger);

            if (_twitchApiManager != null) _logger?.Log(LogLevel.Debug, "TwitchApiManager initialized successfully.");

            _httpManager = new HttpManager(_logger, _twitchApiManager);

            if (_httpManager != null) _logger?.Log(LogLevel.Debug, "HttpManager initialized successfully.");

            _clipManager = new ClipManager(CPH, _logger, _twitchApiManager);

            if (_clipManager != null) _logger?.Log(LogLevel.Debug, "ClipManager initialized successfully.");

            _obsSceneManager = new ObsSceneManager(CPH, _logger);

            if (_obsSceneManager != null) _logger?.Log(LogLevel.Debug, "ObsSceneManager initialized successfully.");

            _httpManager.StartServer();

            _initialized = true;
            _logger?.Log(LogLevel.Info, "Cliparino components initialized successfully.");
        } catch (Exception ex) {
            _logger?.Log(LogLevel.Error, "Initialization encountered an error", ex);
            CPH.LogError($"Critical failure during initialization. {ex.Message ?? "Unknown error"}\n{ex.StackTrace}");
        }
    }

    private async Task<bool> HandleWatchCommand(string url) {
        _logger.Log(LogLevel.Debug, "Handling !watch command.");

        try {
            var clipUrl = string.IsNullOrWhiteSpace(url) ? _clipManager.GetLastClipUrl() : url;

            if (string.IsNullOrEmpty(clipUrl)) {
                _logger.Log(LogLevel.Warn, "No valid clip URL provided.");

                return false;
            }

            var clipData = await _clipManager.GetClipDataAsync(clipUrl);

            _httpManager.HostClip(clipData);

            await _obsSceneManager.PlayClip(clipData);
            await Task.Delay((int)clipData.Duration * 1000 + 3000);
            await HandleStopCommand();

            _clipManager.SetLastClipUrl(clipUrl);

            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while handling !watch command.", ex);

            return false;
        }
    }

    private async Task<bool> HandleShoutoutCommand(string username) {
        _logger.Log(LogLevel.Debug, "Handling !so command.");

        try {
            if (string.IsNullOrWhiteSpace(username)) {
                _logger.Log(LogLevel.Warn, "Shoutout command received without a valid username.");

                return false;
            }

            var clipSettings = GetClipSettings();
            var clipData = await _clipManager.GetRandomClipAsync(username, clipSettings);

            if (clipData == null) {
                _logger.Log(LogLevel.Warn, $"No valid clip found for {username}. Skipping playback.");

                return false;
            }

            var message = GetArgument(CPH, "message", "");

            _twitchApiManager.SendShoutout(username, message);
            _httpManager.HostClip(clipData);

            await _obsSceneManager.PlayClip(clipData);
            await Task.Delay((int)clipData.Duration * 1000 + 3000);
            await HandleStopCommand();

            _clipManager.SetLastClipUrl(clipData.Url);

            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while handling !so command.", ex);

            return false;
        }
    }

    private ClipManager.ClipSettings GetClipSettings() {
        var featuredOnly = GetArgument(CPH, "featuredOnly", false);
        var maxDuration = GetArgument(CPH, "maxClipSeconds", 30);
        var maxAgeDays = GetArgument(CPH, "clipAgeDays", 30);

        return new ClipManager.ClipSettings(featuredOnly, maxDuration, maxAgeDays);
    }

    private async Task<bool> HandleReplayCommand() {
        _logger.Log(LogLevel.Debug, "Handling !replay command.");

        try {
            var lastClipUrl = _clipManager.GetLastClipUrl();

            if (!string.IsNullOrEmpty(lastClipUrl)) {
                var clipData = await _clipManager.GetClipDataAsync(lastClipUrl);

                _httpManager.HostClip(clipData);

                await _obsSceneManager.PlayClip(clipData);
                await Task.Delay((int)clipData.Duration * 1000 + 3000);
                await HandleStopCommand();

                return true;
            }

            _logger.Log(LogLevel.Warn, "No clip available for replay.");

            return false;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while handling !replay command.", ex);

            return false;
        }
    }

    // ReSharper disable once UnusedMember.Global
    public bool StopClip() {
        try {
            if (_logger == null) {
                CPH.LogError("Logger is null. Aborting stop command handling.");

                return false;
            }

            _logger.Log(LogLevel.Info, "Stopping clip.");

            HandleStopCommand().GetAwaiter().GetResult();

            return true;
        } catch (Exception ex) {
            _logger?.Log(LogLevel.Error, "Error occurred while stopping clip.", ex);

            return false;
        }
    }

    private async Task<bool> HandleStopCommand() {
        _logger.Log(LogLevel.Debug, "Handling !stop command.");

        try {
            await _obsSceneManager.StopClip();
            await _httpManager.StopHosting();

            _httpManager.Client.CancelPendingRequests();

            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while handling !stop command.", ex);

            return false;
        }
    }

    private static T GetArgument<T>(IInlineInvokeProxy cph, string argName, T defaultValue = default) {
        return cph.TryGetArg(argName, out T value) ? value : defaultValue;
    }

    public static HttpManager GetHttpManager() {
        return _httpManager;
    }
}

public class Dimensions {
    public Dimensions(int height = 1080, int width = 1920) {
        Height = height;
        Width = width;
    }

    public int Height { get; }
    public int Width { get; }
}