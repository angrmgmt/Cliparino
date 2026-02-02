using System.Globalization;
using System.Text.Json;

namespace Cliparino.Core.UI;

public class SettingsForm : Form {
    private readonly IConfiguration _configuration;

    private CheckBox _enableShoutoutMessageCheckBox = null!;
    private NumericUpDown _heightNumeric = null!;

    private ComboBox _logLevelComboBox = null!;
    private NumericUpDown _maxClipAgeNumeric = null!;
    private NumericUpDown _maxClipLengthNumeric = null!;

    private TextBox _obsHostTextBox = null!;
    private TextBox _obsPasswordTextBox = null!;
    private NumericUpDown _obsPortNumeric = null!;

    private TextBox _sceneNameTextBox = null!;
    private TextBox _shoutoutMessageTemplateTextBox = null!;
    private TextBox _sourceNameTextBox = null!;
    private CheckBox _useFeaturedClipsCheckBox = null!;
    private NumericUpDown _widthNumeric = null!;

    public SettingsForm(IServiceProvider services) {
        _ = services;
        _configuration = services.GetRequiredService<IConfiguration>();

        InitializeComponents();
        LoadSettings();
    }

    private void InitializeComponents() {
        Text = "Cliparino Settings";
        Size = new Size(600, 500);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var tabControl = new TabControl {
            Dock = DockStyle.Top,
            Height = 400
        };

        tabControl.TabPages.Add(CreateObsTab());
        tabControl.TabPages.Add(CreatePlayerTab());
        tabControl.TabPages.Add(CreateShoutoutTab());
        tabControl.TabPages.Add(CreateLoggingTab());
        tabControl.TabPages.Add(CreateTwitchTab());

        var saveButton = new Button {
            Text = "Save",
            DialogResult = DialogResult.OK,
            Location = new Point(400, 420),
            Size = new Size(80, 30)
        };
        saveButton.Click += SaveButton_Click;

        var cancelButton = new Button {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(490, 420),
            Size = new Size(80, 30)
        };

        Controls.Add(tabControl);
        Controls.Add(saveButton);
        Controls.Add(cancelButton);

        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private TabPage CreateObsTab() {
        var tab = new TabPage("OBS Connection");
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };

        var y = 10;

        panel.Controls.Add(new Label { Text = "OBS WebSocket Host:", Location = new Point(10, y), Width = 150 });
        _obsHostTextBox = new TextBox { Location = new Point(170, y), Width = 200 };
        panel.Controls.Add(_obsHostTextBox);
        y += 30;

        panel.Controls.Add(new Label { Text = "OBS WebSocket Port:", Location = new Point(10, y), Width = 150 });
        _obsPortNumeric = new NumericUpDown
            { Location = new Point(170, y), Width = 200, Minimum = 1, Maximum = 65535, Value = 4455 };
        panel.Controls.Add(_obsPortNumeric);
        y += 30;

        panel.Controls.Add(new Label { Text = "OBS WebSocket Password:", Location = new Point(10, y), Width = 150 });
        _obsPasswordTextBox = new TextBox { Location = new Point(170, y), Width = 200, UseSystemPasswordChar = true };
        panel.Controls.Add(_obsPasswordTextBox);
        y += 30;

        var testObsConnectionButton = new Button
            { Text = "Test Connection", Location = new Point(170, y), Width = 150 };
        testObsConnectionButton.Click += TestObsConnection_Click;
        panel.Controls.Add(testObsConnectionButton);

        tab.Controls.Add(panel);

        return tab;
    }

    private TabPage CreatePlayerTab() {
        var tab = new TabPage("Player Settings");
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };

        var y = 10;

        panel.Controls.Add(new Label { Text = "Scene Name:", Location = new Point(10, y), Width = 150 });
        _sceneNameTextBox = new TextBox { Location = new Point(170, y), Width = 200 };
        panel.Controls.Add(_sceneNameTextBox);
        y += 30;

        panel.Controls.Add(new Label { Text = "Source Name:", Location = new Point(10, y), Width = 150 });
        _sourceNameTextBox = new TextBox { Location = new Point(170, y), Width = 200 };
        panel.Controls.Add(_sourceNameTextBox);
        y += 30;

        panel.Controls.Add(new Label { Text = "Width:", Location = new Point(10, y), Width = 150 });
        _widthNumeric = new NumericUpDown
            { Location = new Point(170, y), Width = 100, Minimum = 320, Maximum = 7680, Value = 1920 };
        panel.Controls.Add(_widthNumeric);
        y += 30;

        panel.Controls.Add(new Label { Text = "Height:", Location = new Point(10, y), Width = 150 });
        _heightNumeric = new NumericUpDown
            { Location = new Point(170, y), Width = 100, Minimum = 240, Maximum = 4320, Value = 1080 };
        panel.Controls.Add(_heightNumeric);

        tab.Controls.Add(panel);

