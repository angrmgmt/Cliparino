namespace Cliparino.Core.Models;

public class ObsOptions {
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 4455;
    public string Password { get; set; } = "";
    public string SceneName { get; set; } = "Cliparino";
    public string SourceName { get; set; } = "CliparinoPlayer";
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
}