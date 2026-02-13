using System.Diagnostics;
using System.Drawing.Drawing2D;
using Cliparino.Core.Extensions;
using Cliparino.Core.Services;

namespace Cliparino.Core.UI;

/// <summary>
///     WinForms <see cref="ApplicationContext" /> that hosts the system tray icon and its menu interactions.
/// </summary>
/// <remarks>
///     <para>
///         Cliparino runs as a hybrid application: an ASP.NET Core web host (player page + API) plus a Windows tray UI.
///         This context owns the tray icon lifetime and launches UI actions (settings, logs, diagnostics, update checks).
///     </para>
///     <para>
///         Threading: all menu callbacks run on the WinForms UI thread. Any long-running work must be awaited
///         asynchronously
///         to keep the UI responsive.
///     </para>
/// </remarks>
public class TrayApplicationContext : ApplicationContext {
    private static readonly Icon AppIcon = LoadAppIcon();
    private readonly IServiceProvider _services;
    private readonly NotifyIcon _trayIcon;
    private bool _isCheckingForUpdates;
    private ComponentStatus _lastObsStatus = ComponentStatus.Unknown;
    private ComponentStatus _lastTwitchStatus = ComponentStatus.Unknown;
    private SettingsForm? _settingsForm;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TrayApplicationContext" /> class.
    /// </summary>
    /// <param name="services">
    ///     Root service provider used to resolve application services for UI-triggered operations.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    public TrayApplicationContext(IServiceProvider services) {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _trayIcon = new NotifyIcon {
            Icon = AppIcon,
            Text = "Cliparino - Twitch Clip Player",
            Visible = true,
            ContextMenuStrip = CreateContextMenu()
        };

        _trayIcon.DoubleClick += OnTrayIconDoubleClick;

        var healthReporter = _services.GetService<IHealthReporter>();
        if (healthReporter != null) healthReporter.HealthChanged += OnHealthChanged;

        var obsController = _services.GetService<IObsController>();

        if (obsController != null) {
            obsController.SceneCreated += OnObsSceneCreated;
            obsController.SourceCreated += OnObsSourceCreated;
            obsController.ConfigurationDriftRepaired += OnObsConfigurationDriftRepaired;
        }

        // Subscribe to OAuth authentication events
        var oauthService = _services.GetService<ITwitchOAuthService>();
        if (oauthService != null) oauthService.AuthenticationCompleted += OnAuthenticationCompleted;

        UpdateTrayStatus();
    }

    private void OnAuthenticationCompleted(object? sender, OAuthCompletedEventArgs e) {
        if (e.Success) {
            _trayIcon.ShowBalloonTip(3000,
                "Connected to Twitch",
                $"Successfully logged in as @{e.Username ?? "user"}",
                ToolTipIcon.Info);
            _lastTwitchStatus = ComponentStatus.Healthy;
        } else {
            _trayIcon.ShowBalloonTip(5000,
                "Twitch Login Failed",
                e.ErrorMessage ?? "Authentication failed. Please try again.",
                ToolTipIcon.Error);
        }

        UpdateTrayStatus();
    }

    private void OnObsSceneCreated(object? sender, string sceneName) {
        _trayIcon.ShowBalloonTip(3000,
            "OBS Setup Complete",
            $"Created '{sceneName}' scene in OBS!",
            ToolTipIcon.Info);
    }

    private void OnObsSourceCreated(object? sender, string sourceName) {
        _trayIcon.ShowBalloonTip(3000,
            "OBS Setup Complete",
            "Added clip player to OBS",
            ToolTipIcon.Info);
    }

    private void OnObsConfigurationDriftRepaired(object? sender, EventArgs e) {
        _trayIcon.ShowBalloonTip(3000,
            "OBS Configuration Fixed",
            "Fixed OBS configuration automatically",
            ToolTipIcon.Info);
    }

    private void OnHealthChanged(object? sender, HealthChangedEventArgs e) {
        if (e.ComponentName == "OBS") {
            if (_lastObsStatus != ComponentStatus.Unknown && _lastObsStatus != e.Status)
                ShowConnectionBalloon("OBS", e.Status);
            _lastObsStatus = e.Status;
        } else if (e.ComponentName == "TwitchEvents" || e.ComponentName == "TwitchAuth") {
            if (_lastTwitchStatus != ComponentStatus.Unknown && _lastTwitchStatus != e.Status)
                ShowConnectionBalloon("Twitch", e.Status);
            _lastTwitchStatus = e.Status;
        }

        UpdateTrayStatus();
    }

    private void ShowConnectionBalloon(string service, ComponentStatus status) {
        if (status == ComponentStatus.Healthy)
            _trayIcon.ShowBalloonTip(3000, "Service Connected", $"Reconnected to {service} successfully!",
                ToolTipIcon.Info);
        else if (status == ComponentStatus.Unhealthy || status == ComponentStatus.Degraded)
            _trayIcon.ShowBalloonTip(3000, "Service Issue",
                $"Lost connection to {service} - attempting to reconnect...",
                ToolTipIcon.Warning);
    }

    private void UpdateTrayStatus() {
        var baseIcon = AppIcon;
        var healthReporter = _services.GetService<IHealthReporter>();
        var status = healthReporter?.GetAggregateStatus() ?? GetLegacyAggregateStatus();

        Icon icon;

        if (status == ComponentStatus.Healthy) {
            icon = baseIcon;
        } else {
            var overlayColor = status switch {
                ComponentStatus.Degraded => Color.Yellow,
                ComponentStatus.Unhealthy => Color.Red,
                _ => Color.Gray
            };
            icon = AddOverlay(baseIcon, overlayColor);
        }

        var obsStatus = _lastObsStatus == ComponentStatus.Healthy ? "Connected" : "Disconnected";

        var twitchAuthStore = _services.GetService<ITwitchAuthStore>();
        var twitchConnected = twitchAuthStore?.HasValidTokensAsync().Result ?? false;
        var twitchStatus = twitchConnected ? "Connected" : "Disconnected";

        var queueSize = _services.GetService<IClipQueue>()?.Count ?? 0;

        var tooltip = $"Cliparino - Status:\n" +
                      $"Twitch: {twitchStatus}\n" +
                      $"OBS: {obsStatus}\n" +
                      $"Queue: {queueSize} clips";

        if (tooltip.Length >= 128) tooltip = tooltip[..127];

        // NotifyIcon is thread-safe for Icon and Text properties in modern .NET, 
        // but let's be safe as it's a wrapper around a native handle.
        _trayIcon.Icon = icon;
        _trayIcon.Text = tooltip;
    }

    private ContextMenuStrip CreateContextMenu() {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Open Player Page", null, (_, _) =>
            Process.Start(new ProcessStartInfo("http://localhost:5291") { UseShellExecute = true }));

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Settings", null, OpenSettings);
        menu.Items.Add("View Logs", null, OpenLogs);
        menu.Items.Add("Export Diagnostics", null, ExportDiagnostics);

        menu.Items.Add(new ToolStripSeparator());

        var statusItem = new ToolStripMenuItem("Status");
        statusItem.DropDownItems.Add("View Current Playback", null, ViewStatus);
        statusItem.DropDownItems.Add("View Queue", null, ViewQueue);
        menu.Items.Add(statusItem);

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Run Setup Wizard", null, RunSetupWizard);
        menu.Items.Add("Check for Updates", null, CheckForUpdates);
        menu.Items.Add("About", null, ShowAbout);

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Exit", null, Exit);

        return menu;
    }

    private void OnTrayIconDoubleClick(object? sender, EventArgs e) {
        OpenSettings(sender, e);
    }

    private ComponentStatus GetLegacyAggregateStatus() {
        if (_lastObsStatus == ComponentStatus.Healthy && (_lastTwitchStatus == ComponentStatus.Healthy ||
                                                          _lastTwitchStatus == ComponentStatus.Unknown))
            return ComponentStatus.Healthy;

        if (_lastObsStatus == ComponentStatus.Unhealthy || _lastTwitchStatus == ComponentStatus.Unhealthy)
            return ComponentStatus.Unhealthy;

        return ComponentStatus.Degraded;
    }

    private static Icon LoadAppIcon() {
        var stream = typeof(TrayApplicationContext).Assembly
            .GetManifestResourceStream("Cliparino.Core.cliparino.ico");

        return stream != null ? new Icon(stream) : SystemIcons.Application;
    }

    private Icon AddOverlay(Icon baseIcon, Color color) {
        var traySize = SystemInformation.SmallIconSize;
        using var sized = new Icon(baseIcon, traySize);
        using var bitmap = sized.ToBitmap();
        using var g = Graphics.FromImage(bitmap);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        var size = bitmap.Width / 4;
        var x = bitmap.Width - size - 2;
        var y = bitmap.Height - size - 2;

        using var brush = new SolidBrush(color);
        using var pen = new Pen(Color.White, 1);

        g.FillEllipse(brush, x, y, size, size);
        g.DrawEllipse(pen, x, y, size, size);

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private void OpenSettings(object? sender, EventArgs e) {
        try {
            if (_settingsForm == null || _settingsForm.IsDisposed) {
                _settingsForm = new SettingsForm(_services);
                _settingsForm.Show();
            } else {
                _settingsForm.BringToFront();
                _settingsForm.Activate();
            }
        } catch (ObjectDisposedException) {
            UserFriendlyDialogs.ShowShutdownWarning();
        } catch (Exception) {
            UserFriendlyDialogs.ShowError("Can't open settings right now. Try closing and reopening Cliparino.",
                "Settings Error",
                new[] { "Restart Cliparino", "Check if another instance is already running" });
        }
    }

    private static void OpenLogs(object? sender, EventArgs e) {
        var logsPath = Path.Combine(AppContext.BaseDirectory, "logs");

        if (Directory.Exists(logsPath)) {
            Process.Start(new ProcessStartInfo(logsPath) { UseShellExecute = true });
        } else {
            Directory.CreateDirectory(logsPath);
            MessageBox.Show(
                $"Logs directory was not found, but has been created at:\n\n{logsPath}\n\nLogs will appear here after the application starts logging events.",
                "Cliparino - Logs Directory",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            Process.Start(new ProcessStartInfo(logsPath) { UseShellExecute = true });
        }
    }

    private void ExportDiagnostics(object? sender, EventArgs e) {
        ExportDiagnosticsAsync().SafeFireAndForget("Export Diagnostics");
    }

    private async Task ExportDiagnosticsAsync() {
        try {
            var diagnosticsService = _services.GetService<IDiagnosticsService>();

            if (diagnosticsService == null) {
                UserFriendlyDialogs.ShowError(
                    "Can't export diagnostics right now. Check the 'logs' folder for recent activity.",
                    "Diagnostics Unavailable",
                    new[] { "Check logs folder manually", "Restart Cliparino" });

                return;
            }

            var outputPath = await diagnosticsService.ExportDiagnosticsAsync();
            var result = MessageBox.Show($"Diagnostics exported to:\n{outputPath}\n\nOpen folder?",
                "Cliparino",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (result == DialogResult.Yes)
                Process.Start(new ProcessStartInfo(Path.GetDirectoryName(outputPath)!) { UseShellExecute = true });
        } catch (ObjectDisposedException) {
            UserFriendlyDialogs.ShowShutdownWarning();
        } catch (Exception) {
            UserFriendlyDialogs.ShowError(
                "Can't export diagnostics right now. Check the 'logs' folder for recent activity.",
                "Export Failed",
                new[] { "Check logs folder", "Ensure you have enough disk space" });
        }
    }

    private static void ViewStatus(object? sender, EventArgs e) {
        Process.Start(new ProcessStartInfo("http://localhost:5291/api/status") { UseShellExecute = true });
    }

    private void ViewQueue(object? sender, EventArgs e) {
        try {
            var playbackEngine = _services.GetService<IPlaybackEngine>();

            if (playbackEngine == null) {
                UserFriendlyDialogs.ShowError("Clip player is starting up. Please wait a moment and try again.",
                    "Playback Engine Unavailable",
                    new[] { "Wait a few seconds", "Check if Cliparino is still initializing" });

                return;
            }

            var currentClip = playbackEngine.CurrentClip;
            var queueSize = _services.GetService<IClipQueue>()?.Count ?? 0;

            var message = currentClip != null
                ? $"Currently playing:\n{currentClip.Title}\nby {currentClip.Creator.DisplayName}\n\nQueue size: {queueSize}"
                : $"No clip currently playing.\n\nQueue size: {queueSize}";

            MessageBox.Show(message, "Cliparino Status", MessageBoxButtons.OK, MessageBoxIcon.Information);
        } catch (ObjectDisposedException) {
            UserFriendlyDialogs.ShowShutdownWarning();
        } catch (Exception) {
            UserFriendlyDialogs.ShowError("Clip player is starting up. Please wait a moment and try again.",
                "Status Error",
                new[] { "Wait a few seconds", "Restart Cliparino if this persists" });
        }
    }

    private void CheckForUpdates(object? sender, EventArgs e) {
        CheckForUpdatesAsync().SafeFireAndForget("Check for Updates");
    }

    private async Task CheckForUpdatesAsync() {
        if (_isCheckingForUpdates) return;
        _isCheckingForUpdates = true;

        try {
            var updateChecker = _services.GetService<IUpdateChecker>();

            if (updateChecker == null) {
                UserFriendlyDialogs.ShowError("Can't check for updates right now. We'll try again automatically later.",
                    "Update Checker Unavailable",
                    new[] { "Check your internet connection", "Try again in a few minutes" });

                return;
            }

            var updateInfo = await updateChecker.CheckForUpdatesAsync();

            if (updateInfo == null) {
                MessageBox.Show("Could not check for updates. Please try again later.", "Error", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            } else if (updateInfo.IsNewer) {
                var result = MessageBox.Show(
                    $"A new version is available: {updateInfo.LatestVersion}\n\nWould you like to download it?",
                    "Update Available",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (result == DialogResult.Yes)
                    Process.Start(new ProcessStartInfo(updateInfo.ReleaseUrl) { UseShellExecute = true });
            } else {
                MessageBox.Show("You are running the latest version.", "No Updates", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        } catch (ObjectDisposedException) {
            UserFriendlyDialogs.ShowShutdownWarning();
        } catch (Exception) {
            UserFriendlyDialogs.ShowError("Can't check for updates right now. We'll try again automatically later.",
                "Update Check Failed",
                new[] { "Check your internet connection", "Visit our GitHub page for manual updates" });
        } finally {
            _isCheckingForUpdates = false;
        }
    }

    private static void ShowAbout(object? sender, EventArgs e) {
        var version = typeof(TrayApplicationContext).Assembly.GetName().Version;
        MessageBox.Show($"Cliparino - Twitch Clip Player\n\n" +
                        $"Version: {version}\n\n" +
                        $"A standalone clip player for Twitch streamers.\n\n" +
                        $"For support and updates, visit:\n" +
                        $"github.com/angrmgmt/Cliparino",
            "About Cliparino",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void RunSetupWizard(object? sender, EventArgs e) {
        var wizard = new WelcomeWizard(_services);
        wizard.ShowDialog();
    }

    private void Exit(object? sender, EventArgs e) {
        _trayIcon.Visible = false;

        // Signal the generic host to shut down
        var lifetime = _services.GetService<IHostApplicationLifetime>();
        lifetime?.StopApplication();

        Application.Exit();
    }


    /// <summary>
    ///     Disposes the tray icon and any UI resources created by this application context.
    /// </summary>
    /// <param name="disposing">
    ///     <see langword="true" /> to dispose managed resources; otherwise <see langword="false" />.
    /// </param>
    protected override void Dispose(bool disposing) {
        if (disposing) {
            _trayIcon.Dispose();
            _settingsForm?.Dispose();
        }

        base.Dispose(disposing);
    }
}