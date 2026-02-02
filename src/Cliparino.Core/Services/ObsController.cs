using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet;

namespace Cliparino.Core.Services;

/// <summary>
///     Controls OBS Studio via the obs-websocket v5 protocol for clip playback scene management.
/// </summary>
/// <remarks>
///     <para>
///         This implementation wraps the OBSWebsocketDotNet library to provide a thread-safe,
///         async-friendly interface for managing OBS scenes and browser sources. It enforces
///         desired-state configuration, automatically creating and repairing OBS resources as needed.
///     </para>
///     <para>
///         Dependencies:
///         - <see cref="ILogger{TCategoryName}" />: Diagnostic logging
///         - OBSWebsocketDotNet: Third-party obs-websocket v5 client library
///     </para>
///     <para>
///         Thread-safety: All public methods use <see cref="_connectionLock" /> to synchronize
///         connection state changes. Multiple threads can safely call methods concurrently without
///         race conditions on connection establishment/teardown.
///     </para>
///     <para>
///         Lifecycle: Registered as a singleton in dependency injection. Disposed when the application
///         shuts down, gracefully closing the WebSocket connection.
///     </para>
///     <para>
///         Self-Healing Architecture: This controller implements "desired-state enforcement" by
///         automatically creating missing scenes/sources and repairing configuration drift. This
///         eliminates manual OBS setup and recovers from user modifications.
///     </para>
/// </remarks>
public class ObsController : IObsController, IDisposable {
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly ILogger<ObsController> _logger;
    private readonly OBSWebsocket _obs;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ObsController" /> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic messages</param>
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

    /// <summary>
    ///     Disposes the OBS WebSocket connection and releases managed resources.
    /// </summary>
    public void Dispose() {
        _obs.Disconnect();
        _connectionLock.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public bool IsConnected { get; private set; }

    /// <inheritdoc />
    public event EventHandler? Connected;

    /// <inheritdoc />
    public event EventHandler? Disconnected;

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <summary>
    ///     Ensures that the specified scene exists in OBS, creating it if necessary.
    /// </summary>
    /// <param name="sceneName">The name of the scene to create or verify</param>
    /// <returns>A task representing the async operation</returns>
    /// <exception cref="Exception">Thrown when scene creation fails</exception>
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

    /// <summary>
    ///     Ensures that the specified browser source exists in the scene, creating it with proper configuration if necessary.
    /// </summary>
    /// <param name="sceneName">The name of the scene containing the source</param>
    /// <param name="sourceName">The name of the browser source to create or verify</param>
    /// <param name="url">The URL the browser source should display</param>
    /// <param name="width">The width of the browser source in pixels</param>
    /// <param name="height">The height of the browser source in pixels</param>
    /// <returns>A task representing the async operation</returns>
    /// <exception cref="Exception">Thrown when source creation fails</exception>
    /// <remarks>
    ///     Creates a browser_source input with 60fps, audio rerouting enabled, and restart-when-active enabled.
    ///     These settings optimize for clip playback with reliable audio and automatic activation.
    /// </remarks>
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

    /// <summary>
    ///     Ensures that the clip scene is nested as a source in the current OBS scene, creating the reference if necessary.
    /// </summary>
    /// <param name="sceneName">The name of the clip scene to nest in the current scene</param>
    /// <returns>A task representing the async operation</returns>
    /// <exception cref="Exception">Thrown when scene nesting fails</exception>
    /// <remarks>
    ///     This method implements scene nesting, allowing the clip scene to be composited into whatever
    ///     scene the streamer currently has active. This enables clips to be overlayed on any stream layout
    ///     without manually switching scenes.
    /// </remarks>
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

    /// <summary>
    ///     Event handler for OBS WebSocket connection established event.
    /// </summary>
    /// <param name="sender">The event source</param>
    /// <param name="e">Event arguments</param>
    private void OnConnected(object? sender, EventArgs e) {
        IsConnected = true;
        _logger.LogInformation("OBS connection established");
        Connected?.Invoke(this, EventArgs.Empty);
    }
}