using System.IO;
using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using DigitalDeck.Core;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Data;
using Xunit;

using Path = System.IO.Path;

namespace DigitalDeck.App.Tests;

/// <summary>
/// The Ableton group the app installs with, and the ranking that fills its first folder.
/// </summary>
public class AbletonStarterTests : IDisposable
{
    private readonly string _root;
    private readonly IDeckRepository _repository;
    private readonly IAppSettingsStore _settings;
    private readonly SqliteUsageTracker _usage;
    private readonly FrequentFolderService _frequent;

    public AbletonStarterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "digitaldeck-ableton-tests", Guid.NewGuid().ToString("n"));

        var database = new DeckDatabase(new DeckPaths(_root));
        database.Migrate();

        _repository = new SqliteDeckRepository(database);
        _settings = new SqliteSettingsStore(database);
        _usage = new SqliteUsageTracker(database);
        _frequent = new FrequentFolderService(_repository, _settings, _usage);
    }

    [Fact]
    public async Task TheStarterProfileOpensOnTheAbletonGroup()
    {
        var page = await SeedAsync();

        var root = await _repository.GetChildrenAsync(page.Id, null);
        var ableton = Assert.Single(root, i => i.Label == AbletonEssentials.GroupLabel);

        Assert.Equal(ItemType.Folder, ableton.Type);
        Assert.Equal(0, ableton.Col);
        Assert.Equal(0, ableton.Row);
    }

    [Fact]
    public async Task EverySectionOfLivesBrowserIsAFolderInTheGroup()
    {
        var page = await SeedAsync();
        var group = await GroupAsync(page.Id);

        var children = await _repository.GetChildrenAsync(page.Id, group.Id);

        foreach (var section in AbletonEssentials.Sections)
            Assert.Contains(children, i => i.Label == section.Label && i.Type == ItemType.Folder);

        // Everything fits on the one page a folder is; nothing is seeded off the edge of the grid.
        Assert.All(children, i => Assert.InRange(i.Col, 0, 5));
        Assert.All(children, i => Assert.InRange(i.Row, 0, 3));
    }

    [Fact]
    public async Task TheAudioEffectsComeReadyToLoadAndTheOtherSectionsComeEmpty()
    {
        var page = await SeedAsync();
        var group = await GroupAsync(page.Id);

        var sections = await _repository.GetChildrenAsync(page.Id, group.Id);
        var effectsFolder = sections.Single(i => i.Label == "Efectos de audio");

        var effects = await _repository.GetChildrenAsync(page.Id, effectsFolder.Id);

        Assert.Equal(AbletonEssentials.AudioEffects.Count, effects.Count);
        Assert.All(effects, tile =>
        {
            var action = Assert.IsType<AbletonLoadDeviceAction>(tile.Action);

            Assert.Equal(AbletonBrowserCategories.AudioEffects, action.Category);
            Assert.Equal(tile.Label, action.Name);

            // No URI, and that is the point: Live hands those out and Live has not been asked yet.
            Assert.Null(action.Uri);
            Assert.Null(action.Validate());
        });

        var instruments = sections.Single(i => i.Label == "Instrumentos");
        Assert.Empty(await _repository.GetChildrenAsync(page.Id, instruments.Id));
    }

    [Fact]
    public async Task PressingAButtonRanksWhatItDoesRatherThanWhichTileDidIt()
    {
        var page = await SeedAsync();
        var group = await GroupAsync(page.Id);
        var effectsFolder = (await _repository.GetChildrenAsync(page.Id, group.Id))
            .Single(i => i.Label == "Efectos de audio");

        var effects = await _repository.GetChildrenAsync(page.Id, effectsFolder.Id);
        var reverb = effects.Single(i => i.Label == "Reverb");
        var delay = effects.Single(i => i.Label == "Delay");

        await _usage.RecordAsync(delay);
        await _usage.RecordAsync(reverb);
        await _usage.RecordAsync(reverb);

        // A second tile doing the same thing feeds the same counter, which is what keeps a copy
        // in the "most used" folder from competing with the button it was copied from.
        var copy = DeckItem.NewButton(page.Id, null, "a1", "Reverb (copia)");
        copy.Action = new AbletonLoadDeviceAction
        {
            Name = "Reverb",
            Category = AbletonBrowserCategories.AudioEffects
        };

        await _usage.RecordAsync(copy);

        var top = await _usage.GetTopAsync(10);

        Assert.Equal(2, top.Count);
        Assert.Equal(3, top[0].Uses);
        Assert.Equal("Delay", top[1].Label);
    }

    [Fact]
    public async Task OpeningTheMostUsedFolderFillsItWithTheRanking()
    {
        var page = await SeedAsync();
        var group = await GroupAsync(page.Id);
        var effectsFolder = (await _repository.GetChildrenAsync(page.Id, group.Id))
            .Single(i => i.Label == "Efectos de audio");

        var effects = await _repository.GetChildrenAsync(page.Id, effectsFolder.Id);
        var saturator = effects.Single(i => i.Label == "Saturator");

        var frequentId = await _frequent.GetFolderIdAsync();
        Assert.NotNull(frequentId);
        Assert.True(await _frequent.IsFrequentFolderAsync(frequentId));

        // Nothing pressed yet: the folder is empty, and rebuilding it does not invent anything.
        await _frequent.RebuildAsync(page.Columns, page.Rows);
        Assert.Empty(await _repository.GetChildrenAsync(page.Id, frequentId!));

        await _usage.RecordAsync(saturator);
        await _frequent.RebuildAsync(page.Columns, page.Rows);

        var tile = Assert.Single(await _repository.GetChildrenAsync(page.Id, frequentId!));

        Assert.Equal("Saturator", tile.Label);
        Assert.Equal(saturator.IconGlyph, tile.IconGlyph);
        Assert.IsType<AbletonLoadDeviceAction>(tile.Action);
    }

    [Fact]
    public async Task DeletingTheMostUsedFolderIsRespected()
    {
        var page = await SeedAsync();
        var frequentId = await _frequent.GetFolderIdAsync();

        await _repository.DeleteItemAsync(frequentId!);
        await _frequent.RebuildAsync(page.Columns, page.Rows);

        Assert.Null(await _frequent.GetFolderIdAsync());
    }

    private async Task<Page> SeedAsync()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();

        var profile = (await _repository.GetProfilesAsync())[0];
        return (await _repository.GetPagesAsync(profile.Id))[0];
    }

    private async Task<DeckItem> GroupAsync(string pageId) =>
        (await _repository.GetChildrenAsync(pageId, null))
        .Single(i => i.Label == AbletonEssentials.GroupLabel);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }
}
