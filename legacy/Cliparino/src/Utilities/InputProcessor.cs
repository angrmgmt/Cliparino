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
using System.Linq;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;

#endregion

/// <summary>
///     Provides utilities for processing and parsing user input.
/// </summary>
public static class InputProcessor {
    /// <summary>
    ///     Represents the result of parsing user input for broadcast and search operations.
    /// </summary>
    public class BroadcastSearchResult {
        public string BroadcasterId { get; set; }
        public string SearchTerm { get; set; }
        public bool IsValid => !string.IsNullOrWhiteSpace(BroadcasterId);
    }

    /// <summary>
    ///     Parses user input to extract broadcaster ID and search terms.
    /// </summary>
    /// <param name="input">The user input to parse.</param>
    /// <param name="cph">The CPH proxy for resolving user information.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <returns>A result containing the broadcaster ID and search term.</returns>
    public static BroadcastSearchResult ParseBroadcastSearch(string input, IInlineInvokeProxy cph, CPHLogger logger) {
        var result = new BroadcastSearchResult();
        
        if (string.IsNullOrWhiteSpace(input)) {
            logger?.Log(LogLevel.Warn, "Input is empty. No broadcaster or search term can be resolved.");
            return result;
        }

        input = input.Trim();
        
        // Split the input into parts, assuming format: "@username searchTerm" or just "searchTerm"
        var inputParts = input.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        var firstPart = inputParts[0];
        var secondPart = inputParts.Length > 1 ? inputParts[1] : string.Empty;

        if (ValidationHelper.IsUsername(firstPart)) {
            // If the first part is a username, resolve it
            var username = ValidationHelper.SanitizeUsername(firstPart);
            var userInfo = cph.TwitchGetExtendedUserInfoByLogin(username);

            if (userInfo != null) {
                result.BroadcasterId = userInfo.UserId;
                result.SearchTerm = secondPart;
                logger?.Log(LogLevel.Debug, $"Resolved Broadcaster ID: {result.BroadcasterId} for username: {username}");
            } else {
                logger?.Log(LogLevel.Warn, $"Could not resolve username: {username}");
            }
        } else {
            // Fall back to the current broadcaster if no valid username is provided
            var broadcaster = cph.TwitchGetBroadcaster();
            if (broadcaster != null) {
                result.BroadcasterId = broadcaster.UserId;
                result.SearchTerm = input; // Entire input is the search term
                logger?.Log(LogLevel.Debug, $"Using current broadcaster: {broadcaster.UserName}");
            }
        }

        return result;
    }

    /// <summary>
    ///     Extracts the last segment of a URL path.
    /// </summary>
    /// <param name="url">The URL to process.</param>
    /// <returns>The last segment of the URL path, or empty string if invalid.</returns>
    public static string ExtractLastUrlSegment(string url) {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;

        try {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return segments.LastOrDefault() ?? string.Empty;
        } catch {
            return string.Empty;
        }
    }

    /// <summary>
    ///     Determines the input type based on content analysis.
    /// </summary>
    /// <param name="input">The input to analyze.</param>
    /// <returns>The detected input type.</returns>
    public static InputType DetectInputType(string input) {
        if (string.IsNullOrWhiteSpace(input)) return InputType.Invalid;
        
        if (ValidationHelper.IsValidTwitchUrl(input)) return InputType.Url;
        if (ValidationHelper.IsUsername(input)) return InputType.Username;
        
        return InputType.SearchTerm;
    }

    /// <summary>
    ///     Represents the type of user input detected.
    /// </summary>
    public enum InputType {
        Invalid,
        Url,
        Username,
        SearchTerm
    }
}