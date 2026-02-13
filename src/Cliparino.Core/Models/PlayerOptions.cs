namespace Cliparino.Core.Models;

public class PlayerOptions {
    public string Url { get; set; } = "http://localhost:5291";
    public string SceneName { get; set; } = "Cliparino";
    public string SourceName { get; set; } = "Cliparino Player";
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public string InfoTextColor { get; set; } = "#ffb809";
    public string InfoBackgroundColor { get; set; } = "#0071c5";
    public string InfoFontFamily { get; set; } = "OpenDyslexic, 'Open Sans', sans-serif";
}