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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Streamer.bot.Plugin.Interface.Enums;

#endregion

/// <summary>
///     Provides utilities for implementing retry logic with exponential backoff.
/// </summary>
public static class RetryHelper {
    /// <summary>
    ///     Executes an async operation with retry logic and exponential backoff.
    /// </summary>
    /// <typeparam name="T">The return type of the operation.</typeparam>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="maxRetries">Maximum number of retry attempts.</param>
    /// <param name="baseDelayMs">Base delay in milliseconds for exponential backoff.</param>
    /// <param name="logger">Logger for reporting retry attempts.</param>
    /// <param name="operationName">Name of the operation for logging.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        int maxRetries = 3,
        int baseDelayMs = CliparinoConstants.Timing.DefaultRetryDelayMs,
        CPHLogger logger = null,
        string operationName = "operation",
        CancellationToken cancellationToken = default) {
        
        var lastException = (Exception)null;
        
        for (var attempt = 0; attempt <= maxRetries; attempt++) {
            try {
                return await operation();
            } catch (HttpRequestException ex) when (attempt < maxRetries) {
                lastException = ex;
                var delay = CalculateDelay(attempt, baseDelayMs);
                
                logger?.Log(LogLevel.Warn, $"Attempt {attempt + 1} failed for {operationName}. Retrying in {delay}ms...");
                await Task.Delay(delay, cancellationToken);
            } catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested) {
                logger?.Log(LogLevel.Debug, $"Operation {operationName} was cancelled.");
                throw;
            } catch (Exception ex) when (attempt < maxRetries && IsRetryableException(ex)) {
                lastException = ex;
                var delay = CalculateDelay(attempt, baseDelayMs);
                
                logger?.Log(LogLevel.Warn, $"Attempt {attempt + 1} failed for {operationName}. Retrying in {delay}ms...");
                await Task.Delay(delay, cancellationToken);
            }
        }
        
        logger?.Log(LogLevel.Error, $"All retry attempts failed for {operationName}.");
        throw lastException ?? new InvalidOperationException($"Operation {operationName} failed after {maxRetries} retries.");
    }

    /// <summary>
    ///     Calculates the delay for exponential backoff with jitter.
    /// </summary>
    /// <param name="attempt">The current attempt number (0-based).</param>
    /// <param name="baseDelayMs">Base delay in milliseconds.</param>
    /// <returns>The delay in milliseconds.</returns>
    private static int CalculateDelay(int attempt, int baseDelayMs) {
        var exponentialDelay = baseDelayMs * Math.Pow(2, attempt);
        var jitter = new Random().Next(0, (int)(exponentialDelay * 0.1)); // 10% jitter
        return (int)Math.Min(exponentialDelay + jitter, 30000); // Cap at 30 seconds
    }

    /// <summary>
    ///     Determines if an exception is retryable.
    /// </summary>
    /// <param name="exception">The exception to check.</param>
    /// <returns>True if the exception is retryable, false otherwise.</returns>
    private static bool IsRetryableException(Exception exception) {
        return exception is HttpRequestException ||
               exception is TaskCanceledException ||
               exception is TimeoutException;
    }
}