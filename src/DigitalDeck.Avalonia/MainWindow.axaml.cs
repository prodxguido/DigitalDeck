using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DigitalDeck.ViewModels;

namespace DigitalDeck.Avalonia;

/// <summary>
/// The deck itself.
/// </summary>
/// <remarks>
/// What is in this file is the part a view model cannot know: which cell a click landed in, and
/// whether a press on a tile means "run it" or "edit it". Everything else is bound straight to the
/// shared <see cref="MainViewModel"/>.
/// </remarks>
public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private MainViewModel? Deck => DataContext as MainViewModel;

    private static DeckItemViewModel? TileOf(object? sender) => sender switch
    {
        Button { Tag: DeckItemViewModel tile } => tile,
        MenuItem { Tag: DeckItemViewModel tile } => tile,
        _ => null
    };

    /// <summary>
    /// A press on a tile: run it, or open it for editing.
    /// </summary>
    /// <remarks>
    /// Decided here rather than in the view model because it is a question about the shell's own
    /// mode, and because the view model already refuses to run anything while editing — this only
    /// says what to do instead.
    /// </remarks>
    private async void Tile_Click(object? sender, RoutedEventArgs e)
    {
        if (Deck is not { } deck || TileOf(sender) is not { } tile) return;

        if (deck.IsEditMode) await deck.EditItemCommand.ExecuteAsync(tile);
        else await deck.ActivateCommand.ExecuteAsync(tile);
    }

    private async void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (Deck is { } deck && TileOf(sender) is { } tile) await deck.EditItemCommand.ExecuteAsync(tile);
    }

    private async void Duplicate_Click(object? sender, RoutedEventArgs e)
    {
        if (Deck is { } deck && TileOf(sender) is { } tile) await deck.DuplicateItemCommand.ExecuteAsync(tile);
    }

    private async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (Deck is { } deck && TileOf(sender) is { } tile) await deck.DeleteItemCommand.ExecuteAsync(tile);
    }

    /// <summary>
    /// A press on the empty part of the grid, while editing: a new button, there.
    /// </summary>
    /// <remarks>
    /// Only while editing, and only on the panel itself — a press that landed on a tile never
    /// reaches here. Outside edit mode the empty space does nothing, which is what keeps a deck
    /// on a touch screen from growing a button every time somebody rests a hand on it.
    /// </remarks>
    private async void Grid_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Deck is not { IsEditMode: true } deck || sender is not DeckGridPanel panel) return;

        if (panel.CellFromPoint(e.GetPosition(panel)) is not { } cell) return;

        await deck.AddButtonCommand.ExecuteAsync($"{cell.Col},{cell.Row}");
    }
}
