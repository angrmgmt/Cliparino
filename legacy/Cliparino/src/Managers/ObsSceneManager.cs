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
    private const string CliparinoSourceName = CliparinoConstants.Obs.CliparinoSourceName;
    private const string PlayerSourceName = CliparinoConstants.Obs.PlayerSourceName;
    private const string InactiveUrl = CliparinoConstants.Http.InactiveUrl;
    private readonly IInlineInvokeProxy _cph;
    private readonly HttpManager _httpManager;
    private readonly CPHLogger _logger;

    public ObsSceneManager(IInlineInvokeProxy cph, CPHLogger logger, HttpManager httpManager) {
        _cph = cph ?? throw new ArgumentNullException(nameof(cph));
        _logger = logger;
        _httpManager = httpManager ?? throw new ArgumentNullException(nameof(httpManager));
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
    ///     A task that represents the asynchronous operation, with a boolean indicating success.
    /// </returns>
    /// <remarks>
    ///     If the clip data or the current OBS scene is not available, an error is logged and no action is
    ///     taken.
    /// </remarks>
    public async Task<bool> PlayClipAsync(ClipData clipData) {
        if (clipData == null) {
            _logger.Log(LogLevel.Error, "No clip data provided.");

            return false;
        }

        var scene = CurrentScene();

        if (string.IsNullOrWhiteSpace(scene)) {
            _logger.Log(LogLevel.Error, "Unable to determine current OBS scene.");

            return false;
        }

        _logger.Log(LogLevel.Info, $"Playing clip '{clipData.Title}' ({clipData.Url}).");

        var setupSuccess = SetUpCliparino();

        if (!setupSuccess) {
            _logger.Log(LogLevel.Error, "Failed to set up Cliparino. Cannot play clip.");

            return false;
        }

        var showSuccess = ShowCliparino(scene);

        if (!showSuccess) {
            _logger.Log(LogLevel.Error, "Failed to show Cliparino in OBS. Cannot play clip.");

            return false;
        }

        var browserSourceSuccess = await SetBrowserSourceAsync(_httpManager.ServerUrl);

        if (!browserSourceSuccess) {
            _logger.Log(LogLevel.Error, "Failed to set browser source URL. Clip will not play properly.");

            return false;
        }

        await LogPlayerState();
        _logger.Log(LogLevel.Info, $"Successfully initiated playback of clip '{clipData.Title}'.");

        return true;
    }

    /// <summary>
    ///     Stops the currently playing clip and hides the Cliparino player in OBS.
    /// </summary>
    /// <returns>
    ///     A task that represents the asynchronous operation, with a boolean indicating success.
    /// </returns>
    public async Task<bool> StopClip() {
        _logger.Log(LogLevel.Info, "Stopping clip playback.");

        try {
            await LogPlayerState();

            var browserSourceSuccess = await SetBrowserSourceAsync(InactiveUrl);

            if (!browserSourceSuccess)
                _logger.Log(LogLevel.Error, "Failed to clear browser source URL when stopping clip.");

            var hideSuccess = HideCliparino(CurrentScene());

            if (!hideSuccess) _logger.Log(LogLevel.Error, "Failed to hide Cliparino when stopping clip.");

            var overallSuccess = browserSourceSuccess && hideSuccess;

            if (overallSuccess)
                _logger.Log(LogLevel.Info, "Successfully stopped clip playback.");
            else
                _logger.Log(LogLevel.Warn, "Clip playback stopped with some issues.");

            return overallSuccess;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error stopping clip playback.", ex);

            return false;
        }
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
        try {
            var playerSettings = GetPlayerSettings();

            if (playerSettings == null) {
                _logger.Log(LogLevel.Error, "Failed to retrieve player settings.");

                return "Error: Failed to retrieve player settings.";
            }

            var playerUrl = playerSettings["inputSettings"]?["url"]?.ToString();

            if (string.IsNullOrWhiteSpace(playerUrl)) {
                _logger.Log(LogLevel.Warn, "Player URL is empty or not found in settings.");

                return "Error: No URL found in player settings.";
            }

            _logger.Log(LogLevel.Debug, $"Player URL: {playerUrl}");

            return playerUrl;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error retrieving player URL.", ex);

            return "Error: Exception occurred while retrieving URL.";
        }
    }

    /// <summary>
    ///     Retrieves the settings of the Cliparino player source in OBS.
    /// </summary>
    /// <returns>
    ///     A JSON object representing the player settings, or null if the operation failed.
    /// </returns>
    private JObject GetPlayerSettings() {
        try {
            var payload = new Payload {
                RequestType = "GetInputSettings", RequestData = new { inputName = PlayerSourceName }
            };

            var response = _cph.ObsSendRaw(payload.RequestType, JsonConvert.SerializeObject(payload.RequestData));

            if (!ValidateObsOperation("GetInputSettings", response, $"get settings for input '{PlayerSourceName}'")) {
                _logger.Log(LogLevel.Error, $"Failed to get settings for Player source '{PlayerSourceName}'.");

                return null;
            }

            var settings = JsonConvert.DeserializeObject<JObject>(response);
            _logger.Log(LogLevel.Debug, $"Successfully retrieved settings for Player source '{PlayerSourceName}'.");

            return settings;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error retrieving settings for Player source '{PlayerSourceName}'.", ex);

            return null;
        }
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
    /// <returns>
    ///     True if the sources were successfully made visible, otherwise false.
    /// </returns>
    private bool ShowCliparino(string scene) {
        try {
            _logger.Log(LogLevel.Debug, $"Showing Cliparino sources in scene '{scene}'.");

            if (!_cph.ObsIsSourceVisible(scene, CliparinoSourceName)) {
                _logger.Log(LogLevel.Debug, $"Making '{CliparinoSourceName}' visible in scene '{scene}'.");
                _cph.ObsSetSourceVisibility(scene, CliparinoSourceName, true);

                // Verify the operation was successful
                if (!_cph.ObsIsSourceVisible(scene, CliparinoSourceName)) {
                    _logger.Log(LogLevel.Error, $"Failed to make '{CliparinoSourceName}' visible in scene '{scene}'.");

                    return false;
                }

                _logger.Log(LogLevel.Debug, $"Successfully made '{CliparinoSourceName}' visible in scene '{scene}'.");
            }

            if (!_cph.ObsIsSourceVisible(CliparinoSourceName, PlayerSourceName)) {
                _logger.Log(LogLevel.Debug, $"Making '{PlayerSourceName}' visible in '{CliparinoSourceName}'.");
                _cph.ObsSetSourceVisibility(CliparinoSourceName, PlayerSourceName, true);

                // Verify the operation was successful
                if (!_cph.ObsIsSourceVisible(CliparinoSourceName, PlayerSourceName)) {
                    _logger.Log(LogLevel.Error,
                                $"Failed to make '{PlayerSourceName}' visible in '{CliparinoSourceName}'.");

                    return false;
                }

                _logger.Log(LogLevel.Debug,
                            $"Successfully made '{PlayerSourceName}' visible in '{CliparinoSourceName}'.");
            }

            _logger.Log(LogLevel.Debug, "Successfully made Cliparino sources visible.");

            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error while showing Cliparino in OBS.", ex);

            return false;
        }
    }

    /// <summary>
    ///     Hides the Cliparino sources in the specified OBS scene.
    /// </summary>
    /// <param name="scene">
    ///     The name of the scene where the Cliparino source should be hidden.
    /// </param>
    /// <returns>
    ///     True if the sources were successfully hidden, otherwise false.
    /// </returns>
    private bool HideCliparino(string scene) {
        try {
            _logger.Log(LogLevel.Debug, $"Hiding Cliparino sources in scene '{scene}'.");

            if (_cph.ObsIsSourceVisible(scene, CliparinoSourceName)) {
                _logger.Log(LogLevel.Debug, $"Hiding '{CliparinoSourceName}' in scene '{scene}'.");
                _cph.ObsSetSourceVisibility(scene, CliparinoSourceName, false);

                // Verify the operation was successful
                if (_cph.ObsIsSourceVisible(scene, CliparinoSourceName)) {
                    _logger.Log(LogLevel.Error, $"Failed to hide '{CliparinoSourceName}' in scene '{scene}'.");

                    return false;
                }

                _logger.Log(LogLevel.Debug, $"Successfully hid '{CliparinoSourceName}' in scene '{scene}'.");
            }

            if (_cph.ObsIsSourceVisible(CliparinoSourceName, PlayerSourceName)) {
                _logger.Log(LogLevel.Debug, $"Hiding '{PlayerSourceName}' in '{CliparinoSourceName}'.");
                _cph.ObsSetSourceVisibility(CliparinoSourceName, PlayerSourceName, false);

                // Verify the operation was successful
                if (_cph.ObsIsSourceVisible(CliparinoSourceName, PlayerSourceName)) {
                    _logger.Log(LogLevel.Error, $"Failed to hide '{PlayerSourceName}' in '{CliparinoSourceName}'.");

                    return false;
                }

                _logger.Log(LogLevel.Debug, $"Successfully hid '{PlayerSourceName}' in '{CliparinoSourceName}'.");
            }

            _logger.Log(LogLevel.Debug, "Successfully hid Cliparino sources.");

            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error while hiding Cliparino in OBS.", ex);

            return false;
        }
    }

    /// <summary>
    ///     Modifies the OBS browser source to use the specified URL.
    /// </summary>
    /// <param name="url">
    ///     The URL to set for the OBS browser source.
    /// </param>
    /// <returns>
    ///     A task that represents the asynchronous operation, with a boolean indicating success.
    /// </returns>
    private async Task<bool> SetBrowserSourceAsync(string url) {
        try {
            _logger.Log(LogLevel.Debug, $"Setting Player URL to '{url}'.");

            await Task.Run(() => { _cph.ObsSetBrowserSource(CliparinoSourceName, PlayerSourceName, url); });

            // Verify the URL was actually set by checking the current player URL
            // We'll give it a moment to update since this is async
            await Task.Delay(100);

            var currentUrl = GetPlayerUrl();

            if (currentUrl != null && !currentUrl.StartsWith("Error:") && currentUrl.Contains(url)) {
                _logger.Log(LogLevel.Info, $"Successfully set browser source URL to '{url}'.");

                return true;
            } else {
                _logger.Log(LogLevel.Error,
                            $"Failed to set browser source URL to '{url}'. Current URL: '{currentUrl}'. Clip may not play.");

                return false;
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error setting OBS browser source.", ex);

            return false;
        }
    }

    /// <summary>
    ///     Configures Cliparino and its player source in OBS, adding them as needed.
    /// </summary>
    /// <returns>
    ///     True if Cliparino was successfully set up, otherwise false.
    /// </returns>
    private bool SetUpCliparino() {
        try {
            _logger.Log(LogLevel.Debug, "Setting up Cliparino in OBS.");

            if (!CliparinoExists()) {
                _logger.Log(LogLevel.Info, "Adding Cliparino scene to OBS.");

                if (!CreateCliparinoScene()) {
                    _logger.Log(LogLevel.Error, "Failed to create Cliparino scene.");

                    return false;
                }
            }

            if (!PlayerExists()) {
                _logger.Log(LogLevel.Info, "Adding Player source to Cliparino scene.");

                if (!AddPlayerToCliparino()) {
                    _logger.Log(LogLevel.Error, "Failed to add Player source to Cliparino scene.");

                    return false;
                }

                _logger.Log(LogLevel.Info, $"Configuring audio for source: {PlayerSourceName}");

                if (!ConfigureAudioSettings())
                    _logger.Log(LogLevel.Warn, "Audio configuration failed, but continuing with setup.");
            }

            if (!CliparinoInCurrentScene()) {
                _logger.Log(LogLevel.Info, "Adding Cliparino to current scene.");

                if (!AddCliparinoToScene()) {
                    _logger.Log(LogLevel.Error, "Failed to add Cliparino to current scene.");

                    return false;
                }
            }

            _logger.Log(LogLevel.Info, "Cliparino setup completed successfully.");

            return true;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error setting up Cliparino in OBS.", ex);

            return false;
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
        try {
            var response = GetSceneItemId(scene, source);

            // Handle null response (error occurred)
            if (response == null) {
                _logger.Log(LogLevel.Debug,
                            $"Could not determine if source '{source}' exists in scene '{scene}' due to error.");

                return false;
            }

            // Handle sentinel value indicating not found
            if (response is string && response == "not_found") {
                _logger.Log(LogLevel.Debug, $"Source '{source}' does not exist in scene '{scene}'.");

                return false;
            }

            // Try to get the scene item ID
            var itemId = response.sceneItemId;
            var exists = itemId != null && (int)itemId > 0;

            _logger.Log(LogLevel.Debug,
                        $"Source '{source}' in scene '{scene}': {(exists ? "exists" : "does not exist")} (ID: {itemId}).");

            return exists;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error checking if source '{source}' exists in scene '{scene}'.", ex);

            return false;
        }
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
    ///     A dynamic object containing information about the scene item, or null if the operation failed.
    /// </returns>
    private dynamic GetSceneItemId(string sceneName, string sourceName) {
        try {
            var payload = new Payload {
                RequestType = "GetSceneItemId", RequestData = new { sceneName, sourceName, searchOffset = 0 }
            };

            var rawResponse = _cph.ObsSendRaw(payload.RequestType, JsonConvert.SerializeObject(payload.RequestData));

            // Note: GetSceneItemId may return an error response if the item doesn't exist, which is expected behavior
            // We'll validate but not log errors for this case since it's used to check existence
            if (rawResponse == null) {
                _logger.Log(LogLevel.Debug,
                            $"GetSceneItemId returned null for source '{sourceName}' in scene '{sceneName}'.");

                return null;
            }

            var response = JsonConvert.DeserializeObject<dynamic>(rawResponse);

            // Check if the response indicates an error (source not found)
            if (response?.error != null) {
                _logger.Log(LogLevel.Debug,
                            $"Source '{sourceName}' not found in scene '{sceneName}': {response.error}");

                return "not_found"; // Return a sentinel value to indicate not found
            }

            _logger.Log(LogLevel.Debug,
                        $"Successfully retrieved scene item ID for source '{sourceName}' in scene '{sceneName}'.");

            return response;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error,
                        $"Error retrieving scene item ID for source '{sourceName}' in scene '{sceneName}'.",
                        ex);

            return null;
        }
    }

    /// <summary>
    ///     Adds the Cliparino source to the currently active scene in OBS.
    /// </summary>
    /// <returns>
    ///     True if the Cliparino source was successfully added to the scene, otherwise false.
    /// </returns>
    private bool AddCliparinoToScene() {
        try {
            var currentScene = CurrentScene();
            var payload = new Payload {
                RequestType = "CreateSceneItem",
                RequestData = new {
                    sceneName = currentScene, sourceName = CliparinoSourceName, sceneItemEnabled = true
                }
            };

            var response = _cph.ObsSendRaw(payload.RequestType, JsonConvert.SerializeObject(payload.RequestData));

            if (!ValidateObsOperation("CreateSceneItem", response, $"add Cliparino to scene '{currentScene}'"))
                return false;

            // Double-check that Cliparino is actually in the current scene
            if (CliparinoInCurrentScene()) {
                _logger.Log(LogLevel.Info, $"Added Cliparino to scene '{currentScene}'.");

                return true;
            } else {
                _logger.Log(LogLevel.Error,
                            $"Adding Cliparino to scene '{currentScene}' appeared successful but Cliparino is not in the scene.");

                return false;
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error adding Cliparino to current scene.", ex);

            return false;
        }
    }

    /// <summary>
    ///     Adds the Player source to the Cliparino scene with proper settings and dimensions.
    /// </summary>
    /// <returns>
    ///     True if the Player source was successfully added, otherwise false.
    /// </returns>
    private bool AddPlayerToCliparino() {
        try {
            if (Dimensions == null) {
                _logger.Log(LogLevel.Error, "CPHInline.Dimensions is null. Cannot add Player to Cliparino.");

                return false;
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

            if (!ValidateObsOperation("CreateInput",
                                      response,
                                      $"create browser source '{PlayerSourceName}' in scene '{CliparinoSourceName}'"))
                return false;

            // Double-check that the player actually exists
            if (PlayerExists()) {
                _logger.Log(LogLevel.Info,
                            $"Browser source '{PlayerSourceName}' added to scene '{CliparinoSourceName}' with URL '{InactiveUrl}'.");

                return true;
            } else {
                _logger.Log(LogLevel.Error,
                            $"Browser source '{PlayerSourceName}' creation appeared successful but source does not exist.");

                return false;
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "An error occurred while adding the Player source to Cliparino.", ex);

            return false;
        }
    }

    /// <summary>
    ///     Creates the Cliparino scene in OBS.
    /// </summary>
    /// <returns>
    ///     True if the scene was successfully created, otherwise false.
    /// </returns>
    private bool CreateCliparinoScene() {
        try {
            var payload = new Payload {
                RequestType = "CreateScene", RequestData = new { sceneName = CliparinoSourceName }
            };

            var response = _cph.ObsSendRaw(payload.RequestType, JsonConvert.SerializeObject(payload.RequestData));

            if (!ValidateObsOperation("CreateScene", response, $"create scene '{CliparinoSourceName}'")) return false;

            // Double-check that the scene actually exists
            if (CliparinoExists()) {
                _logger.Log(LogLevel.Info, $"Scene '{CliparinoSourceName}' created successfully.");

                return true;
            } else {
                _logger.Log(LogLevel.Error,
                            $"Scene '{CliparinoSourceName}' creation appeared successful but scene does not exist.");

                return false;
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error in CreateCliparinoScene: {ex.Message}", ex);

            return false;
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
            var rawResponse = _cph.ObsSendRaw("GetSceneList", "{}");

            if (!ValidateObsOperation("GetSceneList", rawResponse, "retrieve scene list")) {
                _logger.Log(LogLevel.Error, "Failed to retrieve scene list from OBS.");

                return false;
            }

            var response = JsonConvert.DeserializeObject<dynamic>(rawResponse);
            var scenes = response?.scenes;

            if (scenes == null) {
                _logger.Log(LogLevel.Error, "Scene list is null or empty in OBS response.");

                return false;
            }

            _logger.Log(LogLevel.Debug, $"Found {((IEnumerable<dynamic>)scenes).Count()} scenes in OBS.");

            var sceneExists = false;

            foreach (var scene in scenes) {
                if ((string)scene.sceneName != sceneName) continue;

                sceneExists = true;

                break;
            }

            _logger.Log(LogLevel.Debug,
                        sceneExists
                            ? $"Scene '{sceneName}' exists in OBS."
                            : $"Scene '{sceneName}' does not exist in OBS.");

            return sceneExists;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"An error occurred while checking if scene '{sceneName}' exists.", ex);

            return false;
        }
    }

    /// <summary>
    ///     Configures audio settings for the Player source in OBS, including the monitoring type, volume,
    ///     and filters.
    /// </summary>
    /// <returns>
    ///     True if all audio settings were successfully configured, otherwise false.
    /// </returns>
    private bool ConfigureAudioSettings() {
        if (!PlayerExists()) {
            _logger.Log(LogLevel.Warn, "Cannot configure audio settings because Player source doesn't exist.");

            return false;
        }

        try {
            var allSuccessful = true;

            // Set the monitor type
            var monitorTypePayload = GenerateSetInputAudioMonitorTypePayload("OBS_MONITORING_TYPE_MONITOR_AND_OUTPUT");
            var monitorTypeResponse = _cph.ObsSendRaw(monitorTypePayload.RequestType,
                                                      JsonConvert.SerializeObject(monitorTypePayload.RequestData));

            if (!ValidateObsOperation("SetInputAudioMonitorType",
                                      monitorTypeResponse,
                                      "set monitor type for Player source")) {
                _logger.Log(LogLevel.Error, "Failed to set monitor type for the Player source.");
                allSuccessful = false;
            }

            // Set input volume
            var inputVolumePayload = GenerateSetInputVolumePayload(-12);
            var inputVolumeResponse = _cph.ObsSendRaw(inputVolumePayload.RequestType,
                                                      JsonConvert.SerializeObject(inputVolumePayload.RequestData));

            if (!ValidateObsOperation("SetInputVolume", inputVolumeResponse, "set volume for Player source")) {
                _logger.Log(LogLevel.Warn, "Failed to set volume for the Player source.");
                allSuccessful = false;
            }

            // Add gain filter
            var gainFilterPayload = GenerateGainFilterPayload(3);
            var gainFilterResponse = _cph.ObsSendRaw(gainFilterPayload.RequestType,
                                                     JsonConvert.SerializeObject(gainFilterPayload.RequestData));

            if (!ValidateObsOperation("CreateSourceFilter", gainFilterResponse, "add Gain filter to Player source")) {
                _logger.Log(LogLevel.Warn, "Failed to add Gain filter to the Player source.");
                allSuccessful = false;
            }

            // Add compressor filter
            var compressorFilterPayload = GenerateCompressorFilterPayload();
            var compressorFilterResponse = _cph.ObsSendRaw(compressorFilterPayload.RequestType,
                                                           JsonConvert.SerializeObject(compressorFilterPayload
                                                                                           .RequestData));

            if (!ValidateObsOperation("CreateSourceFilter",
                                      compressorFilterResponse,
                                      "add Compressor filter to Player source")) {
                _logger.Log(LogLevel.Warn, "Failed to add Compressor filter to the Player source.");
                allSuccessful = false;
            }

            if (allSuccessful)
                _logger.Log(LogLevel.Info, "Audio configuration for Player source completed successfully.");
            else
                _logger.Log(LogLevel.Warn, "Audio configuration completed with some failures.");

            return allSuccessful;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "An error occurred during audio configuration setup.", ex);

            return false;
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
    ///     Validates the response from an OBS operation to determine if it was successful.
    /// </summary>
    /// <param name="operationName">
    ///     The name of the OBS operation being validated (for logging purposes).
    /// </param>
    /// <param name="response">
    ///     The response from the OBS operation.
    /// </param>
    /// <param name="operationDescription">
    ///     A human-readable description of what the operation was attempting to do.
    /// </param>
    /// <returns>
    ///     True if the operation was successful, otherwise false.
    /// </returns>
    private bool ValidateObsOperation(string operationName, object response, string operationDescription) {
        try {
            if (response == null) {
                _logger.Log(LogLevel.Error,
                            $"OBS operation '{operationName}' returned null response while attempting to {operationDescription}.");

                return false;
            }

            var responseString = response.ToString();

            // Empty string or whitespace typically indicates success for many OBS operations
            if (string.IsNullOrWhiteSpace(responseString)) {
                _logger.Log(LogLevel.Debug,
                            $"OBS operation '{operationName}' completed successfully (empty response) for: {operationDescription}.");

                return true;
            }

            // Check for common success patterns
            if (responseString == "{}" || responseString.Equals("true", StringComparison.OrdinalIgnoreCase)) {
                _logger.Log(LogLevel.Debug,
                            $"OBS operation '{operationName}' completed successfully for: {operationDescription}.");

                return true;
            }

            // Try to parse as JSON to check for error indicators
            try {
                var jsonResponse = JsonConvert.DeserializeObject<JObject>(responseString);

                // Check for common error patterns in OBS WebSocket responses
                if (jsonResponse?["error"] != null || jsonResponse?["status"] != null) {
                    var errorMsg = jsonResponse["error"]?.ToString() ?? jsonResponse["status"]?.ToString();
                    _logger.Log(LogLevel.Error,
                                $"OBS operation '{operationName}' failed while attempting to {operationDescription}. Error: {errorMsg}");

                    return false;
                }

                // If we have a structured response without explicit errors, consider it successful
                _logger.Log(LogLevel.Debug,
                            $"OBS operation '{operationName}' completed with structured response for: {operationDescription}.");

                return true;
            } catch {
                // If JSON parsing fails, check if the response looks like an error message
                if (responseString.ToLower().Contains("error") || responseString.ToLower().Contains("fail")) {
                    _logger.Log(LogLevel.Error,
                                $"OBS operation '{operationName}' failed while attempting to {operationDescription}. Response: {responseString}");

                    return false;
                }

                // For non-JSON responses that don't look like errors, assume success but log for investigation
                _logger.Log(LogLevel.Debug,
                            $"OBS operation '{operationName}' completed with non-JSON response for: {operationDescription}. Response: {responseString}");

                return true;
            }
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error,
                        $"Error validating OBS operation '{operationName}' response while attempting to {operationDescription}.",
                        ex);

            return false;
        }
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