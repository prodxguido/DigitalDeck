using DigitalDeck.Core.Model;

namespace DigitalDeck.Core.Layout;

/// <summary>
/// Placement rules for the tile grid.
/// </summary>
/// <remarks>
/// Deliberately does <em>not</em> gravity-compact the way dashboard grid libraries do. On a deck,
/// an empty cell is a choice — spacing groups apart is how a user makes a layout readable at a
/// glance — so tiles stay exactly where they are put. Collisions are resolved by swapping equally
/// sized tiles or by falling back to the nearest free slot, never by rearranging the whole page.
/// </remarks>
public static class GridLayoutEngine
{
    /// <summary>Sizes a tile can take, in (width, height) cells.</summary>
    public static readonly IReadOnlyList<GridRect> TileSizes = new[]
    {
        new GridRect(0, 0, 1, 1),
        new GridRect(0, 0, 2, 1),
        new GridRect(0, 0, 1, 2),
        new GridRect(0, 0, 2, 2),
        new GridRect(0, 0, 3, 1),
        new GridRect(0, 0, 3, 2)
    };

    public static GridRect BoundsOf(DeckItem item) => new(item.Col, item.Row, item.Width, item.Height);

    /// <summary>The item occupying any cell of <paramref name="target"/>, ignoring the item being moved.</summary>
    public static DeckItem? FindOccupant(
        IEnumerable<DeckItem> siblings,
        GridRect target,
        string? ignoreItemId = null)
    {
        foreach (var sibling in siblings)
        {
            if (sibling.Id == ignoreItemId) continue;
            if (BoundsOf(sibling).Overlaps(target)) return sibling;
        }
        return null;
    }

    /// <summary>True when <paramref name="target"/> is inside the page and hits nothing.</summary>
    public static bool CanPlace(
        IEnumerable<DeckItem> siblings,
        GridRect target,
        int columns,
        int rows,
        string? ignoreItemId = null) =>
        target.FitsWithin(columns, rows) && FindOccupant(siblings, target, ignoreItemId) is null;

    /// <summary>
    /// Resolves a drop. Returns the swap partner when two equally sized tiles trade places, so the
    /// caller can persist both moves; returns null for a plain move into free space.
    /// </summary>
    public static PlacementResult Place(
        IReadOnlyList<DeckItem> siblings,
        DeckItem moving,
        int targetCol,
        int targetRow,
        int columns,
        int rows)
    {
        var target = new GridRect(targetCol, targetRow, moving.Width, moving.Height);

        if (!target.FitsWithin(columns, rows))
            return PlacementResult.Rejected("El botón no entra en esa posición de la grilla.");

        var occupant = FindOccupant(siblings, target, moving.Id);

        if (occupant is null)
            return PlacementResult.Moved(target);

        // Two tiles of the same footprint trade places — the drag gesture most users expect.
        var occupantBounds = BoundsOf(occupant);
        if (occupantBounds.Width == moving.Width && occupantBounds.Height == moving.Height)
        {
            var origin = BoundsOf(moving);
            var others = siblings.Where(s => s.Id != moving.Id && s.Id != occupant.Id).ToList();

            if (CanPlace(others, origin, columns, rows))
                return PlacementResult.Swapped(target, occupant, origin);
        }

        // Different sizes: land on the closest free slot rather than shuffling the page around.
        var nearest = FindNearestFreeSlot(siblings, moving, targetCol, targetRow, columns, rows);

        return nearest is { } slot
            ? PlacementResult.Moved(slot)
            : PlacementResult.Rejected("No hay espacio libre cerca de esa posición.");
    }

    /// <summary>Free slot closest to the requested cell, searched outward by Manhattan distance.</summary>
    public static GridRect? FindNearestFreeSlot(
        IReadOnlyList<DeckItem> siblings,
        DeckItem moving,
        int preferredCol,
        int preferredRow,
        int columns,
        int rows)
    {
        GridRect? best = null;
        var bestDistance = int.MaxValue;

        for (var row = 0; row <= rows - moving.Height; row++)
        {
            for (var col = 0; col <= columns - moving.Width; col++)
            {
                var candidate = new GridRect(col, row, moving.Width, moving.Height);
                if (!CanPlace(siblings, candidate, columns, rows, moving.Id)) continue;

                var distance = Math.Abs(col - preferredCol) + Math.Abs(row - preferredRow);
                if (distance >= bestDistance) continue;

                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>First free slot scanning left-to-right, top-to-bottom — used when adding a tile.</summary>
    public static GridRect? FindFirstFreeSlot(
        IReadOnlyList<DeckItem> siblings,
        int width,
        int height,
        int columns,
        int rows)
    {
        for (var row = 0; row <= rows - height; row++)
        {
            for (var col = 0; col <= columns - width; col++)
            {
                var candidate = new GridRect(col, row, width, height);
                if (CanPlace(siblings, candidate, columns, rows)) return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// Pulls tiles that now fall outside the grid back into it, filling the first free cells
    /// scanning left-to-right, top-to-bottom.
    /// </summary>
    /// <remarks>
    /// For when a page's grid was shrunk under tiles that were already placed. Tiles still inside
    /// the new bounds never move — an empty cell on a deck is a deliberate gap, not slack to be
    /// compacted — so only the stranded ones are pulled in, in reading order. Mutates the
    /// <see cref="DeckItem.Col"/> and <see cref="DeckItem.Row"/> of the tiles it relocates and
    /// returns just those; a tile with nowhere left to land keeps its old spot and is not returned,
    /// leaving the caller to notice the grid is simply too small.
    /// </remarks>
    public static IReadOnlyList<DeckItem> RepackIntoBounds(
        IReadOnlyList<DeckItem> siblings,
        int columns,
        int rows)
    {
        var settled = siblings.Where(s => BoundsOf(s).FitsWithin(columns, rows)).ToList();

        var stranded = siblings
            .Where(s => !BoundsOf(s).FitsWithin(columns, rows))
            .OrderBy(s => s.Row)
            .ThenBy(s => s.Col)
            .ToList();

        var moved = new List<DeckItem>();

        foreach (var item in stranded)
        {
            if (FindFirstFreeSlot(settled, item.Width, item.Height, columns, rows) is not { } slot)
                continue;

            item.Col = slot.Col;
            item.Row = slot.Row;
            settled.Add(item);
            moved.Add(item);
        }

        return moved;
    }

    /// <summary>True when a tile can grow to the given size without displacing anything.</summary>
    public static bool CanResize(
        IReadOnlyList<DeckItem> siblings,
        DeckItem item,
        int width,
        int height,
        int columns,
        int rows) =>
        CanPlace(siblings, new GridRect(item.Col, item.Row, width, height), columns, rows, item.Id);
}

/// <summary>Outcome of a drop.</summary>
public readonly record struct PlacementResult(
    bool Success,
    GridRect Target,
    DeckItem? SwapWith,
    GridRect SwapTarget,
    string? Error)
{
    public static PlacementResult Moved(GridRect target) => new(true, target, null, default, null);

    public static PlacementResult Swapped(GridRect target, DeckItem partner, GridRect partnerTarget) =>
        new(true, target, partner, partnerTarget, null);

    public static PlacementResult Rejected(string error) => new(false, default, null, default, error);
}
