/*  Cliparino is a clip player for Twitch.tv built to work with Streamer.bot.
    Copyright (C) 2024 Scott Mongrain - (angrmgmt@gmail.com)

    This library is free software; you can redistribute it and/or
    modify it under the terms of the GNU Lesser General Public
    License as published by the Free Software Foundation; either
    version 2.1 of the License, or (at your option) any later version.

    This library is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
    Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public
    License along with this library; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301
    USA
*/

#region

using System;
using System.Runtime.CompilerServices;
using Streamer.bot.Common.Events;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;

#endregion

/// <summary>
///     Provides logging functionality with customizable log levels and message formatting.
/// </summary>
public class CPHLogger {
    private const string MessagePrefix = CliparinoConstants.Logging.MessagePrefix;
    private readonly IInlineInvokeProxy _cph;
    private readonly bool _loggingEnabled;

    public CPHLogger(IInlineInvokeProxy cph, bool loggingEnabled) {
        _cph = cph;
        _loggingEnabled = loggingEnabled;
    }

    /// <summary>
    ///     Centralizes logging operations for <see cref="IInlineInvokeProxy.LogDebug" />,
    ///     <see cref="IInlineInvokeProxy.LogInfo" />, <see cref="IInlineInvokeProxy.LogWarn" />, and
    ///     <see cref="IInlineInvokeProxy.LogError" />, allowing logging to be gated in one location.
    /// </summary>
    /// <param name="level">
    ///     The severity level of the log message.
    /// </param>
    /// <param name="messageBody">
    ///     The main content of the log message.
    /// </param>
    /// <param name="ex">
    ///     The exception related to the log entry, if applicable. Defaults to null.
    /// </param>
    /// <param name="caller">
    ///     The name of the method that invoked the logger. This is automatically populated by the runtime.
    /// </param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when the <paramref name="caller" /> parameter is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when the <paramref name="level" /> parameter is anything other than one of the values in
    ///     <see cref="LogLevel" />.
    /// </exception>
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

    /// <summary>
    ///     Logs an error message with an optional exception. If an exception is provided, its message and
    ///     stack trace will also be logged.
    /// </summary>
    /// <param name="message">
    ///     The error message to log.
    /// </param>
    /// <param name="ex">
    ///     The exception associated with the error, if any. Defaults to null.
    /// </param>
    private void LogError(string message, Exception ex = null) {
        if (ex == null) {
            _cph.LogError(message);
        } else {
            _cph.LogError($"{message}\nException: {ex.Message}");
            _cph.LogDebug($"StackTrace: {ex.StackTrace}");
        }
    }
}

/// <summary>
///     Represents the severity level for logging messages.
/// </summary>
public enum LogLevel {
    /// <summary>
    ///     Represents the debug log level in the logging system. This log level is typically used for
    ///     diagnostic messages that are useful for developers when debugging the application.
    /// </summary>
    Debug,

    /// <summary>
    ///     Represents an informational log level used to record general information about the
    ///     application's operation.
    /// </summary>
    Info,

    /// <summary>
    ///     Represents a warning log level.
    /// </summary>
    /// <remarks>
    ///     This log level is used to indicate potential issues or situations that may require attention
    ///     but do not necessarily prevent the normal execution of the application.
    /// </remarks>
    Warn,

    /// <summary>
    ///     Represents the error log level.
    /// </summary>
    /// <remarks>
    ///     This log level is used to report errors within the application. It denotes significant problems
    ///     that need attention or could potentially disrupt the application's normal operation. It is also
    ///     the only log level that will be processed even when logging is disabled.
    /// </remarks>
    Error
}