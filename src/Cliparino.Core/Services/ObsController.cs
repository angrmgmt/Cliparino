using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;

namespace Cliparino.Core.Services;

public class ObsController : IObsController, IDisposable {
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly ILogger<ObsController> _logger;
    private readonly OBSWebsocket _obs;

    public ObsController(ILogger<ObsController> logger) {
        _logger = logger;
        _obs = new OBSWebsocket();

        _obs.Connected += OnConnected;
        _obs.Disconnected += (_, _) => {
            IsConnected = false;
            _logger.LogWarning("OBS disconnected");
            Disconnected?.Invoke(this, EventArgs.Empty);
        };
    }

    public void Dispose() {
        _obs.Disconnect();
        _connectionLock.Dispose();
        GC.SuppressFinalize(this);
    }

    public bool IsConnected { get; private set; }

    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    public async Task<bool> ConnectAsync(string host, int port, string password) {
        await _connectionLock.WaitAsync();

        try {
            if (IsConnected) {
                _logger.LogWarning("Already connected to OBS");

                return true;
            }

            _logger.LogInformation("Connecting to OBS at {Host}:{Port}", host, port);

            await Task.Run(() => {
                    try {
                        _obs.ConnectAsync($"ws://{host}:{port}", password);
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Failed to connect to OBS");

                        throw;
                    }
                }
            );

            await Task.Delay(1000);

            if (_obs.IsConnected) {
                IsConnected = true;
                _logger.LogInformation("Successfully connected to OBS");

                return true;
            }

            _logger.LogError("Failed to connect to OBS");

            return false;
        } catch (Exception ex) {
            _logger.LogError(ex, "Error connecting to OBS");

            return false;
        } finally {
            _connectionLock.Release();
        }
    }

    public async Task DisconnectAsync() {
        await _connectionLock.WaitAsync();

        try {
            if (!IsConnected) return;

            _logger.LogInformation("Disconnecting from OBS");
            _obs.Disconnect();
            IsConnected = false;
        } finally {
            _connectionLock.Release();
        }
    }

    public async Task EnsureClipSceneAndSourceExistsAsync(
        string sceneName, string sourceName, string url, int width, int height
    ) {
        if (!IsConnected) {
            _logger.LogWarning("Cannot ensure scene/source exists: Not connected to OBS");

            throw new InvalidOperationException("Not connected to OBS");
        }

        _logger.LogInformation("Ensuring scene '{SceneName}' and source '{SourceName}' exist", sceneName, sourceName);

        await EnsureSceneExistsAsync(sceneName);

        await EnsureSourceExistsAsync(sceneName, sourceName, url, width, height);

        await EnsureSourceInCurrentSceneAsync(sceneName);
    }

    public async Task<bool> SetBrowserSourceUrlAsync(string sceneName, string sourceName, string url) {
        if (!IsConnected) {
            _logger.LogWarning("Cannot set browser source URL: Not connected to OBS");

            return false;
        }

        try {
            await Task.Run(() => {
                    _logger.LogInformation("Setting browser source '{SourceName}' URL to '{Url}'", sourceName, url);

                    var inputSettings = new JObject {
                        ["url"] = url
                    };

                    _obs.SetInputSettings(sourceName, inputSettings);
                    _logger.LogInformation("Browser source URL updated successfully");
                }
            );

            return true;
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to set browser source URL");

            return false;
        }
    }

    public async Task<bool> RefreshBrowserSourceAsync(string sourceName) {
        if (!IsConnected) {
            _logger.LogWarning("Cannot refresh browser source: Not connected to OBS");

            return false;
        }

        try {
            await Task.Run(() => {
                    _logger.LogInformation("Refreshing browser source '{SourceName}'", sourceName);
                    _obs.PressInputPropertiesButton(sourceName, "refreshnocache");
                    _logger.LogInformation("Browser source refreshed successfully");
                }
            );

            return true;
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to refresh browser source");

            return false;
        }
    }

    public async Task<bool> SetSourceVisibilityAsync(string sceneName, string sourceName, bool visible) {
        if (!IsConnected) {
            _logger.LogWarning("Cannot set source visibility: Not connected to OBS");

            return false;
        }

        try {
            await Task.Run(() => {
                    _logger.LogDebug(
                        "Setting source '{SourceName}' visibility to {Visible} in scene '{SceneName}'", sourceName,
                        visible,
                        sceneName
                    );

                    var sceneItems = _obs.GetSceneItemList(sceneName);
                    var sceneItem = sceneItems.FirstOrDefault(item => item.SourceName == sourceName);

                    if (sceneItem != null) {
                        _obs.SetSceneItemEnabled(sceneName, sceneItem.ItemId, visible);
                        _logger.LogDebug("Source visibility updated successfully");
                    } else {
                        _logger.LogWarning(
                            "Source '{SourceName}' not found in scene '{SceneName}'", sourceName, sceneName
                        );
                    }
                }
            );

            return true;
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to set source visibility");

            return false;
        }
    }

