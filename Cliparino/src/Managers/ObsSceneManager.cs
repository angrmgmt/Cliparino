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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Streamer.bot.Common.Events;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;
using Twitch.Common.Models.Api;

#endregion

public class ObsSceneManager {
    private const string CliparinoSourceName = "Cliparino";
    private const string PlayerSourceName = "Player";
    private const string ActiveUrl = "http://localhost:8080/";
    private const string InactiveUrl = "about:blank";

    private readonly IInlineInvokeProxy _cph;
    private readonly CPHLogger _logger;

    public ObsSceneManager(IInlineInvokeProxy cph, CPHLogger logger) {
        _cph = cph ?? throw new ArgumentNullException(nameof(cph));
        _logger = logger;
    }

    private static Dimensions Dimensions => CPHInline.Dimensions;

    public async Task PlayClipAsync(ClipData clipData) {
        if (clipData == null) {
            _logger.Log(LogLevel.Error, "No clip data provided.");

            return;
        }

        var scene = CurrentScene();

        if (string.IsNullOrWhiteSpace(scene)) {
            _logger.Log(LogLevel.Error, "Unable to determine current OBS scene.");

            return;
        }

        _logger.Log(LogLevel.Info, $"Playing clip '{clipData.Title}' ({clipData.Url}).");
        SetUpCliparino();
        ShowCliparino(scene);

        await SetBrowserSourceAsync(ActiveUrl);
        await LogPlayerState();
    }

    public async Task StopClip() {
        _logger.Log(LogLevel.Info, "Stopping clip playback.");

        await LogPlayerState();
        await SetBrowserSourceAsync(InactiveUrl);

        HideCliparino(CurrentScene());
    }

    private async Task LogPlayerState() {
        await Task.Delay(1000);

        var browserSourceUrl = GetPlayerUrl().Contains("Error") ? "No URL found." : GetPlayerUrl();
        var isBrowserVisible = _cph.ObsIsSourceVisible(CliparinoSourceName, PlayerSourceName);

        _logger.Log(LogLevel.Debug,
                    $"Browser Source '{PlayerSourceName}' details - URL: {browserSourceUrl}, Visible: {isBrowserVisible}");
    }

    private string GetPlayerUrl() {
        var playerUrl = GetPlayerSettings()?["url"]?.ToString();

        _logger.Log(LogLevel.Debug, $"Player URL: {playerUrl}");

        return playerUrl ?? "Error: No URL found.";
    }

    private JObject GetPlayerSettings() {
        var payload = new Payload {
            RequestType = "GetInputSettings", RequestData = new { inputName = PlayerSourceName }
        };

        return JsonConvert.DeserializeObject<JObject>(_cph.ObsSendRaw(payload.RequestType,
                                                                      JsonConvert
                                                                          .SerializeObject(payload.RequestData)));
    }

    private string CurrentScene() {
        var currentScene = _cph.ObsGetCurrentScene();

        if (string.IsNullOrEmpty(currentScene)) _logger.Log(LogLevel.Error, "Could not find current scene.");

        return currentScene;
    }

    private void ShowCliparino(string scene) {
        try {
            if (!_cph.ObsIsSourceVisible(scene, CliparinoSourceName))
                _cph.ObsSetSourceVisibility(scene, CliparinoSourceName, true);

            if (!_cph.ObsIsSourceVisible(CliparinoSourceName, PlayerSourceName))
                _cph.ObsSetSourceVisibility(CliparinoSourceName, PlayerSourceName, true);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error while showing Cliparino in OBS.", ex);
        }
    }

    private void HideCliparino(string scene) {
        try {
            if (_cph.ObsIsSourceVisible(scene, CliparinoSourceName))
                _cph.ObsSetSourceVisibility(scene, CliparinoSourceName, false);

            if (_cph.ObsIsSourceVisible(CliparinoSourceName, PlayerSourceName))
                _cph.ObsSetSourceVisibility(CliparinoSourceName, PlayerSourceName, false);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error while hiding Cliparino in OBS.", ex);
        }
    }

    private async Task SetBrowserSourceAsync(string url) {
        try {
            _logger.Log(LogLevel.Debug, $"Setting Player URL to '{url}'.");
            await Task.Run(() => _cph.ObsSetBrowserSource(CliparinoSourceName, PlayerSourceName, url));
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error setting OBS browser source.", ex);
        }
    }

    private void SetUpCliparino() {
        try {
            if (!CliparinoExists()) {
                _logger.Log(LogLevel.Info, "Adding Cliparino scene to OBS.");
                CreateCliparinoScene();
            }

            if (!PlayerExists()) {
                _logger.Log(LogLevel.Info, "Adding Player source to Cliparino scene.");
                AddPlayerToCliparino();
                _logger.Log(LogLevel.Info, $"Configuring audio for source: {PlayerSourceName}");
                ConfigureAudioSettings();
            }

            if (CliparinoInCurrentScene()) return;

            _logger.Log(LogLevel.Info, "Adding Cliparino to current scene.");
            AddCliparinoToScene();
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error setting up Cliparino in OBS.", ex);
        }
    }

