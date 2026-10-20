using DigitalDeck.Core.Layout;
using Xunit;

namespace DigitalDeck.Tests;

/// <summary>
/// Where each cell of the deck's grid lands.
/// </summary>
/// <remarks>
/// This arithmetic used to live inside the WPF panel, where the only way to check it was to open
/// the app and look. It moved to Core when the Avalonia shell needed the same answer, and that is
/// the point worth defending: two panels drawing the same page have to put the same button in the
/// same place, and a rule with two copies is a rule that drifts.
/// </remarks>
public class DeckGridMetricsTests
{
    private const double Gap = 10;

    [Fact]
    public void CellsFillTheSpaceTheGridIsGiven()
    {
        // Six columns of cells plus five gaps between them plus the frame, back to the full width.
        var metrics = DeckGridMetrics.For(800, 500, columns: 6, rows: 4, Gap);

        var used = metrics.CellWidth * 6 + Gap * 5 + 2 * metrics.OffsetX;

        Assert.Equal(800, used, precision: 6);
    }

    [Fact]
    public void TheFrameIsWiderAtTheSidesThanAtTheTop()
    {
        // Deliberate: that is where the eye lands first, and a deck pinned hard against the screen
        // edge looks unfinished.
        var metrics = DeckGridMetrics.For(800, 500, columns: 6, rows: 4, Gap);

        Assert.True(metrics.OffsetX > metrics.OffsetY,
            $"el marco lateral ({metrics.OffsetX}) tendría que ser mayor que el de arriba ({metrics.OffsetY})");
    }

    [Fact]
    public void NeighbouringCellsAreOneGapApart()
    {
        var metrics = DeckGridMetrics.For(800, 500, columns: 6, rows: 4, Gap);

        var first = metrics.CellRect(0, 0);
        var second = metrics.CellRect(1, 0);

        Assert.Equal(first.X + first.Width + Gap, second.X, precision: 6);
    }

    [Fact]
    public void ATileTwoCellsWideCoversBothAndTheGapBetweenThem()
    {
        // Not two cells with a hole in the middle: the gap belongs to the tile once it spans it.
        var metrics = DeckGridMetrics.For(800, 500, columns: 6, rows: 4, Gap);

        Assert.Equal(metrics.CellWidth * 2 + Gap, metrics.SpanWidth(2), precision: 6);
        Assert.Equal(metrics.CellHeight * 3 + Gap * 2, metrics.SpanHeight(3), precision: 6);
    }

    [Fact]
    public void AGridWithRoomToSpareGrowsItsTiles()
    {
        var small = DeckGridMetrics.For(800, 500, columns: 10, rows: 6, Gap);
        var few = DeckGridMetrics.For(800, 500, columns: 4, rows: 3, Gap);

        Assert.True(few.CellWidth > small.CellWidth,
            "una página con pocos botones tiene que mostrarlos más grandes");
    }

    [Fact]
    public void ACellIsNeverStretchedPastTheBackstop()
    {
        // A very wide window with a very tall grid: without the clamp a tile would be drawn
        // grotesquely long. The preset sizes never reach this.
        var metrics = DeckGridMetrics.For(4000, 300, columns: 2, rows: 8, Gap);

        var aspect = metrics.CellWidth / metrics.CellHeight;

        Assert.InRange(aspect, 0, DeckGridMetrics.DefaultMaxCellAspect + 0.001);
    }

    [Fact]
    public void WhateverTheBackstopLeavesOverIsCentred()
    {
        var metrics = DeckGridMetrics.For(4000, 300, columns: 2, rows: 8, Gap);

        var used = metrics.CellWidth * 2 + Gap;

        Assert.Equal((4000 - used) / 2, metrics.OffsetX, precision: 6);
    }

    /// <remarks>
    /// Near the fallback rather than exactly it: the assumed area is the cells and their gaps, and
    /// the frame then comes out of that, so the cell lands a little under. That is the behaviour
    /// the WPF panel always had, and it only shows for the one frame before a real size arrives.
    /// </remarks>
    [Fact]
    public void WithNoSizeYetItFallsBackToASensibleCell()
    {
        // What a panel gets before it has been laid out, and what an infinite measure passes in.
        foreach (var metrics in new[]
                 {
                     DeckGridMetrics.For(0, 0, 6, 4, Gap),
                     DeckGridMetrics.For(double.PositiveInfinity, double.PositiveInfinity, 6, 4, Gap)
                 })
        {
            Assert.InRange(metrics.CellWidth, DeckGridMetrics.FallbackCellSize / 2, DeckGridMetrics.FallbackCellSize);
            Assert.InRange(metrics.CellHeight, DeckGridMetrics.FallbackCellSize / 2, DeckGridMetrics.FallbackCellSize);
        }
    }

    [Fact]
    public void AnEmptyGridIsTreatedAsOneCell()
    {
        // Zero columns is not a page anybody made, but dividing by it is a crash.
        var metrics = DeckGridMetrics.For(800, 500, columns: 0, rows: 0, Gap);

        Assert.True(metrics.CellWidth > 0);
        Assert.True(metrics.CellHeight > 0);
    }

    // ---------- Tiles that no longer fit ----------

    [Fact]
    public void ATileOutsideAShrunkenGridIsPinnedToTheLastCell()
    {
        // Drawn overlapping whatever is there, which reads as "too many buttons for this size" —
        // a problem the person can see and fix — instead of as a button that vanished.
        Assert.Equal((5, 3), DeckGridMetrics.Clamp(9, 7, 1, 1, columns: 6, rows: 4));
    }

    [Fact]
    public void ATileThatSpansIsPinnedByItsFarEdge()
    {
        // A two-wide tile in the last column would hang off the edge; it goes one column left.
        Assert.Equal((4, 2), DeckGridMetrics.Clamp(5, 3, 2, 2, columns: 6, rows: 4));
    }

    [Fact]
    public void ATileThatAlreadyFitsIsLeftWhereItIs()
    {
        Assert.Equal((2, 1), DeckGridMetrics.Clamp(2, 1, 1, 1, columns: 6, rows: 4));
    }

    [Fact]
    public void ATileWiderThanTheWholeGridStillLandsAtTheStart()
    {
        // Negative room to spare: the clamp must not answer a negative column.
        Assert.Equal((0, 0), DeckGridMetrics.Clamp(3, 3, 10, 10, columns: 6, rows: 4));
    }
}
