#region

using System;
using Newtonsoft.Json;
using Streamer.bot.Common.Events;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;

#endregion

public class ObsManager {
    private const string CliparinoSourceName = "Cliparino";
    private const string PlayerSourceName = "Player";
    private const string GetSceneItemIdErrorCode = "{\"code\":600}";

    private const string GetSceneItemIdErrorMessage =
        "Error: No scene items were found in the specified scene by that name or offset.";

    private const string ActiveUrl = "http://localhost:8080/index.htm";
    private const string InactiveUrl = "about:blank";
    private const string PressInputPropertiesButtonErrorCode = "{\"code\":600}";
    private const string PressInputPropertiesButtonErrorMessage = "Error: Unable to find a property by that name.";
    private const int DefaultWidth = 1920;
    private const int DefaultHeight = 1080;
    private readonly IInlineInvokeProxy _cph;
    private readonly CPHLogger _logger;

    private readonly object Defaults = new {
        defaultInputSettings = new {
            css = "body { background-color: rgba(0, 0, 0, 0); margin: 0px auto; overflow: hidden; }",
            fps = 30,
            fps_custom = false,
            height = 600,
            reroute_audio = false,
            restart_when_active = false,
            shutdown = false,
            url = "https://obsproject.com/browser-source",
            webpage_control_level = 1,
            width = 800
        }
    };

    public ObsManager(IInlineInvokeProxy cph, CPHLogger logger) {
        _cph = cph;
        _logger = logger;
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

    public bool SceneExists(string sceneName) {
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
            _logger.Log(LogLevel.Error, $"Error in SceneExists: {ex.Message}");

            return false;
        }
    }

    public void SetUpCliparino() {
        var currentScene = _cph.ObsGetCurrentScene();

        if (string.IsNullOrEmpty(currentScene)) throw new Exception("Could not find current scene.");

        CreateCliparinoScene();
        AddPlayerToCliparino();
        AddCliparinoToScene();
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

    private void AddPlayerToCliparino() {
        try {
            _cph.ObsSetBrowserSource(CliparinoSourceName, PlayerSourceName, InactiveUrl);

            _logger.Log(LogLevel.Info,
                        $"Browser source '{PlayerSourceName}' added to scene '{CliparinoSourceName}' with URL '{InactiveUrl}'.");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error in AddPlayerToCliparino: {ex.Message}");
        }
    }

    private void AddCliparinoToScene() {
        var payload = new Payload {
            RequestType = "CreateSceneItem",
            RequestData = new { sceneName = CurrentScene(), sourceName = CliparinoSourceName, sceneItemEnabled = true }
        };

        _cph.ObsSendRaw(payload.RequestType, JsonConvert.SerializeObject(payload.RequestData));
    }

    private string CurrentScene() {
        var currentScene = _cph.ObsGetCurrentScene();

        if (string.IsNullOrEmpty(currentScene)) throw new Exception("Could not find current scene.");

        return currentScene;
    }

    private bool IsSourceVisible(string sceneName, string sourceName) {
        return _cph.ObsIsSourceVisible(sceneName, sourceName);
    }

    private bool IsPlayerVisible() {
        return IsSourceVisible(CliparinoSourceName, PlayerSourceName);
    }

    private bool IsCliparinoVisible() {
        return IsSourceVisible(CurrentScene(), CliparinoSourceName);
    }

    private void HideCliparino() {
        _cph.ObsHideSource(CurrentScene(), CliparinoSourceName);
        _cph.ObsHideSource(CliparinoSourceName, PlayerSourceName);
    }

    private void ShowCliparino() {
        _cph.ObsShowSource(CliparinoSourceName, PlayerSourceName);
        _cph.ObsShowSource(CurrentScene(), CliparinoSourceName);
    }

    private void RefreshBrowserSource() {
        var payload = new Payload {
            RequestType = "PressInputPropertiesButton",
            RequestData = new { inputName = "Player", propertyName = "refreshnocache" }
        };
        var response = _cph.ObsSendRaw(payload.RequestType, JsonConvert.SerializeObject(payload.RequestData));

        _logger.Log(LogLevel.Info, $"Refreshed browser source 'Player'. Response: {response}");
    }
}

public interface IPayload {
    string RequestType { get; }

    object RequestData { get; }
}

public class Payload : IPayload {
    public string RequestType { get; set; }

    public object RequestData { get; set; }
}

internal class MiscFunctions {
    private async Task EnsureCliparinoInCurrentSceneAsync(string currentScene, CancellationToken token) {
        try {
            currentScene = EnsureSceneIsNotNullOrEmpty(currentScene);

            if (await SourceExistsInSceneAsync(currentScene, "Cliparino")) {
                _logger.Log(LogLevel.Debug, "'Cliparino' is already present in the active scene.");

                return;
            }

            if (token.IsCancellationRequested) return;

            await EnsureCliparinoInSceneAsync(currentScene, token);
        } catch (OperationCanceledException) {
            _logger.Log(LogLevel.Warn, "Cliparino scene setup was cancelled.");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error in {nameof(EnsureCliparinoInCurrentSceneAsync)}: {ex.Message}");
        }
    }

