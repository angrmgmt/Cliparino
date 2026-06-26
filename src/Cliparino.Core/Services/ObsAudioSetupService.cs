using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Cliparino.Core.Services;

/// <summary>
///     Ensures OBS is configured to allow unmuted clip audio by adding
///     <c>--chromium-flags="--autoplay-policy=no-user-gesture-required"</c>
///     to the OBS launch shortcut.
/// </summary>
/// <remarks>
///     <para>
///         OBS's embedded Chromium (CEF) browser enforces the standard autoplay policy,
///         which blocks unmuted audio in cross-origin iframes without prior user interaction.
///         Passing <c>--autoplay-policy=no-user-gesture-required</c> via the CEF flag removes
///         that restriction globally for all browser sources, so the Twitch clip embed can
///         play with audio the moment it loads.
///     </para>
///     <para>
///         The service runs once per Cliparino session.  It looks for an existing OBS desktop
///         shortcut (user desktop, then common desktop) and adds the flag to its arguments.
///         If no shortcut is found, it creates one on the user's Desktop.  State is inferred
///         from the shortcut itself — no separate settings entry is required.
///     </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public class ObsAudioSetupService(ILogger<ObsAudioSetupService> logger, IConfiguration configuration) {
    private const string NewShortcutName = "OBS Studio (Cliparino).lnk";

    private int CefDebuggingPort => configuration.GetValue("OBS:CefDebuggingPort", 9222);

    // Both flags live inside a single --chromium-flags="..." argument, so OBS passes them to CEF together.
    // We add --disable-features to bypass newer Chromium autoplay/engagement checks that can cause muting.
    private string ChromiumFlag =>
        $"--autoplay-policy=no-user-gesture-required --remote-debugging-port={CefDebuggingPort} --disable-features=PreloadMediaEngagementData,AutoplayIgnoreWebAudio";

    private string ChromiumArg => $"--chromium-flags=\"{ChromiumFlag}\"";

    /// <summary>
    ///     Checks whether OBS is configured for unmuted clip audio and, if not, configures it.
    ///     Returns a human-readable message to surface to the user, or <see langword="null" /> when
    ///     no action was necessary.
    /// </summary>
    public string? EnsureObsAudioEnabled() {
        try {
            // Already running with the flag — nothing to do.
            if (IsObsRunningWithFlag()) {
                logger.LogInformation("OBS is already running with the audio autoplay flag");

                return null;
            }

            // Find OBS executable.
            var obsExe = FindObsExecutable();

            if (obsExe == null) {
                logger.LogWarning("Could not locate OBS executable; skipping audio setup");

                return null;
            }

            logger.LogInformation("Found OBS at {Path}", obsExe);

            // Special handling for Steam version — we can't update its internal "shortcut",
            // but we can tell the user how to fix it in Steam.
            if (obsExe.Contains("Steam", StringComparison.OrdinalIgnoreCase)) {
                logger.LogInformation("OBS appears to be the Steam version");

                if (IsObsRunning())
                    return
                        "It looks like you're using the Steam version of OBS. For automatic clip audio, please right-click OBS in Steam -> Properties -> General -> Launch Options, and add:\n\n" +
                        ChromiumArg + "\n\nThen restart OBS.";
            }

            // Prefer the user-configured shortcut over searching common locations.
            var configuredLnkPath = configuration["OBS:ExecutablePath"];
            var configuredLnk = !string.IsNullOrWhiteSpace(configuredLnkPath) &&
                                configuredLnkPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) &&
                                File.Exists(configuredLnkPath)
                ? configuredLnkPath
                : null;

            var existing = configuredLnk ?? FindObsShortcut();

            if (existing != null) {
                if (ShortcutHasFlag(existing)) {
                    logger.LogInformation("OBS shortcut already has the audio flag at {Path}", existing);

                    return null;
                }

                if (AddFlagToShortcut(existing)) {
                    logger.LogInformation("Updated OBS shortcut at {Path}", existing);
                    var obsRunning = IsObsRunning();

                    return obsRunning
                        ? "Cliparino has updated your OBS shortcut for automatic clip audio. Please close and reopen OBS to apply."
                        : "Cliparino has configured OBS for automatic clip audio. Your OBS shortcut has been updated.";
                }
            }

            // If the user configured a specific shortcut, but we couldn't update it, stop here —
            // don't drop a shortcut on the Desktop they don't use.
            if (configuredLnk != null) {
                logger.LogWarning("Could not update configured OBS shortcut at {Path}", configuredLnk);

                return null;
            }

            // No shortcut found and none configured — create one on the Desktop as a fallback.
            var newPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                NewShortcutName);

            if (File.Exists(newPath) && ShortcutHasFlag(newPath)) {
                logger.LogInformation("Cliparino OBS shortcut already exists with the audio flag");

                return null;
            }

            if (!CreateShortcut(newPath, obsExe)) return null;
            {
                logger.LogInformation("Created OBS shortcut with audio flag at {Path}", newPath);
                var obsRunning = IsObsRunning();

                return obsRunning
                    ? "Cliparino has added an 'OBS Studio (Cliparino)' shortcut to your Desktop. Please close OBS and reopen it using that shortcut to enable automatic clip audio."
                    : "Cliparino has added an 'OBS Studio (Cliparino)' shortcut to your Desktop. Use it to open OBS — clips will play with audio automatically.";
            }
        } catch (Exception ex) {
            logger.LogError(ex, "Error during OBS audio setup");

            return null;
        }
    }

    // ── OBS process detection ────────────────────────────────────────────────

    private static bool IsObsRunning() {
        return Process.GetProcessesByName("obs64").Length != 0 ||
               Process.GetProcessesByName("obs32").Length != 0;
    }

    private bool IsObsRunningWithFlag() {
        try {
            // Use WMIC to get the command line of all obs processes.
            // This is safer than reading process memory and works across architectures.
            var startInfo = new ProcessStartInfo {
                FileName = "wmic.exe",
                Arguments = "process where \"name='obs64.exe' or name='obs32.exe'\" get commandline",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(startInfo);

            if (proc == null) return false;

            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            return output.Contains(ChromiumFlag, StringComparison.OrdinalIgnoreCase);
        } catch {
            return false;
        }
    }

    public bool IsRunningWithFlag() {
        return IsObsRunningWithFlag();
    }

    // ── OBS executable discovery ─────────────────────────────────────────────

    private string? FindObsExecutable() {
        // 0. Check the user-configured path first.
        var configured = configuration["OBS:ExecutablePath"];

        if (!string.IsNullOrWhiteSpace(configured)) {
            // If the user pointed us at a shortcut (.lnk), resolve its target exe.
            if (configured.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) {
                var target = ReadShortcutTarget(configured);

                if (!string.IsNullOrEmpty(target) && File.Exists(target))
                    return target;
            } else if (File.Exists(configured)) {
                return configured;
            }
        }

        // 1. Check the running process first — most reliable.
        foreach (var name in new[] { "obs64", "obs32" }) {
            var proc = Process.GetProcessesByName(name).FirstOrDefault();

            if (proc == null) continue;

            try {
                var path = proc.MainModule?.FileName;

                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            } catch {
                /* access denied on 32-bit process from 64-bit host */
            }
        }

        // 2. Registry — standard/per-user installs.
        var regPaths = new[] {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OBS Studio",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\OBS Studio"
        };

        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        foreach (var regPath in regPaths)
            try {
                using var key = root.OpenSubKey(regPath);

                if (key?.GetValue("InstallLocation") is not string installDir) continue;

                var candidate = Path.Combine(installDir, "bin", "64bit", "obs64.exe");

                if (File.Exists(candidate)) return candidate;
            } catch {
                /* registry access denied */
            }

        // 3. Common install locations.
        var pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        var candidates = new[] {
            Path.Combine(pf64, "obs-studio", "bin", "64bit", "obs64.exe"),
            Path.Combine(pf86, "obs-studio", "bin", "64bit", "obs64.exe"),

            // Steam
            Path.Combine(pf86, "Steam", "steamapps", "common", "OBS Studio", "bin", "64bit", "obs64.exe"),
            Path.Combine(pf64, "Steam", "steamapps", "common", "OBS Studio", "bin", "64bit", "obs64.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    // ── Shortcut discovery ──────────────────────────────────────────────────

    private string? FindObsShortcut() {
        var searchPaths = new List<string> {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        };

        foreach (var path in searchPaths.Where(p => !string.IsNullOrEmpty(p)))
        foreach (var lnk in SafeEnumerateLinks(path))
            try {
                var target = ReadShortcutTarget(lnk);

                if (target == null ||
                    (!target.Contains("obs64.exe", StringComparison.OrdinalIgnoreCase) &&
                     !target.Contains("obs32.exe", StringComparison.OrdinalIgnoreCase) &&
                     !target.Contains("obs.exe", StringComparison.OrdinalIgnoreCase))) continue;
                logger.LogInformation("Found OBS shortcut at: {Path}", lnk);

                return lnk;
            } catch (Exception ex) {
                logger.LogDebug(ex, "Could not read shortcut {Path}", lnk);
            }

        return null;
    }

    private static string[] SafeEnumerateLinks(string directory) {
        try {
            return !Directory.Exists(directory)
                ? []
                :

                // Search all subdirectories as well (e.g., Start Menu/Programs/OBS Studio/)
                Directory.GetFiles(directory, "*.lnk", SearchOption.AllDirectories);
        } catch {
            return [];
        }
    }

    // ── Shortcut flag inspection ─────────────────────────────────────────────

    public bool ShortcutHasFlag(string lnkPath) {
        try {
            var args = ReadShortcutArguments(lnkPath);

            return args != null && args.Contains(ChromiumFlag, StringComparison.OrdinalIgnoreCase);
        } catch (Exception ex) {
            logger.LogDebug(ex, "Could not read arguments from {Path}", lnkPath);

            return false;
        }
    }

    // ── Shortcut manipulation ─────────────────────────────────────────────────

    private bool AddFlagToShortcut(string lnkPath) {
        try {
            var existingArgs = ReadShortcutArguments(lnkPath) ?? "";

            // 1. Extract the current chromium-flags value if it exists.
            // Match --chromium-flags="..." or --chromium-flags=...
            var match = Regex.Match(existingArgs,
                @"(?:--)?chromium-flags=(?:""([^""]*)""|(\S+))",
                RegexOptions.IgnoreCase);

            // 2. Build the set of new flags, ensuring we don't duplicate.
            var newFlagsList = ChromiumFlag.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var mergedFlags = new List<string>();

            if (match.Success) {
                var existingInner = (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).Trim();
                var existingFlags = existingInner.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                // Start with existing flags, then add missing ones from our set
                mergedFlags.AddRange(existingFlags);
                foreach (var f in newFlagsList)
                    if (!mergedFlags.Any(existing =>
                            string.Equals(existing.Split('=')[0], f.Split('=')[0], StringComparison.OrdinalIgnoreCase)))
                        mergedFlags.Add(f);
            } else {
                mergedFlags.AddRange(newFlagsList);
            }

            var replacement = $"--chromium-flags=\"{string.Join(" ", mergedFlags)}\"";
            string newArgs;

            // Scrub any existing chromium-flags or orphaned target flags to ensure a clean slate
            newArgs = Regex.Replace(existingArgs, @"(?:--)?chromium-flags=(?:""[^""]*""|\S+)", "",
                RegexOptions.IgnoreCase);

            foreach (var flag in newFlagsList) {
                var flagName = flag.Split('=')[0];
                newArgs = Regex.Replace(newArgs, $@"(?:--)?{Regex.Escape(flagName)}(?:=\S+)?", "",
                    RegexOptions.IgnoreCase);
            }

            newArgs = $"{newArgs.Trim()} {replacement}".Trim();

            WriteShortcutArguments(lnkPath, newArgs);

            return true;
        } catch (Exception ex) {
            logger.LogWarning(ex, "Failed to update OBS shortcut at {Path}", lnkPath);

            return false;
        }
    }

    private bool CreateShortcut(string lnkPath, string obsExe) {
        try {
            WriteShortcut(lnkPath, obsExe, ChromiumArg);

            return true;
        } catch (Exception ex) {
            logger.LogWarning(ex, "Failed to create OBS shortcut at {Path}", lnkPath);

            return false;
        }
    }

    // ── WScript.Shell COM helpers (Windows-only) ─────────────────────────────

    private static string? ReadShortcutTarget(string lnkPath) {
        var wsh = CreateWScriptShell();
        var lnk = wsh?.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
            null, wsh, [lnkPath]);

        return lnk?.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty,
            null, lnk, null) as string;
    }

    private static string? ReadShortcutArguments(string lnkPath) {
        var wsh = CreateWScriptShell();
        var lnk = wsh?.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
            null, wsh, [lnkPath]);

        return lnk?.GetType().InvokeMember("Arguments", BindingFlags.GetProperty,
            null, lnk, null) as string;
    }

    private static void WriteShortcutArguments(string lnkPath, string arguments) {
        var wsh = CreateWScriptShell();

        if (wsh == null) throw new InvalidOperationException("WScript.Shell not available");
        var lnk = wsh.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
            null, wsh, [lnkPath])!;
        lnk.GetType().InvokeMember("Arguments", BindingFlags.SetProperty, null, lnk,
            [arguments]);
        lnk.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
    }

    private static void WriteShortcut(string lnkPath, string targetPath, string arguments) {
        var wsh = CreateWScriptShell();

        if (wsh == null) throw new InvalidOperationException("WScript.Shell not available");
        var lnk = wsh.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
            null, wsh, [lnkPath])!;
        var lnkType = lnk.GetType();
        lnkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk,
            [targetPath]);
        lnkType.InvokeMember("Arguments", BindingFlags.SetProperty, null, lnk,
            [arguments]);
        lnkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk,
            [targetPath]);
        lnkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
    }

    private static object? CreateWScriptShell() {
        try {
            var type = Type.GetTypeFromProgID("WScript.Shell");

            return type == null ? null : Activator.CreateInstance(type);
        } catch {
            return null;
        }
    }
}