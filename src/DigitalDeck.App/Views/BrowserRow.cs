using System.Windows;
using System.Windows.Media;
using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.App.Views;

/// <summary>
/// One line of the browser picker, ready to bind.
/// </summary>
/// <remarks>
/// The entry it wraps comes from Live and says nothing about how it should look; everything
/// presentational is decided here so the template stays free of converters.
/// </remarks>
public sealed class BrowserRow
{
    /// <summary>Tick: this is the item the button will load.</summary>
    private static readonly string ChosenMark = char.ConvertFromUtf32(0x2713);

    public BrowserRow(
        AbletonBrowserEntry entry,
        bool isChosen,
        bool isSelf = false,
        bool inSearch = false)
    {
        Entry = entry;
        IsChosen = isChosen;
        IsSelf = isSelf;
        InSearch = inSearch;
    }

    public AbletonBrowserEntry Entry { get; }

    public bool IsChosen { get; }

    /// <summary>
    /// The row that stands for the folder you are inside.
    /// </summary>
    /// <remarks>
    /// Live has items that are both loadable and full of presets — a device with its own factory
    /// bank. Walking in would otherwise put the device itself out of reach, so it comes along as
    /// the first row of its own listing.
    /// </remarks>
    public bool IsSelf { get; }

    /// <summary>
    /// Whether this row came out of a search rather than out of a folder.
    /// </summary>
    /// <remarks>
    /// A search reaches anywhere under the category while the path the picker keeps is relative to
    /// its root, so there is no path to walk to from here. Everything a search returns is loadable,
    /// which is the gesture that matters anyway.
    /// </remarks>
    public bool InSearch { get; }

    public string Name => IsSelf ? $"Cargar «{Entry.Name}»" : Entry.Name;

    /// <summary>
    /// Whether there is anything under this row to walk into.
    /// </summary>
    /// <remarks>
    /// Live's own is_folder flag is not the question — it comes back false for devices that are
    /// full of presets — so the script answers this one separately. Measured against Live 12.3,
    /// 44 of the 47 items at the top of Audio Effects have children.
    /// </remarks>
    public bool CanOpen => Entry.HasChildren && !IsSelf && !InSearch;

    /// <summary>Whether this row is something the button could load.</summary>
    public bool CanChoose => Entry.IsLoadable;

    /// <summary>
    /// Opening is a separate control, not the row itself.
    /// </summary>
    /// <remarks>
    /// Almost everything Live lists is loadable and has presets underneath at the same time. If one
    /// click had to mean both, the common case — load this device — would cost two of them, which
    /// is worse than the typed name this picker replaced.
    /// </remarks>
    public Visibility OpenVisibility => CanOpen ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>A folder icon only for rows that are nothing but a way in.</summary>
    public string Glyph => CanOpen && !CanChoose ? DeckGlyphs.Folder : DeckGlyphs.Document;

    public string Tick => IsChosen ? ChosenMark : string.Empty;

    /// <summary>What the row itself does when clicked: choose when it can, otherwise open.</summary>
    public bool ChoosesOnClick => CanChoose;

    /// <summary>The chosen row is the only one worth pulling out of a list of near-identical names.</summary>
    public Brush Emphasis => Resource(IsChosen ? "AccentBrush" : "TextBrush");

    /// <summary>
    /// The URI, on hover.
    /// </summary>
    /// <remarks>
    /// Ugly text, but it is the only thing that tells two identically named presets apart, and
    /// this is the moment the user is choosing between them.
    /// </remarks>
    public string? Tip => Entry.Uri.Length == 0 ? null : Entry.Uri;

    private static Brush Resource(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.White;
}

/// <summary>One step of the path across the top of the picker.</summary>
public sealed class BrowserCrumb
{
    public BrowserCrumb(int depth, string label, bool isLast)
    {
        Depth = depth;
        Label = label;
        IsLast = isLast;
    }

    /// <summary>How many path segments are kept when this crumb is clicked.</summary>
    public int Depth { get; }

    public string Label { get; }

    public bool IsLast { get; }

    /// <summary>The crumb you are already standing on is not a place to go.</summary>
    public bool IsClickable => !IsLast;

    public Visibility SeparatorVisibility => IsLast ? Visibility.Collapsed : Visibility.Visible;
}
