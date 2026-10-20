using System.Runtime.InteropServices;
using DigitalDeck.Core.Abstractions;

namespace DigitalDeck.Integrations.Windows;

/// <inheritdoc cref="IWindowEffects"/>
public sealed class WindowsWindowEffects : IWindowEffects
{
    /// <summary>
    /// Asks the desktop compositor to round the corners.
    /// </summary>
    /// <remarks>
    /// This is the supported way to opt back in after <c>WindowStyle="None"</c>, and it costs
    /// nothing at render time — unlike faking it with a transparent window, which would force
    /// software rendering on the whole app. On Windows 10 the attribute does not exist and the call
    /// fails harmlessly.
    /// </remarks>
    public void RoundCorners(nint window)
    {
        if (window == 0) return;

        try
        {
            var preference = RoundPreference;
            DwmSetWindowAttribute(window, WindowCornerPreference, ref preference, sizeof(int));
        }
        catch (Exception)
        {
            // Older Windows, or a compositor that says no. Square corners are not a reason to fail
            // opening a window.
        }
    }

    /// <remarks>
    /// Applied on the handle rather than only through the toolkit because WPF's
    /// <c>ShowActivated</c> governs the first show and nothing after it: without the extended
    /// style, showing the window a second time — on the second press of the hotkey — takes focus.
    /// </remarks>
    public void MakeOverlay(nint window)
    {
        if (window == 0) return;

        try
        {
            var style = GetWindowLong(window, GwlExStyle);

            // NoActivate keeps focus where it is; Transparent passes clicks through to whatever is
            // underneath; ToolWindow keeps it out of Alt+Tab, where a status popup has no business.
            SetWindowLong(window, GwlExStyle, style | WsExNoActivate | WsExTransparent | WsExToolWindow);
        }
        catch (Exception)
        {
            // An overlay that can still be clicked is a smaller problem than one that never opens.
        }
    }

    #region Win32

    private const int WindowCornerPreference = 33;
    private const int RoundPreference = 2;

    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTransparent = 0x00000020;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

    #endregion
}
