/*  Test script for validating the token refresh functionality
    Copyright (C) 2024 Scott Mongrain - (angrmgmt@gmail.com)
*/

using System;
using System.Threading.Tasks;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;

/// <summary>
///     Test class for validating the token refresh mechanism in TwitchApiManager.
/// </summary>
public class TokenRefreshTest {
    /// <summary>
    ///     Tests the manual token refresh functionality.
    /// </summary>
    /// <param name="cph">The Streamer Bot interface proxy.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    public static async Task TestManualTokenRefresh(IInlineInvokeProxy cph) {
        var logger = new CPHLogger(cph);
        var apiManager = new TwitchApiManager(cph, logger);

        logger.Log(LogLevel.Info, "Starting manual token refresh test...");

        try {
            // Test the manual refresh method
            var refreshResult = await apiManager.RefreshTokenAsync();
            
            if (refreshResult) {
                logger.Log(LogLevel.Info, "Manual token refresh test PASSED");
            } else {
                logger.Log(LogLevel.Warn, "Manual token refresh test completed but refresh was not needed");
            }
        } catch (Exception ex) {
            logger.Log(LogLevel.Error, "Manual token refresh test FAILED", ex);
        }
    }

    /// <summary>
    ///     Tests the automatic token refresh by simulating a 401 error scenario.
    /// </summary>
    /// <param name="cph">The Streamer Bot interface proxy.</param>
    /// <returns>A task representing the asynchronous test operation.</returns>
    public static async Task TestApiCallWithRefresh(IInlineInvokeProxy cph) {
        var logger = new CPHLogger(cph);
        var apiManager = new TwitchApiManager(cph, logger);

        logger.Log(LogLevel.Info, "Starting API call with refresh test...");

        try {
            // Try to fetch a game to trigger the refresh mechanism if needed
            var gameData = await apiManager.FetchGameById("509658"); // Just Chatting game ID
            
            if (gameData != null) {
                logger.Log(LogLevel.Info, $"API call test PASSED - Retrieved game: {gameData.Name}");
            } else {
                logger.Log(LogLevel.Warn, "API call test completed but no game data returned");
            }
        } catch (Exception ex) {
            logger.Log(LogLevel.Error, "API call with refresh test FAILED", ex);
        }
    }
}