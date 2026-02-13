using Cliparino.Core.Models;
using Cliparino.Core.Services;
using Cliparino.Core.UI;
using Cliparino.Core.Utilities;
using Microsoft.Extensions.Options;
using Serilog;

/*
Cliparino entry point (hybrid host)

This application runs:
- An ASP.NET Core web host that serves:
  - The player page at http://localhost:5291/
  - Controller-based HTTP APIs (see README for endpoints)
- A WinForms system tray UI (TrayApplicationContext) on the UI thread

Background services (registered via AddHostedService) run on the generic host and are responsible for:
- Playback state machine and queue processing
- Twitch event ingestion and failover behavior
- OBS health supervision and drift repair
- Periodic update checks and diagnostics
*/


Application.SetHighDpiMode(HighDpiMode.SystemAware);
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

var logsDir = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logsDir);

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

Console.WriteLine("Cliparino starting...");
Console.WriteLine($"Base directory: {AppContext.BaseDirectory}");
Console.WriteLine($"Logs directory: {logsDir}");

var configValid = await ConfigurationHelper.ValidateAndRepairConfigAsync();

if (!configValid) {
    MessageBox.Show("Cliparino couldn't start due to configuration errors.\n\nPlease check the logs for details.",
        "Configuration Error",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);

    return;
}

var httpsPort = 5290;
var httpPort = 5291;