    private string EnsureSceneIsNotNullOrEmpty(string currentScene) {
        if (string.IsNullOrWhiteSpace(currentScene)) currentScene = _cph.ObsGetCurrentScene();

        if (!string.IsNullOrWhiteSpace(currentScene)) return currentScene;

        _logger.Log(LogLevel.Warn, "Current scene is empty or null.");

        throw new InvalidOperationException("Current scene is required.");
    }

    private void EnsureClipSourceHidden() {
        var currentScene = _cph.ObsGetCurrentScene();
        const string clipSourceName = "Cliparino";

        _logger.Log(LogLevel.Info,
                    EnsureSourceExistsAndIsVisibleAsync(currentScene, clipSourceName, false).GetAwaiter().GetResult()
                        ? $"{nameof(EnsureClipSourceHidden)} reports {clipSourceName} is visible."
                        : $"{nameof(EnsureClipSourceHidden)} reports {clipSourceName} is hidden.");
    }

    private async Task HostClipDataAsync(ClipData clipData,
                                         string url,
                                         int width,
                                         int height,
                                         CancellationToken token) {
        _logger.Log(LogLevel.Info, $"Setting browser source with URL: {url}, width: {width}, height: {height}");

        var currentScene = _cph.ObsGetCurrentScene();

        if (string.IsNullOrWhiteSpace(currentScene)) {
            _logger.Log(LogLevel.Warn, "Unable to determine the current OBS scene. Aborting clip setup.");

            return;
        }

        if (token.IsCancellationRequested) return;

        await PrepareSceneForClipHostingAsync(token);
        await ProcessAndHostClipDataAsync(url, clipData, token);
    }

    private async Task EnsureCliparinoInSceneAsync(string currentScene, CancellationToken token) {
        try {
            if (string.IsNullOrWhiteSpace(currentScene)) return;

            if (token.IsCancellationRequested) return;

            if (!await AddCliparinoSourceToSceneAsync(currentScene, CliparinoSourceName, token))
                // Retry _logger.Logic to ensure addition
                for (var i = 0; i < 3; i++) {
                    if (await IsCliparinoSourceInScene(currentScene, CliparinoSourceName)) {
                        _logger.Log(LogLevel.Info,
                                    $"Successfully added 'Cliparino' to scene '{currentScene}' after {i + 1} attempts.");

                        break;
                    }

                    _logger.Log(LogLevel.Warn,
                                $"Retry {i + 1}: 'Cliparino' not found in '{currentScene}'. Retrying...");

                    await Task.Delay(1000, token);
                }

            // Final check before failing
            if (!await IsCliparinoSourceInScene(currentScene, CliparinoSourceName))
                _logger.Log(LogLevel.Error,
                            $"Failed to add 'Cliparino' to scene '{currentScene}' after multiple attempts.");
        } catch (OperationCanceledException) {
            _logger.Log(LogLevel.Warn, "Scene setup operation cancelled.");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error in {nameof(EnsureCliparinoInSceneAsync)}: {ex.Message}");
        }
    }

    private async Task<bool> IsCliparinoSourceInScene(string scene, string sourceName) {
        if (string.IsNullOrWhiteSpace(scene) || string.IsNullOrWhiteSpace(sourceName)) {
            _logger.Log(LogLevel.Warn, "Scene or source name is invalid.");

            return false;
        }

        try {
            var sourceExistsInScene = await SourceExistsInSceneAsync(scene, sourceName);

            if (sourceExistsInScene) {
                _logger.Log(LogLevel.Info, $"{nameof(IsCliparinoSourceInScene)} reports source is in scene.");

                return true;
            }

            _logger.Log(LogLevel.Warn, $"Cliparino source is not in scene '{scene}'.");

            return false;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"{CreateErrorPreamble()} {ex.Message}.");

            return false;
        }
    }

    private async Task<bool> AddCliparinoSourceToSceneAsync(string scene, string sourceName, CancellationToken token) {
        await Task.Run(() => AddSceneSource(scene, sourceName), token);

        _logger.Log(LogLevel.Info, $"Cliparino source added to scene '{scene}'.");

        return await IsCliparinoSourceInScene(scene, sourceName);
    }

