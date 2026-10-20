namespace DigitalDeck.Core.Model;

/// <summary>
/// What to draw for a tile saved before the icon set existed.
/// </summary>
/// <remarks>
/// Old decks keep a character from the Windows icon font — Segoe Fluent Icons, and Segoe MDL2
/// Assets before it — in the same field that now holds an icon's name. On Windows that character
/// still draws and the WPF app still draws it. Anywhere else the font does not exist, and these
/// characters live in the Unicode private use area, where a missing font leaves an empty tile or a
/// tofu box: on a Mac an old deck would come up as a grid of blank keys with labels under them.
/// <para>
/// So the character is translated rather than drawn. Written as code points instead of literals for
/// the same reason the WPF app's table is: the private use area is silently destroyed by any tool
/// along the way that guesses the wrong encoding, and a code point survives everything.
/// </para>
/// <para>
/// Two of the old icons — the pencil and the plus — have no counterpart in the new set and are
/// deliberately missing here. <see cref="EquivalentOf"/> answers null for them, and the caller falls
/// back to the default for the tile's kind: an honest "no icon" rather than a wrong one.
/// </para>
/// </remarks>
public static class LegacyGlyphs
{
    /// <summary>Every icon the old font palette offered, against its name in the new set.</summary>
    private static readonly Dictionary<string, string> Equivalents = new(StringComparer.Ordinal)
    {
        [Glyph(0xE8B7)] = "folder",
        [Glyph(0xE7C3)] = "document",   // «Page», the old default for a button.
        [Glyph(0xE8A5)] = "document",
        [Glyph(0xE774)] = "globe",
        [Glyph(0xE71B)] = "link",
        [Glyph(0xE768)] = "play",
        [Glyph(0xE71A)] = "stop",
        [Glyph(0xE767)] = "volume",
        [Glyph(0xE714)] = "clip",       // «Video»: the nearest thing here to a strip of film.
        [Glyph(0xE722)] = "image",      // «Camera»
        [Glyph(0xE8F1)] = "music",      // «Library», which in this app is a library of music.
        [Glyph(0xE721)] = "search",
        [Glyph(0xE713)] = "settings",
        [Glyph(0xE734)] = "star",
        [Glyph(0xE895)] = "loop",       // «Sync»
        [Glyph(0xE896)] = "download",
        [Glyph(0xE8C8)] = "copy",
        [Glyph(0xE715)] = "mail",
        [Glyph(0xE80F)] = "home",
        [Glyph(0xE946)] = "info",
    };

    /// <summary>
    /// The icon that stands in for an old font character, or null when nothing here does.
    /// </summary>
    /// <remarks>
    /// Answers null for anything that is not one of the old characters at all, which includes every
    /// name in the current set — asking about <c>folder</c> is not an error, it is a question with
    /// no answer, and the caller was going to draw it directly anyway.
    /// </remarks>
    public static string? EquivalentOf(string? glyph) =>
        glyph is not null && Equivalents.TryGetValue(glyph, out var name) ? name : null;

    private static string Glyph(int codePoint) => char.ConvertFromUtf32(codePoint);
}
