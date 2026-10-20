using System.Windows;
using System.Windows.Controls;

namespace DigitalDeck.App.Controls;

/// <summary>A horizontally scrolling strip with one or two fixed-height rows.</summary>
public sealed class DockStripPanel : Panel
{
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(int), typeof(DockStripPanel),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsMeasure),
        value => value is int rows && rows is 1 or 2);

    public int Rows { get => (int)GetValue(RowsProperty); set => SetValue(RowsProperty, value); }
    private const double TileWidth = 58;
    private double _rowHeight = 42;

    protected override Size MeasureOverride(Size availableSize)
    {
        _rowHeight = double.IsFinite(availableSize.Height)
            ? Math.Max(32, availableSize.Height / Rows) : 42;
        foreach (UIElement child in InternalChildren)
            child.Measure(new Size(TileWidth, _rowHeight));
        return new Size(Math.Ceiling((double)InternalChildren.Count / Rows) * TileWidth, _rowHeight * Rows);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < InternalChildren.Count; i++)
            InternalChildren[i].Arrange(new Rect(
                i / Rows * TileWidth, i % Rows * _rowHeight, TileWidth, _rowHeight));
        return finalSize;
    }
}
