using Avalonia.Controls;
using Avalonia.Threading;

namespace DigitalDeck.Avalonia;

/// <summary>
/// Shows a window and waits for it, without handing the caller a task to await.
/// </summary>
/// <remarks>
/// <c>IDeckDialogs</c> is synchronous — <c>bool EditItem(...)</c>, not <c>Task&lt;bool&gt;</c> —
/// because that is the shape the view models were written against and the shape WPF's
/// <c>ShowDialog</c> has. Avalonia's is asynchronous, and awaiting it from a synchronous caller on
/// the UI thread would deadlock.
/// <para>
/// A nested dispatcher frame is the same thing WPF does inside its own <c>ShowDialog</c>: keep
/// pumping messages so the dialog stays alive, and stop when it closes. Avalonia exposes the
/// pieces for it, which is what lets the shared view models keep one interface instead of one per
/// toolkit.
/// </para>
/// </remarks>
public static class Modal
{
    /// <summary>Opens <paramref name="dialog"/> and returns once it is closed.</summary>
    public static void Show(Window dialog, Window? owner)
    {
        var frame = new DispatcherFrame();

        dialog.Closed += (_, _) => frame.Continue = false;

        if (owner is not null) _ = dialog.ShowDialog(owner);
        else dialog.Show();

        Dispatcher.UIThread.PushFrame(frame);
    }

    /// <summary>
    /// Waits for a task without letting the UI thread stop pumping.
    /// </summary>
    /// <remarks>
    /// The same nested frame as <see cref="Show"/>, for the same reason: Avalonia's file pickers
    /// are asynchronous and <c>IDeckDialogs</c> is not. Blocking on the task directly would stop
    /// the very thread the picker needs in order to finish.
    /// </remarks>
    public static T Wait<T>(Task<T> task)
    {
        if (task.IsCompleted) return task.GetAwaiter().GetResult();

        var frame = new DispatcherFrame();

        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);

        Dispatcher.UIThread.PushFrame(frame);

        return task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// The window a dialog should hang off, or null when there is not one yet.
    /// </summary>
    /// <remarks>
    /// Null is a real answer during startup and in a headless test, and a dialog with no owner
    /// still opens — it just does not centre on anything or stay above it.
    /// </remarks>
    public static Window? MainWindow =>
        global::Avalonia.Application.Current?.ApplicationLifetime
            is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}
