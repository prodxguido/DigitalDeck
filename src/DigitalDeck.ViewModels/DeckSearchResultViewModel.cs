using DigitalDeck.Core.Services;
using DigitalDeck.Data;

namespace DigitalDeck.ViewModels;

/// <summary>
/// One row in the search list: the tile as it looks on the grid, plus where it was found.
/// </summary>
/// <remarks>
/// It reuses <see cref="DeckItemViewModel"/> rather than re-deriving the icon and the colour, so a
/// result is recognisable as the same button the user is picturing.
/// </remarks>
public sealed class DeckSearchResultViewModel
{
    public DeckSearchResultViewModel(DeckSearchHit hit, IconStore icons)
    {
        Hit = hit;
        Tile = new DeckItemViewModel(hit.Item, icons);
    }

    public DeckSearchHit Hit { get; }

    public DeckItemViewModel Tile { get; }

    public string Label => Hit.Item.Label;

    /// <summary>Page and folders the item lives in, shown under the label.</summary>
    public string Location => Hit.Location;

    /// <summary>What the button does, or the word for a folder — the tie-breaker between namesakes.</summary>
    public string Detail => Hit.IsFolder
        ? "Grupo"
        : Hit.Item.Action?.Summary is { Length: > 0 } summary ? summary : "Sin acción";
}
