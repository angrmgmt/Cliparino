using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Cliparino.Core.Extensions;
using Cliparino.Core.Services;

namespace Cliparino.Core.UI;

/// <summary>
///     First-run welcome wizard that guides users through initial Cliparino setup.
/// </summary>
public sealed class WelcomeWizard : Form {
    private static readonly string CompletionFlagPath = Path.Combine(AppContext.BaseDirectory, ".wizard_completed");
    private readonly Button _backButton;
    private readonly Panel _contentPanel;
    private readonly Button _nextButton;

    private readonly IServiceProvider _services;
    private readonly Button _skipButton;
    private readonly Label _stepIndicator;
    private int _currentStep;

    private CheckBox? _obsInstalledCheck;
    private TextBox? _obsPasswordTextBox;
    private Label? _obsStatusLabel;
    private CheckBox? _obsWebSocketCheck;
    private CheckBox? _showObsPasswordCheckBox;
    private CheckBox? _twitchAccountCheck;
    private Label? _twitchStatusLabel;

    public WelcomeWizard(IServiceProvider services) {
        _services = services;
        _currentStep = 0;

        Text = "Welcome to Cliparino";
        Size = new Size(600, 500);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        // Header with logo/title
        var headerPanel = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = Color.FromArgb(0, 113, 197) };

        var titleLabel = new Label {
            Text = "Welcome to Cliparino!",
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = false,
            Size = new Size(580, 40),
            Location = new Point(10, 15),
            TextAlign = ContentAlignment.MiddleLeft
        };
        headerPanel.Controls.Add(titleLabel);

        var subtitleLabel = new Label {
            Text = "Let's get you set up in just a few steps",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.White,
            AutoSize = false,
            Size = new Size(580, 20),
            Location = new Point(10, 50),
            TextAlign = ContentAlignment.MiddleLeft
        };
        headerPanel.Controls.Add(subtitleLabel);

        Controls.Add(headerPanel);

        // Step indicator
        _stepIndicator = new Label {
            Text = "Step 1 of 5",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.Gray,
            Location = new Point(10, 90),
            Size = new Size(200, 20)
        };
        Controls.Add(_stepIndicator);

        // Content panel
        _contentPanel = new Panel {
            Location = new Point(10, 115), Size = new Size(565, 290), BorderStyle = BorderStyle.None
        };
        Controls.Add(_contentPanel);

        // Button panel
        var buttonPanel = new Panel { Dock = DockStyle.Bottom, Height = 50 };

        _skipButton = new Button { Text = "Setup Later", Location = new Point(10, 10), Size = new Size(100, 30) };
        _skipButton.Click += (_, _) => { Close(); };
        buttonPanel.Controls.Add(_skipButton);

        _backButton = new Button {
            Text = "← Back", Location = new Point(350, 10), Size = new Size(100, 30), Enabled = false
        };
        _backButton.Click += (_, _) => {
            if (_currentStep > 0) {
                _currentStep--;
                ShowCurrentStep();
            }
        };
        buttonPanel.Controls.Add(_backButton);

        _nextButton = new Button { Text = "Next →", Location = new Point(460, 10), Size = new Size(100, 30) };
        _nextButton.Click += OnNextClick;
        buttonPanel.Controls.Add(_nextButton);

        Controls.Add(buttonPanel);

        AcceptButton = _nextButton;

