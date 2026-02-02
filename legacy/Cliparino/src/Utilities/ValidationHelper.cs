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

#endregion

/// <summary>
///     Provides common validation methods for input processing.
/// </summary>
public static class ValidationHelper {
    /// <summary>
    ///     Validates whether the provided string is a well-formed URL pointing to twitch.tv.
    /// </summary>
    /// <param name="input">The string to validate as a URL.</param>
    /// <returns>True if the provided string is a well-formed absolute URL and contains "twitch.tv"; otherwise, false.</returns>
    public static bool IsValidTwitchUrl(string input) {
        return Uri.IsWellFormedUriString(input, UriKind.Absolute) && input.Contains("twitch.tv");
    }

    /// <summary>
    ///     Determines if the input represents a Twitch username (starts with @).
    /// </summary>
    /// <param name="input">The input string to check.</param>
    /// <returns>True if the input is a username, false otherwise.</returns>
    public static bool IsUsername(string input) {
        return !string.IsNullOrWhiteSpace(input) && input.StartsWith("@");
    }

    /// <summary>
    ///     Validates if the input is acceptable for processing.
    /// </summary>
    /// <param name="input">The input string to validate.</param>
    /// <returns>True if the input is valid, false otherwise.</returns>
    public static bool IsValidInput(string input) {
        return !string.IsNullOrWhiteSpace(input) && !input.Equals(CliparinoConstants.Http.InactiveUrl);
    }

    /// <summary>
    ///     Sanitizes a username by removing the @ prefix if present.
    /// </summary>
    /// <param name="username">The username to sanitize.</param>
    /// <returns>The sanitized username without the @ prefix.</returns>
    public static string SanitizeUsername(string username) {
        if (string.IsNullOrWhiteSpace(username)) return username;
        return username.StartsWith("@") ? username.Substring(1) : username;
    }

    /// <summary>
    ///     Validates that all required dependencies are not null.
    /// </summary>
    /// <param name="dependencies">Array of dependencies to validate.</param>
    /// <returns>True if all dependencies are not null, false otherwise.</returns>
    public static bool ValidateDependencies(params object[] dependencies) {
        foreach (var dependency in dependencies) {
            if (dependency == null) return false;
        }
        return true;
    }
}