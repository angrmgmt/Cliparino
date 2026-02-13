using System.Diagnostics;

namespace Cliparino.Core.UI;

/// <summary>
///     Provides helper methods for showing user-friendly dialogs with consistent formatting and help links.
/// </summary>
public static class UserFriendlyDialogs {
    private const string TroubleshootingWikiUrl = "https://github.com/angrmgmt/Cliparino/wiki/Troubleshooting";

    /// <summary>
    ///     Shows a user-friendly error dialog.
    /// </summary>
    /// <param name="message">The main error message.</param>
    /// <param name="title">The dialog title.</param>
    /// <param name="suggestions">Optional list of suggested actions for the user.</param>
    public static void ShowError(string message, string title = "Error", string[]? suggestions = null) {
        var fullMessage = message;

        if (suggestions != null && suggestions.Length > 0) {
            fullMessage += "\n\nWhat you can do:\n";
            foreach (var suggestion in suggestions) fullMessage += $"• {suggestion}\n";
        }

        fullMessage += "\nStill having issues? Click 'Help' to view our troubleshooting guide.";

        // Since standard MessageBox doesn't have a "Help" button that we can easily customize 
        // without complex Win32 API calls or creating a custom Form, we use MessageBoxButtons.OK
        // and add a manual "Open Help" if needed, but TASK-003 requirements say:
        // "Include 'Get Help' button that opens troubleshooting wiki"
        // In WinForms, MessageBox.Show can take a helpFilePath.

        // Actually, a better way for WinForms is to use a custom TaskDialog (available in .NET Core 3.0+)
        // but it's often more flexible to just use a custom Form if we need specific buttons.
        // However, TASK-011 says: "[OK] [Get Help] [View Logs]"

        // I will implement a simple TaskDialog-like helper using MessageBox for now, 
        // but if I want to match TASK-011 exactly, I might need a custom Form.

        // Let's try to use TaskDialog if available in this project.
        // It's in System.Windows.Forms.

        try {
            var page = new TaskDialogPage {
                Caption = "Cliparino",
                Heading = title,
                Text = message,
                Icon = TaskDialogIcon.Error,
                Buttons = { TaskDialogButton.OK }
            };

            if (suggestions != null && suggestions.Length > 0) {
                var expText = "What you can do:\n";
                foreach (var suggestion in suggestions) expText += $"• {suggestion}\n";
                page.Expander = new TaskDialogExpander { Text = expText, Expanded = true };
            }

            var helpButton = new TaskDialogButton("Get Help");
            helpButton.Click += (_, _) => {
                Process.Start(new ProcessStartInfo(TroubleshootingWikiUrl) { UseShellExecute = true });
            };
            page.Buttons.Add(helpButton);

            var logsButton = new TaskDialogButton("View Logs");
            logsButton.Click += (_, _) => {
                var logsPath = Path.Combine(AppContext.BaseDirectory, "logs");
                if (Directory.Exists(logsPath))
                    Process.Start(new ProcessStartInfo(logsPath) { UseShellExecute = true });
            };
            page.Buttons.Add(logsButton);

            TaskDialog.ShowDialog(page);
        } catch {
            // Fallback to standard MessageBox if TaskDialog fails (e.g. on older OS, though .NET 8 requires newer Windows)
            MessageBox.Show(fullMessage, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    ///     Shows the improved shutdown warning dialog with actionable buttons.
    /// </summary>
    public static void ShowShutdownWarning() {
        var dialog = new Form {
            Text = "Cliparino Startup Issue",
            Size = new Size(500, 320),
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var iconPictureBox = new PictureBox {
            Location = new Point(20, 20), Size = new Size(32, 32), Image = SystemIcons.Warning.ToBitmap()
        };
        dialog.Controls.Add(iconPictureBox);

        var titleLabel = new Label {
            Location = new Point(65, 20),
            Size = new Size(400, 25),
            Text = "Cliparino couldn't start properly.",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 10, FontStyle.Bold)
        };
        dialog.Controls.Add(titleLabel);

        var commonFixesLabel = new Label {
            Location = new Point(65, 55),
            Size = new Size(400, 90),
            Text = "Common fixes:\n" +
                   "• Close programs that might be using port 5290\n" +
                   "• Restart OBS Studio\n" +
                   "• Restart Cliparino"
        };
        dialog.Controls.Add(commonFixesLabel);

        var stillHavingLabel = new Label {
            Location = new Point(65, 150),
            Size = new Size(400, 20),
            Text = "Still having issues?",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 9, FontStyle.Bold)
        };
        dialog.Controls.Add(stillHavingLabel);

        var viewLogsButton = new Button { Text = "View Logs", Location = new Point(65, 180), Size = new Size(120, 35) };
        viewLogsButton.Click += (_, _) => {
            var logsPath = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logsPath);
            Process.Start(new ProcessStartInfo(logsPath) { UseShellExecute = true });
        };
        dialog.Controls.Add(viewLogsButton);

        var troubleshootingButton = new Button {
            Text = "Troubleshooting Guide", Location = new Point(195, 180), Size = new Size(160, 35)
        };
        troubleshootingButton.Click += (_, _) => {
            Process.Start(
                new ProcessStartInfo("https://github.com/angrmgmt/Cliparino/wiki/Troubleshooting") {
                    UseShellExecute = true
                });
        };
        dialog.Controls.Add(troubleshootingButton);

        var closeButton = new Button {
            Text = "Close", DialogResult = DialogResult.OK, Location = new Point(365, 180), Size = new Size(100, 35)
        };
        dialog.Controls.Add(closeButton);

        dialog.AcceptButton = closeButton;
        dialog.ShowDialog();
    }
}