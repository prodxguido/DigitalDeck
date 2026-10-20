using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DigitalDeck.Core.Layout;

namespace DigitalDeck.App.Controls;

/// <summary>
/// Lays tiles out on a fixed grid whose cells grow to fill the space they are given, framed by a
/// margin keyed to the gap between them — wider at the sides than top and bottom.
/// </summary>
/// <remarks>
/// Cells fill the panel rather than staying strictly square and leaving bands of dead space down
/// the sides — this app ends up on small fixed screens where every pixel counts, and a page with
/// only a handful of buttons is meant to show them big. <see cref="MaxCellAspect"/> is only a
/// backstop against a wildly mismatched grid stretching a tile grotesquely long; the preset sizes
/// never reach it. The panel also draws the cell guides and the drop indicator itself, which keeps
/// the drag interaction from needing an adorner layer on top.
/// </remarks>
public sealed class DeckGridPanel : Panel
{
    private DeckGridMetrics _metrics =
        new(DeckGridMetrics.FallbackCellSize, DeckGridMetrics.FallbackCellSize, 0, 0, 0);

    #region Panel properties

    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(DeckGridPanel),
        new FrameworkPropertyMetadata(6, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(int), typeof(DeckGridPanel),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(DeckGridPanel),
        new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxCellAspectProperty = DependencyProperty.Register(
        nameof(MaxCellAspect), typeof(double), typeof(DeckGridPanel),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ShowCellGuidesProperty = DependencyProperty.Register(
        nameof(ShowCellGuides), typeof(bool), typeof(DeckGridPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DropIndicatorProperty = DependencyProperty.Register(
        nameof(DropIndicator), typeof(Int32Rect?), typeof(DeckGridPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsDropAllowedProperty = DependencyProperty.Register(
        nameof(IsDropAllowed), typeof(bool), typeof(DeckGridPanel),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public int Rows
    {
        get => (int)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    /// <summary>
    /// How far from square a cell may stretch to fill the panel, as the longer side over the
    /// shorter. 1.0 forces strict squares (and brings the side bands back). The default is loose
    /// enough that every preset grid fills the screen on its own, and only steps in when the grid
    /// and the screen are wildly mismatched.
    /// </summary>
    public double MaxCellAspect
    {
        get => (double)GetValue(MaxCellAspectProperty);
        set => SetValue(MaxCellAspectProperty, value);
    }

    /// <summary>Outlines every cell, so empty slots are visible while editing.</summary>
    public bool ShowCellGuides
    {
        get => (bool)GetValue(ShowCellGuidesProperty);
        set => SetValue(ShowCellGuidesProperty, value);
    }

    /// <summary>Cell footprint highlighted during a drag, in grid units.</summary>
    public Int32Rect? DropIndicator
    {
        get => (Int32Rect?)GetValue(DropIndicatorProperty);
        set => SetValue(DropIndicatorProperty, value);
    }

    /// <summary>Colours the drop indicator to show whether releasing here would work.</summary>
    public bool IsDropAllowed
    {
        get => (bool)GetValue(IsDropAllowedProperty);
        set => SetValue(IsDropAllowedProperty, value);
    }

    #endregion

    #region Attached properties

    public static readonly DependencyProperty ColProperty = DependencyProperty.RegisterAttached(
        "Col", typeof(int), typeof(DeckGridPanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty RowProperty = DependencyProperty.RegisterAttached(
        "Row", typeof(int), typeof(DeckGridPanel),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty ColSpanProperty = DependencyProperty.RegisterAttached(
        "ColSpan", typeof(int), typeof(DeckGridPanel),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static readonly DependencyProperty RowSpanProperty = DependencyProperty.RegisterAttached(
        "RowSpan", typeof(int), typeof(DeckGridPanel),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static int GetCol(DependencyObject element) => (int)element.GetValue(ColProperty);
    public static void SetCol(DependencyObject element, int value) => element.SetValue(ColProperty, value);

    public static int GetRow(DependencyObject element) => (int)element.GetValue(RowProperty);
    public static void SetRow(DependencyObject element, int value) => element.SetValue(RowProperty, value);

    public static int GetColSpan(DependencyObject element) => (int)element.GetValue(ColSpanProperty);
    public static void SetColSpan(DependencyObject element, int value) => element.SetValue(ColSpanProperty, value);

    public static int GetRowSpan(DependencyObject element) => (int)element.GetValue(RowSpanProperty);
    public static void SetRowSpan(DependencyObject element, int value) => element.SetValue(RowSpanProperty, value);

    #endregion

    protected override Size MeasureOverride(Size availableSize)
    {
        var metrics = ComputeMetrics(availableSize);

        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(
                metrics.SpanWidth(GetColSpan(child)), metrics.SpanHeight(GetRowSpan(child))));

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
        _metrics = ComputeMetrics(finalSize);

        foreach (UIElement child in InternalChildren)
        {
            var colSpan = Math.Max(1, GetColSpan(child));
            var rowSpan = Math.Max(1, GetRowSpan(child));

            var (col, row) = DeckGridMetrics.Clamp(
                GetCol(child), GetRow(child), colSpan, rowSpan, Columns, Rows);

            child.Arrange(CellRect(col, row, colSpan, rowSpan));
        }

        return finalSize;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (ShowCellGuides)
        {
            var guidePen = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xA7, 0xAE, 0xBB)), 1);
            guidePen.Freeze();

            for (var row = 0; row < Rows; row++)
            for (var col = 0; col < Columns; col++)
            {
                var rect = CellRect(col, row, 1, 1);
                if (rect.Width <= 0 || rect.Height <= 0) continue;

                dc.DrawRoundedRectangle(null, guidePen, Deflate(rect, 0.5), 8, 8);
            }
        }

        if (DropIndicator is not { } indicator) return;

        var accent = IsDropAllowed
            ? Color.FromRgb(0x33, 0xC9, 0xDC)
            : Color.FromRgb(0xE8, 0x61, 0x5F);

        var fill = new SolidColorBrush(Color.FromArgb(0x24, accent.R, accent.G, accent.B));
        var stroke = new Pen(new SolidColorBrush(accent), 2);
        fill.Freeze();
        stroke.Freeze();

        var target = CellRect(indicator.X, indicator.Y, indicator.Width, indicator.Height);
        if (target.Width > 0 && target.Height > 0)
            dc.DrawRoundedRectangle(fill, stroke, Deflate(target, 1), 10, 10);
    }

    /// <summary>Pixel bounds of a cell span, for arranging tiles and drawing indicators.</summary>
    public Rect CellRect(int col, int row, int colSpan, int rowSpan)
    {
        var (x, y, width, height) = _metrics.CellRect(col, row, colSpan, rowSpan);

        return new Rect(x, y, width, height);
    }

    /// <summary>Which cell a point falls in, or null when it is outside the grid.</summary>
    public (int Col, int Row)? CellFromPoint(Point point)
    {
        var stepX = _metrics.CellWidth + _metrics.Gap;
        var stepY = _metrics.CellHeight + _metrics.Gap;
        if (stepX <= 0 || stepY <= 0) return null;

        var col = (int)Math.Floor((point.X - _metrics.OffsetX) / stepX);
        var row = (int)Math.Floor((point.Y - _metrics.OffsetY) / stepY);

        if (col < 0 || row < 0 || col >= Columns || row >= Rows) return null;

        return (col, row);
    }

    private DeckGridMetrics ComputeMetrics(Size available) =>
        DeckGridMetrics.For(available.Width, available.Height, Columns, Rows, Gap, MaxCellAspect);

    private static Rect Deflate(Rect rect, double amount)
    {
        var width = Math.Max(0, rect.Width - amount * 2);
        var height = Math.Max(0, rect.Height - amount * 2);
        return new Rect(rect.X + amount, rect.Y + amount, width, height);
    }
}
