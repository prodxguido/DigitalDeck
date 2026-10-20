using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DigitalDeck.Core.Abstractions;

namespace DigitalDeck.App.Services;

/// <summary>
/// Keeps a window inside the usable part of whatever screen it is on.
/// </summary>
/// <remarks>
/// Every dialog here uses <c>SizeToContent="Height"</c>, which grows the window downwards from
/// wherever it already is and never repositions it. Open a panel that adds a few hundred pixels and
/// the bottom of the window slides under the taskbar, taking the Save button with it.
/// <para>
/// A fixed <c>MaxHeight</c> cannot solve that, because it is a guess about a screen nobody has
/// measured — the editor shipped with 880, and the machine it was written on has 816 usable
/// pixels once the taskbar is subtracted. So the ceiling is computed from the monitor the window is
/// actually on, and the window is nudged back inside after anything that could have moved it out.
/// </para>
/// <para>
/// Per-monitor rather than <see cref="SystemParameters.WorkArea"/>, which only ever describes the
/// primary screen: this app is built to live on a second monitor, and that one can be a different
/// size and a different scaling factor than the one Windows calls primary.
/// </para>
/// </remarks>
public static class WindowFit
{
    /// <summary>Breathing room kept between the window and the edges of the usable area.</summary>
    private const double Margin = 12;

    /// <summary>Guards against the repositioning below re-triggering the events that call it.</summary>
    private static readonly HashSet<Window> Adjusting = new();

    /// <summary>
    /// Starts keeping <paramref name="window"/> on screen, and keeps doing it as it changes.
    /// </summary>
    public static void KeepOnScreen(Window window, IMonitors monitors)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(monitors);

        void Apply() => Fit(window, monitors);

        // The handle has to exist before the monitor under it can be found.
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) Apply();
        else window.SourceInitialized += (_, _) => Apply();

        // SizeToContent fires this every time a panel opens or closes, which is exactly the moment
        // the window can end up hanging off the bottom.
        window.SizeChanged += (_, _) => Apply();
        window.LocationChanged += (_, _) => Apply();
        window.DpiChanged += (_, _) => Apply();
    }

    /// <summary>
    /// The usable rectangle, in the units WPF positions windows with.
    /// </summary>
    /// <remarks>
    /// The monitor reports pixels; <see cref="Window.Top"/> and friends are device-independent
    /// units. On the machine this was written on those differ by 1.25, so skipping the conversion
    /// would put the ceiling 25% too high and reintroduce the whole problem.
    /// </remarks>
    public static Rect UsableArea(Window window, IMonitors monitors)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = handle == IntPtr.Zero ? null : monitors.GetMonitorForWindow(handle);
        var work = monitor?.WorkArea;

        if (work is null)
        {
            // No monitor to ask — the primary screen's work area is a better guess than nothing.
            return SystemParameters.WorkArea;
        }

        var dpi = VisualTreeHelper.GetDpi(window);
        var scaleX = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
        var scaleY = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;

        return new Rect(
            work.Value.Left / scaleX,
            work.Value.Top / scaleY,
            work.Value.Width / scaleX,
            work.Value.Height / scaleY);
    }

    private static void Fit(Window window, IMonitors monitors)
    {
        if (!Adjusting.Add(window)) return;

        try
        {
            var usable = UsableArea(window, monitors);

            if (usable.Width <= 0 || usable.Height <= 0) return;

            var height = Measured(window.ActualHeight, window.Height);
            var width = Measured(window.ActualWidth, window.Width);

            var placement = Solve(usable, new Size(width, height), new Point(window.Left, window.Top));

            // The ceiling first: with SizeToContent the window would otherwise ask for whatever
            // its content wants, and the scroll viewer inside it never gets a reason to scroll.
            window.MaxHeight = placement.MaxHeight;
            window.MaxWidth = placement.MaxWidth;

            if (!double.IsNaN(placement.Top)) window.Top = placement.Top;
            if (!double.IsNaN(placement.Left)) window.Left = placement.Left;
        }
        finally
        {
            Adjusting.Remove(window);
        }
    }

    private static double Measured(double actual, double requested) =>
        actual > 0 ? actual : requested;

    /// <param name="MaxHeight">The tallest the window may become on this screen.</param>
    /// <param name="Top">Where it has to move to, or NaN when its size is not known yet.</param>
    public readonly record struct Placement(double MaxHeight, double MaxWidth, double Top, double Left);

    /// <summary>
    /// Where a window of this size belongs inside this usable area.
    /// </summary>
    /// <remarks>
    /// Split out from the window it moves so the arithmetic can be tested against screen sizes
    /// nobody here owns — a 1366×768 laptop, a portrait monitor, a taskbar docked to the side —
    /// which is the whole reason this exists rather than another guessed constant.
    /// </remarks>
    public static Placement Solve(Rect usable, Size window, Point position)
    {
        var maxHeight = Math.Max(MinimumHeight, usable.Height - Margin * 2);
        var maxWidth = Math.Max(MinimumWidth, usable.Width - Margin * 2);

        var top = Clamp(position.Y, window.Height, usable.Top, usable.Bottom);
        var left = Clamp(position.X, window.Width, usable.Left, usable.Right);

        return new Placement(maxHeight, maxWidth, top, left);
    }

    /// <summary>Floors, so a screen too small to hold a dialog gets a scrollable one, not a sliver.</summary>
    private const double MinimumHeight = 240;

    private const double MinimumWidth = 320;

    /// <summary>
    /// Slides one axis back inside the usable range.
    /// </summary>
    /// <remarks>
    /// The near edge wins when the window is bigger than the screen, so an oversized dialog is cut
    /// off at the far end rather than at the top — losing the last row beats losing the title bar,
    /// and the ceiling above means it should not come to that.
    /// </remarks>
    private static double Clamp(double position, double size, double min, double max)
    {
        if (double.IsNaN(position) || double.IsNaN(size) || size <= 0) return double.NaN;

        var furthest = max - Margin - size;

        return Math.Max(min + Margin, Math.Min(position, furthest));
    }
}
