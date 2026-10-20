using Avalonia;
using Avalonia.Controls;
using DigitalDeck.Core.Layout;
using DigitalDeck.ViewModels;

namespace DigitalDeck.Avalonia;

/// <summary>
/// Lays tiles out on the deck's grid, in the cells they were saved in.
/// </summary>
/// <remarks>
/// The twin of the WPF panel, and deliberately thin: where each cell lands is
/// <see cref="DeckGridMetrics"/> in Core, so the same page puts the same button in the same place
/// on both desktops. What is left here is measuring and arranging children, which is the part no
/// two toolkits spell alike.
/// <para>
/// Each child's cell is read off its own view model rather than from attached properties. A tile
/// already knows which cell it is in — that is what the deck stores — and copying it onto the
/// container would be a second copy to keep in step with the first.
/// </para>
/// </remarks>
public sealed class DeckGridPanel : Panel
{
    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<DeckGridPanel, int>(nameof(Columns), 6);

    public static readonly StyledProperty<int> RowsProperty =
        AvaloniaProperty.Register<DeckGridPanel, int>(nameof(Rows), 4);

    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<DeckGridPanel, double>(nameof(Gap), DeckGridMetrics.DefaultGap);

    static DeckGridPanel()
    {
        AffectsMeasure<DeckGridPanel>(ColumnsProperty, RowsProperty, GapProperty);
        AffectsArrange<DeckGridPanel>(ColumnsProperty, RowsProperty, GapProperty);
    }

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public int Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var metrics = Metrics(availableSize.Width, availableSize.Height);

        foreach (var child in Children)
        {
            var tile = TileOf(child);

            child.Measure(new Size(
                metrics.SpanWidth(tile?.Width ?? 1), metrics.SpanHeight(tile?.Height ?? 1)));
        }

        var width = double.IsInfinity(availableSize.Width)
            ? metrics.CellWidth * Math.Max(1, Columns) + metrics.Gap * (Math.Max(1, Columns) + 1)
            : availableSize.Width;

        var height = double.IsInfinity(availableSize.Height)
            ? metrics.CellHeight * Math.Max(1, Rows) + metrics.Gap * (Math.Max(1, Rows) + 1)
            : availableSize.Height;

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var metrics = Metrics(finalSize.Width, finalSize.Height);

        foreach (var child in Children)
        {
            var tile = TileOf(child);

            var colSpan = Math.Max(1, tile?.Width ?? 1);
            var rowSpan = Math.Max(1, tile?.Height ?? 1);

            var (col, row) = DeckGridMetrics.Clamp(
                tile?.Col ?? 0, tile?.Row ?? 0, colSpan, rowSpan, Columns, Rows);

            var (x, y, width, height) = metrics.CellRect(col, row, colSpan, rowSpan);

            child.Arrange(new Rect(x, y, width, height));
        }

        return finalSize;
    }

    /// <summary>
    /// Which cell a point falls in, or null when it is outside the grid.
    /// </summary>
    /// <remarks>
    /// What turns a click on the empty part of a page into "a button here". The gaps between cells
    /// count as the cell to their left, which is what a person means when they aim at a hole.
    /// </remarks>
    public (int Col, int Row)? CellFromPoint(Point point)
    {
        var metrics = Metrics(Bounds.Width, Bounds.Height);

        var stepX = metrics.CellWidth + metrics.Gap;
        var stepY = metrics.CellHeight + metrics.Gap;

        if (stepX <= 0 || stepY <= 0) return null;

        var col = (int)Math.Floor((point.X - metrics.OffsetX) / stepX);
        var row = (int)Math.Floor((point.Y - metrics.OffsetY) / stepY);

        if (col < 0 || row < 0 || col >= Columns || row >= Rows) return null;

        return (col, row);
    }

    private DeckGridMetrics Metrics(double width, double height) =>
        DeckGridMetrics.For(width, height, Columns, Rows, Gap);

    private static DeckItemViewModel? TileOf(Control child) => child.DataContext as DeckItemViewModel;
}