        return tab;
    }

    private TabPage CreateShoutoutTab() {
        var tab = new TabPage("Shoutouts");
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };

        var y = 10;

        _enableShoutoutMessageCheckBox = new CheckBox {
            Text = "Enable shoutout messages",
            Location = new Point(10, y),
            Width = 300
        };
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
        panel.Controls.Add(_shoutoutMessageTemplateTextBox);
        y += 70;

        _useFeaturedClipsCheckBox = new CheckBox {
            Text = "Prefer featured clips",
            Location = new Point(10, y),
            Width = 300
        };
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
        panel.Controls.Add(_maxClipAgeNumeric);

        tab.Controls.Add(panel);

        return tab;
    }

    private TabPage CreateLoggingTab() {
        var tab = new TabPage("Logging");
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };

        var y = 10;

        panel.Controls.Add(new Label { Text = "Log Level:", Location = new Point(10, y), Width = 150 });
        _logLevelComboBox = new ComboBox {
            Location = new Point(170, y),
            Width = 200,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _logLevelComboBox.Items.AddRange(["Debug", "Information", "Warning", "Error"]);
        panel.Controls.Add(_logLevelComboBox);
        y += 30;

        var enableDebugLoggingCheckBox = new CheckBox {
            Text = "Enable debug logging",
            Location = new Point(10, y),
            Width = 300
        };
        panel.Controls.Add(enableDebugLoggingCheckBox);

        tab.Controls.Add(panel);

        return tab;
    }

    private TabPage CreateTwitchTab() {
        var tab = new TabPage("Twitch");
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(10) };

        var y = 10;

        var loginButton = new Button {
            Text = "Connect Twitch Account",
            Location = new Point(10, y),
            Size = new Size(200, 40)
        };
        loginButton.Click += TwitchLogin_Click;
        panel.Controls.Add(loginButton);
        y += 50;

        var statusLabel = new Label {
            Text = "Status: Not connected",
            Location = new Point(10, y),
            Width = 400,
            ForeColor = Color.Gray
        };
        panel.Controls.Add(statusLabel);

        tab.Controls.Add(panel);

        return tab;
    }

    private void LoadSettings() {
        _obsHostTextBox.Text = _configuration["Obs:Host"] ?? "localhost";
        _obsPortNumeric.Value = int.Parse(_configuration["Obs:Port"] ?? "4455");
        _obsPasswordTextBox.Text = _configuration["Obs:Password"] ?? "";

        _sceneNameTextBox.Text = _configuration["Player:SceneName"] ?? "Cliparino";
        _sourceNameTextBox.Text = _configuration["Player:SourceName"] ?? "Cliparino Player";
        _widthNumeric.Value = int.Parse(_configuration["Player:Width"] ?? "1920");
        _heightNumeric.Value = int.Parse(_configuration["Player:Height"] ?? "1080");

        _enableShoutoutMessageCheckBox.Checked = bool.Parse(_configuration["Shoutout:EnableMessage"] ?? "true");
        _shoutoutMessageTemplateTextBox.Text = _configuration["Shoutout:MessageTemplate"] ??
                                               "Check out {broadcaster}! They were last playing {game}! twitch.tv/{broadcaster}";
        _useFeaturedClipsCheckBox.Checked = bool.Parse(_configuration["Shoutout:UseFeaturedClips"] ?? "true");
        _maxClipLengthNumeric.Value = int.Parse(_configuration["Shoutout:MaxClipLength"] ?? "60");
        _maxClipAgeNumeric.Value = int.Parse(_configuration["Shoutout:MaxClipAge"] ?? "30");

        _logLevelComboBox.SelectedItem = _configuration["Logging:LogLevel:Default"] ?? "Information";
    }

    private void SaveButton_Click(object? sender, EventArgs e) {
        var settings = new Dictionary<string, string> {
            ["Obs:Host"] = _obsHostTextBox.Text,
            ["Obs:Port"] = _obsPortNumeric.Value.ToString(CultureInfo.InvariantCulture),
            ["Obs:Password"] = _obsPasswordTextBox.Text,
            ["Player:SceneName"] = _sceneNameTextBox.Text,
            ["Player:SourceName"] = _sourceNameTextBox.Text,
            ["Player:Width"] = _widthNumeric.Value.ToString(CultureInfo.InvariantCulture),
            ["Player:Height"] = _heightNumeric.Value.ToString(CultureInfo.InvariantCulture),
            ["Shoutout:EnableMessage"] = _enableShoutoutMessageCheckBox.Checked.ToString(),
            ["Shoutout:MessageTemplate"] = _shoutoutMessageTemplateTextBox.Text,
            ["Shoutout:UseFeaturedClips"] = _useFeaturedClipsCheckBox.Checked.ToString(),
            ["Shoutout:MaxClipLength"] = _maxClipLengthNumeric.Value.ToString(CultureInfo.InvariantCulture),
            ["Shoutout:MaxClipAge"] = _maxClipAgeNumeric.Value.ToString(CultureInfo.InvariantCulture),
            ["Logging:LogLevel:Default"] = _logLevelComboBox.SelectedItem?.ToString() ?? "Information"
        };

        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        try {
            var json = JsonSerializer.Serialize(
                settings, new JsonSerializerOptions {
                    WriteIndented = true
                }
            );

            File.WriteAllText(configPath, json);

            MessageBox.Show(
                "Settings saved successfully.\n\nPlease restart Cliparino for changes to take effect.",
                "Settings Saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );

            Close();
        } catch (Exception ex) {
            MessageBox.Show(
                $"Failed to save settings:\n{ex.Message}",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
        }
    }

    private void TestObsConnection_Click(object? sender, EventArgs e) {
        if (sender is Button button) {
            button.Enabled = false;
            button.Text = "Testing...";

            try {
                MessageBox.Show(
                    "OBS connection testing not yet implemented.\n\nPlease ensure OBS is running with WebSocket server enabled.",
                    "Test Connection",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
            } finally {
                button.Enabled = true;
                button.Text = "Test Connection";
            }
        }
    }

    private void TwitchLogin_Click(object? sender, EventArgs e) {
        MessageBox.Show(
            "Twitch authentication will open in your default browser.\n\nPlease log in and authorize Cliparino.",
            "Twitch Login",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information
        );
    }
}