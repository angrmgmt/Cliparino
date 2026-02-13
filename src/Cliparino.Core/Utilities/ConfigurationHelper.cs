using System.Text;
using System.Text.Json;

namespace Cliparino.Core.Utilities;

public static class ConfigurationHelper {
    public static async Task<bool> UpdatePortsInConfigAsync(int httpsPort, int httpPort) {
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        try {
            if (!File.Exists(configPath)) return false;

            var json = await File.ReadAllTextAsync(configPath);
            using var document = JsonDocument.Parse(json);

            var options = new JsonWriterOptions { Indented = true };
            using var stream = new MemoryStream();

            using (var writer = new Utf8JsonWriter(stream, options)) {
                writer.WriteStartObject();

                foreach (var property in document.RootElement.EnumerateObject())
                    if (property.Name == "Twitch") {
                        writer.WriteStartObject(property.Name);
                        foreach (var twitchProp in property.Value.EnumerateObject())
                            if (twitchProp.Name == "RedirectUri")
                                writer.WriteString(twitchProp.Name, $"https://localhost:{httpsPort}/auth/callback");
                            else
                                twitchProp.WriteTo(writer);

                        writer.WriteEndObject();
                    } else if (property.Name == "Player") {
                        writer.WriteStartObject(property.Name);
                        foreach (var playerProp in property.Value.EnumerateObject())
                            if (playerProp.Name == "Url")
                                writer.WriteString(playerProp.Name, $"http://localhost:{httpPort}");
                            else
                                playerProp.WriteTo(writer);

                        writer.WriteEndObject();
                    } else {
                        property.WriteTo(writer);
                    }

                writer.WriteEndObject();
            }

            var updatedJson = Encoding.UTF8.GetString(stream.ToArray());
            await File.WriteAllTextAsync(configPath, updatedJson);

            return true;
        } catch {
            return false;
        }
    }

    public static async Task<bool> CreateBackupAsync() {
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        try {
            if (!File.Exists(configPath)) return false;

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPath = Path.Combine(AppContext.BaseDirectory, $"appsettings.json.backup.{timestamp}");

            var fileContent = await File.ReadAllBytesAsync(configPath);
            await File.WriteAllBytesAsync(backupPath, fileContent);

            return true;
        } catch {
            return false;
        }
    }

    public static async Task<bool> ValidateAndRepairConfigAsync() {
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        try {
            if (!File.Exists(configPath)) return false;

            var json = await File.ReadAllTextAsync(configPath);
            JsonDocument.Parse(json);

            return true;
        } catch (JsonException ex) {
            var backupCreated = await CreateBackupAsync();

            var message = backupCreated
                ? $"Configuration file is invalid at line {ex.LineNumber}, column {ex.BytePositionInLine}.\n\n" +
                  $"A backup has been created.\n\nWould you like to restore default settings?"
                : $"Configuration file is invalid at line {ex.LineNumber}, column {ex.BytePositionInLine}.\n\n" +
                  $"Would you like to restore default settings?";

            var result = MessageBox.Show(message,
                "Configuration Error",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Error);

            if (result == DialogResult.Yes) {
                await RestoreDefaultConfigAsync();

                return true;
            }

            return false;
        }
    }

    public static async Task RestoreDefaultConfigAsync() {
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        var defaultConfig = @"{
  ""Serilog"": {
    ""Using"": [
      ""Serilog.Sinks.Console"",
      ""Serilog.Sinks.File""
    ],
    ""MinimumLevel"": {
      ""Default"": ""Information"",
      ""Override"": {
        ""Microsoft.AspNetCore"": ""Warning"",
        ""System.Net.Http"": ""Warning""
      }
    },
    ""WriteTo"": [
      {
        ""Name"": ""Console"",
        ""Args"": {
          ""outputTemplate"": ""[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}""
        }
      },
      {
        ""Name"": ""File"",
        ""Args"": {
          ""path"": ""logs/cliparino-.log"",
          ""rollingInterval"": ""Day"",
          ""retainedFileCountLimit"": 7,
          ""outputTemplate"": ""{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}""
        }
      }
    ]
  },
  ""Logging"": {
    ""LogLevel"": {
      ""Default"": ""Information"",
      ""Microsoft.AspNetCore"": ""Warning""
    }
  },
  ""AllowedHosts"": ""*"",
  ""Twitch"": {
    ""ClientId"": ""cdwomham1pa9s03paoqu9eu7mdyxoz"",
    ""RedirectUri"": ""https://localhost:5290/auth/callback""
  },
  ""OBS"": {
    ""Host"": ""localhost"",
    ""Port"": ""4455"",
    ""Password"": """",
    ""SceneName"": ""Cliparino"",
    ""SourceName"": ""CliparinoPlayer"",
    ""Width"": ""1920"",
    ""Height"": ""1080""
  },
  ""Player"": {
    ""Url"": ""http://localhost:5291""
  },
  ""Shoutout"": {
    ""Message"": ""Check out {channel}! They were last seen playing {game}: https://twitch.tv/{channel}"",
    ""UseFeaturedClipsFirst"": true,
    ""MaxClipLengthSeconds"": 60,
    ""MaxClipAgeDays"": 365,
    ""SendTwitchShoutout"": true
  },
  ""ClipSearch"": {
    ""SearchWindowDays"": 90,
    ""FuzzyMatchThreshold"": 0.4,
    ""RequireApproval"": true,
    ""ApprovalTimeoutSeconds"": 30,
    ""ExemptRoles"": [
      ""broadcaster"",
      ""moderator""
    ]
  },
  ""Update"": {
    ""GitHubRepo"": ""angrmgmt/Cliparino"",
    ""CheckOnStartup"": true,
    ""CheckIntervalHours"": 24
  }
}";

        await File.WriteAllTextAsync(configPath, defaultConfig);
    }
}