// Cliparino is a clip player for Twitch.tv built to work with Streamer.bot.
// Copyright (C) 2024 Scott Mongrain $angrmgmt@gmail.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301
// USA

#region

using System;
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