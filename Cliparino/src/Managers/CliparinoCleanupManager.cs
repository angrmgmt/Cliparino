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
using System.Threading;
using System.Threading.Tasks;
using Streamer.bot.Common.Events;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;

#endregion

public class CliparinoCleanupManager {
    private readonly IInlineInvokeProxy _cph;
    private readonly CPHLogger _logger;
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
    private bool _disposed;

    public CliparinoCleanupManager(IInlineInvokeProxy cph, CPHLogger logger) {
        _cph = cph ?? throw new ArgumentNullException(nameof(cph));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task CleanupResources() {
        await _semaphore.WaitAsync();

        try {
            _logger.Log(LogLevel.Info, "Cleaning up resources.");
            _cph.SetGlobalVar("last_clip_url", null);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error during Cliparino cleanup.", ex);
        } finally {
            _semaphore.Release();
        }
    }

    public void Dispose() {
        if (_disposed) return;

        try {
            _disposed = true;
            _semaphore.Dispose();
            _logger.Log(LogLevel.Info, "CliparinoCleanupManager disposed.");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error disposing CliparinoCleanupManager.", ex);
        }
    }
}