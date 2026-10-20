namespace DigitalDeck.Core.Abstractions;

/// <summary>
/// The things a window needs from the desktop that no UI toolkit exposes.
/// </summary>
/// <remarks>
/// Both of these are asks the compositor answers, not drawing the app can do for itself, and each
/// desktop answers them its own way. Neither is worth failing over: a window with square corners is
/// ugly, and an overlay that takes the focus is annoying, but an app that refuses to open is worse.
/// An implementation that cannot do one of them does nothing and says nothing.
/// <para>
/// The handles are the platform's own — an <c>HWND</c> on Windows, an <c>NSWindow</c> elsewhere —
/// and opaque here, the same as in <see cref="IMonitors"/>.
/// </para>
/// </remarks>
public interface IWindowEffects
{
    /// <summary>
    /// Rounds the window's corners.
    /// </summary>
    /// <remarks>
    /// The app draws its own chrome, which on a desktop that rounds everything else leaves it the
    /// one square window on screen.
    /// </remarks>
    void RoundCorners(nint window);

    /// <summary>
    /// Turns a window into a status overlay: it never takes the keyboard focus, clicks pass through
    /// it, and it stays out of the window switcher.
    /// </summary>
    /// <remarks>
    /// The focus half is the one that matters. The voice overlay appears while somebody is working
    /// in Live, and a window that steals the focus to say "escuchando" costs them their selection,
    /// their edit and their playhead — worse than having no overlay at all.
    /// </remarks>
    void MakeOverlay(nint window);
}
