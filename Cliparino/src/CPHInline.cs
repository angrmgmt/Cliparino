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
    private CliparinoCleanupManager _cleanupManager;
    private ClipManager _clipManager;
    private HttpManager _httpManager;
    private CPHLogger _logger;
    private bool _loggingEnabled;
    private ObsSceneManager _obsSceneManager;
    private TwitchApiManager _twitchApiManager;

    // ReSharper disable once UnusedMember.Global
    public void Init() {
        _loggingEnabled = GetArgument("logging", false);

        _logger = new CPHLogger(CPH, _loggingEnabled);
        _obsSceneManager = new ObsSceneManager(CPH, _logger);
        _httpManager = new HttpManager(_logger, _twitchApiManager);
        _twitchApiManager = new TwitchApiManager(CPH, _logger, _httpManager);
        _clipManager = new ClipManager(CPH, _logger, _twitchApiManager);
        _cleanupManager = new CliparinoCleanupManager(CPH, _logger);

        _httpManager.StartServer();
    }

    // ReSharper disable once UnusedMember.Global
    public bool Execute() {
        _logger.Log(LogLevel.Debug, "Cliparino Execute started.");

        try {
            var command = GetArgument("command", "");

            if (string.IsNullOrWhiteSpace(command)) {
                _logger.Log(LogLevel.Warn, "Command argument is missing.");

                return false;
            }

            _logger.Log(LogLevel.Info, $"Executing command: {command}");

            switch (command.ToLower()) {
                case "!watch": HandleWatchCommand(GetArgument("input0", "")).GetAwaiter().GetResult(); break;
                case "!so": HandleShoutoutCommand(GetArgument("input0", "")).GetAwaiter().GetResult(); break;
                case "!replay": HandleReplayCommand().GetAwaiter().GetResult(); break;
                case "!stop": HandleStopCommand().GetAwaiter().GetResult(); break;
                default:
                    _logger.Log(LogLevel.Warn, $"Unknown command: {command}");

                    return false;
            }

            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "An error occurred while executing Cliparino.", ex);

            return false;
        } finally {
            _logger.Log(LogLevel.Debug, "Cliparino Execute completed.");
        }
    }

    private async Task HandleWatchCommand(string url) {
        _logger.Log(LogLevel.Debug, "Handling !watch command.");

        try {
            var clipUrl = string.IsNullOrWhiteSpace(url) ? _clipManager.GetLastClipUrl() : url;

            if (string.IsNullOrEmpty(clipUrl)) {
                _logger.Log(LogLevel.Warn, "No valid clip URL provided.");

                return;
            }

            var clipData = await _clipManager.GetClipDataAsync(clipUrl);

            _httpManager.HostClip(clipData);
            _obsSceneManager.PlayClip(clipData);
            _clipManager.SetLastClipUrl(clipUrl);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while handling !watch command.", ex);
        }
    }

    private async Task HandleShoutoutCommand(string username) {
        _logger.Log(LogLevel.Debug, "Handling !so command.");

        try {
            if (string.IsNullOrWhiteSpace(username)) {
                _logger.Log(LogLevel.Warn, "Shoutout command received without a valid username.");

                return;
            }

            var clipSettings = GetClipSettings();
            var clipData = await Task.Run(() => _clipManager.GetRandomClip(username, clipSettings));
            var message = GetArgument("message", "");

            _twitchApiManager.SendShoutout(username, message);
            _httpManager.HostClip(clipData);
            _obsSceneManager.PlayClip(clipData);
            _clipManager.SetLastClipUrl(clipData.Url);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while handling !so command.", ex);
        }
    }

    private ClipManager.ClipSettings GetClipSettings() {
        var featuredOnly = GetArgument("featuredOnly", false);
        var maxDuration = GetArgument("maxClipSeconds", 30);
        var maxAgeDays = GetArgument("clipAgeDays", 30);

        return new ClipManager.ClipSettings(featuredOnly, maxDuration, maxAgeDays);
    }

    private async Task HandleReplayCommand() {
        _logger.Log(LogLevel.Debug, "Handling !replay command.");

        try {
            var lastClipUrl = _clipManager.GetLastClipUrl();

            if (!string.IsNullOrEmpty(lastClipUrl)) {
                var clipData = await _clipManager.GetClipDataAsync(lastClipUrl);

                _httpManager.HostClip(clipData);
                _obsSceneManager.PlayClip(clipData);
            } else {
                _logger.Log(LogLevel.Warn, "No clip available for replay.");
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while handling !replay command.", ex);
        }
    }

    private async Task HandleStopCommand() {
        _logger.Log(LogLevel.Debug, "Handling !stop command.");

        try {
            _obsSceneManager.StopClip();
            _httpManager.StopHosting();
            await _cleanupManager.CleanupResources();
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error occurred while handling !stop command.", ex);
        }
    }

    private T GetArgument<T>(string argName, T defaultValue = default) {
        return CPH.TryGetArg(argName, out T value) ? value : defaultValue;
    }
}