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

/// <summary>
///     Manages OBS scenes and sources for Cliparino, a clip player for Twitch.tv, ensuring seamless
///     clip playback integration with OBS.
/// </summary>
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

    /// <summary>
    ///     Gets the dimensions of the display from the Streamer.bot API.
    /// </summary>
    private static Dimensions Dimensions => CPHInline.Dimensions;

    /// <summary>
    ///     Plays a Twitch.tv clip using the Cliparino player in the current OBS scene.
    /// </summary>
    /// <param name="clipData">
    ///     The data of the clip to be played, including its URL and title.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation.
    /// </returns>
    /// <remarks>
    ///     If the clip data or the current OBS scene is not available, an error is logged and no action is
    ///     taken.
    /// </remarks>
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

    /// <summary>
    ///     Stops the currently playing clip and hides the Cliparino player in OBS.
    /// </summary>
    /// <returns>
    ///     A task that represents the asynchronous operation.
    /// </returns>
    public async Task StopClip() {
        _logger.Log(LogLevel.Info, "Stopping clip playback.");

        await LogPlayerState();
        await SetBrowserSourceAsync(InactiveUrl);

        HideCliparino(CurrentScene());
    }

    /// <summary>
    ///     Logs the current state of the OBS browser source used for Cliparino playback.
    /// </summary>
    /// <returns>
    ///     A task that represents the delay operation.
    /// </returns>
    private async Task LogPlayerState() {
        await Task.Delay(1000);

        var browserSourceUrl = GetPlayerUrl().Contains("Error") ? "No URL found." : GetPlayerUrl();
        var isBrowserVisible = _cph.ObsIsSourceVisible(CliparinoSourceName, PlayerSourceName);

        _logger.Log(LogLevel.Debug,
                    $"Browser Source '{PlayerSourceName}' details - URL: {browserSourceUrl}, Visible: {isBrowserVisible}");
    }

    /// <summary>
    ///     Retrieves the URL of the Cliparino player from the OBS settings.
    /// </summary>
    /// <returns>
    ///     The URL string of the player if found; otherwise, a message indicating no URL was found.
    /// </returns>
    private string GetPlayerUrl() {
        var playerUrl = GetPlayerSettings()?["url"]?.ToString();

        _logger.Log(LogLevel.Debug, $"Player URL: {playerUrl}");

        return playerUrl ?? "Error: No URL found.";
    }

    /// <summary>
    ///     Retrieves the settings of the Cliparino player source in OBS.
    /// </summary>
    /// <returns>
    ///     A JSON object representing the player settings.
    /// </returns>
    private JObject GetPlayerSettings() {
        var payload = new Payload {
            RequestType = "GetInputSettings", RequestData = new { inputName = PlayerSourceName }
        };

        return JsonConvert.DeserializeObject<JObject>(_cph.ObsSendRaw(payload.RequestType,
                                                                      JsonConvert
                                                                          .SerializeObject(payload.RequestData)));
    }

    /// <summary>
    ///     Retrieves the name of the currently active OBS scene.
    /// </summary>
    /// <returns>
    ///     The name of the current scene, or null if it could not be determined.
    /// </returns>
    private string CurrentScene() {
        var currentScene = _cph.ObsGetCurrentScene();

        if (string.IsNullOrEmpty(currentScene)) _logger.Log(LogLevel.Error, "Could not find current scene.");

        return currentScene;
    }

    /// <summary>
    ///     Ensures the Cliparino sources are visible in the specified OBS scene.
    /// </summary>
    /// <param name="scene">
    ///     The name of the scene where the Cliparino source should be shown.
    /// </param>
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

    /// <summary>
    ///     Hides the Cliparino sources in the specified OBS scene.
    /// </summary>
    /// <param name="scene">
    ///     The name of the scene where the Cliparino source should be hidden.
    /// </param>
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

    /// <summary>
    ///     Modifies the OBS browser source to use the specified URL.
    /// </summary>
    /// <param name="url">
    ///     The URL to set for the OBS browser source.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation.
    /// </returns>
    private async Task SetBrowserSourceAsync(string url) {
        try {
            _logger.Log(LogLevel.Debug, $"Setting Player URL to '{url}'.");
            await Task.Run(() => _cph.ObsSetBrowserSource(CliparinoSourceName, PlayerSourceName, url));
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error setting OBS browser source.", ex);
        }
    }

    /// <summary>
    ///     Configures Cliparino and its player source in OBS, adding them as needed.
    /// </summary>
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

    /// <summary>
    ///     Checks if a specific source exists in a specified scene.
    /// </summary>
    /// <param name="scene">
    ///     The name of the scene to check.
    /// </param>
    /// <param name="source">
    ///     The name of the source to look for.
    /// </param>
    /// <returns>
    ///     True if the source exists in the scene, otherwise false.
    /// </returns>
    private bool SourceIsInScene(string scene, string source) {
        var response = GetSceneItemId(scene, source);
        var itemId = response is string ? -1 : response.sceneItemId;

        return itemId > 0;
    }

    /// <summary>
    ///     Determines if the player source exists in the Cliparino scene.
    /// </summary>
    /// <returns>
    ///     True if the player source exists, otherwise false.
    /// </returns>
    private bool PlayerExists() {
        return SourceIsInScene(CliparinoSourceName, PlayerSourceName);
    }

    /// <summary>
    ///     Checks if the Cliparino source exists in the currently active scene.
    /// </summary>
    /// <returns>
    ///     True if the Cliparino source exists in the current scene, otherwise false.
    /// </returns>
    private bool CliparinoInCurrentScene() {
        return SourceIsInScene(CurrentScene(), CliparinoSourceName);
    }

    /// <summary>
    ///     Retrieves the scene item ID for a given source in a specified scene.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene.
    /// </param>
    /// <param name="sourceName">
    ///     The name of the source in the scene.
    /// </param>
    /// <returns>
    ///     A dynamic object containing information about the scene item.
    /// </returns>
    private dynamic GetSceneItemId(string sceneName, string sourceName) {
        var payload = new Payload {
            RequestType = "GetSceneItemId", RequestData = new { sceneName, sourceName, searchOffset = 0 }
        };

        var response =
            JsonConvert.DeserializeObject<dynamic>(_cph.ObsSendRaw(payload.RequestType,
                                                                   JsonConvert.SerializeObject(payload.RequestData)));

        return response;
    }

    /// <summary>
    ///     Adds the Cliparino source to the currently active scene in OBS.
    /// </summary>
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

    /// <summary>
    ///     Adds the Player source to the Cliparino scene with proper settings and dimensions.
    /// </summary>
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

    /// <summary>
    ///     Creates the Cliparino scene in OBS.
    /// </summary>
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

    /// <summary>
    ///     Checks if the Cliparino scene exists in OBS.
    /// </summary>
    /// <returns>
    ///     True if the Cliparino scene exists, otherwise false.
    /// </returns>
    private bool CliparinoExists() {
        try {
            return SceneExists(CliparinoSourceName);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error checking if Cliparino exists in OBS.", ex);

            return false;
        }
    }

    /// <summary>
    ///     Checks if a scene with the given name exists in OBS.
    /// </summary>
    /// <param name="sceneName">
    ///     The name of the scene to check.
    /// </param>
    /// <returns>
    ///     True if the scene exists, otherwise false.
    /// </returns>
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

    /// <summary>
    ///     Configures audio settings for the Player source in OBS, including the monitoring type, volume,
    ///     and filters.
    /// </summary>
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

    /// <summary>
    ///     Generates a payload for creating a compressor filter.
    /// </summary>
    /// <returns>
    ///     An instance of a payload object for creating a compressor filter.
    /// </returns>
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

    /// <summary>
    ///     Generates a payload for creating a gain filter with a specified gain value.
    /// </summary>
    /// <param name="gainValue">
    ///     The gain value in decibels.
    /// </param>
    /// <returns>
    ///     An instance of a payload object for creating a gain filter.
    /// </returns>
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

    /// <summary>
    ///     Generates a payload for setting the volume of an input source in OBS.
    /// </summary>
    /// <param name="volumeValue">
    ///     The volume level in decibels.
    /// </param>
    /// <returns>
    ///     An instance of a payload object for setting input volume.
    /// </returns>
    private static IPayload GenerateSetInputVolumePayload(double volumeValue) {
        return new Payload {
            RequestType = "SetInputVolume", RequestData = new { inputName = "Player", inputVolumeDb = volumeValue }
        };
    }

    /// <summary>
    ///     Generates a payload for setting the audio monitoring type of the associated input source in
    ///     OBS.
    /// </summary>
    /// <param name="monitorType">
    ///     The monitor type as a string (e.g., "OBS_MONITORING_TYPE_MONITOR_AND_OUTPUT").
    /// </param>
    /// <returns>
    ///     An instance of a payload object for setting the monitoring type.
    /// </returns>
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