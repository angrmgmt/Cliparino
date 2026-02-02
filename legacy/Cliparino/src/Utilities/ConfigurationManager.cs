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

using Streamer.bot.Plugin.Interface;

#endregion

/// <summary>
///     Centralized configuration management for retrieving application settings.
/// </summary>
public static class ConfigurationManager {
    /// <summary>
    ///     Gets the display dimensions from the CPH arguments.
    /// </summary>
    /// <param name="cph">The CPH proxy instance.</param>
    /// <returns>The configured dimensions.</returns>
    public static Dimensions GetDimensions(IInlineInvokeProxy cph) {
        var height = CPHInline.GetArgument(cph, "height", CliparinoConstants.Display.DefaultHeight);
        var width = CPHInline.GetArgument(cph, "width", CliparinoConstants.Display.DefaultWidth);
        return new Dimensions(height, width);
    }

    /// <summary>
    ///     Gets the logging configuration from the CPH arguments.
    /// </summary>
    /// <param name="cph">The CPH proxy instance.</param>
    /// <returns>True if logging is enabled, false otherwise.</returns>
    public static bool GetLoggingEnabled(IInlineInvokeProxy cph) {
        return CPHInline.GetArgument(cph, "logging", false);
    }

    /// <summary>
    ///     Gets the clip settings from the CPH arguments.
    /// </summary>
    /// <param name="cph">The CPH proxy instance.</param>
    /// <returns>The configured clip settings.</returns>
    public static ClipManager.ClipSettings GetClipSettings(IInlineInvokeProxy cph) {
        var featuredOnly = CPHInline.GetArgument(cph, "featuredOnly", CliparinoConstants.Clips.DefaultFeaturedOnly);
        var maxDuration = CPHInline.GetArgument(cph, "maxClipSeconds", CliparinoConstants.Clips.DefaultMaxClipSeconds);
        var maxAgeDays = CPHInline.GetArgument(cph, "clipAgeDays", CliparinoConstants.Clips.DefaultClipAgeDays);
        return new ClipManager.ClipSettings(featuredOnly, maxDuration, maxAgeDays);
    }

    /// <summary>
    ///     Gets the shoutout message from the CPH arguments.
    /// </summary>
    /// <param name="cph">The CPH proxy instance.</param>
    /// <returns>The configured shoutout message.</returns>
    public static string GetShoutoutMessage(IInlineInvokeProxy cph) {
        return CPHInline.GetArgument(cph, "soMessage", "");
    }

    /// <summary>
    ///     Gets the message ID from the CPH arguments.
    /// </summary>
    /// <param name="cph">The CPH proxy instance.</param>
    /// <returns>The message ID.</returns>
    public static string GetMessageId(IInlineInvokeProxy cph) {
        return CPHInline.GetArgument(cph, "messageId", "");
    }
}