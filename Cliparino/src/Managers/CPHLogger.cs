#region

using System;
using System.Runtime.CompilerServices;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;
using Streamer.bot.Common.Events;

#endregion

public class CPHLogger {
    private const string MessagePrefix = "Cliparino :: ";
    private readonly IInlineInvokeProxy _cph;
    private readonly bool _loggingEnabled;

    public CPHLogger(IInlineInvokeProxy cph, bool loggingEnabled) {
        _cph = cph;
        _loggingEnabled = loggingEnabled;
    }

    public void Log(LogLevel level, string messageBody, Exception ex = null, [CallerMemberName] string caller = "") {
        if (caller == null) throw new ArgumentNullException(nameof(caller));

        if (!_loggingEnabled && level != LogLevel.Error) return;

        var message = $"{MessagePrefix}{caller} :: {messageBody}";

        switch (level) {
            case LogLevel.Debug: _cph.LogDebug(message); break;
            case LogLevel.Info: _cph.LogInfo(message); break;
            case LogLevel.Warn: _cph.LogWarn(message); break;
            case LogLevel.Error: LogError(message, ex); break;
            default: throw new ArgumentOutOfRangeException(nameof(level), level, null);
        }
    }

    private void LogError(string message, Exception ex = null) {
        if (ex == null) throw new ArgumentNullException(nameof(ex));

        _cph.LogError($"[Cliparino] {message}{$"\nException: {ex.Message}"}");

        _cph.LogDebug($"[Cliparino] StackTrace: {ex.StackTrace}");
    }
}

public enum LogLevel {
    Debug,

    Info,

    Warn,

    Error
}