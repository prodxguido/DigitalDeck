namespace DigitalDeck.Core.Layout;

/// <summary>
/// Where each cell of the deck's grid lands, given the space the grid was handed.
/// </summary>
/// <remarks>
/// Cells fill the space rather than staying strictly square and leaving bands of dead room down the
/// sides: this app ends up on small fixed screens where every pixel counts, and a page with a
/// handful of buttons is meant to show them big.
/// <para>
/// The arithmetic lives here, in Core, rather than inside a panel, because there is a panel per
/// toolkit and there must not be a layout per toolkit — the same deck on a Mac and on Windows has
/// to put the same button in the same place. What each panel still owns is measuring and arranging
/// its own children; what it asks for is this.
/// </para>
/// </remarks>
/// <param name="CellWidth">The width of one cell.</param>
/// <param name="CellHeight">The height of one cell.</param>
/// <param name="OffsetX">Left edge of the first column, once the frame and any slack are taken.</param>
/// <param name="OffsetY">Top edge of the first row.</param>
/// <param name="Gap">The space between two neighbouring cells.</param>
public readonly record struct DeckGridMetrics(
    double CellWidth,
    double CellHeight,
    double OffsetX,
    double OffsetY,
    double Gap)
{
    /// <summary>What a cell measures when the grid has no size to work from yet.</summary>
    public const double FallbackCellSize = 96;

    /// <summary>The default space between cells.</summary>
    public const double DefaultGap = 10;

    /// <summary>
    /// How far from square a cell may be stretched before the grid stops filling.
    /// </summary>
    /// <remarks>
    /// A backstop, not a look: it only bites when the grid and the screen are so mismatched that a
    /// filled tile would be grotesquely long. The preset sizes never reach it.
    /// </remarks>
    public const double DefaultMaxCellAspect = 2;

    /// <summary>Works out the cell size and where the grid starts, for a given area.</summary>
    public static DeckGridMetrics For(
        double width,
        double height,
        int columns,
        int rows,
        double gap = DefaultGap,
        double maxCellAspect = DefaultMaxCellAspect)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);
        gap = Math.Max(0, gap);

        if (double.IsInfinity(width) || width <= 0)
            width = columns * FallbackCellSize + gap * (columns - 1);

        if (double.IsInfinity(height) || height <= 0)
            height = rows * FallbackCellSize + gap * (rows - 1);

        // The frame is a multiple of the inner gap, so it still reads as part of the same rhythm
        // rather than an arbitrary border. The sides get more room than the top and bottom: that is
        // where the eye lands first, and a deck pinned hard against the screen edge looks unfinished.
        var innerWidth = Math.Max(0, width - 2 * (gap * 3));
        var innerHeight = Math.Max(0, height - 2 * gap);

        // Each axis is filled on its own, so a grid with room to spare grows its tiles to take it.
        var cellW = Math.Max(0, (innerWidth - gap * (columns - 1)) / columns);
        var cellH = Math.Max(0, (innerHeight - gap * (rows - 1)) / rows);

        var maxAspect = Math.Max(1.0, maxCellAspect);

        if (cellW > cellH * maxAspect) cellW = cellH * maxAspect;
        else if (cellH > cellW * maxAspect) cellH = cellW * maxAspect;

        // Whatever slack the backstop leaves is centred, which keeps the frame even on the two
        // sides that still touch.
        var usedWidth = cellW * columns + gap * (columns - 1);
        var usedHeight = cellH * rows + gap * (rows - 1);

        return new DeckGridMetrics(cellW, cellH, (width - usedWidth) / 2, (height - usedHeight) / 2, gap);
    }

    /// <summary>The rectangle a tile occupies, in pixels from the grid's top-left.</summary>
    public (double X, double Y, double Width, double Height) CellRect(
        int col, int row, int colSpan = 1, int rowSpan = 1)
    {
        colSpan = Math.Max(1, colSpan);
        rowSpan = Math.Max(1, rowSpan);

        return (
            OffsetX + col * (CellWidth + Gap),
            OffsetY + row * (CellHeight + Gap),
            SpanWidth(colSpan),
            SpanHeight(rowSpan));
    }

    /// <summary>How wide a tile is that covers <paramref name="colSpan"/> columns.</summary>
    public double SpanWidth(int colSpan)
    {
        colSpan = Math.Max(1, colSpan);

        return Math.Max(0, colSpan * CellWidth + (colSpan - 1) * Gap);
    }

    /// <inheritdoc cref="SpanWidth"/>
    public double SpanHeight(int rowSpan)
    {
        rowSpan = Math.Max(1, rowSpan);

        return Math.Max(0, rowSpan * CellHeight + (rowSpan - 1) * Gap);
    }

    /// <summary>
    /// Keeps a tile's cell inside a grid that may have shrunk under it.
    /// </summary>
    /// <remarks>
    /// A tile whose stored cell is outside the current grid is pinned to the last valid one rather
    /// than drawn off-screen. It overlaps whatever is there, which reads as "too many buttons for
    /// this size" — a problem the person can see and fix — instead of as a button that vanished.
    /// </remarks>
    public static (int Col, int Row) Clamp(
        int col, int row, int colSpan, int rowSpan, int columns, int rows) =>
        (Math.Clamp(col, 0, Math.Max(0, columns - Math.Max(1, colSpan))),
         Math.Clamp(row, 0, Math.Max(0, rows - Math.Max(1, rowSpan))));
}