    private bool SourceIsInScene(string scene, string source) {
        var response = GetSceneItemId(scene, source);
        var itemId = response is string ? -1 : response.sceneItemId;

        return itemId > 0;
    }

    private bool PlayerExists() {
        return SourceIsInScene(CliparinoSourceName, PlayerSourceName);
    }

    private bool CliparinoInCurrentScene() {
        return SourceIsInScene(CurrentScene(), CliparinoSourceName);
    }

    private dynamic GetSceneItemId(string sceneName, string sourceName) {
        var payload = new Payload {
            RequestType = "GetSceneItemId", RequestData = new { sceneName, sourceName, searchOffset = 0 }
        };

        var response =
            JsonConvert.DeserializeObject<dynamic>(_cph.ObsSendRaw(payload.RequestType,
                                                                   JsonConvert.SerializeObject(payload.RequestData)));

        return response;
    }

    private void AddCliparinoToScene() {
        var payload = new Payload {
            RequestType = "CreateSceneItem",
            RequestData = new { sceneName = CurrentScene(), sourceName = CliparinoSourceName, sceneItemEnabled = true }
        };

        _cph.ObsSendRaw(payload.RequestType, JsonConvert.SerializeObject(payload.RequestData));

        if (CliparinoInCurrentScene())
            _logger.Log(LogLevel.Info, $"Added Cliparino to scene '{CurrentScene()}'.");
        else
            _logger.Log(LogLevel.Error, $"Failed to add Cliparino to scene '{CurrentScene()}'.");
    }

