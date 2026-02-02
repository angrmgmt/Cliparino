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
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;

#endregion

/// <summary>
///     Factory for creating and initializing manager instances with proper dependency injection.
/// </summary>
public static class ManagerFactory {
    /// <summary>
    ///     Creates and initializes all required managers for Cliparino.
    /// </summary>
    /// <param name="cph">The CPH proxy instance.</param>
    /// <param name="logger">The logger instance.</param>
    /// <returns>A tuple containing all initialized managers.</returns>
    public static (TwitchApiManager twitchApi, HttpManager http, ClipManager clip, ObsSceneManager obs) CreateManagers(
        IInlineInvokeProxy cph, CPHLogger logger) {
        
        try {
            logger.Log(LogLevel.Debug, "Creating TwitchApiManager...");
            var twitchApiManager = new TwitchApiManager(cph, logger);

            logger.Log(LogLevel.Debug, "Creating HttpManager...");
            var httpManager = new HttpManager(cph, logger, twitchApiManager);

            logger.Log(LogLevel.Debug, "Creating ClipManager...");
            var clipManager = new ClipManager(cph, logger, twitchApiManager);

            logger.Log(LogLevel.Debug, "Creating ObsSceneManager...");
            var obsSceneManager = new ObsSceneManager(cph, logger, httpManager);

            logger.Log(LogLevel.Debug, "All managers created successfully.");
            
            return (twitchApiManager, httpManager, clipManager, obsSceneManager);
        } catch (Exception ex) {
            logger.Log(LogLevel.Error, "Failed to create managers.", ex);
            throw;
        }
    }

    /// <summary>
    ///     Validates that all managers are properly initialized.
    /// </summary>
    /// <param name="managers">The managers to validate.</param>
    /// <param name="logger">The logger for reporting validation results.</param>
    /// <returns>True if all managers are valid, false otherwise.</returns>
    public static bool ValidateManagers(
        (TwitchApiManager twitchApi, HttpManager http, ClipManager clip, ObsSceneManager obs) managers,
        CPHLogger logger) {
        
        var isValid = ValidationHelper.ValidateDependencies(
            managers.twitchApi, 
            managers.http, 
            managers.clip, 
            managers.obs);

        if (!isValid) {
            logger.Log(LogLevel.Error, "One or more managers failed validation.");
        } else {
            logger.Log(LogLevel.Debug, "All managers validated successfully.");
        }

        return isValid;
    }
}