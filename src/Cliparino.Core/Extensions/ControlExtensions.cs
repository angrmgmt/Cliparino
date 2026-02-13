namespace Cliparino.Core.Extensions;

public static class ControlExtensions {
    /// <summary>
    ///     Invokes the specified action on the control's UI thread if required,
    ///     with defensive checks for handle creation and disposal.
    /// </summary>
    /// <param name="control">The WinForms control to invoke on.</param>
    /// <param name="action">The action to execute on the UI thread.</param>
    public static void InvokeIfRequired(this Control control, Action action) {
        if (!control.IsHandleCreated || control.IsDisposed)
            return;

        if (control.InvokeRequired)
            try {
                control.Invoke(action);
            } catch (ObjectDisposedException) {
                // Control was disposed between the check and the call to invoke
            } catch (InvalidOperationException) {
                // Handle was destroyed between the check and the call to invoke
            }
        else
            action();
    }

    /// <summary>
    ///     Invokes the specified async action on the control's UI thread if required,
    ///     with defensive checks for handle creation and disposal.
    /// </summary>
    /// <param name="control">The WinForms control to invoke on.</param>
    /// <param name="action">The async action to execute on the UI thread.</param>
    public static async Task InvokeIfRequiredAsync(this Control control, Func<Task> action) {
        if (!control.IsHandleCreated || control.IsDisposed)
            return;

        if (control.InvokeRequired)
            try {
                await control.Invoke(action);
            } catch (ObjectDisposedException) {
                // Control was disposed between the check and the call to invoke
            } catch (InvalidOperationException) {
                // Handle was destroyed between the check and the call to invoke
            }
        else
            await action();
    }
}