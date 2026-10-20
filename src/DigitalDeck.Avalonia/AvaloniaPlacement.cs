using Avalonia;
using Avalonia.Controls;
using DigitalDeck.Core.Layout;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Services;

namespace DigitalDeck.Avalonia;

/// <summary>
/// Puts the deck back on the screen it was left on.
/// </summary>
/// <remarks>
/// The whole point of the app is to live on a second screen, so this is not cosmetic: a window
/// restored onto a monitor that is no longer connected is invisible, and a person who cannot find
/// their deck assumes it did not start.
/// <para>
/// Which screen a remembered one became is <see cref="MonitorMatch"/>, shared with the WPF app, and
/// what gets stored is <see cref="WindowPlacement"/> — the same shape in the same settings key, so
/// a deck that moves between the two shells keeps its position instead of forgetting it once.
/// </para>
/// </remarks>
public static class AvaloniaPlacement
{
    /// <summary>Avalonia's screens, described the way the shared rules expect them.</summary>
    /// <remarks>
    /// Avalonia already speaks in physical pixels here, which is the same thing the WPF side goes
    /// out of its way to get: across monitors with different scale factors, the toolkit's own units
    /// mean different things on each side of the boundary.
    /// </remarks>
    public static IReadOnlyList<MonitorInfo> Monitors(Window window)
    {
        var screens = window.Screens;

        if (screens is null) return [];

        return screens.All
            .Select(screen => new MonitorInfo(
                screen.DisplayName ?? string.Empty,
                Rect(screen.Bounds),
                Rect(screen.WorkingArea),
                screen.IsPrimary))
            .ToList();
    }

    /// <summary>Where the window is now, ready to be stored, or null when it is on no screen.</summary>
    public static WindowPlacement? Capture(Window window)
    {
        var monitors = Monitors(window);

        if (monitors.Count == 0) return null;

        var bounds = new MonitorRect(
            window.Position.X,
            window.Position.Y,
            window.Position.X + (int)window.Width,
            window.Position.Y + (int)window.Height);

        // The screen the top-left corner is on. Good enough: a window is dragged by its title bar,
        // so that corner is the one the person put where they wanted it.
        var monitor =
            monitors.FirstOrDefault(m => Contains(m.Bounds, bounds.Left, bounds.Top))
            ?? MonitorMatch.Primary(monitors);

        return monitor is null
            ? null
            : WindowPlacement.Of(monitor, bounds, window.WindowState == WindowState.FullScreen);
    }

    /// <summary>
    /// Puts the window where <paramref name="saved"/> says, or centred when there is nothing usable.
    /// </summary>
    /// <returns>True when the window should come back up in fullscreen.</returns>
    public static bool Restore(Window window, WindowPlacement? saved)
    {
        var monitors = Monitors(window);

        if (monitors.Count == 0) return false;

        var monitor = MonitorMatch.FindBest(monitors, saved?.DeviceName, saved?.MonitorRect);

        if (monitor is null) return false;

        if (saved is null)
        {
            Place(window, WindowPlacement.CentredIn(monitor));
            return false;
        }

        if (saved.Fullscreen)
        {
            Place(window, monitor.Bounds);
            return true;
        }

        // Onto the screen that was found rather than the coordinates that were saved: the screen
        // may be in a different place on the desk than it was, and the saved corner would then be
        // somewhere else entirely — or nowhere.
        var offsetX = monitor.Bounds.Left - saved.MonitorRect.Left;
        var offsetY = monitor.Bounds.Top - saved.MonitorRect.Top;

        var moved = new MonitorRect(
            saved.Left + offsetX,
            saved.Top + offsetY,
            saved.Left + offsetX + saved.Width,
            saved.Top + offsetY + saved.Height);

        Place(window, Fit(moved, monitor));

        return false;
    }

    /// <summary>Keeps a rectangle inside a screen's work area, moving it before shrinking it.</summary>
    private static MonitorRect Fit(MonitorRect bounds, MonitorInfo monitor)
    {
        var area = monitor.WorkArea;

        var width = Math.Min(bounds.Width, area.Width);
        var height = Math.Min(bounds.Height, area.Height);

        var left = Math.Clamp(bounds.Left, area.Left, Math.Max(area.Left, area.Right - width));
        var top = Math.Clamp(bounds.Top, area.Top, Math.Max(area.Top, area.Bottom - height));

        return new MonitorRect(left, top, left + width, top + height);
    }

    private static void Place(Window window, MonitorRect bounds)
    {
        window.Position = new PixelPoint(bounds.Left, bounds.Top);
        window.Width = bounds.Width;
        window.Height = bounds.Height;
    }

    private static bool Contains(MonitorRect rect, int x, int y) =>
        x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom;

    private static MonitorRect Rect(PixelRect rect) =>
        new(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height);
}
