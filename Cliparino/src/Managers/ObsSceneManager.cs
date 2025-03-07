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
using Streamer.bot.Common.Events;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;
using Twitch.Common.Models.Api;

#endregion

public class ObsSceneManager {
    private const string CliparinoSourceName = "Cliparino";
    private const string PlayerSourceName = "Player";
    private const string ActiveUrl = "http://localhost:8080/";
    private const string InactiveUrl = "about:blank";
    private readonly IInlineInvokeProxy _cph;
    private readonly CPHLogger _logger;

    public ObsSceneManager(IInlineInvokeProxy cph, CPHLogger logger) {
        _cph = cph ?? throw new ArgumentNullException(nameof(cph));
        _logger = logger;
    }

    public void PlayClip(ClipData clipData) {
        if (clipData == null) {
            _logger.Log(LogLevel.Warn, "ObsSceneManager: No clip data provided.");

            return;
        }

        if (string.IsNullOrWhiteSpace(_cph.ObsGetCurrentScene())) {
            _logger.Log(LogLevel.Warn, "ObsSceneManager: Unable to determine current OBS scene.");

            return;
        }

        ShowCliparino(_cph.ObsGetCurrentScene());
        SetBrowserSource(ActiveUrl);
    }

    public void StopClip() {
        _logger.Log(LogLevel.Info, "ObsSceneManager: Stopping clip playback.");
        SetBrowserSource(InactiveUrl);
        HideCliparino(_cph.ObsGetCurrentScene());
    }

    private void ShowCliparino(string scene) {
        if (!_cph.ObsIsSourceVisible(scene, CliparinoSourceName))
            _cph.ObsSetSourceVisibility(scene, CliparinoSourceName, true);

        if (!_cph.ObsIsSourceVisible(CliparinoSourceName, PlayerSourceName))
            _cph.ObsSetSourceVisibility(CliparinoSourceName, PlayerSourceName, true);
    }

    private void HideCliparino(string scene) {
        if (_cph.ObsIsSourceVisible(scene, CliparinoSourceName))
            _cph.ObsSetSourceVisibility(scene, CliparinoSourceName, false);

        if (_cph.ObsIsSourceVisible(CliparinoSourceName, PlayerSourceName))
            _cph.ObsSetSourceVisibility(CliparinoSourceName, PlayerSourceName, false);
    }

    private void SetBrowserSource(string url) {
        _cph.ObsSetBrowserSource(CliparinoSourceName, PlayerSourceName, url);
    }

    //TODO: Add SetUpCliparino method to add scene and source.
    //TODO: Add CliparinoExists property/method to see if it exists already.
    //TODO: Add CliparinoInScene method to check if Cliparino exists in current scene.
    //TODO: Add all audio configuration settings.
}