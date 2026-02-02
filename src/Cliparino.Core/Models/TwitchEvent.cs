namespace Cliparino.Core.Models;

public abstract record TwitchEvent;

public record ChatMessageEvent(ChatMessage Message) : TwitchEvent;

public record RaidEvent(string RaiderUsername, string RaiderId, int ViewerCount) : TwitchEvent;