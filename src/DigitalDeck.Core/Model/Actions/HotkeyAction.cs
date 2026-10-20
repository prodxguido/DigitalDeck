using System.Text.Json.Serialization;
using DigitalDeck.Core.Abstractions;

namespace DigitalDeck.Core.Model.Actions;

/// <summary>
/// Presses a key combination in another program.
/// </summary>
/// <remarks>
/// The way in for everything that has no API worth the name: a plugin host's own shortcut, a
/// browser, a video editor. It is deliberately the last option offered in the editor — a button
/// that drives something properly keeps working when the other program moves its menus around, and
/// this one does not.
/// <para>
/// Not marked dangerous: nothing here starts a program, and a chord sent to something that is not
/// running does nothing at all. See <see cref="IsPotentiallyDangerous"/> for what that flag means.
/// </para>
/// </remarks>
public sealed class HotkeyAction : ActionPayload
{
    /// <summary>The combination, written the way <see cref="HotkeySpec"/> reads it: <c>Ctrl+Shift+E</c>.</summary>
    public string Keys { get; set; } = string.Empty;

    /// <summary>
    /// Which program to press it in, as the start of its process name.
    /// </summary>
    /// <remarks>
    /// Required, and the reason is in <see cref="IHotkeySender"/>: without it the chord would go to
    /// whatever has the focus, which after touching a tile is the deck itself.
    /// </remarks>
    public string Program { get; set; } = string.Empty;

    [JsonIgnore]
    public override string Summary =>
        Keys.Length == 0 ? "Atajo de teclado"
        : Program.Length == 0 ? Keys
        : $"{Keys} en {Program}";

    public override string? Validate()
    {
        if (!HotkeySpec.TryParse(Keys, out _))
            return "Elegí una combinación de teclas para este botón.";

        if (string.IsNullOrWhiteSpace(Program))
            return "Decime en qué programa hay que apretarla.";

        return null;
    }

    /// <summary>The combination as a value, or null when what is stored does not parse.</summary>
    public HotkeySpec? Spec => HotkeySpec.TryParse(Keys, out var spec) ? spec : null;
}
