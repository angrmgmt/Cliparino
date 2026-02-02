using Cliparino.Core.Services;
using Cliparino.Core.UI;
using Serilog;

Application.SetHighDpiMode(HighDpiMode.SystemAware);
Application.EnableVisualStyles();
Application.SetCompatibleTextRenderingDefault(false);

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
);

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
builder.Services.AddSingleton<ICommandRouter, CommandRouter>();
builder.Services.AddHostedService<TwitchEventCoordinator>();

builder.Services.AddSingleton<IObsController, ObsController>();
builder.Services.AddHostedService<ObsHealthSupervisor>();

builder.Services.AddSingleton<IHealthReporter, HealthReporter>();
builder.Services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
builder.Services.AddSingleton<IUpdateChecker, UpdateChecker>();
builder.Services.AddHostedService<PeriodicUpdateCheckService>();

builder.WebHost.UseUrls("http://localhost:5290");

var app = builder.Build();

app.UseStaticFiles();

app.MapGet(
    "/", async context => {
        var playbackEngine = context.RequestServices.GetRequiredService<IPlaybackEngine>();
        var currentClip = playbackEngine.CurrentClip;

        if (currentClip == null) {
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync(
                """
                <!DOCTYPE html>
                <html>
                <head><title>Cliparino - Idle</title></head>
                <body style="background: #0071c5; color: white; font-family: sans-serif; display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0;">
                    <div style="text-align: center;">
                        <h1>Cliparino</h1>
                        <p>Waiting for clips...</p>
                        <p><small>Player state: Idle</small></p>
                    </div>
                </body>
                </html>
                """
            );

            return;
        }

        var htmlTemplate = await File.ReadAllTextAsync(Path.Combine(app.Environment.WebRootPath, "index.html"));

        var html = htmlTemplate
            .Replace("{{clipId}}", currentClip.Id)
            .Replace("{{streamerName}}", currentClip.BroadcasterName)
            .Replace("{{gameName}}", currentClip.GameName)
            .Replace("{{clipTitle}}", currentClip.Title)
            .Replace("{{curatorName}}", currentClip.CreatorName);

        context.Response.ContentType = "text/html";
        await context.Response.WriteAsync(html);
    }
);

app.MapControllers();

app.UseSerilogRequestLogging();

app.Logger.LogInformation("Cliparino server starting on http://localhost:5290");
app.Logger.LogInformation("Player page: http://localhost:5290");
app.Logger.LogInformation("API endpoints: http://localhost:5290/api/status, /api/play, /api/replay, /api/stop");

try {
    var appTask = app.RunAsync();

    var trayContext = new TrayApplicationContext(app.Services);
    Application.Run(trayContext);

    await appTask;
} catch (Exception ex) {
    Log.Fatal(ex, "Application terminated unexpectedly");
} finally {
    Log.CloseAndFlush();
}