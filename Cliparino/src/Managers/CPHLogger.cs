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

        _cph.LogError($"[Cliparino] {message}\nException: {ex.Message}");

        _cph.LogDebug($"[Cliparino] StackTrace: {ex.StackTrace}");
    }
}

public enum LogLevel {
    Debug,
    Info,
    Warn,
    Error
}