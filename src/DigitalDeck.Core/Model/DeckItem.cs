using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.Core.Model;

/// <summary>
/// A tile in the grid — a button or a folder. Nesting is modelled as an adjacency list
/// (<see cref="ParentId"/> points at the containing folder, null means the page root), which keeps
/// a drag-and-drop move to a single row update and imposes no depth limit.
/// </summary>
public sealed class DeckItem
{
    public const string DefaultColor = "#33C9DC";

    public required string Id { get; set; }
    public required string PageId { get; set; }

    /// <summary>Containing folder, or null when the item sits at the root of its page.</summary>
    public string? ParentId { get; set; }

    public ItemType Type { get; set; } = ItemType.Button;

    /// <summary>Ordering among siblings; see <see cref="Ordering.FractionalIndex"/>.</summary>
    public required string SortKey { get; set; }

    public int Col { get; set; }
    public int Row { get; set; }
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;

    public string Label { get; set; } = string.Empty;

    /// <summary>Path into the icon store, relative to the data directory. Null when using a glyph.</summary>
    public string? IconPath { get; set; }

    /// <summary>Segoe Fluent Icons glyph, used when no custom image is set.</summary>
    public string? IconGlyph { get; set; }

    public string ColorHex { get; set; } = DefaultColor;

    /// <summary>What pressing the button does. Always null for folders.</summary>
    public ActionPayload? Action { get; set; }

    public static DeckItem NewButton(string pageId, string? parentId, string sortKey, string label) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        PageId = pageId,
        ParentId = parentId,
        SortKey = sortKey,
        Type = ItemType.Button,
        Label = label
    };

    public static DeckItem NewFolder(string pageId, string? parentId, string sortKey, string label) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        PageId = pageId,
        ParentId = parentId,
        SortKey = sortKey,
        Type = ItemType.Folder,
        Label = label
    };
}
