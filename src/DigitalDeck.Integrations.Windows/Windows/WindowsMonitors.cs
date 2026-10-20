using System.Runtime.InteropServices;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;

namespace DigitalDeck.Integrations.Windows;

/// <inheritdoc cref="IMonitors"/>
/// <remarks>
/// Nothing but the Win32 half. Placement goes through <c>SetWindowPos</c> with physical pixels for
/// the reason given on the interface, and the choosing of a monitor lives in
/// <c>MonitorMatch</c> — everything here needs a real display server to say anything at all.
/// </remarks>
public sealed class WindowsMonitors : IMonitors
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();

        bool Callback(IntPtr monitor, IntPtr _, ref Rect __, IntPtr ___)
        {
            if (TryGetInfo(monitor, out var info)) monitors.Add(info);
            return true;
        }

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        return monitors;
    }

    public MonitorInfo? GetMonitorForWindow(nint windowHandle)
    {
        var monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        return monitor == IntPtr.Zero ? null : TryGetInfo(monitor, out var info) ? info : null;
    }

    public void SetWindowBounds(nint windowHandle, MonitorRect bounds) =>
        SetWindowPos(windowHandle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            SwpNoZOrder | SwpNoActivate);

    private static bool TryGetInfo(IntPtr monitor, out MonitorInfo info)
    {
        var raw = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };

        if (!GetMonitorInfo(monitor, ref raw))
        {
            info = null!;
            return false;
        }

        info = new MonitorInfo(
            raw.szDevice,
            new MonitorRect(raw.rcMonitor.Left, raw.rcMonitor.Top, raw.rcMonitor.Right, raw.rcMonitor.Bottom),
            new MonitorRect(raw.rcWork.Left, raw.rcWork.Top, raw.rcWork.Right, raw.rcWork.Bottom),
            (raw.dwFlags & 1) != 0);

        return true;
    }

    #region Win32

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumDelegate(IntPtr monitor, IntPtr deviceContext, ref Rect rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clip, MonitorEnumDelegate callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    #endregion
}
