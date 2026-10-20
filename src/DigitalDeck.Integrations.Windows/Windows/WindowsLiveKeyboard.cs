using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;

namespace DigitalDeck.Integrations.Windows;

/// <inheritdoc cref="ILiveKeyboard"/>
/// <remarks>
/// Nothing here but the three combinations and Live's process name. The hard part — raising a
/// window, landing the keyboard focus on it, and sending keys a host will actually act on — is
/// <see cref="WindowsHotkeySender"/>, which does the same job for any program. This class came
/// first and taught that one everything it knows.
/// </remarks>
public sealed class WindowsLiveKeyboard : ILiveKeyboard
{
    /// <summary>Live's process is named "Ableton Live 12 Suite" and the like; the prefix is stable.</summary>
    public const string ProcessPrefix = "Ableton Live";

    private readonly IHotkeySender _sender;

    public WindowsLiveKeyboard()
        : this(new WindowsHotkeySender())
    {
    }

    public WindowsLiveKeyboard(IHotkeySender sender) =>
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    /// <summary>
    /// The factory shortcut for one of Live's menu commands.
    /// </summary>
    /// <remarks>
    /// Live's own defaults. Somebody who has remapped them in Live's preferences would have to put
    /// them back, which is why the editor shows which keys the button sends rather than hiding it.
    /// </remarks>
    public static HotkeySpec Chord(LiveShortcut shortcut) => shortcut switch
    {
        LiveShortcut.ExportAudioVideo =>
            new HotkeySpec(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'R'),
        LiveShortcut.SaveSet =>
            new HotkeySpec(HotkeyModifiers.Control, 'S'),
        LiveShortcut.SaveSetAs =>
            new HotkeySpec(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'S'),
        _ => throw new ArgumentOutOfRangeException(nameof(shortcut), shortcut, null)
    };

    public Task SendAsync(LiveShortcut shortcut, CancellationToken ct = default) =>
        _sender.SendAsync(Chord(shortcut), ProcessPrefix, ct);
}
