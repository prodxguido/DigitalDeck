using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Layout;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Services;

namespace DigitalDeck.App.Services;

/// <summary>
/// Remembers which monitor the deck was on and puts it back there next time.
/// </summary>
/// <remarks>
/// The whole point of the app is to live on a second screen, so getting this wrong is not a
/// cosmetic issue — a window restored onto a monitor that is no longer connected is invisible.
/// The monitor's own geometry is stored alongside the window's, because matching a remembered
/// screen has to compare screens, not windows. Every restore ends on a monitor that exists.
/// </remarks>
public sealed class WindowPlacementService
{
    private readonly IMonitors _monitors;
    private readonly IAppSettingsStore _settings;

    public WindowPlacementService(IMonitors monitors, IAppSettingsStore settings)
    {
        _monitors = monitors;
        _settings = settings;
    }

    /// <summary>Positions the window where it was last time, or centred on the primary monitor.</summary>
    /// <param name="key">
    /// Which remembered position to use. Companion windows pass their own, because the deck and the
    /// mixer are meant to sit on different screens and one shared memory would stack them.
    /// </param>
    /// <returns>True when the window should come back up in fullscreen.</returns>
    public async Task<bool> RestoreAsync(Window window, string key = SettingKeys.WindowPlacement)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var saved = await ReadAsync(key).ConfigureAwait(true);

        var monitor = MonitorMatch.FindBest(_monitors.GetMonitors(), saved?.DeviceName, saved?.MonitorRect);
        if (monitor is null) return false;

        if (saved is null)
        {
            CentreOn(handle, monitor);
            return false;
        }

        if (saved.Fullscreen)
        {
            ApplyFullscreen(window, handle, monitor);
            return true;
        }

        // Clamped into the monitor's work area so a resolution change between sessions cannot
        // leave the window hanging off the edge.
        var width = Math.Clamp(saved.Width, 640, monitor.WorkArea.Width);
        var height = Math.Clamp(saved.Height, 360, monitor.WorkArea.Height);

        var left = Math.Clamp(saved.Left, monitor.WorkArea.Left, Math.Max(monitor.WorkArea.Left, monitor.WorkArea.Right - width));
        var top = Math.Clamp(saved.Top, monitor.WorkArea.Top, Math.Max(monitor.WorkArea.Top, monitor.WorkArea.Bottom - height));

        _monitors.SetWindowBounds(handle, new MonitorRect(left, top, left + width, top + height));
        return false;
    }

    public async Task SaveAsync(
        Window window,
        bool isFullscreen,
        MonitorRect? windowedBounds,
        string key = SettingKeys.WindowPlacement)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var monitor = _monitors.GetMonitorForWindow(handle);
        if (monitor is null) return;

        // In fullscreen the live window rectangle is the monitor rectangle, which says nothing
        // about where to restore a windowed deck — so the caller hands over the pre-fullscreen
        // bounds it stashed.
        //
        // Both branches are physical pixels, which is what RestoreAsync feeds back to SetWindowPos.
        // Reading Window.Left and friends here instead would mix in device-independent units: on a
        // monitor at 125% those are four fifths of the real numbers, so every session would restore
        // the window a little further towards the corner and a little smaller than the last.
        var bounds = isFullscreen
            ? windowedBounds ?? WindowPlacement.CentredIn(monitor)
            : GetWindowBounds(window) ?? WindowPlacement.CentredIn(monitor);

        var state = new WindowPlacement
        {
            DeviceName = monitor.DeviceName,
            MonitorLeft = monitor.Bounds.Left,
            MonitorTop = monitor.Bounds.Top,
            MonitorWidth = monitor.Bounds.Width,
            MonitorHeight = monitor.Bounds.Height,
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Fullscreen = isFullscreen
        };

        await _settings.SetAsync(key, JsonSerializer.Serialize(state)).ConfigureAwait(false);
    }

    /// <summary>Fills the monitor the window is currently on, edge to edge.</summary>
    public void ApplyFullscreen(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = _monitors.GetMonitorForWindow(handle);

        if (monitor is not null) ApplyFullscreen(window, handle, monitor);
    }

    /// <summary>Puts the window back to a given rectangle in physical pixels.</summary>
    public void ApplyBounds(Window window, MonitorRect bounds)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero) _monitors.SetWindowBounds(handle, bounds);
    }

    public void ApplyDock(Window window, bool bottom = false, int rows = 1, bool overlayTaskbar = false)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = _monitors.GetMonitorForWindow(handle);
        if (monitor is null) return;
        var scale = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformToDevice.M22 ?? 1;
        var area = monitor.WorkArea;
        // Taskbar height comes from the monitor's physical work-area inset, so DPI scaling
        // does not get applied twice. Auto-hide / vertical taskbars use a 48-DIP fallback.
        var inset = Math.Max(monitor.Bounds.Bottom - area.Bottom, area.Top - monitor.Bounds.Top);
        var rowHeight = inset >= 32 * scale && inset <= 96 * scale
            ? inset : (int)Math.Round(48 * scale);
        rowHeight = Math.Max(rowHeight, (int)Math.Ceiling(40 * scale));
        var height = Math.Min(area.Height, rowHeight * Math.Clamp(rows, 1, 2));
        var dockBottom = overlayTaskbar ? monitor.Bounds.Bottom : area.Bottom;
        var top = bottom ? dockBottom - height : area.Top;
        _monitors.SetWindowBounds(handle,
            new MonitorRect(area.Left, top, area.Right, top + height));
    }

    /// <summary>The window's current rectangle in physical pixels, for stashing before fullscreen.</summary>
    public MonitorRect? GetWindowBounds(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return null;

        var source = PresentationSource.FromVisual(window);
        var scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1;

        var left = (int)Math.Round(window.Left * scaleX);
        var top = (int)Math.Round(window.Top * scaleY);
        var width = (int)Math.Round(window.Width * scaleX);
        var height = (int)Math.Round(window.Height * scaleY);

        return new MonitorRect(left, top, left + width, top + height);
    }

    private void ApplyFullscreen(Window window, IntPtr handle, MonitorInfo monitor)
    {
        window.WindowState = WindowState.Normal;
        _monitors.SetWindowBounds(handle, monitor.Bounds);
    }

    private void CentreOn(IntPtr handle, MonitorInfo monitor) =>
        _monitors.SetWindowBounds(handle, WindowPlacement.CentredIn(monitor));

    private async Task<WindowPlacement?> ReadAsync(string key) =>
        WindowPlacement.Read(await _settings.GetAsync(key).ConfigureAwait(true));
}
