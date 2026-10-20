using System.Text.Json;
using DigitalDeck.Core.Model;

namespace DigitalDeck.Core.Layout;

/// <summary>
/// Where a window was left: its rectangle, and enough about the screen to find that screen again.
/// </summary>
/// <remarks>
/// The monitor's own geometry is stored alongside the window's because matching a remembered screen
/// has to compare screens, not windows — see <see cref="Services.MonitorMatch"/>. Getting it wrong
/// is not cosmetic: the whole point of the app is to live on a second screen, and a window restored
/// onto a monitor that is no longer connected is invisible.
/// <para>
/// The property names are the ones already written into people's settings, and changing one would
/// silently lose every remembered position. This is a stored shape, not a free one.
/// </para>
/// </remarks>
public sealed class WindowPlacement
{
    public string? DeviceName { get; set; }

    public int MonitorLeft { get; set; }
    public int MonitorTop { get; set; }
    public int MonitorWidth { get; set; }
    public int MonitorHeight { get; set; }

    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public bool Fullscreen { get; set; }

    /// <summary>The screen's rectangle, as it was when this was saved.</summary>
    public MonitorRect MonitorRect =>
        new(MonitorLeft, MonitorTop, MonitorLeft + MonitorWidth, MonitorTop + MonitorHeight);

    /// <summary>The window's own rectangle.</summary>
    public MonitorRect Bounds => new(Left, Top, Left + Width, Top + Height);

    /// <summary>How big a window opens when there is nothing remembered.</summary>
    public const int PreferredWidth = 1160;

    /// <summary>How tall a window opens when there is nothing remembered.</summary>
    public const int PreferredHeight = 720;

    /// <summary>Describes a window sitting on a screen, ready to be stored.</summary>
    public static WindowPlacement Of(MonitorInfo monitor, MonitorRect bounds, bool fullscreen) =>
        new()
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
            Fullscreen = fullscreen
        };

    /// <summary>
    /// A window centred on a screen, at a comfortable size for it.
    /// </summary>
    /// <remarks>
    /// Bounded by the work area rather than the whole screen so it does not open under the taskbar
    /// or the menu bar, and inset from it so the window reads as placed rather than jammed in.
    /// </remarks>
    public static MonitorRect CentredIn(
        MonitorInfo monitor, int preferredWidth = PreferredWidth, int preferredHeight = PreferredHeight)
    {
        var width = Math.Min(preferredWidth, monitor.WorkArea.Width - 80);
        var height = Math.Min(preferredHeight, monitor.WorkArea.Height - 80);

        var left = monitor.WorkArea.Left + (monitor.WorkArea.Width - width) / 2;
        var top = monitor.WorkArea.Top + (monitor.WorkArea.Height - height) / 2;

        return new MonitorRect(left, top, left + width, top + height);
    }

    /// <summary>Reads what was stored, or null when there is nothing usable there.</summary>
    /// <remarks>
    /// Null rather than an exception for anything unreadable: a setting written by a different
    /// build is a window that opens centred, not an app that refuses to start.
    /// </remarks>
    public static WindowPlacement? Read(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;

        try
        {
            return JsonSerializer.Deserialize<WindowPlacement>(stored);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>How this is written into the settings table.</summary>
    public string ToJson() => JsonSerializer.Serialize(this);
}