    private async Task PrepareSceneForClipHostingAsync(CancellationToken token) {
        _logger.Log(LogLevel.Info, $"Preparing scene '{CliparinoSourceName}' for clip hosting.");

        if (!SceneExists(CliparinoSourceName)) CreateScene(CliparinoSourceName);

        if (token.IsCancellationRequested) return;

        if (!await EnsureSourceExistsAndIsVisibleAsync(CliparinoSourceName, PlayerSourceName))
            _logger.Log(LogLevel.Warn, "'Player' source did not exist and failed to be added to 'Cliparino'.");

        await ConfigureAudioForPlayerSourceAsync(token);
    }

    private void EnsurePlayerSourceIsVisible(string sceneName, string sourceName) {
        if (_cph.ObsIsSourceVisible(sceneName, sourceName)) return;

        _logger.Log(LogLevel.Info, $"Setting source '{sourceName}' to visible in scene '{sceneName}'.");
        _cph.ObsSetSourceVisibility(sceneName, sourceName, true);

        _logger.Log(LogLevel.Warn,
                    $"The source '{sourceName}' does not exist in scene '{sceneName}'. Attempting to add.");
        AddBrowserSource(sceneName, sourceName, "http://localhost:8080/index.htm");
    }

    private async Task ProcessAndHostClipDataAsync(string clipUrl, ClipData clipData, CancellationToken token) {
        try {
            _logger.Log(LogLevel.Info, $"Processing and hosting clip with URL: {clipUrl}");

            var validatedClipData = clipData ?? await FetchValidClipDataWithCache(null, clipUrl);

            if (validatedClipData == null) {
                _logger.Log(LogLevel.Error, "Validated clip data is null. Aborting hosting process.");

                return;
            }

            await HostClipWithDetailsAsync(clipUrl, validatedClipData, token);
        } catch (OperationCanceledException) {
            _logger.Log(LogLevel.Warn, "Clip processing was cancelled.");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error in {nameof(ProcessAndHostClipDataAsync)}: {ex.Message}");
        }
    }

    private async Task<bool> EnsureSourceExistsAndIsVisibleAsync(string sceneName,
                                                                 string sourceName,
                                                                 bool setVisible = true) {
        _logger.Log(LogLevel.Debug, $"Ensuring source '{sourceName}' exists and is visible in scene '{sceneName}'.");

        if (!await SourceExistsInSceneAsync(sceneName, sourceName)) {
            _logger.Log(LogLevel.Warn,
                        $"The source '{sourceName}' does not exist in scene '{sceneName}'. Attempting to add.");

            AddBrowserSource(sceneName, sourceName, "http://localhost:8080/index.htm");

            // Wait briefly for OBS to register the change
            await Task.Delay(500);

            // Retry fetching scene sources to confirm addition
            for (var i = 0; i < 3; i++) {
                if (await SourceExistsInSceneAsync(sceneName, sourceName)) {
                    _logger.Log(LogLevel.Info,
                                $"Successfully added '{sourceName}' to '{sceneName}' after {i + 1} attempts.");

                    break;
                }

                _logger.Log(LogLevel.Warn, $"Retry {i + 1}: '{sourceName}' not found in '{sceneName}'. Retrying...");

                await Task.Delay(1000);
            }

            // Final check before failing
            if (!await SourceExistsInSceneAsync(sceneName, sourceName)) {
                _logger.Log(LogLevel.Error,
                            $"Failed to create the '{sourceName}' source in the '{sceneName}' scene after multiple attempts.");

                return false;
            }
        }

        if (!setVisible) return true;

        if (_cph.ObsIsSourceVisible(sceneName, sourceName)) return true;

        _logger.Log(LogLevel.Info, $"Setting source '{sourceName}' to visible in scene '{sceneName}'.");
        _cph.ObsSetSourceVisibility(sceneName, sourceName, true);

        return true;
    }

    private async Task SetBrowserSourceAsync(string baseUrl, string targetScene = null) {
        _logger.Log(LogLevel.Debug, $"{nameof(SetBrowserSourceAsync)} was called for URL '{baseUrl}'.");

        var sourceUrl = CreateSourceUrl(baseUrl);
        if (targetScene == null) targetScene = _cph.ObsGetCurrentScene();

        if (string.IsNullOrEmpty(targetScene)) throw new InvalidOperationException("Unable to retrieve target scene.");

        await UpdateOrAddBrowserSourceAsync(targetScene, sourceUrl, "Cliparino", baseUrl);
    }

    private async Task UpdateOrAddBrowserSourceAsync(string targetScene,
                                                     string sourceUrl,
                                                     string sourceName,
                                                     string baseUrl) {
        if (!await SourceExistsInSceneAsync(targetScene, sourceName)) {
            AddSceneSource(targetScene, sourceName);
            _logger.Log(LogLevel.Info, $"Added '{sourceName}' scene source to '{targetScene}'.");
        } else {
            UpdateBrowserSource(targetScene, sourceName, sourceUrl);

            if (baseUrl != "about:blank") return;

            _logger.Log(LogLevel.Info, "Hiding Cliparino source after setting 'about:blank'.");
            _cph.ObsSetSourceVisibility(targetScene, sourceName, false);
        }
    }

