namespace Cliparino.Core.Models;

public class ShoutoutOptions {
    public bool EnableMessage { get; set; } = true;

    public string MessageTemplate { get; set; } =
        "Check out {broadcaster}! They were last playing {game}! twitch.tv/{broadcaster}";

    public bool UseFeaturedClips { get; set; } = true;
    public int MaxClipLength { get; set; } = 60;
    public int MaxClipAge { get; set; } = 30;
    public bool SendTwitchShoutout { get; set; } = true;
}