using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;
using Cliparino.Core.Extensions;
using Cliparino.Core.Services;
using Serilog;

namespace Cliparino.Core.UI;

/// <summary>
///     Provides a Windows Forms UI for viewing and editing Cliparino configuration.
/// </summary>
/// <remarks>
///     <para>
///         The form reads configuration values from <see cref="IConfiguration" /> and persists changes to an
///         <c>appsettings.json</c> file located next to the executable (see the Save action in the UI).
///     </para>
///     <para>
///         Threading: this form must be created and interacted with on the WinForms UI thread.
///     </para>
/// </remarks>
public class SettingsForm : Form {
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _services;
    private readonly ToolTip _toolTip = new();
    private NumericUpDown _approvalTimeoutNumeric = null!;
    private NumericUpDown _checkIntervalHoursNumeric = null!;
    private CheckBox _checkOnStartupCheckBox = null!;
    private CheckBox _enableShoutoutMessageCheckBox = null!;
    private CheckedListBox _exemptRolesCheckedListBox = null!;
    private TrackBar _fuzzyMatchThresholdTrackBar = null!;
    private Label _fuzzyMatchThresholdValueLabel = null!;
    private TextBox _gitHubRepoTextBox = null!;
    private NumericUpDown _heightNumeric = null!;
    private Panel _infoBackgroundColorSwatch = null!;
    private TextBox _infoBackgroundColorTextBox = null!;
    private ComboBox _infoFontFamilyComboBox = null!;
    private Panel _infoTextColorSwatch = null!;
    private TextBox _infoTextColorTextBox = null!;
    private int _initialHeight = 1080;
    private string _initialObsHost = "localhost";
    private string _initialObsPassword = "";
    private int _initialObsPort = 4455;
    private string _initialSceneName = "Cliparino";
    private string _initialSourceName = "CliparinoPlayer";
    private int _initialWidth = 1920;
    private ComboBox _logLevelComboBox = null!;
    private NumericUpDown _maxClipAgeNumeric = null!;
    private NumericUpDown _maxClipLengthNumeric = null!;
    private Label? _obsAuthCheck;
    private TextBox _obsHostTextBox = null!;
    private RadioButton _obsLocalRadioButton = null!;
    private Label _obsPasswordStoredIndicator = null!;
    private TextBox _obsPasswordTextBox = null!;
    private Label? _obsPortCheck;
    private NumericUpDown _obsPortNumeric = null!;
    private RadioButton _obsRemoteRadioButton = null!;
    private Label? _obsRunningCheck;
    private CheckBox _requireApprovalCheckBox = null!;
    private TextBox _sceneNameTextBox = null!;
    private NumericUpDown _searchWindowDaysNumeric = null!;
    private TextBox _shoutoutMessageTemplateTextBox = null!;
    private CheckBox _showPasswordCheckBox = null!;
    private TextBox _sourceNameTextBox = null!;
    private Label? _twitchStatusLabel;
    private CheckBox _useFeaturedClipsCheckBox = null!;
    private NumericUpDown _widthNumeric = null!;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SettingsForm" /> class.
    /// </summary>
    /// <param name="services">Root service provider used to resolve <see cref="IConfiguration" />.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    public SettingsForm(IServiceProvider services) {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _configuration = services.GetRequiredService<IConfiguration>();

        InitializeComponents();
        LoadSettings();
        UpdateTwitchStatus().SafeFireAndForget("Twitch Status Update");

        // Subscribe to OAuth events to update the status when auth completes
        var oauthService = _services.GetService<ITwitchOAuthService>();