        ShowCurrentStep();
    }

    /// <summary>
    ///     Gets whether the wizard has been completed previously.
    /// </summary>
    public static bool HasCompletedWizard => File.Exists(CompletionFlagPath);

    /// <summary>
    ///     Gets whether first-run conditions are met (no auth tokens, no wizard completion).
    /// </summary>
    public static async Task<bool> ShouldShowWizardAsync(IServiceProvider services) {
        if (HasCompletedWizard) return false;

        var authStore = services.GetService<ITwitchAuthStore>();

        if (authStore != null) {
            var hasTokens = await authStore.HasValidTokensAsync();

            if (hasTokens) {
                MarkWizardCompleted();

                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Marks the wizard as completed so it won't show again.
    /// </summary>
    public static void MarkWizardCompleted() {
        try {
            File.WriteAllText(CompletionFlagPath, DateTime.Now.ToString("O"));
        } catch {
            // Ignore failures - wizard may show again next time
        }
    }

    /// <summary>
    ///     Clears the wizard completion flag (for testing or reset).
    /// </summary>
    public static void ResetWizard() {
        try {
            if (File.Exists(CompletionFlagPath)) File.Delete(CompletionFlagPath);
        } catch {
            // Ignore
        }
    }

    private void OnNextClick(object? sender, EventArgs e) {
        _currentStep++;

        if (_currentStep >= 5) {
            SaveObsPasswordAsync().SafeFireAndForget("Save OBS Password");
            MarkWizardCompleted();
            Close();
        } else {
            ShowCurrentStep();
        }
    }

    private void ShowCurrentStep() {
        _contentPanel.Controls.Clear();
        _stepIndicator.Text = $"Step {_currentStep + 1} of 5";
        _backButton.Enabled = _currentStep > 0;
        _nextButton.Text = _currentStep == 4 ? "Finish" : "Next →";

        switch (_currentStep) {
            case 0:
                ShowWelcomeStep();

                break;
            case 1:
                ShowObsSetupStep();

                break;
            case 2:
                ShowTwitchAuthStep();

                break;
            case 3:
                ShowTestPlaybackStep();

                break;
            case 4:
                ShowSuccessStep();

                break;
        }
    }

    private async Task SaveObsPasswordAsync() {
        if (_obsPasswordTextBox == null || string.IsNullOrEmpty(_obsPasswordTextBox.Text)) return;

        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        try {
            if (!File.Exists(configPath)) return;

            var json = await File.ReadAllTextAsync(configPath);
            using var document = JsonDocument.Parse(json);

            var options = new JsonWriterOptions { Indented = true };
            using var stream = new MemoryStream();

            using (var writer = new Utf8JsonWriter(stream, options)) {
                writer.WriteStartObject();

                foreach (var property in document.RootElement.EnumerateObject())
                    if (property.Name == "OBS") {
                        writer.WriteStartObject(property.Name);
                        foreach (var obsProp in property.Value.EnumerateObject())
                            if (obsProp.Name == "Password")
                                writer.WriteString(obsProp.Name, _obsPasswordTextBox.Text);
                            else
                                obsProp.WriteTo(writer);

                        writer.WriteEndObject();
                    } else {
                        property.WriteTo(writer);
                    }

                writer.WriteEndObject();
            }

            var updatedJson = Encoding.UTF8.GetString(stream.ToArray());
            await File.WriteAllTextAsync(configPath, updatedJson);
        } catch {
            // Silently fail - wizard completion shouldn't be blocked by config save failure
        }
    }

    private void ShowWelcomeStep() {
        var welcomeLabel = new Label {
            Text = "Before we begin, let's make sure you have everything ready:",
            Font = new Font("Segoe UI", 10),
            Location = new Point(0, 0),
            Size = new Size(560, 30)
        };
        _contentPanel.Controls.Add(welcomeLabel);

        var y = 40;

        _obsInstalledCheck = new CheckBox {
            Text = "OBS Studio is installed",
            Font = new Font("Segoe UI", 10),
            Location = new Point(20, y),
            Size = new Size(500, 25)
        };
        _contentPanel.Controls.Add(_obsInstalledCheck);
        y += 30;

        _obsWebSocketCheck = new CheckBox {
            Text = "OBS WebSocket is enabled (OBS 28+ has it built-in)",
            Font = new Font("Segoe UI", 10),
            Location = new Point(20, y),
            Size = new Size(500, 25)
        };
        _contentPanel.Controls.Add(_obsWebSocketCheck);
        y += 30;

        _twitchAccountCheck = new CheckBox {
            Text = "I have a Twitch account ready to connect",
            Font = new Font("Segoe UI", 10),
            Location = new Point(20, y),
            Size = new Size(500, 25)
        };
        _contentPanel.Controls.Add(_twitchAccountCheck);
        y += 50;

        var checkSystemButton =
            new Button { Text = "Check My System", Location = new Point(20, y), Size = new Size(150, 35) };
        checkSystemButton.Click += (_, _) => CheckSystemAsync().SafeFireAndForget("Check System");
        _contentPanel.Controls.Add(checkSystemButton);

        y += 50;

        var helpLabel = new Label {
            Text = "Need help? Click here for setup instructions →",
            Font = new Font("Segoe UI", 9, FontStyle.Underline),
            ForeColor = Color.Blue,
            Location = new Point(20, y),
            Size = new Size(300, 20),
            Cursor = Cursors.Hand
        };
        helpLabel.Click += (_, _) => {
            Process.Start(
                new ProcessStartInfo("https://github.com/angrmgmt/Cliparino/wiki/Setup") { UseShellExecute = true });
        };
        _contentPanel.Controls.Add(helpLabel);
    }

    private async Task CheckSystemAsync() {
        // Check if OBS is running
        var obsProcesses = Process.GetProcessesByName("obs64");
        if (obsProcesses.Length == 0) obsProcesses = Process.GetProcessesByName("obs");

        _obsInstalledCheck!.Checked = obsProcesses.Length > 0;

        // Try to connect to OBS WebSocket to see if it's enabled
        var obsController = _services.GetService<IObsController>();

        if (obsController != null)
            try {
                var connected = await obsController.ConnectAsync("localhost", 4455, "");
                _obsWebSocketCheck!.Checked = connected;
                if (connected) await obsController.DisconnectAsync();
            } catch {
                _obsWebSocketCheck!.Checked = false;
            }

        // Check if we already have auth tokens
        var authStore = _services.GetService<ITwitchAuthStore>();
        if (authStore != null) _twitchAccountCheck!.Checked = await authStore.HasValidTokensAsync();
    }

    private void ShowObsSetupStep() {
        var headerLabel = new Label {
            Text = "Connect to OBS Studio",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            Location = new Point(0, 0),
            Size = new Size(560, 25)
        };
        _contentPanel.Controls.Add(headerLabel);

        var descLabel = new Label {
            Text =
                "Cliparino connects to OBS to display clips in your stream.\n\nMake sure OBS is running with WebSocket enabled.",
            Font = new Font("Segoe UI", 10),
            Location = new Point(0, 30),
            Size = new Size(560, 60)
        };
        _contentPanel.Controls.Add(descLabel);

        var passwordLabel = new Label {
            Text = "WebSocket Password (if configured in OBS):",
            Font = new Font("Segoe UI", 9),
            Location = new Point(0, 95),
            Size = new Size(300, 20)
        };
        _contentPanel.Controls.Add(passwordLabel);

        _obsPasswordTextBox = new TextBox {
            Location = new Point(0, 115),
            Width = 250,
            UseSystemPasswordChar = true,
            PlaceholderText = "Leave blank if no password set"
        };
        _contentPanel.Controls.Add(_obsPasswordTextBox);

        _showObsPasswordCheckBox = new CheckBox { Text = "Show", Location = new Point(260, 117), Width = 60 };
        _showObsPasswordCheckBox.CheckedChanged += (_, _) => {
            if (_obsPasswordTextBox != null)
                _obsPasswordTextBox.UseSystemPasswordChar = !_showObsPasswordCheckBox!.Checked;
        };
        _contentPanel.Controls.Add(_showObsPasswordCheckBox);

        _obsStatusLabel = new Label {
            Text = "Status: Checking...",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Location = new Point(0, 150),
            Size = new Size(560, 25)
        };
        _contentPanel.Controls.Add(_obsStatusLabel);

        var testButton = new Button {
            Text = "Test OBS Connection", Location = new Point(0, 190), Size = new Size(170, 35)
        };
        testButton.Click += (_, _) => TestObsConnectionAsync().SafeFireAndForget("Test OBS Connection");
        _contentPanel.Controls.Add(testButton);

        var helpLink = new LinkLabel {
            Text = "How to enable OBS WebSocket", Location = new Point(180, 198), Size = new Size(200, 20)
        };
        helpLink.LinkClicked += (_, _) => {
            Process.Start(
                new ProcessStartInfo("https://github.com/angrmgmt/Cliparino/wiki/OBS-Setup") {
                    UseShellExecute = true
                });
        };
        _contentPanel.Controls.Add(helpLink);

        // Auto-check on load
        TestObsConnectionAsync().SafeFireAndForget("Initial OBS Test");
    }

    private async Task TestObsConnectionAsync() {
        _obsStatusLabel!.Text = "Status: Testing connection...";
        _obsStatusLabel.ForeColor = Color.Gray;

        var obsController = _services.GetService<IObsController>();

        if (obsController == null) {
            _obsStatusLabel.Text = "Status: ❌ OBS service unavailable";
            _obsStatusLabel.ForeColor = Color.Red;

            return;
        }

        try {
            var password = _obsPasswordTextBox?.Text ?? "";
            var connected = await obsController.ConnectAsync("localhost", 4455, password);

            if (connected) {
                _obsStatusLabel.Text = "Status: ✅ Connected to OBS successfully!";
                _obsStatusLabel.ForeColor = Color.Green;
            } else {
                _obsStatusLabel.Text = string.IsNullOrEmpty(password)
                    ? "Status: ⚠️ Could not connect. Is OBS running?"
                    : "Status: ⚠️ Could not connect. Check password or if OBS is running.";
                _obsStatusLabel.ForeColor = Color.Orange;
            }
        } catch (Exception ex) {
            _obsStatusLabel.Text = $"Status: ❌ Error: {ex.Message}";
            _obsStatusLabel.ForeColor = Color.Red;
        }
    }

    private void ShowTwitchAuthStep() {
        var headerLabel = new Label {
            Text = "Connect Your Twitch Account",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            Location = new Point(0, 0),
            Size = new Size(560, 25)
        };
        _contentPanel.Controls.Add(headerLabel);

        var descLabel = new Label {
            Text =
                "Cliparino needs permission to read chat commands and play clips from Twitch.\n\nClick the button below to authorize with Twitch.",
            Font = new Font("Segoe UI", 10),
            Location = new Point(0, 30),
            Size = new Size(560, 60)
        };
        _contentPanel.Controls.Add(descLabel);

        _twitchStatusLabel = new Label {
            Text = "Status: Not connected",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Location = new Point(0, 100),
            Size = new Size(560, 25)
        };
        _contentPanel.Controls.Add(_twitchStatusLabel);

        var connectButton = new Button {
            Text = "Connect Twitch Account",
            Location = new Point(0, 140),
            Size = new Size(180, 40),
            Font = new Font("Segoe UI", 10)
        };
        connectButton.Click += (_, _) => ConnectTwitchAsync(connectButton).SafeFireAndForget("Connect Twitch");
        _contentPanel.Controls.Add(connectButton);

        // Check current status
        CheckTwitchStatusAsync().SafeFireAndForget("Twitch Status Check");
    }

    private async Task CheckTwitchStatusAsync() {
        var authStore = _services.GetService<ITwitchAuthStore>();

        if (authStore != null) {
            var hasTokens = await authStore.HasValidTokensAsync();

            if (hasTokens) {
                var userId = await authStore.GetUserIdAsync();
                _twitchStatusLabel!.Text = $"Status: ✅ Connected as @{userId ?? "user"}";
                _twitchStatusLabel.ForeColor = Color.Green;
            } else {
                _twitchStatusLabel!.Text = "Status: Not connected";
                _twitchStatusLabel.ForeColor = Color.Gray;
            }
        }
    }

    private async Task ConnectTwitchAsync(Button button) {
        button.Enabled = false;
        button.Text = "Opening browser...";

        try {
            using var httpClient = new HttpClient();
            var response = await httpClient.GetStringAsync("http://localhost:5291/auth/login");
            var authData = JsonSerializer.Deserialize<Dictionary<string, string>>(response);

            if (authData != null && authData.TryGetValue("authUrl", out var authUrl)) {
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

                _twitchStatusLabel!.Text = "Status: ⏳ Waiting for authorization...";
                _twitchStatusLabel.ForeColor = Color.Orange;

                // Subscribe to auth completion event
                var oauthService = _services.GetService<ITwitchOAuthService>();
                if (oauthService != null)
                    oauthService.AuthenticationCompleted += (_, e) => {
                        Invoke(() => {
                            if (e.Success) {
                                _twitchStatusLabel.Text = $"Status: ✅ Connected as @{e.Username ?? "user"}";
                                _twitchStatusLabel.ForeColor = Color.Green;
                            } else {
                                _twitchStatusLabel.Text = $"Status: ❌ {e.ErrorMessage ?? "Authentication failed"}";
                                _twitchStatusLabel.ForeColor = Color.Red;
                            }
                        });
                    };
            }
        } catch (Exception ex) {
            _twitchStatusLabel!.Text = $"Status: ❌ Error: {ex.Message}";
            _twitchStatusLabel.ForeColor = Color.Red;
        } finally {
            button.Enabled = true;
            button.Text = "Connect Twitch Account";
        }
    }

    private void ShowTestPlaybackStep() {
        var headerLabel = new Label {
            Text = "Test Clip Playback",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            Location = new Point(0, 0),
            Size = new Size(560, 25)
        };
        _contentPanel.Controls.Add(headerLabel);

        var descLabel = new Label {
            Text =
                "Let's make sure everything is working by playing a test clip.\n\n1. Make sure OBS is running with the Cliparino scene visible\n2. Click the button below to play a sample clip",
            Font = new Font("Segoe UI", 10),
            Location = new Point(0, 30),
            Size = new Size(560, 80)
        };
        _contentPanel.Controls.Add(descLabel);

        var testButton = new Button {
            Text = "Play Test Clip",
            Location = new Point(0, 130),
            Size = new Size(150, 40),
            Font = new Font("Segoe UI", 10)
        };
        testButton.Click += (_, _) => {
            // Open the player page to show something is working
            Process.Start(new ProcessStartInfo("http://localhost:5291") { UseShellExecute = true });
            MessageBox.Show(
                "A browser window has opened with the Cliparino player.\n\nIn OBS, add this page as a browser source to see clips:\nhttp://localhost:5291\n\nYou can test by typing !watch and a clip URL in your Twitch chat.",
                "Test Playback",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };
        _contentPanel.Controls.Add(testButton);

        var skipLabel = new Label {
            Text = "You can also skip this step and test later using chat commands.",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.Gray,
            Location = new Point(0, 190),
            Size = new Size(560, 20)
        };
        _contentPanel.Controls.Add(skipLabel);
    }

    private void ShowSuccessStep() {
        var headerLabel = new Label {
            Text = "You're All Set! 🎉",
            Font = new Font("Segoe UI", 14, FontStyle.Bold),
            Location = new Point(0, 0),
            Size = new Size(560, 30)
        };
        _contentPanel.Controls.Add(headerLabel);

        var descLabel = new Label {
            Text = "Cliparino is now ready to use. Here's what you can do:\n\n" +
                   "Chat Commands:\n" +
                   "• !watch <clip-url> - Play a specific clip\n" +
                   "• !so @username - Shoutout a streamer with their clip\n" +
                   "• !stop - Stop the current clip\n" +
                   "• !replay - Replay the last clip\n\n" +
                   "Cliparino runs in your system tray. Right-click the icon for options.",
            Font = new Font("Segoe UI", 10),
            Location = new Point(0, 40),
            Size = new Size(560, 180)
        };
        _contentPanel.Controls.Add(descLabel);

        var docsLink = new LinkLabel {
            Text = "View full documentation →", Location = new Point(0, 230), Size = new Size(200, 20)
        };
        docsLink.LinkClicked += (_, _) => {
            Process.Start(
                new ProcessStartInfo("https://github.com/angrmgmt/Cliparino/wiki") { UseShellExecute = true });
        };
        _contentPanel.Controls.Add(docsLink);
    }
}