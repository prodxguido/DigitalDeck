using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using DigitalDeck.Avalonia;
using DigitalDeck.Core;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Services;
using DigitalDeck.Data;
using DigitalDeck.Data.Packs;
using DigitalDeck.ViewModels;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(DigitalDeck.Avalonia.Tests.TestApp))]

namespace DigitalDeck.Avalonia.Tests;

/// <summary>The app under a platform with no desktop behind it.</summary>
/// <remarks>
/// Skia rather than the stub renderer, and that is deliberate: with real drawing the frame a
/// window produces can be captured and looked at, which on a machine with no interactive session
/// is the only way to see this UI at all. It also means the tests measure a window that was really
/// rendered rather than one that was only laid out.
/// </remarks>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// That the Avalonia deck really builds, and puts its buttons where the deck says they go.
/// </summary>
/// <remarks>
/// Headless: Avalonia lays everything out against a platform with no window and no screen, which
/// is what makes this runnable on a machine with no interactive session — and what turns a XAML
/// mistake into a failing test instead of a blank window the first time somebody opens the app.
/// <para>
/// The deck it opens is a throwaway one seeded into a temp folder, never the user's own.
/// </para>
/// </remarks>
public class DeckWindowTests : IDisposable
{
    private readonly string _root;
    private readonly DeckDatabase _database;
    private readonly IDeckRepository _repository;
    private readonly IAppSettingsStore _settings;
    private readonly MainViewModel _deck;

