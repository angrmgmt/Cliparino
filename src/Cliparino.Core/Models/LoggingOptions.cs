namespace Cliparino.Core.Models;

public class LoggingOptions {
    public LogLevelOptions LogLevel { get; set; } = new();
}

public class LogLevelOptions {
    public string Default { get; set; } = "Information";
}