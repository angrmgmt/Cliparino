namespace Cliparino.Core.Models;

public abstract record ChatCommand(ChatMessage Source);

public record WatchClipCommand(ChatMessage Source, string ClipIdentifier) : ChatCommand(Source);

public record WatchSearchCommand(ChatMessage Source, string BroadcasterName, string SearchTerms) : ChatCommand(Source);

public record StopCommand(ChatMessage Source) : ChatCommand(Source);

public record ReplayCommand(ChatMessage Source) : ChatCommand(Source);

public record ShoutoutCommand(ChatMessage Source, string TargetUsername) : ChatCommand(Source);