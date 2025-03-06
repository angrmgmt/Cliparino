#region

using System;
using System.Threading;
using System.Threading.Tasks;
using Streamer.bot.Plugin.Interface;
using Streamer.bot.Plugin.Interface.Enums;
using Streamer.bot.Plugin.Interface.Model;
using Streamer.bot.Common.Events;

#endregion

public class CliparinoCleanupManager {
    private readonly IInlineInvokeProxy _cph;
    private readonly CPHLogger _logger;

    public CliparinoCleanupManager(IInlineInvokeProxy cph, CPHLogger logger) {
        _cph = cph;
        _logger = logger;
    }

    public async Task CleanupAsync() {
        _logger.Log(LogLevel.Info, "Cleaning up Cliparino resources...");

        await Task.Delay(500);

        _cph.ExecuteAction("CleanupServer");
    }

    public void ReleaseSemaphore(SemaphoreSlim semaphore) {
        if (semaphore.CurrentCount != 0) return;

        semaphore.Release();
        _logger.Log(LogLevel.Debug, "Released semaphore lock.");
    }
}

internal class MiscCleanupMethods {
    private async Task CleanupServer(HttpListener server = null) {
        _logger.Log(LogLevel.Debug, "Entering CleanupServer.");

        using (new ScopedSemaphore(ServerSemaphore, Log)) {
            try {
                await CancelAllOperationsAsync();

                var serverInstance = TakeServerInstance(server);

                await CleanupListeningTaskAsync();

                StopAndDisposeServer(serverInstance);
            } catch (Exception ex) {
                _logger.Log(LogLevel.Error, $"Unexpected error during CleanupServer: {ex.Message}");
            }
        }

        _logger.Log(LogLevel.Debug, "Exiting CleanupServer.");
    }

    private async Task CancelAllOperationsAsync() {
        if (_cancellationTokenSource == null) {
            _logger.Log(LogLevel.Debug, "Cancellation token source is already null.");

            return;
        }

        using (await ScopedSemaphore.WaitAsync(TokenSemaphore, Log)) {
            try {
                _cancellationTokenSource.Cancel();
                _logger.Log(LogLevel.Debug, "All ongoing operations canceled.");
            } catch (Exception ex) {
                _logger.Log(LogLevel.Error, $"Error while canceling operations: {ex.Message}");
            }
        }
    }

    private HttpListener TakeServerInstance(HttpListener server) {
        lock (ServerLock) {
            if (_server == null && server == null) Log(LogLevel.Warn, "No server instance available for cleanup.");

            var instance = server ?? _server;

            // Ensure the server is nullified regardless of whether it was passed or taken.
            _server = null;

            return instance;
        }
    }

    private async Task CleanupListeningTaskAsync() {
        if (_listeningTask == null) {
            _logger.Log(LogLevel.Debug, "No listening task to cleanup.");

            return;
        }

        _logger.Log(LogLevel.Debug, "Cleaning up listening task.");

        try {
            await _listeningTask;
        } catch (OperationCanceledException) {
            _logger.Log(LogLevel.Info, "Listening task gracefully canceled.");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error cleaning up listening task: {ex.Message}");
        } finally {
            _listeningTask = null;
        }
    }

    private void StopAndDisposeServer(HttpListener serverInstance) {
        if (serverInstance == null) {
            _logger.Log(LogLevel.Info, "No server instance to stop or dispose.");

            return;
        }

        try {
            serverInstance.Stop();
            serverInstance.Close();
            _logger.Log(LogLevel.Info, "Server successfully stopped and disposed.");
        } catch (Exception ex) {
            _logger.Log(LogLevel.Error, $"Error stopping or disposing server: {ex.Message}");
        }
    }
}