namespace DigitalDeck.Core.Abstractions;

/// <summary>A Live menu command with no place in the Live Object Model, reached by its shortcut.</summary>
public enum LiveShortcut
{
    /// <summary>File → Export Audio/Video. Factory shortcut <c>Ctrl+Shift+R</c>.</summary>
    ExportAudioVideo,

    /// <summary>File → Save Live Set. Factory shortcut <c>Ctrl+S</c>.</summary>
    SaveSet,

    /// <summary>File → Save Live Set As. Factory shortcut <c>Ctrl+Shift+S</c>.</summary>
    SaveSetAs
}

/// <summary>
/// Presses a keyboard shortcut inside Ableton Live.
/// </summary>
/// <remarks>
/// Export, Save and Save As are not on the Live API — the Remote Script cannot touch them. The only
/// way from the outside is to do what a person does: bring Live to the front and press the keys.
/// The combinations are Live's factory defaults; someone who has remapped them in Live's
/// preferences would have to change them back, which is why the button says which keys it sends.
/// <para>
/// This is why it works "no matter which user": nothing here depends on the OSC bridge, the Remote
/// Script or a saved path — only on Live being open somewhere on the machine.
/// </para>
/// </remarks>
public interface ILiveKeyboard
{
    /// <summary>
    /// Brings Live to the front and sends <paramref name="shortcut"/>. Throws with a message meant
    /// for the status bar when Live is not running.
    /// </summary>
    Task SendAsync(LiveShortcut shortcut, CancellationToken ct = default);
}