    public DeckWindowTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "digitaldeck-avalonia-tests", Guid.NewGuid().ToString("n"));

        var paths = new DeckPaths(_root);
        _database = new DeckDatabase(paths);
        _database.Migrate();

        _repository = new SqliteDeckRepository(_database);
        _settings = new SqliteSettingsStore(_database);

        var usage = new SqliteUsageTracker(_database);
        var icons = new IconStore(paths);

        _deck = new MainViewModel(
            _repository,
            new ActionDispatcher(Array.Empty<IActionHandler>()),
            _settings,
            new SilentDialogs(),
            new DeckPackService(_repository, icons),
            icons,
            new NoAbletonForTests(),
            new NoAbletonForTests(),
            new AvaloniaUiThread(),
            new NoRemoteScriptForTests(),
            usage,
            new FrequentFolderService(_repository, _settings, usage),
            new NoStartup(),
            new TemplateLibrary());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* A leftover temp folder is not worth failing a run over. */ }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<Window> OpenDeck()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _deck.InitializeAsync();
        await _deck.SettleAsync();

        var window = new MainWindow { DataContext = _deck };

        window.Show();
        window.UpdateLayout();

        return window;
    }

    private static IReadOnlyList<Button> Tiles(Visual root) =>
        root.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.DataContext is DeckItemViewModel)
            .ToList();

    [AvaloniaFact]
    public async Task TheWindowBuildsAndPutsItsButtonsOnTheGrid()
    {
        var window = await OpenDeck();

        try
        {
            var tiles = Tiles(window);

            Assert.NotEmpty(tiles);
            Assert.Equal(_deck.Items.Count, tiles.Count);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EveryTileEndsUpWithARealSize()
    {
        // A tile measuring zero is what a broken panel looks like: the window comes up, the labels
        // are all there in the tree, and the person sees an empty grid.
        var window = await OpenDeck();

        try
        {
            Assert.All(Tiles(window), tile =>
            {
                Assert.True(tile.Bounds.Width > 0 && tile.Bounds.Height > 0,
                    $"«{((DeckItemViewModel)tile.DataContext!).Label}» quedó de {tile.Bounds.Width}x{tile.Bounds.Height}");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TilesLandInTheCellTheDeckSavedThemIn()
    {
        // The grid is the whole point of a deck: a button is where it is because somebody put it
        // there, and two tiles in different columns must not come back at the same place.
        var window = await OpenDeck();

        try
        {
            var byColumn = Tiles(window)
                .Select(tile => (Tile: (DeckItemViewModel)tile.DataContext!, tile.Bounds))
                .GroupBy(placed => placed.Tile.Col)
                .OrderBy(group => group.Key)
                .ToList();

            Assert.True(byColumn.Count > 1, "el deck sembrado tendría que ocupar más de una columna");

            // Left to right on screen, in the same order as the columns they are stored in.
            var lefts = byColumn.Select(group => group.First().Bounds.X).ToList();

            Assert.Equal(lefts.OrderBy(x => x), lefts);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task PressingAnEmptyCellWhileEditingAsksForAButtonThere()
    {
        // Lo que convierte un deck que se mira en uno que se arma. La celda sale de dónde cayó el
        // dedo, así que el botón aparece donde la persona apuntó y no en el primer hueco libre.
        var window = await OpenDeck();

        try
        {
            var grid = window.GetVisualDescendants().OfType<DeckGridPanel>().Single();

            Assert.NotNull(grid.CellFromPoint(new Point(grid.Bounds.Width / 2, grid.Bounds.Height / 2)));

            // Fuera de la grilla no hay celda: un click al costado no crea nada.
            Assert.Null(grid.CellFromPoint(new Point(-50, -50)));
            Assert.Null(grid.CellFromPoint(new Point(grid.Bounds.Width + 500, 10)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EachCellOfTheGridAnswersForItself()
    {
        var window = await OpenDeck();

        try
        {
            var grid = window.GetVisualDescendants().OfType<DeckGridPanel>().Single();

            // El centro de la celda (0,0) y el de la (1,0) tienen que dar celdas distintas.
            var metrics = DigitalDeck.Core.Layout.DeckGridMetrics.For(
                grid.Bounds.Width, grid.Bounds.Height, grid.Columns, grid.Rows);

            var first = metrics.CellRect(0, 0);
            var second = metrics.CellRect(1, 0);

            Assert.Equal((0, 0), grid.CellFromPoint(new Point(first.X + first.Width / 2, first.Y + first.Height / 2)));
            Assert.Equal((1, 0), grid.CellFromPoint(new Point(second.X + second.Width / 2, second.Y + second.Height / 2)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SearchingFindsAButtonAnywhereInTheProfile()
    {
        // Mira todas las páginas y todos los grupos, sin importar la profundidad: un botón olvidado
        // cuesta un atajo en vez de una recorrida.
        var window = await OpenDeck();

        try
        {
            await _deck.OpenSearchCommand.ExecuteAsync(null);

            Assert.True(_deck.IsSearchOpen);

            // Algo que el deck sembrado seguro tiene, y que no está en la página de arranque.
            _deck.SearchQuery = "ableton";

            await _deck.SettleAsync();
            window.UpdateLayout();

            Assert.NotEmpty(_deck.SearchResults);
            Assert.All(_deck.SearchResults, result => Assert.NotEmpty(result.Location));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ClosingTheSearchPutsTheGridBack()
    {
        var window = await OpenDeck();

        try
        {
            await _deck.OpenSearchCommand.ExecuteAsync(null);
            Assert.True(_deck.IsSearchOpen);

            _deck.CloseSearchCommand.Execute(null);
            window.UpdateLayout();

            Assert.False(_deck.IsSearchOpen);
            Assert.NotEmpty(Tiles(window));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task OpeningAGroupChangesWhatIsOnTheGrid()
    {
        var window = await OpenDeck();

        try
        {
            var folder = _deck.Items.FirstOrDefault(item => item.IsFolder);

            Assert.NotNull(folder);

            var before = _deck.Items.Select(item => item.Label).ToArray();

            await _deck.ActivateCommand.ExecuteAsync(folder);
            await _deck.SettleAsync();
            window.UpdateLayout();

            Assert.NotEqual(before, _deck.Items.Select(item => item.Label).ToArray());
            Assert.Equal(_deck.Items.Count, Tiles(window).Count);
        }
        finally
        {
            window.Close();
        }
    }
}
