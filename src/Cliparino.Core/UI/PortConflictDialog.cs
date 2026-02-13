using System.Diagnostics;

namespace Cliparino.Core.UI;

public class PortConflictDialog : Form {
    private readonly int _conflictingPort;
    private readonly string? _processName;
    private readonly int _suggestedPort;

    public PortConflictDialog(int conflictingPort, int suggestedPort, string? processName = null) {
        _conflictingPort = conflictingPort;
        _suggestedPort = suggestedPort;
        _processName = processName;

        InitializeComponents();
    }

    public bool UseAlternativePort { get; private set; }
    public int SelectedPort { get; private set; }

    private void InitializeComponents() {
        Text = "Port Conflict Detected";
        Size = new Size(500, 280);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var iconPictureBox = new PictureBox {
            Location = new Point(20, 20), Size = new Size(32, 32), Image = SystemIcons.Warning.ToBitmap()
        };
        Controls.Add(iconPictureBox);

        var messageLabel = new Label {
            Location = new Point(65, 20),
            Size = new Size(400, 80),
            Text = $"Cliparino couldn't start properly.\n\n" +
                   $"Port {_conflictingPort} is already in use" +
                   (_processName != null ? $" by {_processName}." : ".") +
                   $"\n\nWould you like to use port {_suggestedPort} instead?"
        };
        Controls.Add(messageLabel);

        var explanationLabel = new Label {
            Location = new Point(65, 110),
            Size = new Size(400, 60),
            Text = "Common fixes:\n" +
                   $"• Close programs that might be using port {_conflictingPort}\n" +
                   "• Use a different port (recommended)\n" +
                   "• Restart Cliparino",
            ForeColor = Color.DimGray
        };
        Controls.Add(explanationLabel);

        var useAlternativeButton = new Button {
            Text = $"Use Port {_suggestedPort}",
            DialogResult = DialogResult.OK,
            Location = new Point(150, 190),
            Size = new Size(150, 35)
        };
        useAlternativeButton.Click += (_, _) => {
            UseAlternativePort = true;
            SelectedPort = _suggestedPort;
            Close();
        };
        Controls.Add(useAlternativeButton);

        var cancelButton = new Button {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(310, 190),
            Size = new Size(100, 35)
        };
        cancelButton.Click += (_, _) => {
            UseAlternativePort = false;
            Close();
        };
        Controls.Add(cancelButton);

        var viewLogsLink = new LinkLabel { Text = "View Logs", Location = new Point(20, 195), Size = new Size(80, 20) };
        viewLogsLink.LinkClicked += (_, _) => {
            var logsPath = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logsPath);
            Process.Start(new ProcessStartInfo(logsPath) { UseShellExecute = true });
        };
        Controls.Add(viewLogsLink);

        AcceptButton = useAlternativeButton;
        CancelButton = cancelButton;
    }
}