if (!PortUtilities.IsPortAvailable(httpsPort) || !PortUtilities.IsPortAvailable(httpPort)) {
    var conflictingPort = !PortUtilities.IsPortAvailable(httpsPort) ? httpsPort : httpPort;
    var processName = PortUtilities.GetProcessUsingPort(conflictingPort);

    var suggestedHttpsPort = PortUtilities.FindNextAvailablePort(httpsPort);
    var suggestedHttpPort = PortUtilities.FindNextAvailablePort(httpPort);

    if (suggestedHttpsPort == -1 || suggestedHttpPort == -1) {
        MessageBox.Show($"Cliparino couldn't find available ports.\n\n" +
                        $"Please close applications using ports {httpsPort} and {httpPort}, then restart Cliparino.",
            "Port Conflict",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);

        return;
    }

    var dialog = new PortConflictDialog(conflictingPort,
        conflictingPort == httpsPort ? suggestedHttpsPort : suggestedHttpPort, processName);
    var result = dialog.ShowDialog();

    if (result == DialogResult.OK && dialog.UseAlternativePort) {
        if (conflictingPort == httpsPort) {
            httpsPort = suggestedHttpsPort;
            httpPort = suggestedHttpPort;
        } else {
            httpsPort = suggestedHttpsPort;
            httpPort = suggestedHttpPort;
        }

        await ConfigurationHelper.CreateBackupAsync();
        var updated = await ConfigurationHelper.UpdatePortsInConfigAsync(httpsPort, httpPort);

        if (!updated)
            MessageBox.Show("Failed to update configuration with new ports.\n\nPlease restart Cliparino.",
                "Configuration Update Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

        Log.Information("Port conflict resolved: using ports {HttpsPort} and {HttpPort}", httpsPort, httpPort);
    } else {
        return;
    }
}

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", false, true);

builder.Services.Configure<ObsOptions>(builder.Configuration.GetSection("OBS"));
builder.Services.Configure<PlayerOptions>(builder.Configuration.GetSection("Player"));
builder.Services.Configure<ShoutoutOptions>(builder.Configuration.GetSection("Shoutout"));
builder.Services.Configure<LoggingOptions>(builder.Configuration.GetSection("Logging"));
builder.Services.Configure<ChatFeedbackOptions>(builder.Configuration.GetSection("ChatFeedback"));

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddHttpClient("Twitch");

builder.Services.AddSingleton<ITwitchAuthStore, TwitchAuthStore>();
builder.Services.AddSingleton<ITwitchOAuthService, TwitchOAuthService>();
builder.Services.AddSingleton<ITwitchHelixClient, TwitchHelixClient>();

builder.Services.AddSingleton<IClipQueue, ClipQueue>();
builder.Services.AddSingleton<PlaybackEngine>();
builder.Services.AddSingleton<IPlaybackEngine>(sp => sp.GetRequiredService<PlaybackEngine>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<PlaybackEngine>());

builder.Services.AddSingleton<IShoutoutService, ShoutoutService>();
builder.Services.AddSingleton<IClipSearchService, ClipSearchService>();
builder.Services.AddSingleton<IApprovalService, ApprovalService>();

builder.Services.AddSingleton<TwitchEventSubWebSocketSource>();
builder.Services.AddSingleton<TwitchIrcEventSource>();
builder.Services.AddSingleton<IChatFeedbackService, ChatFeedbackService>();
builder.Services.AddSingleton<ICommandRouter, CommandRouter>();
builder.Services.AddHostedService<TwitchEventCoordinator>();

builder.Services.AddSingleton<IObsController, ObsController>();
builder.Services.AddHostedService<ObsHealthSupervisor>();

builder.Services.AddSingleton<IHealthReporter, HealthReporter>();
builder.Services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
builder.Services.AddSingleton<IUpdateChecker, UpdateChecker>();
builder.Services.AddHostedService<PeriodicUpdateCheckService>();

builder.WebHost.UseUrls($"https://localhost:{httpsPort}", $"http://localhost:{httpPort}");

var app = builder.Build();

AppDomain.CurrentDomain.UnhandledException += (_, e) => {
    HandleGlobalException(e.ExceptionObject as Exception, "AppDomain Unhandled Exception", app);
};

TaskScheduler.UnobservedTaskException += (_, e) => {
    HandleGlobalException(e.Exception, "TaskScheduler Unobserved Task Exception", app);
    e.SetObserved();
};

app.UseStaticFiles();

app.MapGet("/", async context => {
    var playerOptions = context.RequestServices.GetRequiredService<IOptionsMonitor<PlayerOptions>>().CurrentValue;
    var htmlTemplate = await File.ReadAllTextAsync(Path.Combine(app.Environment.WebRootPath, "index.html"));

    // Inject player style configuration
    var styleInjection = $$"""

                               <style>
                                   :root {
                                       --info-text-color: {{playerOptions.InfoTextColor}};
                                       --info-bg-color: {{playerOptions.InfoBackgroundColor}};
                                       --info-font-family: {{playerOptions.InfoFontFamily}};
                                   }
                               </style>
                           """;

    htmlTemplate = htmlTemplate.Replace("</head>", $"{styleInjection}</head>");

    context.Response.ContentType = "text/html";
    await context.Response.WriteAsync(htmlTemplate);
});

app.MapControllers();

app.UseSerilogRequestLogging();

app.Logger.LogInformation("Cliparino server starting on https://localhost:{HttpsPort} and http://localhost:{HttpPort}",
    httpsPort, httpPort);
app.Logger.LogInformation("Player page: http://localhost:{HttpPort}", httpPort);
app.Logger.LogInformation("API endpoints: http://localhost:{HttpPort}/api/status, /api/play, /api/replay, /api/stop",
    httpPort);

try {
    var appTask = app.RunAsync();

    var trayContext = new TrayApplicationContext(app.Services);
    Application.Run(trayContext);

    await appTask;
} catch (Exception ex) {
    HandleGlobalException(ex, "Application Startup Error", app);
    Log.Fatal(ex, "Application terminated unexpectedly");
} finally {
    Log.CloseAndFlush();
}

return;

void HandleGlobalException(Exception? ex, string source, WebApplication? webApp) {
    if (ex == null) return;

    Log.Error(ex, "Global exception caught in {Source}", source);

    try {
        var diagnosticsService = webApp?.Services.GetService<IDiagnosticsService>();

        if (diagnosticsService != null) {
            var zipBytes = diagnosticsService.ExportDiagnosticsZipAsync().Result;
            var fileName = $"diagnostics_crash_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
            var filePath = Path.Combine(AppContext.BaseDirectory, "logs", fileName);
            File.WriteAllBytes(filePath, zipBytes);

            MessageBox.Show(
                $"Cliparino encountered a critical error and needs to close.\n\nDiagnostics have been saved to:\n{filePath}\n\nPlease report this issue on GitHub and attach the diagnostics file.",
                "Critical Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        } else {
            MessageBox.Show($"Cliparino encountered a critical error: {ex.Message}",
                "Critical Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    } catch (Exception fatalEx) {
        Log.Error(fatalEx, "Failed to export diagnostics during crash");
        MessageBox.Show($"Cliparino encountered a critical error: {ex.Message}",
            "Critical Error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}