using System.Runtime.InteropServices;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;

namespace DigitalDeck.Integrations.Windows;

/// <inheritdoc cref="IGlobalHotkey"/>
/// <remarks>
/// <c>RegisterHotKey</c> rather than a low-level keyboard hook, which is the other way to do this.
/// A hook would allow hold-to-talk, since it sees key release as well as press — but it also sees
/// every keystroke the user ever types, in every application, which is both a thing antivirus
/// software reacts to and a thing this feature has no business doing. Tap-to-listen costs nothing
/// by comparison.
/// </remarks>
public sealed class WindowsGlobalHotkey : IGlobalHotkey
{
    /// <summary>The message Windows posts to the window when the combination is pressed.</summary>
    private const int WmHotkey = 0x0312;

    /// <summary>
    /// Suppresses the repeats Windows would otherwise send while the keys are held down.
    /// </summary>
    /// <remarks>
    /// Without it, resting on the combination for half a second starts listening several times
    /// over. Every one of those would open the microphone and put an overlay on screen.
    /// </remarks>
    private const uint ModNoRepeat = 0x4000;

    /// <summary>Identifies this hotkey among any others the same window claims.</summary>
    private const int Id = 1;

    private IntPtr _window;
    private bool _registered;
    private bool _disposed;

    public HotkeySpec? Current { get; private set; }

    public event Action? Pressed;

    public void Attach(nint hostWindow) => _window = hostWindow;

    public bool TryRegister(HotkeySpec spec, out string problem)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Unregister();

        if (!spec.IsValid || _window == IntPtr.Zero)
        {
            problem = "Esa combinación no se puede usar como atajo.";
            return false;
        }

        if (!RegisterHotKey(_window, Id, (uint)spec.Modifiers | ModNoRepeat, spec.VirtualKey))
        {
            problem = $"Otra aplicación ya está usando {spec}. Elegí otra combinación.";
            return false;
        }

        _registered = true;
        Current = spec;
        problem = string.Empty;

        return true;
    }

    public void Unregister()
    {
        if (!_registered) return;

        UnregisterHotKey(_window, Id);

        _registered = false;
        Current = null;
    }

    public bool OnHostMessage(int message, nint wParam)
    {
        if (message != WmHotkey || (int)wParam != Id) return false;

        Pressed?.Invoke();

        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        Unregister();
    }

    #region Win32

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    #endregion
}
