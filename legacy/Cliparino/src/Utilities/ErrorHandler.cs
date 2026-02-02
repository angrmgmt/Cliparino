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
using System.Threading.Tasks;
using Streamer.bot.Plugin.Interface.Enums;

#endregion

/// <summary>
///     Provides centralized error handling and logging functionality.
/// </summary>
public static class ErrorHandler {
    /// <summary>
    ///     Handles exceptions with consistent logging and returns a default value.
    /// </summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="logger">The logger to use for error reporting.</param>
    /// <param name="action">The action to execute.</param>
    /// <param name="operationName">The name of the operation for logging.</param>
    /// <param name="defaultValue">The default value to return on error.</param>
    /// <returns>The result of the action or the default value on error.</returns>
    public static T HandleError<T>(CPHLogger logger, Func<T> action, string operationName, T defaultValue = default) {
        try {
            return action();
        } catch (Exception ex) {
            logger.Log(LogLevel.Error, $"Error occurred during {operationName}.", ex);
            return defaultValue;
        }
    }

    /// <summary>
    ///     Handles async exceptions with consistent logging and returns a default value.
    /// </summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="logger">The logger to use for error reporting.</param>
    /// <param name="action">The async action to execute.</param>
    /// <param name="operationName">The name of the operation for logging.</param>
    /// <param name="defaultValue">The default value to return on error.</param>
    /// <returns>The result of the action or the default value on error.</returns>
    public static async Task<T> HandleErrorAsync<T>(CPHLogger logger, Func<Task<T>> action, string operationName, T defaultValue = default) {
        try {
            return await action();
        } catch (Exception ex) {
            logger.Log(LogLevel.Error, $"Error occurred during {operationName}.", ex);
            return defaultValue;
        }
    }

    /// <summary>
    ///     Handles exceptions for void operations with consistent logging.
    /// </summary>
    /// <param name="logger">The logger to use for error reporting.</param>
    /// <param name="action">The action to execute.</param>
    /// <param name="operationName">The name of the operation for logging.</param>
    /// <returns>True if the operation succeeded, false otherwise.</returns>
    public static bool HandleError(CPHLogger logger, Action action, string operationName) {
        try {
            action();
            return true;
        } catch (Exception ex) {
            logger.Log(LogLevel.Error, $"Error occurred during {operationName}.", ex);
            return false;
        }
    }

    /// <summary>
    ///     Handles async exceptions for void operations with consistent logging.
    /// </summary>
    /// <param name="logger">The logger to use for error reporting.</param>
    /// <param name="action">The async action to execute.</param>
    /// <param name="operationName">The name of the operation for logging.</param>
    /// <returns>True if the operation succeeded, false otherwise.</returns>
    public static async Task<bool> HandleErrorAsync(CPHLogger logger, Func<Task> action, string operationName) {
        try {
            await action();
            return true;
        } catch (Exception ex) {
            logger.Log(LogLevel.Error, $"Error occurred during {operationName}.", ex);
            return false;
        }
    }
}