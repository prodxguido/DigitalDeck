using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using DigitalDeck.Core;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Services;
using DigitalDeck.Data;
using DigitalDeck.Data.Packs;
using DigitalDeck.ViewModels;
using Xunit;

namespace DigitalDeck.Avalonia.Tests;

/// <summary>
/// Saves a picture of the deck, so somebody can look at it.
/// </summary>
/// <remarks>
/// Headless renders for real, so the frame it hands back is what the window looks like. Only runs
/// when asked, because writing PNGs is not what a test run is for — but on a machine with no
/// interactive session it is the only way to see this at all.
/// <code>
/// $env:DIGITALDECK_SHOT = "1"
/// dotnet test --filter FullyQualifiedName~Screenshot
/// </code>
/// </remarks>
public class Screenshot : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "digitaldeck-shot", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [AvaloniaFact]
    public async Task OfTheDeck()
    {
        if (Environment.GetEnvironmentVariable("DIGITALDECK_SHOT") != "1") return;

        var paths = new DeckPaths(_root);
        var database = new DeckDatabase(paths);
        database.Migrate();

        var repository = new SqliteDeckRepository(database);
        var settings = new SqliteSettingsStore(database);
        var usage = new SqliteUsageTracker(database);
        var icons = new IconStore(paths);

        var deck = new MainViewModel(
            repository,
            new ActionDispatcher(Array.Empty<IActionHandler>()),
            settings,
            new SilentDialogs(),
            new DeckPackService(repository, icons),
            icons,
            new NoAbletonForTests(),
            new NoAbletonForTests(),
            new AvaloniaUiThread(),
            new NoRemoteScriptForTests(),
            usage,
            new FrequentFolderService(repository, settings, usage),
            new NoStartup(),
            new TemplateLibrary());

        await new DeckSeeder(repository, settings).SeedIfEmptyAsync();
        await deck.InitializeAsync();
        await deck.SettleAsync();

        var shell = new MainWindow { DataContext = deck, Width = 1160, Height = 720 };

        shell.Show();
        shell.UpdateLayout();

        var destination = Environment.GetEnvironmentVariable("DIGITALDECK_SHOT_PATH")
                          ?? Path.Combine(Path.GetTempPath(), "digitaldeck-avalonia.png");

        using (var frame = shell.CaptureRenderedFrame())
        using (var file = File.Create(destination))
        {
            frame?.Save(file);
        }

        shell.Close();

        Assert.True(File.Exists(destination), $"no se escribió {destination}");
    }
}
