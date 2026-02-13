namespace Cliparino.Core.Services;

/// <summary>
///     Defines the contract for controlling OBS Studio via the obs-websocket protocol.
/// </summary>
/// <remarks>
///     <para>
///         This interface is implemented by <see cref="ObsController" /> and is used by
///         <see cref="ObsHealthSupervisor" /> for connection management and drift repair, and by
///         the playback system to update browser sources with clip URLs.
///     </para>
///     <para>
///         Key responsibilities:
///         - Establish and maintain WebSocket connection to OBS Studio
///         - Create and configure scenes and browser sources (desired-state enforcement)
///         - Update browser source URLs for clip playback
///         - Detect configuration drift and repair discrepancies
///         - Provide connection state events for health monitoring
///     </para>
///     <para>
///         Desired-State Enforcement: This controller follows a "just works" philosophy. When asked
///         to ensure a scene and source exist, it will create them if missing, update them if misconfigured,
///         and verify their settings match expectations. This self-healing approach prevents manual
///         OBS configuration and automatically repairs drift.
///     </para>
///     <para>
///         Protocol: Uses obs-websocket v5 protocol (compatible with OBS Studio 28+). Earlier versions
///         of OBS Studio are not supported.
///     </para>
///     <para>
///         Thread-safety: All methods are async and thread-safe. Connection state is managed internally
///         with proper synchronization. Safe to call from multiple threads, though typically accessed
///         from playback engine and health supervisor.
///     </para>
/// </remarks>
public interface IObsController {
    /// <summary>
    ///     Gets a value indicating whether the WebSocket connection to OBS is currently established.
    /// </summary>
    /// <value>
    ///     True if connected to OBS Studio; false if disconnected, connecting, or connection failed.
    /// </value>
    bool IsConnected { get; }

    /// <summary>
    ///     Establishes a WebSocket connection to OBS Studio.
    /// </summary>
    /// <param name="host">The OBS WebSocket server host (typically "localhost" or "127.0.0.1")</param>
    /// <param name="port">The OBS WebSocket server port (default: 4455)</param>
    /// <param name="password">The WebSocket password, or empty string if authentication is disabled</param>
    /// <returns>
    ///     A task containing true if the connection was established successfully, or false if the
    ///     connection failed (e.g., OBS not running, wrong password, network error).
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         This method will raise the <see cref="Connected" /> event upon successful connection.
    ///         If the connection fails, retry logic is typically handled by <see cref="ObsHealthSupervisor" />.
    ///     </para>
    ///     <para>
    ///         The connection remains active until <see cref="DisconnectAsync" /> is called, OBS closes,
    ///         or a network error occurs. The <see cref="Disconnected" /> event is raised in these cases.
    ///     </para>
    /// </remarks>
    Task<bool> ConnectAsync(string host, int port, string password);

    /// <summary>
    ///     Closes the WebSocket connection to OBS Studio gracefully.
    /// </summary>
    /// <returns>A task representing the async disconnection operation</returns>
    /// <remarks>
    ///     This method will raise the <see cref="Disconnected" /> event. Calling this method when
    ///     already disconnected has no effect.
    /// </remarks>
    Task DisconnectAsync();

    /// <summary>
    ///     Ensures that a scene and browser source exist in OBS with the specified configuration,
    ///     creating or updating them as needed (desired-state enforcement).
    /// </summary>
    /// <param name="sceneName">The name of the scene to create or verify</param>
    /// <param name="sourceName">The name of the browser source to create or verify within the scene</param>
    /// <param name="url">The URL the browser source should display</param>
    /// <param name="width">The width of the browser source in pixels</param>
    /// <param name="height">The height of the browser source in pixels</param>
    /// <returns>A task representing the async operation</returns>
    /// <remarks>
    ///     <para>
    ///         This method implements the "just works" philosophy:
    ///         - If the scene doesn't exist, it is created
    ///         - If the browser source doesn't exist in the scene, it is created
    ///         - If the source exists but has wrong dimensions or URL, it is updated
    ///         - If everything is correct, no changes are made
    ///     </para>
    ///     <para>
    ///         This self-healing approach eliminates the need for manual OBS configuration and
    ///         automatically repairs configuration drift (e.g., user accidentally modifies source settings).
    ///     </para>
    ///     <para>
    ///         Typical usage: Called by <see cref="ObsHealthSupervisor" /> on startup and periodically
    ///         to ensure the OBS configuration remains correct.
    ///     </para>
    /// </remarks>
    Task EnsureClipSceneAndSourceExistsAsync(string sceneName, string sourceName, string url, int width, int height);

    /// <summary>
    ///     Updates the URL of an existing browser source to display a different clip.
    /// </summary>
    /// <param name="sceneName">The name of the scene containing the browser source</param>
    /// <param name="sourceName">The name of the browser source to update</param>
    /// <param name="url">The new URL to display</param>
    /// <returns>
    ///     A task containing true if the URL was updated successfully, or false if the source
    ///     was not found or an error occurred.
    /// </returns>
    /// <remarks>
    ///     This method is called by <see cref="PlaybackEngine" /> when transitioning to the Playing
    ///     state to update the browser source with the current clip's URL.
    /// </remarks>
    Task<bool> SetBrowserSourceUrlAsync(string sceneName, string sourceName, string url);

