namespace DigitalDeck.Core.Model;

/// <summary>
/// The modifier keys of a combination.
/// </summary>
/// <remarks>
/// The values are Windows' own <c>MOD_*</c> constants, so the registration of a global hotkey can
/// pass them straight through. That is a convenience, not a dependency: nothing in this file calls
/// Windows, which is why it lives in Core and can be read, written and tested anywhere.
/// </remarks>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008
}

/// <summary>
/// A global key combination, in the form it is stored and the form it is shown.
/// </summary>
/// <remarks>
/// Kept as a value that round-trips through text because the user chooses it and the choice is
/// saved. Storing the raw virtual-key number instead would work and be unreadable — nobody can look
/// at <c>voice_hotkey = 32</c> in a settings table and know it says Space.
/// </remarks>
public readonly record struct HotkeySpec(HotkeyModifiers Modifiers, uint VirtualKey)
{
    /// <summary>
    /// What the assistant listens on until someone changes it.
    /// </summary>
    /// <remarks>
    /// Chosen to stay out of Ableton's way. Live uses Ctrl and Ctrl+Shift heavily but leaves
    /// Ctrl+Alt combinations almost entirely alone, and Space on its own is Live's play/stop — the
    /// one key a producer would least forgive us for taking.
    /// </remarks>
    public static readonly HotkeySpec Default =
        new(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKeys.Space);

    public bool IsValid => VirtualKey != 0;

    /// <summary>
    /// Reads a combination written as <c>Ctrl+Alt+Space</c>. Order does not matter and the modifier
    /// names are the ones people actually type.
    /// </summary>
    public static bool TryParse(string? text, out HotkeySpec spec)
    {
        spec = default;

        if (string.IsNullOrWhiteSpace(text)) return false;

        var modifiers = HotkeyModifiers.None;
        uint key = 0;

        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = raw.ToLowerInvariant();

            switch (part)
            {
                case "ctrl" or "control":
                    modifiers |= HotkeyModifiers.Control;
                    continue;
                case "alt":
                    modifiers |= HotkeyModifiers.Alt;
                    continue;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    continue;
                case "win" or "windows" or "meta":
                    modifiers |= HotkeyModifiers.Windows;
                    continue;
            }

            // Two keys in one combination is not a typo we can guess our way out of.
            if (key != 0) return false;
            if (!VirtualKeys.TryParse(part, out key)) return false;
        }

        if (key == 0) return false;

        spec = new HotkeySpec(modifiers, key);

        return true;
    }

    /// <summary>The combination as it should be shown, and as <see cref="TryParse"/> reads it back.</summary>
    public override string ToString()
    {
        if (!IsValid) return string.Empty;

        var parts = new List<string>(4);

        // Fixed order regardless of how it was typed, so the same combination is always written the
        // same way in the settings table and on screen.
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");

        parts.Add(VirtualKeys.Name(VirtualKey));

        return string.Join('+', parts);
    }
}

/// <summary>The handful of virtual-key codes worth offering as a hotkey, by name.</summary>
/// <remarks>
/// Deliberately not every key Windows defines. Punctuation keys are the ones whose codes move
/// between keyboard layouts, so a hotkey saved on a Spanish keyboard would land somewhere else on a
/// US one; leaving them out means every combination this accepts means the same thing everywhere.
/// </remarks>
public static class VirtualKeys
{
    public const uint Space = 0x20;

    public static bool TryParse(string name, out uint key)
    {
        key = 0;

        if (string.IsNullOrEmpty(name)) return false;

        if (name is "space" or "espacio")
        {
            key = Space;
            return true;
        }

        if (name.Length == 1)
        {
            var c = char.ToUpperInvariant(name[0]);

            // A-Z and 0-9 have virtual-key codes equal to their ASCII values.
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                key = c;
                return true;
            }

            return false;
        }

        if (name[0] == 'f' && uint.TryParse(name[1..], out var number) && number is >= 1 and <= 24)
        {
            key = 0x70 + number - 1;   // VK_F1 is 0x70 and they run consecutively
            return true;
        }

        return false;
    }

    public static string Name(uint key) => key switch
    {
        Space => "Space",
        >= 0x70 and <= 0x87 => $"F{key - 0x70 + 1}",
        >= 'A' and <= 'Z' or >= '0' and <= '9' => ((char)key).ToString(),
        _ => $"0x{key:X2}"
    };
}
