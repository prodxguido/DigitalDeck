namespace DigitalDeck.Core.Model;

/// <summary>A monitor's rectangle in physical pixels on the virtual desktop.</summary>
public readonly record struct MonitorRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>
/// One display, as the app remembers it.
/// </summary>
/// <param name="DeviceName">
/// Something like <c>\\.\DISPLAY1</c> on Windows. Every desktop names its displays somehow and
/// every one of them can renumber them across reboots and cable changes, so it is a hint for
/// matching, never a reliable identity on its own — which is the whole reason
/// <see cref="Services.MonitorMatch"/> exists.
/// </param>
public sealed record MonitorInfo(string DeviceName, MonitorRect Bounds, MonitorRect WorkArea, bool IsPrimary);