    private void AddPlayerToCliparino() {
        try {
            if (Dimensions == null) {
                _logger.Log(LogLevel.Error, "CPHInline.Dimensions is null. Cannot add Player to Cliparino.");

                return;
            }

            var height = Dimensions.Height;
            var width = Dimensions.Width;

            _logger.Log(LogLevel.Debug, $"Adding Player source to Cliparino with dimensions: {width}x{height}.");

            var inputSettings = new {
                fps = 60,
                fps_custom = true,
                height,
                reroute_audio = true,
                restart_when_active = true,
                shutdown = true,
                url = InactiveUrl,
                webpage_control_level = 2,
                width
            };
            var payload = new Payload {
                RequestType = "CreateInput",
                RequestData = new {
                    sceneName = CliparinoSourceName,
                    inputName = PlayerSourceName,
                    inputKind = "browser_source",
                    inputSettings,
                    sceneItemEnabled = true
                }
            };

            var response = _cph.ObsSendRaw(payload.RequestType, JsonConvert.SerializeObject(payload.RequestData));

            if (PlayerExists())
                _logger.Log(LogLevel.Info,
                            $"Browser source '{PlayerSourceName}' added to scene '{CliparinoSourceName}' with URL '{InactiveUrl}'.");
            else
                _logger.Log(LogLevel.Error,
                            $"Browser source '{PlayerSourceName}' could not be added.\nResponse: {response}");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "An error occurred while adding the Player source to Cliparino.", ex);
        }
    }

    private void CreateCliparinoScene() {
        try {
            var payload = new Payload {
                RequestType = "CreateScene", RequestData = new { sceneName = CliparinoSourceName }
            };

            var response = _cph.ObsSendRaw(payload.RequestType, JsonConvert.SerializeObject(payload.RequestData));

            if (CliparinoExists())
                _logger.Log(LogLevel.Info, $"Scene '{CliparinoSourceName}' created successfully.");
            else
                _logger.Log(LogLevel.Error,
                            $"Scene '{CliparinoSourceName}' could not be created.\nResponse: {response}");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error in CreateCliparinoScene: {ex.Message}");
        }
    }

    private bool CliparinoExists() {
        try {
            return SceneExists(CliparinoSourceName);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error checking if Cliparino exists in OBS.", ex);

            return false;
        }
    }

    private bool SceneExists(string sceneName) {
        try {
            var sceneExists = false;
            var response = JsonConvert.DeserializeObject<dynamic>(_cph.ObsSendRaw("GetSceneList", "{}"));

            var scenes = response?.scenes;

            _logger.Log(LogLevel.Debug, $"Scenes pulled from OBS: {JsonConvert.SerializeObject(scenes)}");

            if (scenes != null)
                foreach (var scene in scenes) {
                    if ((string)scene.sceneName != sceneName) continue;

                    sceneExists = true;

                    break;
                }

            if (!sceneExists) _logger.Log(LogLevel.Warn, $"Scene '{sceneName}' does not exist.");

            return sceneExists;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "An error occurred while checking if Cliparino scene exists.", ex);

            return false;
        }
    }

    private void ConfigureAudioSettings() {
        if (!PlayerExists()) {
            _logger.Log(LogLevel.Warn, "Cannot configure audio settings because Player source doesn't exist.");

            return;
        }

        try {
            var monitorTypePayload = GenerateSetInputAudioMonitorTypePayload("OBS_MONITORING_TYPE_MONITOR_AND_OUTPUT");
            var monitorTypeResponse = _cph.ObsSendRaw(monitorTypePayload.RequestType,
                                                      JsonConvert.SerializeObject(monitorTypePayload.RequestData));

            if (string.IsNullOrEmpty(monitorTypeResponse) || monitorTypeResponse != "{}") {
                _logger.Log(LogLevel.Error, "Failed to set monitor type for the Player source.");

                return;
            }

            var inputVolumePayload = GenerateSetInputVolumePayload(-6);
            var inputVolumeResponse = _cph.ObsSendRaw(inputVolumePayload.RequestType,
                                                      JsonConvert.SerializeObject(inputVolumePayload.RequestData));


            if (string.IsNullOrEmpty(inputVolumeResponse) || inputVolumeResponse != "{}") {
                _logger.Log(LogLevel.Warn, "Failed to set volume for the Player source.");

                return;
            }

            var gainFilterPayload = GenerateGainFilterPayload(3);
            var gainFilterResponse = _cph.ObsSendRaw(gainFilterPayload.RequestType,
                                                     JsonConvert.SerializeObject(gainFilterPayload.RequestData));

            if (string.IsNullOrEmpty(gainFilterResponse) || gainFilterResponse != "{}") {
                _logger.Log(LogLevel.Warn, "Failed to add Gain filter to the Player source.");

                return;
            }

            var compressorFilterPayload = GenerateCompressorFilterPayload();
            var compressorFilterResponse = _cph.ObsSendRaw(compressorFilterPayload.RequestType,
                                                           JsonConvert.SerializeObject(compressorFilterPayload
                                                                                           .RequestData));

            if (string.IsNullOrEmpty(compressorFilterResponse) || compressorFilterResponse != "{}") {
                _logger.Log(LogLevel.Warn, "Failed to add Compressor filter to the Player source.");

                return;
            }

            _logger.Log(LogLevel.Info, "Audio configuration for Player source completed successfully.");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "An error occurred during audio configuration setup.", ex);
        }
    }

    private static IPayload GenerateCompressorFilterPayload() {
        return new Payload {
            RequestType = "CreateSourceFilter",
            RequestData = new {
                sourceName = "Player",
                filterName = "Compressor",
                filterKind = "compressor_filter",
                filterSettings = new {
                    attack_time = 69,
                    output_gain = 0,
                    ratio = 4,
                    release_time = 120,
                    sidechain_source = "Mic/Aux",
                    threshold = -28
                }
            }
        };
    }

    private static IPayload GenerateGainFilterPayload(double gainValue) {
        return new Payload {
            RequestType = "CreateSourceFilter",
            RequestData = new {
                sourceName = "Player",
                filterName = "Gain",
                filterKind = "gain_filter",
                filterSettings = new { db = gainValue }
            }
        };
    }

    private static IPayload GenerateSetInputVolumePayload(double volumeValue) {
        return new Payload {
            RequestType = "SetInputVolume", RequestData = new { inputName = "Player", inputVolumeDb = volumeValue }
        };
    }

    private static IPayload GenerateSetInputAudioMonitorTypePayload(string monitorType) {
        return new Payload {
            RequestType = "SetInputAudioMonitorType", RequestData = new { inputName = "Player", monitorType }
        };
    }

    /// <summary>
    ///     Represents a structure for defining a payload object used in interactions or data passing,
    ///     typically containing request type and accompanying request data.
    /// </summary>
    private interface IPayload {
        /// <summary>
        ///     The type of the request being sent.
        /// </summary>
        string RequestType { get; }

        /// <summary>
        ///     The data associated with a request.
        /// </summary>
        object RequestData { get; }
    }

    /// <summary>
    ///     Represents the payload structure used to communicate with the OBS WebSocket API or other API
    ///     endpoints. Encapsulates a request type and corresponding data payload in a structured format.
    /// </summary>
    private class Payload : IPayload {
        /// <summary>
        ///     The type of request being handled.
        /// </summary>
        public string RequestType { get; set; }

        /// <summary>
        ///     The data payload for a request. This property is commonly used to define the input parameters
        ///     required for executing API or system commands.
        /// </summary>
        public object RequestData { get; set; }
    }
}