    private void UpdateBrowserSource(string sceneName, string sourceName, string url) {
        _logger.Log(LogLevel.Debug, $"Update URL to OBS: {url}");

        try {
            var payload = new {
                requestType = "SetInputSettings",
                requestData = new { inputName = sourceName, inputSettings = new { url }, overlay = true }
            };

            var response = _cph.ObsSendRaw(payload.requestType, JsonConvert.SerializeObject(payload.requestData));
            _logger.Log(LogLevel.Info,
                        $"Browser source '{sourceName}' in scene '{sceneName}' updated with new URL '{url}'.");
            _logger.Log(LogLevel.Debug, $"Response from OBS: {response}");

            RefreshBrowserSource();
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error in {nameof(UpdateBrowserSource)}: {ex.Message}");
        }
    }

    private Task ConfigureAudioForPlayerSourceAsync(CancellationToken token) {
        var monitorTypePayload = GenerateSetAudioMonitorTypePayload("monitorAndOutput");
        var monitorTypeResponse = _cph.ObsSendRaw(monitorTypePayload.RequestType,
                                                  JsonConvert.SerializeObject(monitorTypePayload.RequestData));

        if (token.IsCancellationRequested) return Task.FromCanceled(token);

        if (string.IsNullOrEmpty(monitorTypeResponse) || monitorTypeResponse != "{}") {
            _logger.Log(LogLevel.Error, "Failed to set monitor type for the Player source.");

            return Task.CompletedTask;
        }

        if (token.IsCancellationRequested) return Task.FromCanceled(token);

        var inputVolumePayload = GenerateSetInputVolumePayload(0);
        var inputVolumeResponse = _cph.ObsSendRaw(inputVolumePayload.RequestType,
                                                  JsonConvert.SerializeObject(inputVolumePayload.RequestData));


        if (string.IsNullOrEmpty(inputVolumeResponse) || inputVolumeResponse != "{}") {
            _logger.Log(LogLevel.Warn, "Failed to set volume for the Player source.");

            return Task.CompletedTask;
        }

        if (token.IsCancellationRequested) return Task.FromCanceled(token);

        var gainFilterPayload = GenerateGainFilterPayload(0);
        var gainFilterResponse = _cph.ObsSendRaw(gainFilterPayload.RequestType,
                                                 JsonConvert.SerializeObject(gainFilterPayload.RequestData));

        if (token.IsCancellationRequested) return Task.FromCanceled(token);

        if (string.IsNullOrEmpty(gainFilterResponse) || gainFilterResponse != "{}") {
            _logger.Log(LogLevel.Warn, "Failed to add Gain filter to the Player source.");

            return Task.CompletedTask;
        }

        if (token.IsCancellationRequested) return Task.FromCanceled(token);

        var compressorFilterPayload = GenerateCompressorFilterPayload();
        var compressorFilterResponse = _cph.ObsSendRaw(compressorFilterPayload.RequestType,
                                                       JsonConvert.SerializeObject(compressorFilterPayload
                                                                                       .RequestData));

        if (string.IsNullOrEmpty(compressorFilterResponse) || compressorFilterResponse != "{}") {
            _logger.Log(LogLevel.Warn, "Failed to add Compressor filter to the Player source.");

            return Task.CompletedTask;
        }

        if (token.IsCancellationRequested) return Task.FromCanceled(token);

        _logger.Log(LogLevel.Info, "Audio configuration for Player source completed successfully.");

        return Task.CompletedTask;
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

    private Task<bool> SourceExistsInSceneAsync(string sceneName, string sourceName) {
        var sceneItemId = GetSceneItemId(sceneName, sourceName);

        return Task.FromResult(sceneItemId != -1);
    }

    public async Task<bool> EnsureSceneExistsAsync(string sceneName) {
        if (SceneExists(sceneName)) return true;

        CreateCliparinoScene();
        await Task.Delay(500); // Allow OBS to process

        return SceneExists(sceneName);
    }

    public async Task<bool> EnsureSourceExistsAsync(string sceneName, string sourceName, string sourceUrl) {
        if (SourceExists(sceneName, sourceName)) return true;

        CreatePlayer(sceneName, sourceName, sourceUrl);
        await Task.Delay(500);

        return SourceExists(sceneName, sourceName);
    }

    public void SetSourceVisibility(string sceneName, string sourceName, bool isVisible) {
        _cph.ObsSetSourceVisibility(sceneName, sourceName, isVisible);
    }

    private bool SourceExists(string sceneName, string sourceName) {
        var response = GetSceneItemId(sceneName, sourceName);

        return response != GetSceneItemIdErrorCode && response != GetSceneItemIdErrorMessage;
    }
}