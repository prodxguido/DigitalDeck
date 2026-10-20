using DigitalDeck.Core.Model;

namespace DigitalDeck.Core.Abstractions;

/// <summary>
/// One key combination claimed from the whole system, so the deck answers it while somebody is
/// working in another program.
/// </summary>
/// <remarks>
/// Registering is genuinely allowed to fail: the combination belongs to whoever asked for it first,
/// and on a producer's machine that could be anything. The failure has to reach the user, because a
/// silently dead hotkey is indistinguishable from a broken feature — which is why
/// <see cref="TryRegister"/> hands back a sentence to show rather than a code to interpret.
/// </remarks>
public interface IGlobalHotkey : IDisposable
{
    /// <summary>The combination currently claimed, or null when none is.</summary>
    HotkeySpec? Current { get; }

    /// <summary>Raised when the combination is pressed, wherever the keyboard focus is.</summary>
    event Action? Pressed;

    /// <summary>
    /// Ties the hotkey to the window that owns it.
    /// </summary>
    /// <remarks>
    /// Windows delivers a hotkey as a message to a window, so it needs one before it can register
    /// anything; a platform that delivers them another way can ignore the handle. Called once, when
    /// the shell has a real window.
    /// </remarks>
    void Attach(nint hostWindow);

    /// <summary>
    /// Claims a combination, releasing whatever was claimed before.
    /// </summary>
    /// <param name="problem">Why it failed, written for the status bar. Empty when it worked.</param>
    /// <remarks>
    /// The old combination goes even when the new one turns out to be unavailable. Keeping it would
    /// leave the user with a hotkey they did not choose and an error saying the one they did choose
    /// failed.
    /// </remarks>
    bool TryRegister(HotkeySpec spec, out string problem);

    /// <summary>Gives the combination back to the system. Safe when nothing is claimed.</summary>
    void Unregister();

    /// <summary>
    /// Feeds one message from the host window's queue.
    /// </summary>
    /// <remarks>
    /// Here because Windows delivers a hotkey as a window message, and only the shell has the
    /// window's message loop. An implementation whose platform pushes hotkeys some other way
    /// ignores this and raises <see cref="Pressed"/> from its own source; a shell with no message
    /// loop to hook simply never calls it.
    /// </remarks>
    /// <returns>True when the message was this hotkey firing and needs no further handling.</returns>
    bool OnHostMessage(int message, nint wParam);
}
