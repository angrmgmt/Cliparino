using System.Diagnostics;
using Cliparino.Core.Services;

namespace Cliparino.Core.UI;

public class TrayApplicationContext : ApplicationContext
{
    private readonly IServiceProvider _services;
    private readonly NotifyIcon _trayIcon;
    private SettingsForm? _settingsForm;

    public TrayApplicationContext(IServiceProvider services)
    {
        _services = services;
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Cliparino - Twitch Clip Player",
            Visible = true,
            ContextMenuStrip = CreateContextMenu()
        };

        _trayIcon.DoubleClick += OnTrayIconDoubleClick;
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add("Open Player Page", null, (s, e) =>
            Process.Start(new ProcessStartInfo("http://localhost:5290") { UseShellExecute = true }));

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

        menu.Items.Add("Check for Updates", null, CheckForUpdates);
        menu.Items.Add("About", null, ShowAbout);

        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Exit", null, Exit);

        return menu;
    }

    private void OnTrayIconDoubleClick(object? sender, EventArgs e)
    {
        OpenSettings(sender, e);
    }

    private void OpenSettings(object? sender, EventArgs e)
    {
        if (_settingsForm == null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(_services);
            _settingsForm.Show();
        }
        else
        {
            _settingsForm.BringToFront();
            _settingsForm.Activate();
        }
    }

    private void OpenLogs(object? sender, EventArgs e)
    {
        var logsPath = Path.Combine(AppContext.BaseDirectory, "logs");
        if (Directory.Exists(logsPath))
            Process.Start(new ProcessStartInfo(logsPath) { UseShellExecute = true });
        else
            MessageBox.Show("Logs directory not found.", "Cliparino", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async void ExportDiagnostics(object? sender, EventArgs e)
    {
        var diagnosticsService = _services.GetService<IDiagnosticsService>();
        if (diagnosticsService == null)
        {
            MessageBox.Show("Diagnostics service unavailable.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            var outputPath = await diagnosticsService.ExportDiagnosticsAsync();
            var result = MessageBox.Show(
                $"Diagnostics exported to:\n{outputPath}\n\nOpen folder?",
                "Cliparino",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (result == DialogResult.Yes)
                Process.Start(new ProcessStartInfo(Path.GetDirectoryName(outputPath)!) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to export diagnostics:\n{ex.Message}", "Error", MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ViewStatus(object? sender, EventArgs e)
    {
        Process.Start(new ProcessStartInfo("http://localhost:5290/api/status") { UseShellExecute = true });
    }

    private void ViewQueue(object? sender, EventArgs e)
    {
        var playbackEngine = _services.GetService<IPlaybackEngine>();
        if (playbackEngine == null)
        {
            MessageBox.Show("Playback engine unavailable.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var currentClip = playbackEngine.CurrentClip;
        var queueSize = _services.GetService<IClipQueue>()?.Count ?? 0;

        var message = currentClip != null
            ? $"Currently playing:\n{currentClip.Title}\nby {currentClip.CreatorName}\n\nQueue size: {queueSize}"
            : $"No clip currently playing.\n\nQueue size: {queueSize}";

        MessageBox.Show(message, "Cliparino Status", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async void CheckForUpdates(object? sender, EventArgs e)
    {
        var updateChecker = _services.GetService<IUpdateChecker>();
        if (updateChecker == null)
        {
            MessageBox.Show("Update checker unavailable.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            var updateInfo = await updateChecker.CheckForUpdatesAsync();
            if (updateInfo?.IsNewer == true)
            {
                var result = MessageBox.Show(
                    $"A new version is available: {updateInfo.LatestVersion}\n\nWould you like to download it?",
                    "Update Available",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (result == DialogResult.Yes)
                    Process.Start(new ProcessStartInfo(updateInfo.ReleaseUrl) { UseShellExecute = true });
            }
            else
            {
                MessageBox.Show("You are running the latest version.", "No Updates", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to check for updates:\n{ex.Message}", "Error", MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ShowAbout(object? sender, EventArgs e)
    {
        var version = typeof(TrayApplicationContext).Assembly.GetName().Version;
        MessageBox.Show(
            $"Cliparino - Twitch Clip Player\n\n" +
            $"Version: {version}\n\n" +
            $"A standalone clip player for Twitch streamers.\n\n" +
            $"For support and updates, visit:\n" +
            $"github.com/angrmgmt/Cliparino",
            "About Cliparino",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void Exit(object? sender, EventArgs e)
    {
        _trayIcon.Visible = false;
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon?.Dispose();
            _settingsForm?.Dispose();
        }

        base.Dispose(disposing);
    }
}