        if (oauthService != null) oauthService.AuthenticationCompleted += OnAuthenticationCompleted;
    }

    protected override void Dispose(bool disposing) {
        if (disposing) {
            // Unsubscribe from events to prevent memory leaks
            var oauthService = _services.GetService<ITwitchOAuthService>();

            if (oauthService != null) oauthService.AuthenticationCompleted -= OnAuthenticationCompleted;
        }

        base.Dispose(disposing);
    }

    private void OnAuthenticationCompleted(object? sender, OAuthCompletedEventArgs e) {
        Log.Information("Twitch authentication completed. Success: {Success}, Username: {Username}, Error: {Error}",
            e.Success, e.Username ?? "N/A", e.ErrorMessage ?? "None");

        this.InvokeIfRequired(() =>
            UpdateTwitchStatus().SafeFireAndForget("Twitch Status Update After Auth"));
    }

    private void InitializeComponents() {
        Text = "Cliparino Settings";
        Size = new Size(600, 500);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var tabControl = new TabControl { Dock = DockStyle.Top, Height = 400 };

        tabControl.TabPages.Add(CreateObsTab());
        tabControl.TabPages.Add(CreatePlayerTab());
        tabControl.TabPages.Add(CreateShoutoutTab());
        tabControl.TabPages.Add(CreateClipSearchTab());
        tabControl.TabPages.Add(CreateUpdateTab());
        tabControl.TabPages.Add(CreateLoggingTab());
        tabControl.TabPages.Add(CreateTwitchTab());

        var resetButton = new Button {
            Text = "Reset to Defaults", Location = new Point(10, 420), Size = new Size(120, 30)
        };
        resetButton.Click += ResetButton_Click;

        var saveButton = new Button {
            Text = "Save", DialogResult = DialogResult.OK, Location = new Point(400, 420), Size = new Size(80, 30)
        };
        saveButton.Click += SaveButton_Click;

        var cancelButton = new Button {
            Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(490, 420), Size = new Size(80, 30)
        };
        cancelButton.Click += (_, _) => Close();

        Controls.Add(tabControl);
        Controls.Add(resetButton);
        Controls.Add(saveButton);
        Controls.Add(cancelButton);

        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private TabPage CreateObsTab() {
        var tab = new TabPage("\U0001F3A5 OBS Connection");
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };

        var y = 10;

        // Pre-flight status section
        var preflightLabel = new Label {
            Text = "OBS Connection Status:",
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
            Location = new Point(10, y),
            Size = new Size(200, 20)
        };
        panel.Controls.Add(preflightLabel);
        y += 25;

        _obsRunningCheck = new Label {
            Text = "⏳ Checking OBS process...", Location = new Point(30, y), Size = new Size(400, 20)
        };
        panel.Controls.Add(_obsRunningCheck);
        y += 22;

        _obsPortCheck = new Label {
            Text = "⏳ Checking WebSocket port...", Location = new Point(30, y), Size = new Size(400, 20)
        };
        panel.Controls.Add(_obsPortCheck);
        y += 22;

        _obsAuthCheck = new Label {
            Text = "⏳ Checking credentials...", Location = new Point(30, y), Size = new Size(400, 20)
        };
        panel.Controls.Add(_obsAuthCheck);
        y += 30;

        var runChecksButton = new Button {
            Text = "Run Pre-Flight Checks", Location = new Point(10, y), Size = new Size(150, 28)
        };
        runChecksButton.Click += (_, _) =>
            RunObsPreFlightChecksAsync().SafeFireAndForget("OBS Pre-flight Checks (Button)");
        panel.Controls.Add(runChecksButton);
        y += 40;

        // Separator
        var separator = new Label {
            BorderStyle = BorderStyle.Fixed3D, Location = new Point(10, y), Size = new Size(530, 2)
        };
        panel.Controls.Add(separator);
        y += 15;

        // Connection settings
        panel.Controls.Add(new Label { Text = "OBS Location:", Location = new Point(10, y), Width = 150 });
        _obsLocalRadioButton = new RadioButton {
            Text = "Same computer (localhost)", Location = new Point(170, y), Width = 200, Checked = true
        };
        _obsLocalRadioButton.CheckedChanged += (_, _) => _obsHostTextBox.Enabled = _obsRemoteRadioButton.Checked;
        panel.Controls.Add(_obsLocalRadioButton);
        y += 25;

        _obsRemoteRadioButton =
            new RadioButton { Text = "Different computer", Location = new Point(170, y), Width = 150 };
        panel.Controls.Add(_obsRemoteRadioButton);

        _obsHostTextBox = new TextBox { Location = new Point(325, y), Width = 150, Enabled = false };
        _toolTip.SetToolTip(_obsHostTextBox, "Enter the IP address of the computer running OBS");
        panel.Controls.Add(_obsHostTextBox);
        y += 35;

        panel.Controls.Add(new Label { Text = "Port:", Location = new Point(10, y), Width = 150 });
        _obsPortNumeric = new NumericUpDown {
            Location = new Point(170, y),
            Width = 80,
            Minimum = 1,
            Maximum = 65535,
            Value = 4455
        };
        _toolTip.SetToolTip(_obsPortNumeric, "Default for OBS 28+ is 4455");
        panel.Controls.Add(_obsPortNumeric);
        y += 35;

        panel.Controls.Add(new Label { Text = "Password (if set in OBS):", Location = new Point(10, y), Width = 150 });
        _obsPasswordTextBox = new TextBox { Location = new Point(170, y), Width = 200, UseSystemPasswordChar = true };
        _toolTip.SetToolTip(_obsPasswordTextBox,
            "Password is hidden for security. Use 'Show' checkbox to reveal it. '✅ Stored' indicator shows if a password is saved.");
        panel.Controls.Add(_obsPasswordTextBox);

        _showPasswordCheckBox = new CheckBox { Text = "Show", Location = new Point(380, y), Width = 60 };
        _showPasswordCheckBox.CheckedChanged += (_, _) =>
            _obsPasswordTextBox.UseSystemPasswordChar = !_showPasswordCheckBox.Checked;
        panel.Controls.Add(_showPasswordCheckBox);

        _obsPasswordStoredIndicator = new Label {
            Text = "",
            Location = new Point(450, y + 3),
            Width = 100,
            ForeColor = Color.Green,
            Font = new Font(Font.FontFamily, 8)
        };
        panel.Controls.Add(_obsPasswordStoredIndicator);
        y += 40;

        var testButton =
            new Button { Text = "Test Connection", Location = new Point(170, y), Size = new Size(120, 30) };
        testButton.Click += TestObsConnection_Click;
        panel.Controls.Add(testButton);

        var helpButton = new Button { Text = "Help", Location = new Point(300, y), Size = new Size(80, 30) };
        helpButton.Click += (_, _) => ShowObsWebSocketHelp();
        panel.Controls.Add(helpButton);

        tab.Controls.Add(panel);

        // Run pre-flight checks when the tab is loaded
        tab.Enter += (_, _) => RunObsPreFlightChecksAsync().SafeFireAndForget("OBS Pre-flight Checks (Tab Enter)");

        return tab;
    }

    private void UpdateObsPreFlightUiConnected() {
        if (_obsRunningCheck != null) {
            _obsRunningCheck.Text = "✅ OBS Studio is running";
            _obsRunningCheck.ForeColor = Color.Green;
        }

        if (_obsPortCheck != null) {
            _obsPortCheck.Text = "✅ WebSocket port is reachable (Connected)";
            _obsPortCheck.ForeColor = Color.Green;
        }

        if (_obsAuthCheck != null) {
            _obsAuthCheck.Text = "✅ Authentication successful";
            _obsAuthCheck.ForeColor = Color.Green;
        }
    }

    private async Task RunObsPreFlightChecksAsync() {
        var obsController = _services.GetService<IObsController>();

        if (obsController is { IsConnected: true }) {
            Log.Information("OBS already connected, skipping detailed pre-flight checks");
            UpdateObsPreFlightUiConnected();

            return;
        }

        // Check 1: Is OBS running?
        _obsRunningCheck!.Text = "⏳ Checking OBS process...";
        _obsRunningCheck.ForeColor = Color.Gray;

        var obsProcesses = Process.GetProcessesByName("obs64");
        if (obsProcesses.Length == 0) obsProcesses = Process.GetProcessesByName("obs");

        if (obsProcesses.Length > 0) {
            _obsRunningCheck.Text = "✅ OBS Studio is running";
            _obsRunningCheck.ForeColor = Color.Green;
        } else {
            _obsRunningCheck.Text = "⚠️ OBS Studio is not running";
            _obsRunningCheck.ForeColor = Color.Orange;
        }

        // Check 2: Is WebSocket port reachable?
        _obsPortCheck!.Text = "⏳ Checking WebSocket port...";
        _obsPortCheck.ForeColor = Color.Gray;

        var host = _obsLocalRadioButton.Checked ? "localhost" : _obsHostTextBox.Text;
        var port = (int)_obsPortNumeric.Value;

        try {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var timeoutTask = Task.Delay(2000);

            if (await Task.WhenAny(connectTask, timeoutTask) == connectTask && client.Connected) {
                _obsPortCheck.Text = $"✅ WebSocket port {port} is reachable";
                _obsPortCheck.ForeColor = Color.Green;
            } else {
                _obsPortCheck.Text =
                    $"⚠️ WebSocket port {port} not reachable\n     → Check that WebSocket server is enabled in OBS";
                _obsPortCheck.ForeColor = Color.Orange;
            }
        } catch {
            _obsPortCheck.Text = $"❌ Cannot reach WebSocket port {port}";
            _obsPortCheck.ForeColor = Color.Red;
        }

        // Check 3: Try to validate credentials
        _obsAuthCheck!.Text = "⏳ Checking credentials...";
        _obsAuthCheck.ForeColor = Color.Gray;

        if (obsController != null) {
            try {
                var password = _obsPasswordTextBox.Text;
                var connected = await obsController.ConnectAsync(host, port, password);

                if (connected) {
                    Log.Information("Pre-flight: OBS Authentication successful");
                    _obsAuthCheck.Text = "✅ Authentication successful";
                    _obsAuthCheck.ForeColor = Color.Green;

                    // Don't disconnect - let ObsHealthSupervisor manage the connection
                } else {
                    Log.Warning("Pre-flight: OBS Authentication failed (Host: {Host}, Port: {Port})", host, port);
                    _obsAuthCheck.Text = string.IsNullOrEmpty(password)
                        ? "⚠️ Connection failed - may need password"
                        : "❌ Authentication failed - check password";
                    _obsAuthCheck.ForeColor = Color.Orange;
                }
            } catch (Exception ex) {
                _obsAuthCheck.Text = $"❌ Error: {ex.Message}";
                _obsAuthCheck.ForeColor = Color.Red;
            }
        } else {
            _obsAuthCheck.Text = "⚠️ OBS service not available";
            _obsAuthCheck.ForeColor = Color.Orange;
        }
    }

    private void ShowObsWebSocketHelp() {
        var message = """
                      How to Enable OBS WebSocket Server:

                      1. Open OBS Studio
                      2. Go to Tools → WebSocket Server Settings
                      3. Check "Enable WebSocket server"
                      4. Note the port number (default: 4455)
                      5. If you set a password, enter it in Cliparino

                      OBS 28 and newer have WebSocket built-in.
                      For older versions, install the obs-websocket plugin.

                      For more help, visit our wiki.
                      """;

        var result = MessageBox.Show(message,
            "How to Enable WebSocket",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);

        if (result == DialogResult.Cancel)
            Process.Start(
                new ProcessStartInfo("https://github.com/angrmgmt/Cliparino/wiki/OBS-Setup") {
                    UseShellExecute = true
                });
    }

    private static void UpdateColorSwatch(TextBox textBox, Panel swatch) {
        try {
            var hex = textBox.Text.Trim();
            if (!hex.StartsWith('#')) hex = "#" + hex;
            swatch.BackColor = ColorTranslator.FromHtml(hex);
        } catch {
            // Silently ignore invalid color input
        }
    }

    private static void ShowColorPicker(TextBox textBox, Panel swatch) {
        using var dialog = new ColorDialog();
        dialog.FullOpen = true;

        try {
            var hex = textBox.Text.Trim();

            if (!hex.StartsWith('#')) hex = "#" + hex;
            dialog.Color = ColorTranslator.FromHtml(hex);
        } catch {
            // Use the default color if the current text is invalid
        }

        if (dialog.ShowDialog() != DialogResult.OK) return;

        textBox.Text = "#" + dialog.Color.R.ToString("x2") + dialog.Color.G.ToString("x2") +
                       dialog.Color.B.ToString("x2");
        swatch.BackColor = dialog.Color;
    }

    private TabPage CreatePlayerTab() {
        var tab = new TabPage("\u25B6\uFE0F Player Settings");
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };
        var y = 10;

        panel.Controls.Add(new Label { Text = "Scene Name:", Location = new Point(10, y), Width = 150 });
        _sceneNameTextBox = new TextBox { Location = new Point(170, y), Width = 200 };
        _toolTip.SetToolTip(_sceneNameTextBox,
            "The OBS scene where clips will be played (will be created automatically if it doesn't exist)");
        panel.Controls.Add(_sceneNameTextBox);
        y += 30;

        panel.Controls.Add(new Label { Text = "Source Name:", Location = new Point(10, y), Width = 150 });
        _sourceNameTextBox = new TextBox { Location = new Point(170, y), Width = 200 };
        _toolTip.SetToolTip(_sourceNameTextBox,
            "The browser source name in OBS for the clip player (will be created automatically)");
        panel.Controls.Add(_sourceNameTextBox);
        y += 30;

        panel.Controls.Add(new Label { Text = "Width:", Location = new Point(10, y), Width = 150 });
        _widthNumeric = new NumericUpDown {
            Location = new Point(170, y),
            Width = 100,
            Minimum = 320,
            Maximum = 7680,
            Value = 1920
        };
        _toolTip.SetToolTip(_widthNumeric, "The width of the clip player in pixels (e.g., 1920 for Full HD)");
        panel.Controls.Add(_widthNumeric);
        y += 30;

        panel.Controls.Add(new Label { Text = "Height:", Location = new Point(10, y), Width = 150 });
        _heightNumeric = new NumericUpDown {
            Location = new Point(170, y),
            Width = 100,
            Minimum = 240,
            Maximum = 4320,
            Value = 1080
        };
        _toolTip.SetToolTip(_heightNumeric, "The height of the clip player in pixels (e.g., 1080 for Full HD)");
        panel.Controls.Add(_heightNumeric);
        y += 40;

        panel.Controls.Add(new Label {
            Text = "Info Text Color:", Location = new Point(10, y), Width = 150, Font = new Font(Font, FontStyle.Bold)
        });
        y += 20;

        panel.Controls.Add(new Label { Text = "Text Color (hex):", Location = new Point(10, y), Width = 150 });
        _infoTextColorTextBox = new TextBox { Location = new Point(170, y), Width = 150, Text = "#ffb809" };
        _toolTip.SetToolTip(_infoTextColorTextBox, "Text color for clip info (hex color code, e.g., #ffb809)");
        panel.Controls.Add(_infoTextColorTextBox);
        _infoTextColorSwatch = new Panel {
            Location = new Point(330, y), Size = new Size(24, 24), BorderStyle = BorderStyle.FixedSingle
        };
        _toolTip.SetToolTip(_infoTextColorSwatch, "Click to open color picker");
        _infoTextColorTextBox.TextChanged += (_, _) => UpdateColorSwatch(_infoTextColorTextBox, _infoTextColorSwatch);
        _infoTextColorSwatch.Click += (_, _) => ShowColorPicker(_infoTextColorTextBox, _infoTextColorSwatch);
        panel.Controls.Add(_infoTextColorSwatch);
        y += 30;

        panel.Controls.Add(new Label { Text = "Background Color (hex):", Location = new Point(10, y), Width = 150 });
        _infoBackgroundColorTextBox = new TextBox { Location = new Point(170, y), Width = 150, Text = "#0071c5" };
        _toolTip.SetToolTip(_infoBackgroundColorTextBox,
            "Background color for info bars (hex color code, e.g., #0071c5)");
        panel.Controls.Add(_infoBackgroundColorTextBox);
        _infoBackgroundColorSwatch = new Panel {
            Location = new Point(330, y), Size = new Size(24, 24), BorderStyle = BorderStyle.FixedSingle
        };
        _toolTip.SetToolTip(_infoBackgroundColorSwatch, "Click to open color picker");
        _infoBackgroundColorTextBox.TextChanged += (_, _) =>
            UpdateColorSwatch(_infoBackgroundColorTextBox, _infoBackgroundColorSwatch);
        _infoBackgroundColorSwatch.Click +=
            (_, _) => ShowColorPicker(_infoBackgroundColorTextBox, _infoBackgroundColorSwatch);
        panel.Controls.Add(_infoBackgroundColorSwatch);
        y += 30;

        panel.Controls.Add(new Label { Text = "Font Family:", Location = new Point(10, y), Width = 150 });
        _infoFontFamilyComboBox = new ComboBox {
            Location = new Point(170, y),
            Width = 300,
            DropDownStyle = ComboBoxStyle.DropDown,
            Text = "OpenDyslexic, 'Open Sans', sans-serif"
        };

        try {
            foreach (var family in FontFamily.Families)
                _infoFontFamilyComboBox.Items.Add(family.Name);
        } catch {
            _infoFontFamilyComboBox.Items.AddRange(["OpenDyslexic", "Open Sans", "sans-serif"]);
        }

        _toolTip.SetToolTip(_infoFontFamilyComboBox,
            "CSS font-family for clip info text (select a system font or type a custom CSS font-family string)");
        panel.Controls.Add(_infoFontFamilyComboBox);

        tab.Controls.Add(panel);

        return tab;
    }

    private TabPage CreateShoutoutTab() {
        var tab = new TabPage("\U0001F4E2 Shoutouts");
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };
        var y = 10;

        _enableShoutoutMessageCheckBox =
            new CheckBox { Text = "Enable shoutout messages", Location = new Point(10, y), Width = 300 };
        _toolTip.SetToolTip(_enableShoutoutMessageCheckBox, "Send a chat message when performing a shoutout");
        panel.Controls.Add(_enableShoutoutMessageCheckBox);
        y += 30;

        panel.Controls.Add(new Label { Text = "Message Template:", Location = new Point(10, y), Width = 150 });
        y += 20;
        _shoutoutMessageTemplateTextBox = new TextBox {
            Location = new Point(10, y),
            Width = 500,
            Height = 60,
            Multiline = true,
            Text = "Check out {broadcaster}! They were last playing {game}! twitch.tv/{broadcaster}"
        };
        _toolTip.SetToolTip(_shoutoutMessageTemplateTextBox,
            "Template for shoutout messages. Use {broadcaster} for channel name and {game} for the game they were playing");
        panel.Controls.Add(_shoutoutMessageTemplateTextBox);
        y += 70;

        _useFeaturedClipsCheckBox =
            new CheckBox { Text = "Prefer featured clips", Location = new Point(10, y), Width = 300 };
        _toolTip.SetToolTip(_useFeaturedClipsCheckBox,
            "Prioritize clips marked as 'featured' by the broadcaster when selecting random clips");
        panel.Controls.Add(_useFeaturedClipsCheckBox);
        y += 30;

        panel.Controls.Add(new Label { Text = "Max Clip Length (seconds):", Location = new Point(10, y), Width = 180 });
        _maxClipLengthNumeric = new NumericUpDown {
            Location = new Point(200, y),
            Width = 100,
            Minimum = 5,
            Maximum = 300,
            Value = 60
        };
        _toolTip.SetToolTip(_maxClipLengthNumeric, "Maximum duration of clips to select for shoutouts (in seconds)");
        panel.Controls.Add(_maxClipLengthNumeric);
        y += 30;

        panel.Controls.Add(new Label { Text = "Max Clip Age (days):", Location = new Point(10, y), Width = 180 });
        _maxClipAgeNumeric = new NumericUpDown {
            Location = new Point(200, y),
            Width = 100,
            Minimum = 1,
            Maximum = 365,
            Value = 30
        };
        _toolTip.SetToolTip(_maxClipAgeNumeric, "How far back to search for clips when performing shoutouts (in days)");
        panel.Controls.Add(_maxClipAgeNumeric);

        tab.Controls.Add(panel);

        return tab;
    }

    private TabPage CreateLoggingTab() {
        var tab = new TabPage("\U0001F4DD Logging");
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };
        var y = 10;

        panel.Controls.Add(new Label { Text = "Log Level:", Location = new Point(10, y), Width = 150 });
        _logLevelComboBox = new ComboBox {
            Location = new Point(170, y), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList
        };
        _logLevelComboBox.Items.AddRange(["Debug", "Information", "Warning", "Error"]);
        _toolTip.SetToolTip(_logLevelComboBox,
            "Set the logging verbosity: Debug (most detailed), Information (normal), Warning, or Error (least detailed)");
        panel.Controls.Add(_logLevelComboBox);
        y += 30;

        var enableDebugLoggingCheckBox =
            new CheckBox { Text = "Enable debug logging", Location = new Point(10, y), Width = 300 };
        panel.Controls.Add(enableDebugLoggingCheckBox);

        tab.Controls.Add(panel);

        return tab;
    }

    private TabPage CreateClipSearchTab() {
        var tab = new TabPage("\U0001F50D Clip Search");
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };

        var y = 10;

        panel.Controls.Add(new Label { Text = "Search Window (days):", Location = new Point(10, y), Width = 180 });
        _searchWindowDaysNumeric = new NumericUpDown {
            Location = new Point(200, y),
            Width = 100,
            Minimum = 1,
            Maximum = 365,
            Value = 90
        };
        _toolTip.SetToolTip(_searchWindowDaysNumeric,
            "How far back to search for clips when a user requests a clip by name (1-365 days)");
        panel.Controls.Add(_searchWindowDaysNumeric);
        y += 30;

        panel.Controls.Add(new Label { Text = "Fuzzy Match Threshold:", Location = new Point(10, y), Width = 180 });
        _fuzzyMatchThresholdTrackBar = new TrackBar {
            Location = new Point(200, y),
            Width = 200,
            Minimum = 0,
            Maximum = 100,
            Value = 40,
            TickFrequency = 10
        };
        _fuzzyMatchThresholdValueLabel = new Label { Location = new Point(410, y + 5), Width = 50, Text = "0.40" };
        _fuzzyMatchThresholdTrackBar.ValueChanged += (_, _) => {
            _fuzzyMatchThresholdValueLabel.Text = (_fuzzyMatchThresholdTrackBar.Value / 100.0).ToString("F2");
        };
        _toolTip.SetToolTip(_fuzzyMatchThresholdTrackBar,
            "How similar a clip name must be to match (0.0 = exact match, 1.0 = any match)");
        panel.Controls.Add(_fuzzyMatchThresholdTrackBar);
        panel.Controls.Add(_fuzzyMatchThresholdValueLabel);
        y += 50;

        _requireApprovalCheckBox = new CheckBox {
            Text = "Require moderator approval for clip searches", Location = new Point(10, y), Width = 400
        };
        _toolTip.SetToolTip(_requireApprovalCheckBox,
            "When enabled, clip search requests from viewers require moderator approval");
        panel.Controls.Add(_requireApprovalCheckBox);
        y += 30;

        panel.Controls.Add(new Label {
            Text = "Approval Timeout (seconds):", Location = new Point(10, y), Width = 180
        });
        _approvalTimeoutNumeric = new NumericUpDown {
            Location = new Point(200, y),
            Width = 100,
            Minimum = 10,
            Maximum = 300,
            Value = 30
        };
        _toolTip.SetToolTip(_approvalTimeoutNumeric,
            "How long to wait for moderator approval before timing out (10-300 seconds)");
        panel.Controls.Add(_approvalTimeoutNumeric);
        y += 30;

        panel.Controls.Add(new Label {
            Text = "Exempt Roles (skip approval):", Location = new Point(10, y), Width = 200
        });
        y += 25;
        _exemptRolesCheckedListBox = new CheckedListBox { Location = new Point(10, y), Width = 250, Height = 80 };
        _exemptRolesCheckedListBox.Items.Add("Broadcaster");
        _exemptRolesCheckedListBox.Items.Add("Moderator");
        _exemptRolesCheckedListBox.Items.Add("VIP");
        _exemptRolesCheckedListBox.Items.Add("Subscriber");
        _toolTip.SetToolTip(_exemptRolesCheckedListBox, "Users with these roles can search for clips without approval");
        panel.Controls.Add(_exemptRolesCheckedListBox);
        y += 90;

        var resetButton = new Button {
            Text = "Reset to Defaults", Location = new Point(10, y), Size = new Size(130, 30)
        };
        resetButton.Click += (_, _) => {
            _searchWindowDaysNumeric.Value = 90;
            _fuzzyMatchThresholdTrackBar.Value = 40;
            _requireApprovalCheckBox.Checked = true;
            _approvalTimeoutNumeric.Value = 30;
            _exemptRolesCheckedListBox.SetItemChecked(0, true);
            _exemptRolesCheckedListBox.SetItemChecked(1, true);
            _exemptRolesCheckedListBox.SetItemChecked(2, false);
            _exemptRolesCheckedListBox.SetItemChecked(3, false);
        };
        panel.Controls.Add(resetButton);

        tab.Controls.Add(panel);

        return tab;
    }

    private TabPage CreateUpdateTab() {
        var tab = new TabPage("\u2B06\uFE0F Updates");
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };
        var y = 10;

        _checkOnStartupCheckBox =
            new CheckBox { Text = "Check for updates on startup", Location = new Point(10, y), Width = 300 };
        _toolTip.SetToolTip(_checkOnStartupCheckBox, "Automatically check for new versions when Cliparino starts");
        panel.Controls.Add(_checkOnStartupCheckBox);
        y += 30;

        panel.Controls.Add(new Label { Text = "Check Interval (hours):", Location = new Point(10, y), Width = 180 });
        _checkIntervalHoursNumeric = new NumericUpDown {
            Location = new Point(200, y),
            Width = 100,
            Minimum = 1,
            Maximum = 168,
            Value = 24
        };
        _toolTip.SetToolTip(_checkIntervalHoursNumeric,
            "How often to check for updates while Cliparino is running (1-168 hours)");
        panel.Controls.Add(_checkIntervalHoursNumeric);
        y += 30;

        panel.Controls.Add(new Label { Text = "GitHub Repository:", Location = new Point(10, y), Width = 180 });
        _gitHubRepoTextBox = new TextBox { Location = new Point(200, y), Width = 250 };
        _toolTip.SetToolTip(_gitHubRepoTextBox, "GitHub repository to check for updates (format: owner/repo)");
        panel.Controls.Add(_gitHubRepoTextBox);
        y += 40;

        var resetButton = new Button {
            Text = "Reset to Defaults", Location = new Point(10, y), Size = new Size(130, 30)
        };
        resetButton.Click += (_, _) => {
            _checkOnStartupCheckBox.Checked = true;
            _checkIntervalHoursNumeric.Value = 24;
            _gitHubRepoTextBox.Text = "angrmgmt/Cliparino";
        };
        panel.Controls.Add(resetButton);

        tab.Controls.Add(panel);

        return tab;
    }

    private TabPage CreateTwitchTab() {
        var tab = new TabPage("\U0001F3AE Twitch");
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };
        var y = 10;

        var loginButton = new Button {
            Text = "Connect Twitch Account", Location = new Point(10, y), Size = new Size(200, 40)
        };
        loginButton.Click += TwitchLogin_Click;
        panel.Controls.Add(loginButton);
        y += 50;

        _twitchStatusLabel = new Label {
            Text = "Status: Checking...", Location = new Point(10, y), Width = 400, ForeColor = Color.Gray
        };
        panel.Controls.Add(_twitchStatusLabel);

        tab.Controls.Add(panel);

        return tab;
    }

    private void LoadSettings() {
        _initialObsHost = _configuration["OBS:Host"] ?? "localhost";

        if (_initialObsHost == "localhost" || _initialObsHost == "127.0.0.1") {
            _obsLocalRadioButton.Checked = true;
            _obsHostTextBox.Text = "localhost";
            _obsHostTextBox.Enabled = false;
        } else {
            _obsRemoteRadioButton.Checked = true;
            _obsHostTextBox.Text = _initialObsHost;
            _obsHostTextBox.Enabled = true;
        }

        _initialObsPort = int.Parse(_configuration["OBS:Port"] ?? "4455");
        _obsPortNumeric.Value = _initialObsPort;

        _initialObsPassword = _configuration["OBS:Password"] ?? "";
        _obsPasswordTextBox.Text = _initialObsPassword;

        // Update password stored indicator
        if (!string.IsNullOrEmpty(_initialObsPassword)) {
            _obsPasswordStoredIndicator.Text = "✅ Stored";
            _obsPasswordStoredIndicator.ForeColor = Color.Green;
        } else {
            _obsPasswordStoredIndicator.Text = "";
        }

        _initialSceneName = _configuration["OBS:SceneName"] ?? _configuration["Player:SceneName"] ?? "Cliparino";
        _sceneNameTextBox.Text = _initialSceneName;

        _initialSourceName =
            _configuration["OBS:SourceName"] ?? _configuration["Player:SourceName"] ?? "CliparinoPlayer";
        _sourceNameTextBox.Text = _initialSourceName;

        _initialWidth = int.Parse(_configuration["OBS:Width"] ?? _configuration["Player:Width"] ?? "1920");
        _widthNumeric.Value = _initialWidth;

        _initialHeight = int.Parse(_configuration["OBS:Height"] ?? _configuration["Player:Height"] ?? "1080");
        _heightNumeric.Value = _initialHeight;

        _infoTextColorTextBox.Text = _configuration["Player:InfoTextColor"] ?? "#ffb809";
        UpdateColorSwatch(_infoTextColorTextBox, _infoTextColorSwatch);
        _infoBackgroundColorTextBox.Text = _configuration["Player:InfoBackgroundColor"] ?? "#0071c5";
        UpdateColorSwatch(_infoBackgroundColorTextBox, _infoBackgroundColorSwatch);
        _infoFontFamilyComboBox.Text =
            _configuration["Player:InfoFontFamily"] ?? "OpenDyslexic, 'Open Sans', sans-serif";

        _enableShoutoutMessageCheckBox.Checked = bool.Parse(_configuration["Shoutout:EnableMessage"] ?? "true");
        _shoutoutMessageTemplateTextBox.Text = _configuration["Shoutout:MessageTemplate"] ??
                                               "Check out {broadcaster}! They were last playing {game}! twitch.tv/{broadcaster}";
        _useFeaturedClipsCheckBox.Checked = bool.Parse(_configuration["Shoutout:UseFeaturedClips"] ?? "true");
        _maxClipLengthNumeric.Value = int.Parse(_configuration["Shoutout:MaxClipLength"] ?? "60");
        _maxClipAgeNumeric.Value = int.Parse(_configuration["Shoutout:MaxClipAge"] ?? "30");

        _searchWindowDaysNumeric.Value = int.Parse(_configuration["ClipSearch:SearchWindowDays"] ?? "90");
        var threshold = double.Parse(_configuration["ClipSearch:FuzzyMatchThreshold"] ?? "0.4",
            CultureInfo.InvariantCulture);
        _fuzzyMatchThresholdTrackBar.Value = (int)(threshold * 100);
        _requireApprovalCheckBox.Checked = bool.Parse(_configuration["ClipSearch:RequireApproval"] ?? "true");
        _approvalTimeoutNumeric.Value = int.Parse(_configuration["ClipSearch:ApprovalTimeoutSeconds"] ?? "30");

        var exemptRoles = _configuration.GetSection("ClipSearch:ExemptRoles").GetChildren()
            .Select(x => x.Value?.ToLower()).ToList();
        _exemptRolesCheckedListBox.SetItemChecked(0, exemptRoles.Contains("broadcaster"));
        _exemptRolesCheckedListBox.SetItemChecked(1, exemptRoles.Contains("moderator"));
        _exemptRolesCheckedListBox.SetItemChecked(2, exemptRoles.Contains("vip"));
        _exemptRolesCheckedListBox.SetItemChecked(3, exemptRoles.Contains("subscriber"));

        _checkOnStartupCheckBox.Checked = bool.Parse(_configuration["Update:CheckOnStartup"] ?? "true");
        _checkIntervalHoursNumeric.Value = int.Parse(_configuration["Update:CheckIntervalHours"] ?? "24");
        _gitHubRepoTextBox.Text = _configuration["Update:GitHubRepo"] ?? "angrmgmt/Cliparino";

        _logLevelComboBox.SelectedItem = _configuration["Logging:LogLevel:Default"] ?? "Information";
    }

    private void SaveButton_Click(object? sender, EventArgs e) {
        SaveSettingsAsync().SafeFireAndForget("Save Settings");
    }

    private async Task SaveSettingsAsync() {
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        try {
            var existingConfig = new Dictionary<string, object>();

            if (File.Exists(configPath)) {
                var existingJson = await File.ReadAllTextAsync(configPath);
                var doc = JsonSerializer.Deserialize<Dictionary<string, object>>(existingJson);
                if (doc != null) existingConfig = doc;
            }

            existingConfig["OBS"] = new Dictionary<string, object> {
                ["Host"] = _obsLocalRadioButton.Checked ? "localhost" : _obsHostTextBox.Text,
                ["Port"] = _obsPortNumeric.Value.ToString(CultureInfo.InvariantCulture),
                ["Password"] = _obsPasswordTextBox.Text,
                ["SceneName"] = _sceneNameTextBox.Text,
                ["SourceName"] = _sourceNameTextBox.Text,
                ["Width"] = _widthNumeric.Value.ToString(CultureInfo.InvariantCulture),
                ["Height"] = _heightNumeric.Value.ToString(CultureInfo.InvariantCulture)
            };

            existingConfig["Player"] = new Dictionary<string, object> {
                ["Url"] = _configuration["Player:Url"] ?? "http://localhost:5291",
                ["SceneName"] = _sceneNameTextBox.Text,
                ["SourceName"] = _sourceNameTextBox.Text,
                ["Width"] = _widthNumeric.Value.ToString(CultureInfo.InvariantCulture),
                ["Height"] = _heightNumeric.Value.ToString(CultureInfo.InvariantCulture),
                ["InfoTextColor"] = _infoTextColorTextBox.Text,
                ["InfoBackgroundColor"] = _infoBackgroundColorTextBox.Text,
                ["InfoFontFamily"] = _infoFontFamilyComboBox.Text
            };

            existingConfig["Shoutout"] = new Dictionary<string, object> {
                ["EnableMessage"] = _enableShoutoutMessageCheckBox.Checked,
                ["MessageTemplate"] = _shoutoutMessageTemplateTextBox.Text,
                ["UseFeaturedClips"] = _useFeaturedClipsCheckBox.Checked,
                ["MaxClipLength"] = (int)_maxClipLengthNumeric.Value,
                ["MaxClipAge"] = (int)_maxClipAgeNumeric.Value,
                ["SendTwitchShoutout"] = bool.Parse(_configuration["Shoutout:SendTwitchShoutout"] ?? "true")
            };

            var exemptRolesList = new List<string>();
            if (_exemptRolesCheckedListBox.GetItemChecked(0)) exemptRolesList.Add("broadcaster");
            if (_exemptRolesCheckedListBox.GetItemChecked(1)) exemptRolesList.Add("moderator");
            if (_exemptRolesCheckedListBox.GetItemChecked(2)) exemptRolesList.Add("vip");
            if (_exemptRolesCheckedListBox.GetItemChecked(3)) exemptRolesList.Add("subscriber");

            existingConfig["ClipSearch"] = new Dictionary<string, object> {
                ["SearchWindowDays"] = (int)_searchWindowDaysNumeric.Value,
                ["FuzzyMatchThreshold"] = _fuzzyMatchThresholdTrackBar.Value / 100.0,
                ["RequireApproval"] = _requireApprovalCheckBox.Checked,
                ["ApprovalTimeoutSeconds"] = (int)_approvalTimeoutNumeric.Value,
                ["ExemptRoles"] = exemptRolesList
            };

            existingConfig["Update"] = new Dictionary<string, object> {
                ["GitHubRepo"] = _gitHubRepoTextBox.Text,
                ["CheckOnStartup"] = _checkOnStartupCheckBox.Checked,
                ["CheckIntervalHours"] = (int)_checkIntervalHoursNumeric.Value
            };

            if (!existingConfig.ContainsKey("Logging")) existingConfig["Logging"] = new Dictionary<string, object>();
            var loggingSection = existingConfig["Logging"] as Dictionary<string, object> ??
                                 new Dictionary<string, object>();
            if (!loggingSection.ContainsKey("LogLevel")) loggingSection["LogLevel"] = new Dictionary<string, object>();
            var logLevelSection = loggingSection["LogLevel"] as Dictionary<string, object> ??
                                  new Dictionary<string, object>();
            logLevelSection["Default"] = _logLevelComboBox.SelectedItem?.ToString() ?? "Information";
            loggingSection["LogLevel"] = logLevelSection;
            existingConfig["Logging"] = loggingSection;

            var json = JsonSerializer.Serialize(existingConfig, JsonOptions);

            await File.WriteAllTextAsync(configPath, json);

            // Only trigger OBS reconnection if OBS settings actually changed
            var newHost = _obsLocalRadioButton.Checked ? "localhost" : _obsHostTextBox.Text;
            var newPort = (int)_obsPortNumeric.Value;
            var newPassword = _obsPasswordTextBox.Text;
            var newSceneName = _sceneNameTextBox.Text;
            var newSourceName = _sourceNameTextBox.Text;
            var newWidth = (int)_widthNumeric.Value;
            var newHeight = (int)_heightNumeric.Value;

            var obsSettingsChanged =
                _initialObsHost != newHost ||
                _initialObsPort != newPort ||
                _initialObsPassword != newPassword ||
                _initialSceneName != newSceneName ||
                _initialSourceName != newSourceName ||
                _initialWidth != newWidth ||
                _initialHeight != newHeight;

            if (obsSettingsChanged)
                Log.Information(
                    "OBS settings changed (Password changed: {PasswordChanged}). Connection will be reset by health supervisor.",
                    _initialObsPassword != newPassword);

            MessageBox.Show("Settings saved successfully.\n\nChanges will take effect automatically.",
                "Settings Saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            Close();
        } catch (Exception ex) {
            MessageBox.Show($"Failed to save settings:\n{ex.Message}",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void TestObsConnection_Click(object? sender, EventArgs e) {
        TestObsConnectionAsync(sender as Button).SafeFireAndForget("Test OBS Connection");
    }

    private async Task TestObsConnectionAsync(Button? button) {
        if (button != null) {
            button.Enabled = false;
            button.Text = "Testing...";

            try {
                var obsController = _services.GetService<IObsController>();

                if (obsController == null) {
                    MessageBox.Show("OBS controller service is not available.\n\nPlease restart Cliparino.",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);

                    return;
                }

                var host = _obsHostTextBox.Text;
                var port = (int)_obsPortNumeric.Value;
                var password = _obsPasswordTextBox.Text;

                // If already connected, check if we're testing the same settings
                if (obsController.IsConnected) {
                    var currentHost = _configuration["OBS:Host"];
                    var currentPort = _configuration["OBS:Port"];
                    var currentPassword = _configuration["OBS:Password"];

                    // If testing the same connection that's already active, just report success
                    if (host == currentHost && port.ToString() == currentPort && password == currentPassword) {
                        MessageBox.Show(
                            $"Already connected to OBS!\n\nHost: {host}\nPort: {port}\n\nConnection is working correctly.",
                            "Test Connection",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);

                        return;
                    }

                    // If testing different settings, we need to disconnect first to allow the new connection
                    var result = MessageBox.Show(
                        "To test these settings, the current OBS connection must be temporarily closed.\n\nContinue?",
                        "Change Connection",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (result == DialogResult.Yes)
                        await obsController.DisconnectAsync();
                    else
                        return;
                }

                var success = await obsController.ConnectAsync(host, port, password);

                if (success) {
                    UpdateObsPreFlightUiConnected();
                    MessageBox.Show(
                        $"Successfully connected to OBS!\n\nHost: {host}\nPort: {port}\n\nConnection is working correctly.",
                        "Test Connection",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    // Leave connection as-is - ObsHealthSupervisor manages the connection lifecycle
                } else {
                    MessageBox.Show(
                        $"Failed to connect to OBS.\n\nHost: {host}\nPort: {port}\n\nPlease ensure:\n- OBS is running\n- WebSocket server is enabled in OBS\n- The password is correct\n- The host and port are correct",
                        "Test Connection Failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    // ObsHealthSupervisor will attempt to reconnect
                }
            } catch (Exception ex) {
                MessageBox.Show($"Error testing OBS connection:\n{ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            } finally {
                button.Enabled = true;
                button.Text = "Test Connection";
            }
        }
    }

    private void ResetButton_Click(object? sender, EventArgs e) {
        var result = MessageBox.Show(
            "Reset all settings to defaults? This can't be undone.\n\nA backup of your current settings will be created.",
            "Reset Settings",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes) return;

        try {
            var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

            if (File.Exists(configPath)) {
                var backupPath = configPath + ".backup";
                File.Copy(configPath, backupPath, true);
            }

            // Create default config
            var defaultConfig = new Dictionary<string, object> {
                ["OBS"] =
                    new Dictionary<string, string> { ["Host"] = "localhost", ["Port"] = "4455", ["Password"] = "" },
                ["Player"] =
                    new Dictionary<string, object> {
                        ["Url"] = "http://localhost:5291",
                        ["SceneName"] = "Cliparino",
                        ["SourceName"] = "Cliparino Player",
                        ["Width"] = "1920",
                        ["Height"] = "1080"
                    },
                ["Shoutout"] = new Dictionary<string, object> {
                    ["EnableMessage"] = true,
                    ["MessageTemplate"] =
                        "Check out {broadcaster}! They were last playing {game}! twitch.tv/{broadcaster}",
                    ["UseFeaturedClips"] = true,
                    ["MaxClipLength"] = 60,
                    ["MaxClipAge"] = 30,
                    ["SendTwitchShoutout"] = true
                },
                ["ClipSearch"] =
                    new Dictionary<string, object> {
                        ["SearchWindowDays"] = 90,
                        ["FuzzyMatchThreshold"] = 0.4,
                        ["RequireApproval"] = true,
                        ["ApprovalTimeoutSeconds"] = 30,
                        ["ExemptRoles"] = new[] { "broadcaster", "moderator" }
                    },
                ["Update"] =
                    new Dictionary<string, object> {
                        ["CheckOnStartup"] = true, ["CheckIntervalHours"] = 24, ["GitHubRepo"] = "angrmgmt/Cliparino"
                    },
                ["Logging"] = new Dictionary<string, object> {
                    ["LogLevel"] = new Dictionary<string, string> { ["Default"] = "Information" }
                }
            };

            var json = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);

            LoadSettings();
            MessageBox.Show("Settings reset to defaults. Please restart Cliparino for all changes to take effect.",
                "Reset Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        } catch (Exception ex) {
            MessageBox.Show($"Failed to reset settings: {ex.Message}", "Error", MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }


    private async Task UpdateTwitchStatus() {
        if (_twitchStatusLabel == null) return;

        try {
            var authStore = _services.GetService<ITwitchAuthStore>();
            var healthReporter = _services.GetService<IHealthReporter>();

            if (authStore == null) {
                _twitchStatusLabel.Text = "Status: Auth service unavailable";
                _twitchStatusLabel.ForeColor = Color.Red;

                return;
            }

            var hasTokens = await authStore.HasValidTokensAsync();

            if (hasTokens) {
                _twitchStatusLabel.Text = "Status: ✅ Connected";
                _twitchStatusLabel.ForeColor = Color.Green;
                _toolTip.SetToolTip(_twitchStatusLabel,
                    "Cliparino is connected to Twitch and ready to receive commands.");

                // Also update the tray icon if possible
                healthReporter?.ReportHealth("TwitchAuth", ComponentStatus.Healthy);
            } else {
                _twitchStatusLabel.Text = "Status: Not connected";
                _twitchStatusLabel.ForeColor = Color.Gray;
                _toolTip.SetToolTip(_twitchStatusLabel,
                    "Cliparino is not connected to Twitch. Please authenticate to enable chat commands.");

                healthReporter?.ReportHealth("TwitchAuth", ComponentStatus.Unhealthy);
            }
        } catch (Exception ex) {
            _twitchStatusLabel.Text = $"Status: Error - {ex.Message}";
            _twitchStatusLabel.ForeColor = Color.Red;
        }
    }

    private static void TwitchLogin_Click(object? sender, EventArgs e) {
        TwitchLoginAsync(sender as Button).SafeFireAndForget("Twitch Login");
    }

    private static async Task TwitchLoginAsync(Button? button) {
        if (button != null) {
            button.Enabled = false;
            button.Text = "Opening browser...";

            try {
                var httpClient = new HttpClient();
                var response = await httpClient.GetStringAsync("http://localhost:5291/auth/login");
                var authData = JsonSerializer.Deserialize<Dictionary<string, string>>(response);

                if (authData != null && authData.TryGetValue("authUrl", out var authUrl)) {
                    MessageBox.Show(
                        "Twitch authentication will now open in your default browser.\n\nPlease log in and authorize Cliparino.",
                        "Twitch Login",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
                } else {
                    MessageBox.Show("Failed to get authorization URL from server.",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            } catch (Exception ex) {
                MessageBox.Show($"Failed to start authentication:\n{ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            } finally {
                button.Enabled = true;
                button.Text = "Connect Twitch Account";
            }
        }
    }
}