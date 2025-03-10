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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Streamer.bot.Common.Events;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;
using Twitch.Common.Models.Api;

#endregion

public class ClipManager {
    private readonly IInlineInvokeProxy _cph;
    private readonly CPHLogger _logger;
    private readonly TwitchApiManager _twitchApiManager;
    private string _lastClipUrl;

    public ClipManager(IInlineInvokeProxy cph, CPHLogger logger, TwitchApiManager twitchApiManager) {
        _cph = cph ?? throw new ArgumentNullException(nameof(cph));
        _logger = logger;
        _twitchApiManager = twitchApiManager;
    }

    public string GetLastClipUrl() {
        return _lastClipUrl ?? _cph.GetGlobalVar<string>("last_clip_url");
    }

    public void SetLastClipUrl(string url) {
        if (string.IsNullOrWhiteSpace(url)) return;

        _lastClipUrl = url;
        _cph.SetGlobalVar("last_clip_url", url);
    }

    public async Task<ClipData> GetRandomClipAsync(string userName, ClipSettings clipSettings) {
        clipSettings.Deconstruct(out var featuredOnly, out var maxClipSeconds, out var clipAgeDays);

        _logger.Log(LogLevel.Debug,
                    $"Getting random clip for userName: {userName}, featuredOnly: {featuredOnly}, maxSeconds: {maxClipSeconds}, ageDays: {clipAgeDays}");

        try {
            var twitchUser = _cph.TwitchGetExtendedUserInfoByLogin(userName);

            if (twitchUser == null) {
                _logger.Log(LogLevel.Warn, $"Twitch user not found for userName: {userName}");

                return null;
            }

            var userId = twitchUser.UserId;
            var validPeriods = new[] { 1, 7, 30, 365, 36500 };

            return await Task.Run(() => {
                                      foreach (var period in validPeriods.Where(p => p >= clipAgeDays)) {
                                          var clips = RetrieveClips(userId, period).ToList();

                                          if (!clips.Any()) continue;

                                          var clip = GetMatchingClip(clips, featuredOnly, maxClipSeconds, userId);

                                          if (clip != null) return clip;
                                      }

                                      _logger.Log(LogLevel.Warn,
                                                  $"No clips found for userName: {userName} after exhausting all periods and filter combinations.");

                                      return null;
                                  });
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "An error occurred while getting random clip.", ex);

            return null;
        }
    }

    private IEnumerable<ClipData> RetrieveClips(string userId, int clipAgeDays) {
        _logger.Log(LogLevel.Debug, $"RetrieveClips called with userId: {userId}, clipAgeDays: {clipAgeDays}");

        try {
            var endDate = DateTime.UtcNow;
            var startDate = endDate.AddDays(-clipAgeDays);

            return _cph.GetClipsForUserById(userId, startDate, endDate);
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "An error occurred while retrieving clips.", ex);

            return Array.Empty<ClipData>();
        }
    }

    private ClipData GetMatchingClip(IEnumerable<ClipData> clips, bool featuredOnly, int maxSeconds, string userId) {
        clips = clips.ToList();

        var matchingClips = FilterClips(clips, featuredOnly, maxSeconds);

        if (matchingClips.Any()) {
            var selectedClip = SelectRandomClip(matchingClips);

            _logger.Log(LogLevel.Debug, $"Selected clip: {selectedClip.Url}");

            return selectedClip;
        }

        _logger.Log(LogLevel.Debug, $"No matching clips found with the current filter (featuredOnly: {featuredOnly}).");

        if (!featuredOnly) return null;

        matchingClips = FilterClips(clips, false, maxSeconds);

        if (matchingClips.Any()) {
            var selectedClip = SelectRandomClip(matchingClips);

            _logger.Log(LogLevel.Debug, $"Selected clip without featuredOnly: {selectedClip.Url}");

            return selectedClip;
        }

        _logger.Log(LogLevel.Debug, $"No matching clips found without featuredOnly for userId: {userId}");

        return null;
    }

    private static List<ClipData> FilterClips(IEnumerable<ClipData> clips, bool featuredOnly, int maxSeconds) {
        return clips.Where(c => (!featuredOnly || c.IsFeatured) && c.Duration <= maxSeconds).ToList();
    }

    private static ClipData SelectRandomClip(List<ClipData> clips) {
        return clips[new Random().Next(clips.Count)];
    }

    public async Task<ClipData> GetClipDataAsync(string clipUrl) {
        if (string.IsNullOrWhiteSpace(clipUrl)) {
            _logger.Log(LogLevel.Error, "Invalid clip URL provided.");

            return null;
        }

        var clipId = ExtractClipId(clipUrl);

        if (!string.IsNullOrWhiteSpace(clipId)) return await FetchClipDataAsync(clipId);

        _logger.Log(LogLevel.Error, "Could not extract clip ID from URL.");

        return null;
    }

    private static string ExtractClipId(string clipUrl) {
        var segments = clipUrl.Split('/');

        return segments.Length > 0 ? segments.Last().Trim() : null;
    }

    private async Task<ClipData> FetchClipDataAsync(string clipId) {
        try {
            var clipData = await _twitchApiManager.FetchClipById(clipId);

            if (clipData == null) {
                _logger.Log(LogLevel.Error, $"No clip data found for ID {clipId}.");

                return null;
            }

            _logger.Log(LogLevel.Info, $"Retrieved clip '{clipData.Title}' by {clipData.CreatorName}.");

            return clipData;
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, "Error retrieving clip data.", ex);

            return null;
        }
    }

    public class ClipSettings {
        public ClipSettings(bool featuredOnly, int maxClipSeconds, int clipAgeDays) {
            FeaturedOnly = featuredOnly;
            MaxClipSeconds = maxClipSeconds;
            ClipAgeDays = clipAgeDays;
        }

        public bool FeaturedOnly { get; }
        public int MaxClipSeconds { get; }
        public int ClipAgeDays { get; }

        public void Deconstruct(out bool featuredOnly, out int maxClipSeconds, out int clipAgeDays) {
            featuredOnly = FeaturedOnly;
            maxClipSeconds = MaxClipSeconds;
            clipAgeDays = ClipAgeDays;
        }
    }
}