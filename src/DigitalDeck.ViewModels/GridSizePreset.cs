namespace DigitalDeck.ViewModels;

/// <summary>
/// One quick-pick grid size, offered as a button on the breadcrumb bar.
/// </summary>
/// <remarks>
/// A plain <c>(int, int)</c> tuple would read just as well in code, but its named elements are a
/// compile-time convenience only — WPF's binding engine resolves a path by reflection, and there is
/// no real <c>Columns</c> or <c>Rows</c> property on a <see cref="ValueTuple"/> for it to find. This
/// exists so the buttons in <c>MainWindow.xaml</c> have something to bind to.
/// </remarks>
public sealed record GridSizePreset(int Columns, int Rows)
{
    public string Label => $"{Columns}×{Rows}";

    /// <summary>What <c>SetGridSizeCommand</c> expects as its parameter.</summary>
    public string Parameter => $"{Columns}x{Rows}";
}