    /// <summary>
    ///     Triggers a refresh of the browser source to reload its current URL.
    /// </summary>
    /// <param name="sourceName">The name of the browser source to refresh</param>
    /// <returns>
    ///     A task containing true if the refresh was triggered successfully, or false if the source
    ///     was not found or an error occurred.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         This method forces the OBS browser source to reload its page, similar to pressing F5
    ///         in a web browser. It's useful for recovering from browser rendering issues or forcing
    ///         a clip to restart.
    ///     </para>
    ///     <para>
    ///         Note: Refreshing may cause a brief visual flicker as the browser source reloads.
    ///     </para>
    /// </remarks>
    Task<bool> RefreshBrowserSourceAsync(string sourceName);

    /// <summary>
    ///     Sets the visibility of a source within a scene.
    /// </summary>
    /// <param name="sceneName">The name of the scene containing the source</param>
    /// <param name="sourceName">The name of the source to show or hide</param>
    /// <param name="visible">True to show the source; false to hide it</param>
    /// <returns>
    ///     A task containing true if the visibility was set successfully, or false if the source
    ///     was not found or an error occurred.
    /// </returns>
    /// <remarks>
    ///     This method is useful for showing/hiding the clip player without removing the source.
    ///     However, the current implementation primarily relies on the player's idle page (displayed
    ///     when no clip is active) rather than toggling visibility.
    /// </remarks>
    Task<bool> SetSourceVisibilityAsync(string sceneName, string sourceName, bool visible);

    /// <summary>
    ///     Checks if the scene and source configuration has drifted from the expected state.
    /// </summary>
    /// <param name="sceneName">The expected scene name</param>
    /// <param name="sourceName">The expected source name</param>
    /// <param name="expectedUrl">The expected browser source URL</param>
    /// <param name="expectedWidth">The expected browser source width in pixels</param>
    /// <param name="expectedHeight">The expected browser source height in pixels</param>
    /// <returns>
    ///     A task containing true if drift was detected (configuration doesn't match expectations),
    ///     or false if configuration is correct or could not be verified.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         This method is used by <see cref="ObsHealthSupervisor" /> to periodically verify that
    ///         the OBS configuration hasn't been manually changed or corrupted.
    ///     </para>
    ///     <para>
    ///         If drift is detected, the supervisor typically calls <see cref="RepairConfigurationDriftAsync" />
    ///         to repair the configuration back to the desired state.
    ///     </para>
    /// </remarks>
    Task<bool> CheckConfigurationDriftAsync(string sceneName, string sourceName, string expectedUrl, int expectedWidth,
        int expectedHeight);

    /// <summary>
    ///     Repairs configuration drift by updating the source settings to match expected values.
    /// </summary>
    /// <param name="sourceName">The name of the source to repair</param>
    /// <param name="expectedUrl">The expected browser source URL</param>
    /// <param name="expectedWidth">The expected browser source width in pixels</param>
    /// <param name="expectedHeight">The expected browser source height in pixels</param>
    /// <returns>
    ///     A task containing true if the repair was successful, or false if an error occurred.
    /// </returns>
    /// <remarks>
    ///     <para>
    ///         This method updates the browser source settings to match the expected configuration,
    ///         correcting any drift detected by <see cref="CheckConfigurationDriftAsync" />.
    ///     </para>
    ///     <para>
    ///         Upon successful repair, the <see cref="ConfigurationDriftRepaired" /> event is raised
    ///         to notify subscribers that automatic healing has occurred.
    ///     </para>
    /// </remarks>
    Task<bool> RepairConfigurationDriftAsync(string sourceName, string expectedUrl, int expectedWidth,
        int expectedHeight);

    /// <summary>
    ///     Gets the name of the currently active scene in OBS.
    /// </summary>
    /// <returns>
    ///     A task containing the name of the current scene, or null if the scene could not be
    ///     determined (e.g., not connected, error).
    /// </returns>
    /// <remarks>
    ///     This method is useful for diagnostics and determining if the clip scene is currently visible.
    /// </remarks>
    Task<string?> GetCurrentSceneAsync();

    /// <summary>
    ///     Event raised when the WebSocket connection to OBS is successfully established.
    /// </summary>
    /// <remarks>
    ///     Subscribers: <see cref="ObsHealthSupervisor" /> uses this event to trigger post-connection
    ///     verification (ensuring scene/source exist, checking for drift).
    /// </remarks>
    event EventHandler? Connected;

    /// <summary>
    ///     Event raised when the WebSocket connection to OBS is closed or lost.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This event is raised in the following scenarios:
    ///         - OBS Studio is closed
    ///         - Network connection to OBS is lost
    ///         - <see cref="DisconnectAsync" /> is called
    ///         - WebSocket error occurs
    ///     </para>
    ///     <para>
    ///         Subscribers: <see cref="ObsHealthSupervisor" /> uses this event to trigger automatic
    ///         reconnection with exponential backoff.
    ///     </para>
    /// </remarks>
    event EventHandler? Disconnected;

    /// <summary>
    ///     Event raised when a new scene is created in OBS (for notification purposes).
    /// </summary>
    event EventHandler<string>? SceneCreated;

    /// <summary>
    ///     Event raised when a new source is created in OBS (for notification purposes).
    /// </summary>
    event EventHandler<string>? SourceCreated;

    /// <summary>
    ///     Event raised when configuration drift is automatically repaired.
    /// </summary>
    event EventHandler? ConfigurationDriftRepaired;
}