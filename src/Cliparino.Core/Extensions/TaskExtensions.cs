using Serilog;

namespace Cliparino.Core.Extensions;

public static class TaskExtensions {
    /// <summary>
    ///     Safely executes a task in a fire-and-forget manner, catching and logging any exceptions.
    /// </summary>
    /// <param name="task">The task to execute.</param>
    /// <param name="operationName">A name for the operation to include in logs.</param>
    public static async void SafeFireAndForget(this Task task, string operationName) {
        try {
            await task;
        } catch (OperationCanceledException) {
            Log.Debug("{OperationName} was canceled", operationName);
        } catch (Exception ex) {
            Log.Error(ex, "An unhandled exception occurred during {OperationName}", operationName);

            // In a WinForms app, we might want to show a message box if we're on the UI thread,
            // but for a general-purpose extension, logging is the minimum and safest action.
        }
    }
}