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

using System.Diagnostics.CodeAnalysis;

/// <summary>
///     Central repository for application constants and configuration values.
/// </summary>
public static class CliparinoConstants {
    /// <summary>
    ///     HTTP server configuration constants.
    /// </summary>
    public static class Http {
        public const int BasePort = 8080;
        public const int MaxPortRetries = 10;
        public const int NonceLength = 16;
        public const string HelixApiUrl = "https://api.twitch.tv/helix/";
        public const string InactiveUrl = "about:blank";
    }

    /// <summary>
    ///     OBS source and scene name constants.
    /// </summary>
    public static class Obs {
        public const string CliparinoSourceName = "Cliparino";
        public const string PlayerSourceName = "Player";
    }

    /// <summary>
    ///     Timing constants for various operations.
    /// </summary>
    public static class Timing {
        public const int ClipEndBufferMs = 3000;
        public const int ApprovalTimeoutMs = 60000;
        public const int ApprovalCheckIntervalMs = 500;
        public const int DefaultRetryDelayMs = 500;
    }

    /// <summary>
    ///     Logging configuration constants.
    /// </summary>
    public static class Logging {
        public const string MessagePrefix = "Cliparino :: ";
    }

    /// <summary>
    ///     Default display dimensions.
    /// </summary>
    public static class Display {
        public const int DefaultHeight = 1080;
        public const int DefaultWidth = 1920;
    }

    /// <summary>
    ///     Default clip settings.
    /// </summary>
    public static class Clips {
        public const int DefaultMaxClipSeconds = 30;
        public const int DefaultClipAgeDays = 30;
        public const bool DefaultFeaturedOnly = false;
    }

    /// <summary>
    ///     Windows API message constants.
    /// </summary>
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public static class WindowsApi {
        public const uint WM_LBUTTONDOWN = 0x0201;
        public const uint WM_LBUTTONUP = 0x0202;
        public const uint WM_RBUTTONDOWN = 0x0204;
        public const uint WM_RBUTTONUP = 0x0205;
    }

    /// <summary>
    ///     Common user messages and prompts.
    /// </summary>
    public static class Messages {
        public const string NoClipFound = "No matching clip was found. Please refine your search.";
        public const string UnableToRetrieveClipData = "Unable to retrieve clip data. Please try again with a valid URL.";
        public const string UnableToResolveChannel = "Unable to resolve channel by username. Please try again with a valid username or URL.";
        public const string ProvideValidSearchTerm = "Please provide a valid search term to find a clip.";
        public const string NoClipAvailableForReplay = "No clip available for replay.";
        public const string ApprovalWaitMessage = "I'll wait a minute for a mod to approve or deny this clip, starting now.";
        public const string ApprovalTimeoutMessage = "Time's up! The clip wasn't approved, maybe next time!";
        public const string ApprovalSuccessMessage = "The clip has been approved!";
        public const string ClipApprovalPrompt = "Did you mean this clip? {0}";
    }
}