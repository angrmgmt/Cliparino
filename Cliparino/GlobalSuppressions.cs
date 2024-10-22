// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

#region

using System.Diagnostics.CodeAnalysis;

using Streamer.bot.Common.Events;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;

#endregion

[assembly:
    SuppressMessage("Design",
                    "CA1050:Declare types in namespaces",
                    Justification = "Streamer.bot does not use typical folder structure for its loose files.",
                    Scope = "type",
                    Target = "~T:CPHInline")]