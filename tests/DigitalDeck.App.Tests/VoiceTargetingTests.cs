using System.IO;
using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using DigitalDeck.Core;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Core.Ordering;
using DigitalDeck.Core.Services;
using DigitalDeck.Core.Voice;
using DigitalDeck.Data;
using DigitalDeck.Data.Packs;
using Xunit;

namespace DigitalDeck.App.Tests;

/// <summary>
/// What the assistant is allowed to hear, once it reaches past the folder on screen.
/// </summary>
/// <remarks>
/// Widening the vocabulary to the whole profile is what makes a name enough to reach a button
/// anywhere — and it is also what makes the same name turn up more than once, which the matcher
/// answers by refusing to guess. These are about the rule that keeps that refusal from swallowing
/// the feature: a name belongs to exactly one button, and the one you can see claims it.
/// </remarks>
public sealed class VoiceTargetingTests : IDisposable
{
    private readonly string _root;
    private readonly IDeckRepository _repository;
    private readonly IAppSettingsStore _settings;
    private readonly MainViewModel _viewModel;
    private readonly DeckDatabase _database;

    public VoiceTargetingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "digitaldeck-voice-tests", Guid.NewGuid().ToString("n"));

        var paths = new DeckPaths(_root);
        _database = new DeckDatabase(paths);
        _database.Migrate();

        _repository = new SqliteDeckRepository(_database);

        var icons = new IconStore(paths);
        var settings = new SqliteSettingsStore(_database);
        var usage = new SqliteUsageTracker(_database);

        _settings = settings;

        var ableton = new FakeAbletonClient();

        _viewModel = new MainViewModel(
            _repository,
            new ActionDispatcher([]),
            settings,
            new SilentDialogs(),
            new DeckPackService(_repository, icons),
            icons,
            ableton,
            ableton,
            new ImmediateUiThread(),
            new FakeRemoteScriptInstaller(),
            usage,
            new FrequentFolderService(_repository, settings, usage),
            new FakeStartupRegistration(),
            new TemplateLibrary());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder left behind is not worth failing a test over.
        }
    }

    [Fact]
    public async Task AButtonOnAnotherPageCanStillBeNamed()
    {
        await StartAsync();

        var page = await SecondPageAsync();
        await AddAsync(page.Id, null, "Mastering");

        var targets = await _viewModel.PrepareVoiceTargetsAsync();

        Assert.Contains(targets, t => t.Label == "Mastering");
    }

    [Fact]
    public async Task AButtonInsideAFolderCanStillBeNamed()
    {
        await StartAsync();

        var page = _viewModel.SelectedPage!;
        var folder = await AddFolderAsync(page.Id, "Bateria");
        await AddAsync(page.Id, folder.Id, "Compresor de bombo");

        var targets = await _viewModel.PrepareVoiceTargetsAsync();

        Assert.Contains(targets, t => t.Label == "Compresor de bombo");
    }

    /// <remarks>
    /// The rule the whole widening rests on. Without it the matcher sees a tie, refuses, and the
    /// name stops working the moment a second button anywhere is called the same.
    /// </remarks>
    [Fact]
    public async Task ANameThatExistsTwiceIsClaimedByTheOneOnScreen()
    {
        await StartAsync();

        var here = _viewModel.SelectedPage!;
        var visible = await AddAsync(here.Id, null, "Reverb");

        var elsewhere = await SecondPageAsync();
        await AddAsync(elsewhere.Id, null, "Reverb");

        // Re-selecting the page it is already on is what puts the new tile on screen, which is
        // what makes this a test about something visible.
        _viewModel.SelectedPage = null;
        _viewModel.SelectedPage = here;
        await _viewModel.SettleAsync();

        var targets = await _viewModel.PrepareVoiceTargetsAsync();

        var reverbs = targets.Where(t => t.Label == "Reverb").ToList();

        Assert.Single(reverbs);
        Assert.Equal(visible.Id, reverbs[0].Id);
    }

    /// <remarks>
    /// The favourites folder is built from copies of tiles that live elsewhere, so a profile with
    /// duplicate names is the normal case rather than a mistake to be prevented.
    /// </remarks>
    [Fact]
    public async Task EveryNameAppearsOnlyOnceHoweverManyTilesCarryIt()
    {
        await StartAsync();

        var page = _viewModel.SelectedPage!;

        foreach (var _ in Enumerable.Range(0, 4))
            await AddAsync(page.Id, null, "Serum");

        var targets = await _viewModel.PrepareVoiceTargetsAsync();

        Assert.Single(targets, t => t.Label == "Serum");
    }

    /// <remarks>
    /// Navigating rather than firing from where we stand: it leaves the user looking at the button
    /// that just ran, which is the only way they learn where it lives.
    /// </remarks>
    [Fact]
    public async Task PressingAButtonOnAnotherPageGoesThereFirst()
    {
        await StartAsync();

        var elsewhere = await SecondPageAsync();
        var far = await AddAsync(elsewhere.Id, null, "Mastering");

        var targets = await _viewModel.PrepareVoiceTargetsAsync();
        Assert.Contains(targets, t => t.Id == far.Id);

        await _viewModel.ActivateByIdAsync(far.Id);
        await _viewModel.SettleAsync();

        Assert.Equal(elsewhere.Id, _viewModel.SelectedPage?.Id);
        Assert.Contains(_viewModel.Items, i => i.Id == far.Id);
    }

    [Fact]
    public async Task SayingAFolderOnAnotherPageOpensIt()
    {
        await StartAsync();

        var elsewhere = await SecondPageAsync();
        var folder = await AddFolderAsync(elsewhere.Id, "Sintes");
        await AddAsync(elsewhere.Id, folder.Id, "Diva");

        await _viewModel.PrepareVoiceTargetsAsync();
        await _viewModel.ActivateByIdAsync(folder.Id);
        await _viewModel.SettleAsync();

        Assert.Equal(elsewhere.Id, _viewModel.SelectedPage?.Id);
        Assert.Contains(_viewModel.Items, i => i.Label == "Diva");
    }

    [Fact]
    public async Task ATileWithNoNameIsNotSomethingToSay()
    {
        await StartAsync();

        await AddAsync(_viewModel.SelectedPage!.Id, null, "   ");

        var targets = await _viewModel.PrepareVoiceTargetsAsync();

        Assert.DoesNotContain(targets, t => t.Label.Trim().Length == 0);
    }

    // ---------- Names people type ----------

    /// <remarks>
    /// Compared the way the assistant compares rather than as plain text, which is the whole point:
    /// the rule is about two tiles nothing said out loud could tell apart.
    /// </remarks>
    [Theory]
    [InlineData("ESTUDIO")]
    [InlineData("estudio")]
    [InlineData("Estudio")]
    [InlineData("  Estudio  ")]
    public async Task ANameAlreadyInUseIsOfferedToTheEditorAsTaken(string attempt)
    {
        await StartAsync();
        await AddAsync(_viewModel.SelectedPage!.Id, null, "Estudio");

        var taken = await NamesInUseAsync(exceptItemId: null);

        Assert.Contains(VoicePhrase.Normalise(attempt), taken);
    }

    /// <remarks>
    /// A version number is a real difference, and forbidding it would push people into naming
    /// schemes worse than the problem being solved.
    /// </remarks>
    [Fact]
    public async Task NamesThatDifferByMoreThanCaseAreNotTaken()
    {
        await StartAsync();
        await AddAsync(_viewModel.SelectedPage!.Id, null, "Reverb 1");

        var taken = await NamesInUseAsync(exceptItemId: null);

        Assert.DoesNotContain(VoicePhrase.Normalise("Reverb 2"), taken);
    }

    /// <remarks>
    /// Renaming a tile must not trip over the name it already has, or nobody could ever open the
    /// editor on a tile and press save.
    /// </remarks>
    [Fact]
    public async Task ATileDoesNotCollideWithItself()
    {
        await StartAsync();
        var item = await AddAsync(_viewModel.SelectedPage!.Id, null, "Estudio");

        var taken = await NamesInUseAsync(item.Id);

        Assert.DoesNotContain(VoicePhrase.Normalise("Estudio"), taken);
    }

    /// <remarks>
    /// The two paths the rule deliberately does not reach. Favourites is built from copies of tiles
    /// that live elsewhere, so enforcing uniqueness there would empty the folder; a pack arrives
    /// with whatever names it was saved with, so enforcing it there would fail the import. Both
    /// stay allowed — the rule is about the one place a person is choosing a name.
    /// </remarks>
    [Fact]
    public async Task TheFavouritesFolderCanStillCopyNamesThatAlreadyExist()
    {
        await StartAsync();

        var page = _viewModel.SelectedPage!;
        var original = await AddAsync(page.Id, null, "Serum", action: true);
        var folder = await AddFolderAsync(page.Id, "Favoritos");

        await _settings.SetAsync(SettingKeys.FrequentFolderId, folder.Id);

        var usage = new SqliteUsageTracker(_database);
        await usage.RecordAsync(original);

        await new FrequentFolderService(_repository, _settings, usage).RebuildAsync(4, 3);

        var inside = await _repository.GetChildrenAsync(page.Id, folder.Id);

        Assert.Contains(inside, i => i.Label == "Serum");
    }

    // ---------- Setting up a deck ----------

    private async Task StartAsync()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();
    }

    /// <summary>The same question the editor is handed, asked from a test.</summary>
    private async Task<IReadOnlyCollection<string>> NamesInUseAsync(string? exceptItemId)
    {
        var items = await _repository.GetProfileItemsAsync(_viewModel.SelectedProfile!.Id);

        return items
            .Where(item => item.Id != exceptItemId)
            .Select(item => VoicePhrase.Normalise(item.Label))
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task<Page> SecondPageAsync()
    {
        var page = await _repository.CreatePageAsync(_viewModel.SelectedProfile!.Id, "Mezcla", columns: 4, rows: 3);

        _viewModel.Pages.Add(page);

        return page;
    }

    /// <param name="action">
    /// Given only where a test needs the tile to be rankable: the usage tracker keys on what a
    /// button does, so one with no action is never recorded and never reaches the favourites folder.
    /// </param>
    private async Task<DeckItem> AddAsync(string pageId, string? parentId, string label, bool action = false)
    {
        var item = DeckItem.NewButton(pageId, parentId, FractionalIndex.Last(), label);

        if (action) item.Action = new OpenUrlAction { Url = $"https://example.test/{label}" };

        await _repository.AddItemAsync(item);

        return item;
    }

    private async Task<DeckItem> AddFolderAsync(string pageId, string label)
    {
        var item = DeckItem.NewFolder(pageId, null, FractionalIndex.Last(), label);
        await _repository.AddItemAsync(item);

        return item;
    }

}
