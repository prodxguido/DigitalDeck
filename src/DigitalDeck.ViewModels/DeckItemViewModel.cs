using CommunityToolkit.Mvvm.ComponentModel;
using DigitalDeck.Core.Model;
using DigitalDeck.Data;

namespace DigitalDeck.ViewModels;

/// <summary>
/// A single tile. Wraps the stored item rather than copying its fields, so there is only ever one
/// source of truth; the view refreshes through <see cref="Refresh"/> after an edit is saved.
/// </summary>
public sealed class DeckItemViewModel : ObservableObject
{
    private readonly IconStore _icons;

    private bool _isSelected;
    private bool _isLit;

    private int _childCount;

    /// <summary>How many items live inside this folder. Always zero for a button.</summary>
    public int ChildCount
    {
        get => _childCount;
        set => SetProperty(ref _childCount, value);
    }

    public DeckItemViewModel(DeckItem model, IconStore icons)
    {
        Model = model;
        _icons = icons;
    }

    public DeckItem Model { get; }

    public string Id => Model.Id;
    public string Label => Model.Label;
    public bool IsFolder => Model.Type == ItemType.Folder;
    public int Col => Model.Col;
    public int Row => Model.Row;
    public int Width => Model.Width;
    public int Height => Model.Height;

    /// <summary>The name or character stored on the item, or the default for its kind.</summary>
    public string IconName => Model.IconGlyph is { Length: > 0 } glyph
        ? glyph
        : IsFolder ? DeckIconPaths.DefaultFolder : DeckIconPaths.DefaultButton;

    /// <summary>
    /// The icon's name when it is one of the app's own, or null when it is a legacy font character.
    /// </summary>
    /// <remarks>
    /// Null is not a failure: decks built before the icon set existed store a character from the
    /// Windows icon font, and those keep rendering through <see cref="Glyph"/>. Nobody's deck
    /// changes appearance because the app was updated. The view turns the name into a drawing —
    /// which drawing type depends on the toolkit, and this class does not know one.
    /// </remarks>
    public string? DrawnIconName => DeckIconPaths.Has(IconName) ? IconName : null;

    /// <summary>Icon character for tiles saved before the icon set existed.</summary>
    public string Glyph => DeckIconPaths.Has(IconName) ? string.Empty : IconName;

    /// <summary>
    /// An icon of the app's own to draw, whatever the tile has stored. Never null.
    /// </summary>
    /// <remarks>
    /// For a shell with no Windows icon font behind it. <see cref="Glyph"/> is only worth drawing
    /// where Segoe exists, and on a Mac it does not: the character comes out as a blank or a tofu
    /// box, so an old deck opens as a grid of unmarked keys. This answers with the nearest icon in
    /// the current set instead, and with the default for the tile's kind when there is no near one
    /// — the same default a tile that never had an icon gets, so nothing ever draws as nothing.
    /// <para>
    /// The WPF app keeps using <see cref="DrawnIconName"/> and <see cref="Glyph"/>, because on
    /// Windows the stored character still draws exactly as the person who chose it saw it.
    /// </para>
    /// </remarks>
    public string DrawableIconName =>
        DrawnIconName
        ?? LegacyGlyphs.EquivalentOf(IconName)
        ?? (IsFolder ? DeckIconPaths.DefaultFolder : DeckIconPaths.DefaultButton);

    /// <summary>The tile's colour as the user picked it, <c>#RRGGBB</c>.</summary>
    /// <remarks>
    /// The text rather than a brush, for the same reason as <see cref="DrawnIconName"/>: every
    /// toolkit spells a brush differently and this class is shared by all of them.
    /// </remarks>
    public string ColorHex => Model.ColorHex;

    /// <summary>The user's own image for this tile on disk, or null when it has none.</summary>
    public string? IconPath => Model.IconPath is { Length: > 0 } relative
        ? _icons.GetAbsolutePath(relative)
        : null;

    /// <summary>
    /// Ticked for deletion while «Eliminar varios» is running.
    /// </summary>
    /// <remarks>
    /// It lives on the tile rather than in a list on the view model so the grid can light the tile
    /// up through the ordinary binding it already has, instead of asking the view model about every
    /// tile on every repaint.
    /// </remarks>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// True while Live is in the state this button puts it in — playing, click on, record armed.
    /// </summary>
    /// <remarks>
    /// Set from outside rather than worked out here: the tile knows what its button does, but not
    /// what Live is doing, and giving every tile on screen its own subscription to the bridge would
    /// mean a hundred of them for one set. <see cref="MainViewModel"/> holds the one subscription
    /// and pushes the answer down. Always false for buttons that are not about a state Live
    /// publishes; see <see cref="Core.Services.LiveButtonLight"/>.
    /// </remarks>
    public bool IsLit
    {
        get => _isLit;
        set => SetProperty(ref _isLit, value);
    }

    public string ToolTipText => IsFolder
        ? $"{Label} · grupo"
        : Model.Action?.Summary is { Length: > 0 } summary ? $"{Label}\n{summary}" : Label;

    /// <summary>Re-reads every bound value after the underlying item changed.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);
}
