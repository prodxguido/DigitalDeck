using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.Data.Packs;

/// <summary>
/// The interchange format: a zip holding this manifest plus the icons it references.
/// </summary>
/// <remarks>
/// This is the one place the tree is written as nested JSON. Storage keeps the tree flat with a
/// parent reference because that is what makes an interactive edit cheap; a pack is read whole,
/// once, so nesting is the clearer shape and survives being opened in a text editor.
/// </remarks>
public sealed class DeckPack
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string App { get; set; } = "DigitalDeck";
    public string? AppVersion { get; set; }
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;
    public PackProfile Profile { get; set; } = new();
}

public sealed class PackProfile
{
    public string Name { get; set; } = string.Empty;
    public List<PackPage> Pages { get; set; } = new();
}

public sealed class PackPage
{
    public string Name { get; set; } = string.Empty;
    public int Columns { get; set; } = 6;
    public int Rows { get; set; } = 4;
    public List<PackItem> Items { get; set; } = new();
}

public sealed class PackItem
{
    public ItemType Type { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Col { get; set; }
    public int Row { get; set; }
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;
    public string ColorHex { get; set; } = DeckItem.DefaultColor;
    public string? IconGlyph { get; set; }

    /// <summary>Filename of the icon inside the pack's <c>icons/</c> folder.</summary>
    public string? IconFile { get; set; }

    public ActionPayload? Action { get; set; }
    public List<PackItem> Children { get; set; } = new();
}