    public async Task<bool> CheckConfigurationDriftAsync(
        string sceneName, string sourceName, string expectedUrl, int expectedWidth, int expectedHeight
    ) {
        if (!IsConnected) {
            _logger.LogWarning("Cannot check configuration drift: Not connected to OBS");

            return false;
        }

        try {
            return await Task.Run(() => {
                    var settings = _obs.GetInputSettings(sourceName);

                    var actualUrl = settings.Settings["url"]?.ToString() ?? "";
                    var actualWidth = settings.Settings["width"]?.ToObject<int>() ?? 0;
                    var actualHeight = settings.Settings["height"]?.ToObject<int>() ?? 0;

                    var urlDrift = actualUrl != expectedUrl;
                    var widthDrift = actualWidth != expectedWidth;
                    var heightDrift = actualHeight != expectedHeight;

                    if (urlDrift || widthDrift || heightDrift) {
                        _logger.LogWarning(
                            "Configuration drift detected for source '{SourceName}': URL={UrlDrift}, Width={WidthDrift}, Height={HeightDrift}",
                            sourceName, urlDrift, widthDrift, heightDrift
                        );

                        return true;
                    }

                    _logger.LogDebug("No configuration drift detected for source '{SourceName}'", sourceName);

                    return false;
                }
            );
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to check configuration drift");

            return false;
        }
    }

    public async Task<string?> GetCurrentSceneAsync() {
        if (!IsConnected) {
            _logger.LogWarning("Cannot get current scene: Not connected to OBS");

            return null;
        }

        try {
            return await Task.Run(() => {
                    var scene = _obs.GetCurrentProgramScene();
                    _logger.LogDebug("Current scene: {Scene}", scene);

                    return scene;
                }
            );
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to get current scene");

            return null;
        }
    }

    private async Task EnsureSceneExistsAsync(string sceneName) {
        await Task.Run(() => {
                try {
                    var scenes = _obs.ListScenes();
                    var sceneExists = scenes.Any(s => s.Name == sceneName);

                    if (!sceneExists) {
                        _logger.LogInformation("Creating scene '{SceneName}'", sceneName);
                        _obs.CreateScene(sceneName);
                        _logger.LogInformation("Scene '{SceneName}' created successfully", sceneName);
                    } else {
                        _logger.LogDebug("Scene '{SceneName}' already exists", sceneName);
                    }
                } catch (Exception ex) {
                    _logger.LogError(ex, "Failed to ensure scene '{SceneName}' exists", sceneName);

                    throw;
                }
            }
        );
    }

    private async Task EnsureSourceExistsAsync(string sceneName, string sourceName, string url, int width, int height) {
        await Task.Run(() => {
                try {
                    var sceneItems = _obs.GetSceneItemList(sceneName);
                    var sourceExists = sceneItems.Any(item => item.SourceName == sourceName);

                    if (!sourceExists) {
                        _logger.LogInformation(
                            "Creating browser source '{SourceName}' in scene '{SceneName}'", sourceName, sceneName
                        );

                        var inputSettings = new JObject {
                            ["url"] = url,
                            ["width"] = width,
                            ["height"] = height,
                            ["fps"] = 60,
                            ["fps_custom"] = true,
                            ["reroute_audio"] = true,
                            ["restart_when_active"] = true,
                            ["shutdown"] = true,
                            ["webpage_control_level"] = 2
                        };

                        var inputId = _obs.CreateInput(sceneName, sourceName, "browser_source", inputSettings, true);
                        _logger.LogInformation(
                            "Browser source '{SourceName}' created with ID {InputId}", sourceName, inputId
                        );
                    } else {
                        _logger.LogDebug(
                            "Source '{SourceName}' already exists in scene '{SceneName}'", sourceName, sceneName
                        );
                    }
                } catch (Exception ex) {
                    _logger.LogError(
                        ex, "Failed to ensure source '{SourceName}' exists in scene '{SceneName}'", sourceName,
                        sceneName
                    );

                    throw;
                }
            }
        );
    }

    private async Task EnsureSourceInCurrentSceneAsync(string sceneName) {
        await Task.Run(() => {
                try {
                    var currentScene = _obs.GetCurrentProgramScene();

                    if (currentScene != sceneName) {
                        var currentSceneItems = _obs.GetSceneItemList(currentScene);
                        var sceneSourceExists = currentSceneItems.Any(item => item.SourceName == sceneName);

                        if (!sceneSourceExists) {
                            _logger.LogInformation(
                                "Adding scene '{SceneName}' to current scene '{CurrentScene}'", sceneName, currentScene
                            );
                            var sceneItemId = _obs.CreateSceneItem(currentScene, sceneName);
                            _logger.LogInformation(
                                "Scene '{SceneName}' added to current scene with ID {SceneItemId}", sceneName,
                                sceneItemId
                            );
                        } else {
                            _logger.LogDebug("Scene '{SceneName}' already exists in current scene", sceneName);
                        }
                    }
                } catch (Exception ex) {
                    _logger.LogError(ex, "Failed to ensure scene exists in current scene");

                    throw;
                }
            }
        );
    }

    private void OnConnected(object? sender, EventArgs e) {
        IsConnected = true;
        _logger.LogInformation("OBS connection established");
        Connected?.Invoke(this, EventArgs.Empty);
    }
}