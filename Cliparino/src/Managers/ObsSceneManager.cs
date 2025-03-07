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
using Newtonsoft.Json;
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
    private const string GetSceneItemIdErrorCode = "{\"code\":600}";

    private const string GetSceneItemIdErrorMessage =
        "Error: No scene items were found in the specified scene by that name or offset.";

    private readonly IInlineInvokeProxy _cph;
    private readonly CPHLogger _logger;

    public ObsSceneManager(IInlineInvokeProxy cph, CPHLogger logger) {
        _cph = cph ?? throw new ArgumentNullException(nameof(cph));
        _logger = logger;
    }

    public void PlayClip(ClipData clipData) {
        if (clipData == null) {
            _logger.Log(LogLevel.Error, "No clip data provided.");

            return;
        }

        var scene = CurrentScene();

        if (string.IsNullOrWhiteSpace(scene)) {
            _logger.Log(LogLevel.Error, "Unable to determine current OBS scene.");

            return;
        }

        SetUpCliparino();
        ShowCliparino(scene);
        SetBrowserSource(ActiveUrl);
    }

    public void StopClip() {
        _logger.Log(LogLevel.Info, "Stopping clip playback.");
        SetBrowserSource(InactiveUrl);
        HideCliparino(CurrentScene());
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

    private void SetBrowserSource(string url) {
        try {
            _cph.ObsSetBrowserSource(CliparinoSourceName, PlayerSourceName, url);
            ConfigureAudioSettings();
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
        var notAnError = response != GetSceneItemIdErrorCode && response != GetSceneItemIdErrorMessage;
        var gotAnId = response.sceneItemId is int;

        return notAnError && gotAnId;
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
    }

    private void AddPlayerToCliparino() {
        try {
            _cph.ObsSetBrowserSource(CliparinoSourceName, PlayerSourceName, InactiveUrl);

            _logger.Log(LogLevel.Info,
                        $"Browser source '{PlayerSourceName}' added to scene '{CliparinoSourceName}' with URL '{InactiveUrl}'.");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "An error occurred while adding the Player source to Cliparino.", ex);
        }
    }

    private void CreateCliparinoScene() {
        try {
            var payload = new Payload { RequestType = "CreateScene", RequestData = new { CliparinoSourceName } };

            _cph.ObsSendRaw(payload.RequestType, JsonConvert.SerializeObject(payload.RequestData));
            _logger.Log(LogLevel.Info, $"Scene '{CliparinoSourceName}' created successfully.");
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

            _logger.Log(LogLevel.Debug,
                        $"The result of the scene list request was {JsonConvert.SerializeObject(response)}");

            var scenes = response?.scenes;

            _logger.Log(LogLevel.Debug, $"Scenes pulled from OBS: {JsonConvert.SerializeObject(scenes)}");

            if (scenes != null)
                foreach (var scene in scenes) {
                    if ((string)scene.sceneName != sceneName) continue;

                    sceneExists = true;

                    break;
                }

            if (!sceneExists) _logger.Log(LogLevel.Warn, $"Scene '{sceneName}' does not exist.");

            _logger.Log(LogLevel.Debug, $"Scene {sceneName} exists: {sceneExists}");

            return sceneExists;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "An error occurred while checking if Cliparino scene exists.", ex);

            return false;
        }
    }

    private void ConfigureAudioSettings() {
        try {
            var monitorTypePayload = GenerateSetAudioMonitorTypePayload("monitorAndOutput");
            var monitorTypeResponse = _cph.ObsSendRaw(monitorTypePayload.RequestType,
                                                      JsonConvert.SerializeObject(monitorTypePayload.RequestData));

            if (string.IsNullOrEmpty(monitorTypeResponse) || monitorTypeResponse != "{}") {
                _logger.Log(LogLevel.Error, "Failed to set monitor type for the Player source.");

                return;
            }

            var inputVolumePayload = GenerateSetInputVolumePayload(0);
            var inputVolumeResponse = _cph.ObsSendRaw(inputVolumePayload.RequestType,
                                                      JsonConvert.SerializeObject(inputVolumePayload.RequestData));


            if (string.IsNullOrEmpty(inputVolumeResponse) || inputVolumeResponse != "{}") {
                _logger.Log(LogLevel.Warn, "Failed to set volume for the Player source.");

                return;
            }

            var gainFilterPayload = GenerateGainFilterPayload(0);
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
            RequestType = "SetInputSettings",
            RequestData = new {
                inputName = "Player",
                inputSettings = new {
                    threshold = -15.0,
                    ratio = 4.0,
                    attack = 1.0,
                    release = 50.0,
                    makeUpGain = 0.0
                }
            }
        };
    }

    private static IPayload GenerateGainFilterPayload(double gainValue) {
        return new Payload {
            RequestType = "SetInputSettings",
            RequestData = new { inputName = "Player", inputSettings = new { gain = gainValue } }
        };
    }

    private static IPayload GenerateSetInputVolumePayload(double volumeValue) {
        return new Payload {
            RequestType = "SetInputVolume",
            RequestData = new { inputName = "Player", inputSettings = new { volume = volumeValue } }
        };
    }

    private static IPayload GenerateSetAudioMonitorTypePayload(string monitorType) {
        return new Payload {
            RequestType = "SetAudioMonitorType",
            RequestData = new { inputName = "Player", inputSettings = new { monitorType } }
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