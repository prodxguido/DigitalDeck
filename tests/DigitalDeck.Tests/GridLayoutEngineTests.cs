using DigitalDeck.Core.Layout;
using DigitalDeck.Core.Model;
using Xunit;

namespace DigitalDeck.Tests;

public class GridLayoutEngineTests
{
    private const int Columns = 6;
    private const int Rows = 4;

    private static DeckItem Tile(string id, int col, int row, int width = 1, int height = 1) => new()
    {
        Id = id,
        PageId = "page",
        SortKey = "V",
        Col = col,
        Row = row,
        Width = width,
        Height = height,
        Label = id
    };

    [Fact]
    public void RepackPullsStrandedTilesBackInReadingOrderAndLeavesTheRestAlone()
    {
        var insideA = Tile("a", 0, 0);
        var insideB = Tile("b", 0, 1);
        var strandedFirst = Tile("c", 3, 0);   // outside a 2-wide grid
        var strandedSecond = Tile("d", 5, 0);

        var moved = GridLayoutEngine.RepackIntoBounds(
            new List<DeckItem> { insideA, strandedSecond, insideB, strandedFirst },
            columns: 2, rows: 4);

        // Only the two stranded tiles moved.
        Assert.Equal(new[] { "c", "d" }, moved.Select(m => m.Id));

        // The ones already inside stayed exactly where they were, gaps and all.
        Assert.Equal((0, 0), (insideA.Col, insideA.Row));
        Assert.Equal((0, 1), (insideB.Col, insideB.Row));

        // Stranded tiles filled the first free cells, scanning left-to-right, top-to-bottom.
        Assert.Equal((1, 0), (strandedFirst.Col, strandedFirst.Row));
        Assert.Equal((1, 1), (strandedSecond.Col, strandedSecond.Row));
    }

    [Fact]
    public void RepackLeavesATileStrandedWhenTheGridHasNoRoomLeft()
    {
        var fillsEverything = Tile("big", 0, 0, width: 2, height: 2);
        var stranded = Tile("x", 9, 0);

        var moved = GridLayoutEngine.RepackIntoBounds(
            new List<DeckItem> { fillsEverything, stranded },
            columns: 2, rows: 2);

        Assert.Empty(moved);
        Assert.Equal((9, 0), (stranded.Col, stranded.Row));
    }

    [Fact]
    public void RectanglesOverlapOnlyWhenTheyShareACell()
    {
        Assert.True(new GridRect(0, 0, 2, 2).Overlaps(new GridRect(1, 1, 1, 1)));
        Assert.False(new GridRect(0, 0, 2, 2).Overlaps(new GridRect(2, 0, 1, 1)));
        Assert.False(new GridRect(0, 0, 1, 1).Overlaps(new GridRect(0, 1, 1, 1)));
    }

    [Fact]
    public void MovingToAFreeCellIsAPlainMove()
    {
        var moving = Tile("a", 0, 0);
        var items = new List<DeckItem> { moving, Tile("b", 1, 0) };

        var result = GridLayoutEngine.Place(items, moving, 3, 2, Columns, Rows);

        Assert.True(result.Success);
        Assert.Null(result.SwapWith);
        Assert.Equal(new GridRect(3, 2, 1, 1), result.Target);
    }

    [Fact]
    public void DroppingOnAnEquallySizedTileSwapsThem()
    {
        var moving = Tile("a", 0, 0);
        var other = Tile("b", 3, 2);
        var items = new List<DeckItem> { moving, other };

        var result = GridLayoutEngine.Place(items, moving, 3, 2, Columns, Rows);

        Assert.True(result.Success);
        Assert.Same(other, result.SwapWith);
        Assert.Equal(new GridRect(3, 2, 1, 1), result.Target);
        Assert.Equal(new GridRect(0, 0, 1, 1), result.SwapTarget);
    }

    [Fact]
    public void DroppingOnADifferentlySizedTileFallsBackToTheNearestFreeSlot()
    {
        var moving = Tile("a", 0, 0);
        var wide = Tile("b", 2, 2, width: 2);
        var items = new List<DeckItem> { moving, wide };

        var result = GridLayoutEngine.Place(items, moving, 2, 2, Columns, Rows);

        Assert.True(result.Success);
        Assert.Null(result.SwapWith);
        Assert.False(new GridRect(2, 2, 2, 1).Overlaps(result.Target));
    }

    [Fact]
    public void ATileCannotHangOffTheEdgeOfThePage()
    {
        var moving = Tile("a", 0, 0, width: 2);
        var items = new List<DeckItem> { moving };

        var result = GridLayoutEngine.Place(items, moving, Columns - 1, 0, Columns, Rows);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void EmptyCellsAreLeftAloneRatherThanCompacted()
    {
        // Spacing is a layout choice on a deck, so nothing should slide up to close a gap.
        var moving = Tile("a", 0, 0);
        var items = new List<DeckItem> { moving, Tile("b", 5, 3) };

        var result = GridLayoutEngine.Place(items, moving, 2, 2, Columns, Rows);

        Assert.Equal(new GridRect(2, 2, 1, 1), result.Target);
        Assert.Equal(5, items[1].Col);
        Assert.Equal(3, items[1].Row);
    }

    [Fact]
    public void FirstFreeSlotScansLeftToRightThenDown()
    {
        var items = new List<DeckItem> { Tile("a", 0, 0), Tile("b", 1, 0) };

        var slot = GridLayoutEngine.FindFirstFreeSlot(items, 1, 1, Columns, Rows);

        Assert.Equal(new GridRect(2, 0, 1, 1), slot);
    }

    [Fact]
    public void FirstFreeSlotIsNullWhenThePageIsFull()
    {
        var items = new List<DeckItem>();
        for (var row = 0; row < Rows; row++)
        for (var col = 0; col < Columns; col++)
            items.Add(Tile($"{col}-{row}", col, row));

        Assert.Null(GridLayoutEngine.FindFirstFreeSlot(items, 1, 1, Columns, Rows));
    }

    [Fact]
    public void ATileCanOnlyGrowIntoFreeSpace()
    {
        var item = Tile("a", 0, 0);
        var blocked = new List<DeckItem> { item, Tile("b", 1, 0) };
        var clear = new List<DeckItem> { item };

        Assert.False(GridLayoutEngine.CanResize(blocked, item, 2, 1, Columns, Rows));
        Assert.True(GridLayoutEngine.CanResize(clear, item, 2, 1, Columns, Rows));
    }
}
