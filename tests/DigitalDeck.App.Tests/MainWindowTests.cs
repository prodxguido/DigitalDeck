using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using DigitalDeck.App.Views;
using DigitalDeck.Core;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Services;
using DigitalDeck.Data;
using DigitalDeck.Data.Packs;
using DigitalDeck.Integrations.Voice;
using DigitalDeck.Integrations.Windows;
using Xunit;

using Path = System.IO.Path;

namespace DigitalDeck.App.Tests;

/// <summary>
/// That the deck's own window really builds, and that a tile draws.
/// </summary>
/// <remarks>
/// The main window had no test at all, which made it the largest piece of XAML in the app with
/// nothing watching it: a resource key that stops existing, or a binding renamed on the view model,
/// is a runtime failure the compiler is happy with, and this is the window where that surfaces in
/// front of the user on every launch.
/// <para>
/// It shows the window rather than only constructing it, because the tile template is applied at
/// layout: a template that does not parse is exactly what this is here to catch, and constructing
/// alone never touches it.
/// </para>
/// </remarks>
public sealed class MainWindowTests : IClassFixture<WpfThread>, IDisposable
{
    private readonly WpfThread _ui;
    private readonly string _root;
    private readonly DeckDatabase _database;
    private readonly IDeckRepository _repository;
    private readonly IAppSettingsStore _settings;
    private readonly IconStore _icons;

    public MainWindowTests(WpfThread ui)
    {
        _ui = ui;
        _root = Path.Combine(Path.GetTempPath(), "digitaldeck-window-tests", Guid.NewGuid().ToString("n"));

        var paths = new DeckPaths(_root);
        _database = new DeckDatabase(paths);
        _database.Migrate();

        _repository = new SqliteDeckRepository(_database);
        _settings = new SqliteSettingsStore(_database);
        _icons = new IconStore(paths);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* A leftover temp folder is not worth failing a run over. */ }
        catch (UnauthorizedAccessException) { }
    }

    private MainViewModel BuildViewModel()
    {
        var usage = new SqliteUsageTracker(_database);
        var ableton = new FakeAbletonClient();

        return new MainViewModel(
            _repository,
            new ActionDispatcher(Array.Empty<IActionHandler>()),
            _settings,
            new SilentDialogs(),
            new DeckPackService(_repository, _icons),
            _icons,
            ableton,
            ableton,
            new ImmediateUiThread(),
            new FakeRemoteScriptInstaller(),
            usage,
            new FrequentFolderService(_repository, _settings, usage),
            new FakeStartupRegistration(),
            new TemplateLibrary());
    }

    /// <summary>Every tile currently on the grid, whatever it is bound to.</summary>
    private static IReadOnlyList<ContentPresenter> Tiles(DependencyObject root)
    {
        var found = new List<ContentPresenter>();

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is ContentPresenter { Content: DeckItemViewModel } presenter) found.Add(presenter);

            found.AddRange(Tiles(child));
        }

        return found;
    }

    [Fact]
    public async Task TheDeckWindowBuildsAndPutsItsButtonsOnTheGrid()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();

        var labels = await _ui.Run(async () =>
        {
            var viewModel = BuildViewModel();

            using var voice = new VoiceService(
                _settings, new DeckPaths(_root), new WindowsMonitors(), new WindowsWindowEffects(),
                new WindowsGlobalHotkey(), new EmbeddedVoiceModelInstaller());

            var window = new MainWindow(
                viewModel,
                new WindowPlacementService(new WindowsMonitors(), _settings),
                voice,
                new WindowsWindowEffects());

            try
            {
                window.ShowInTaskbar = false;
                window.Show();

                await viewModel.InitializeAsync();
                await viewModel.SettleAsync();

                // Let the grid lay out, which is when the tile template is applied.
                window.UpdateLayout();

                return Tiles(window)
                    .Select(tile => ((DeckItemViewModel)tile.Content).Label)
                    .ToArray();
            }
            finally
            {
                window.Close();
            }
        });

        // The seeded deck opens on a page that has buttons; which ones is the seeder's business.
        Assert.NotEmpty(labels);
    }
}
