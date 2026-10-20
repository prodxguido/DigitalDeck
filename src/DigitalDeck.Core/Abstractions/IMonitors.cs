using DigitalDeck.Core.Model;

namespace DigitalDeck.Core.Abstractions;

/// <summary>
/// The displays attached to the machine, and moving a window onto one of them.
/// </summary>
/// <remarks>
/// Placement is in physical pixels rather than the toolkit's own device-independent units. Across a
/// boundary between monitors with different scale factors, those units mean different things on
/// each side, and setting them directly lands the window in the wrong place or the wrong size. Every
/// desktop has that problem and every desktop solves it below the toolkit, which is why this
/// interface exists at all.
/// <para>
/// The choosing — which monitor a remembered one became — is not here. That is
/// <see cref="Services.MonitorMatch"/>, which is ordinary logic over the list and needs no display
/// server to be tested.
/// </para>
/// </remarks>
public interface IMonitors
{
    /// <summary>Every display, in whatever order the system reports them.</summary>
    IReadOnlyList<MonitorInfo> GetMonitors();

    /// <summary>
    /// The display a window is mostly on, or null when it cannot be told.
    /// </summary>
    /// <param name="windowHandle">
    /// The platform's own handle for the window — an <c>HWND</c> on Windows, an <c>NSWindow</c>
    /// elsewhere. Opaque here on purpose: this interface never looks inside it, it only hands it
    /// back to the implementation that produced it.
    /// </param>
    MonitorInfo? GetMonitorForWindow(nint windowHandle);

    /// <summary>Moves and sizes a window using physical pixels.</summary>
    void SetWindowBounds(nint windowHandle, MonitorRect bounds);
}
