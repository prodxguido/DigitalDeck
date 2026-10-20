using System.IO;
using System.Windows;
using System.Windows.Controls;
using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using DigitalDeck.App.Views;
using DigitalDeck.Core;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Services;
using DigitalDeck.Data;
using DigitalDeck.Data.Packs;
using Xunit;

namespace DigitalDeck.App.Tests;

/// <summary>
/// Installing one of the templates that ship inside the app.
/// </summary>
/// <remarks>
/// The point of the feature is that somebody handed a build has the templates already. What that
/// costs is a path from an embedded resource onto the deck, and every step of it — reading the
/// list, writing the file out, placing what comes out, cleaning up — is here rather than left to
/// be discovered by clicking. Both destinations are covered: the answer to the picker's question
/// is the difference between a tab and a tile, and neither is a fallback for the other.
/// </remarks>
public sealed class InstallTemplatesTests : IDisposable
{
    private readonly string _root;
    private readonly IDeckRepository _repository;
    private readonly TemplateLibrary _library = new();
    private readonly SilentDialogs _dialogs = new();
    private readonly MainViewModel _viewModel;

    public InstallTemplatesTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "digitaldeck-install-tests", Guid.NewGuid().ToString("n"));

        var paths = new DeckPaths(_root);
        var database = new DeckDatabase(paths);
        database.Migrate();

        _repository = new SqliteDeckRepository(database);

        var icons = new IconStore(paths);
        var settings = new SqliteSettingsStore(database);
        var usage = new SqliteUsageTracker(database);

        var ableton = new FakeAbletonClient();

        _viewModel = new MainViewModel(
            _repository,
            new ActionDispatcher(Array.Empty<IActionHandler>()),
            settings,
            _dialogs,
            new DeckPackService(_repository, icons),
            icons,
            ableton,
            ableton,
            new ImmediateUiThread(),
            new FakeRemoteScriptInstaller(),
            usage,
            new FrequentFolderService(_repository, settings, usage),
            new FakeStartupRegistration(),
            _library);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* A leftover temp folder is not worth failing a run over. */ }
    }

    private async Task OpenADeckAsync()
    {
        var profile = await _repository.CreateProfileAsync("Prueba", createDefaultPage: true);

        await _viewModel.InitializeAsync();

        _viewModel.SelectedProfile = _viewModel.Profiles.Single(p => p.Id == profile.Id);
    }

    private DeckTemplate Template(string fileName) =>
        _library.List().Single(template => template.FileName == fileName);

    [Fact]
    public async Task TheWholeShelfIsOfferedWithoutHuntingForAFile()
    {
        await OpenADeckAsync();

        await _viewModel.InstallTemplatesCommand.ExecuteAsync(null);

        Assert.NotEmpty(_dialogs.OfferedTemplates);
        Assert.Contains(_dialogs.OfferedTemplates, t => t.FileName == "tareas-basicas.deckpack");
    }

    [Fact]
    public async Task InstallingOnePutsItOnThePageAsAGroup()
    {
        await OpenADeckAsync();
        _dialogs.TargetToChoose = PackImportTarget.CurrentPage;
        _dialogs.TemplatesToChoose = [Template("tareas-basicas.deckpack")];

        await _viewModel.InstallTemplatesCommand.ExecuteAsync(null);

        var tile = Assert.Single(_viewModel.Items);

        Assert.True(tile.IsFolder);
        Assert.Equal("TAREAS", tile.Label);
    }

    [Fact]
    public async Task InstallingATemplateNeverCreatesAProfile()
    {
        // The whole point of a template is that it lands next to what you are already using. As a
        // profile it would sit behind a switch that takes everything else away to reach it, which
        // is the opposite of a shelf. There are two importers in the app and only one of them is
        // right here; this is what stops the wrong one being wired up by accident.
        await OpenADeckAsync();

        var profilesBefore = (await _repository.GetProfilesAsync()).Count;
        var pagesBefore = (await _repository.GetPagesAsync(_viewModel.SelectedProfile!.Id)).Count;

        _dialogs.TargetToChoose = PackImportTarget.CurrentPage;
        _dialogs.TemplatesToChoose = [Template("tareas-basicas.deckpack"), Template("valhalla.deckpack")];

        await _viewModel.InstallTemplatesCommand.ExecuteAsync(null);

        Assert.Equal(profilesBefore, (await _repository.GetProfilesAsync()).Count);
        Assert.Equal(profilesBefore, _viewModel.Profiles.Count);

        // Nor a page: this one was asked for as a group, and a group that quietly added a tab as
        // well would be just as surprising as one that added a profile.
        Assert.Equal(pagesBefore, (await _repository.GetPagesAsync(_viewModel.SelectedProfile.Id)).Count);
    }

    [Fact]
    public async Task ItLandsOnThePageYouAreLookingAt()
    {
        await OpenADeckAsync();

        var page = _viewModel.SelectedPage!;
        _dialogs.TargetToChoose = PackImportTarget.CurrentPage;
        _dialogs.TemplatesToChoose = [Template("valhalla.deckpack")];

        await _viewModel.InstallTemplatesCommand.ExecuteAsync(null);

        var placed = await _repository.GetChildrenAsync(page.Id, null);

        Assert.Single(placed);
        Assert.Equal(ItemType.Folder, placed[0].Type);
    }

    [Fact]
    public async Task InsideAGroupItLandsInsideThatGroup()
    {
        // "Importar como grupo acá" means acá. Standing inside a folder and installing a template
        // has to put it there, not back at the root of the page.
        await OpenADeckAsync();

        var page = _viewModel.SelectedPage!;
        var folder = DeckItem.NewFolder(page.Id, null, "a", "MIS COSAS");
        await _repository.AddItemAsync(folder);

        // The same press a user makes: Activate on a folder walks into it.
        await _viewModel.InitializeAsync();
        await _viewModel.ActivateCommand.ExecuteAsync(
            _viewModel.Items.Single(i => i.Id == folder.Id));

        _dialogs.TargetToChoose = PackImportTarget.CurrentPage;
        _dialogs.TemplatesToChoose = [Template("tareas-basicas.deckpack")];

        await _viewModel.InstallTemplatesCommand.ExecuteAsync(null);

        var inside = await _repository.GetChildrenAsync(page.Id, folder.Id);

        Assert.Single(inside);
        Assert.Equal("TAREAS", inside[0].Label);

        // And nothing extra at the root beyond the folder that was already there.
        Assert.Single(await _repository.GetChildrenAsync(page.Id, null));
    }

    [Fact]
    public async Task InstallingSeveralPlacesThemAllSideBySide()
    {
        await OpenADeckAsync();
        _dialogs.TargetToChoose = PackImportTarget.CurrentPage;
        _dialogs.TemplatesToChoose =
        [
            Template("tareas-basicas.deckpack"),
            Template("valhalla.deckpack"),
        ];

        await _viewModel.InstallTemplatesCommand.ExecuteAsync(null);

        Assert.Equal(2, _viewModel.Items.Count);

        // Each one takes the first free cell, so nothing lands on top of anything else.
        Assert.Equal(2, _viewModel.Items.Select(i => (i.Col, i.Row)).Distinct().Count());
    }

    [Fact]
    public async Task AsAPageItArrivesWithItsButtonsAlreadyOut()
    {
        // The wrapper folder is how a template travels, not something anybody asked for. Carried
        // onto a page it would make a tab holding one tile, with everything real one press further
        // in — which is the whole cost this answer exists to avoid.
        await OpenADeckAsync();

        var startedOn = _viewModel.SelectedPage!;
        _dialogs.TargetToChoose = PackImportTarget.NewPage;
        _dialogs.TemplatesToChoose = [Template("valhalla.deckpack")];

        await _viewModel.InstallTemplatesCommand.ExecuteAsync(null);
        await _viewModel.SettleAsync();

        var page = Assert.Single(_viewModel.Pages, p => p.Id != startedOn.Id);

        Assert.Equal("Valhalla DSP", page.Name);
        Assert.Same(page, _viewModel.SelectedPage);

        var placed = await _repository.GetChildrenAsync(page.Id, null);

        Assert.Equal(9, placed.Count);
        Assert.All(placed, item => Assert.Equal(ItemType.Button, item.Type));

        // And the page it was installed from is exactly as it was.
        Assert.Empty(await _repository.GetChildrenAsync(startedOn.Id, null));
    }

    [Fact]
    public async Task SeveralAsPagesGetATabEachAndLandOnTheFirst()
    {
        await OpenADeckAsync();

        var startedOn = _viewModel.SelectedPage!;
        var profilesBefore = (await _repository.GetProfilesAsync()).Count;

        _dialogs.TargetToChoose = PackImportTarget.NewPage;
        _dialogs.TemplatesToChoose = [Template("valhalla.deckpack"), Template("waves.deckpack")];

        await _viewModel.InstallTemplatesCommand.ExecuteAsync(null);
        await _viewModel.SettleAsync();

        var added = _viewModel.Pages.Where(p => p.Id != startedOn.Id).ToList();

        Assert.Equal(2, added.Count);
        Assert.Equal(["Valhalla DSP", "Waves"], added.Select(p => p.Name));

        // Installing nine shelves and being left looking at the page you started on gives no sign
        // anything happened; the first of them is where what you just installed begins.
        Assert.Same(added[0], _viewModel.SelectedPage);

        // Pages of this deck, not a deck of their own to go and switch to.
        Assert.Equal(profilesBefore, (await _repository.GetProfilesAsync()).Count);
    }

    [Fact]
    public async Task TheSameTemplateTwiceGetsTwoTabsTellableApart()
    {
        // Tabs are told apart by what they say, so a second «Valhalla DSP» is two tabs to open to
        // find out which is which.
        await OpenADeckAsync();

        var startedOn = _viewModel.SelectedPage!;
        _dialogs.TargetToChoose = PackImportTarget.NewPage;
        _dialogs.TemplatesToChoose = [Template("valhalla.deckpack")];

        await _viewModel.InstallTemplatesCommand.ExecuteAsync(null);
        await _viewModel.InstallTemplatesCommand.ExecuteAsync(null);
        await _viewModel.SettleAsync();

        var added = _viewModel.Pages.Where(p => p.Id != startedOn.Id).Select(p => p.Name).ToList();

        Assert.Equal(["Valhalla DSP", "Valhalla DSP (2)"], added);
    }

    [Fact]
    public async Task CancellingInstallsNothing()
    {
        await OpenADeckAsync();
        _dialogs.TemplatesToChoose = [];

        await _viewModel.InstallTemplatesCommand.ExecuteAsync(null);

        Assert.Empty(_viewModel.Items);
    }

    [Fact]
    public async Task InstallingTwiceLeavesTwoCopiesRatherThanFailing()
    {
        // Nothing here overwrites anything: a template is a shelf, and a second shelf next to the
        // first is a strange thing to want but not an error to refuse.
        await OpenADeckAsync();
        _dialogs.TargetToChoose = PackImportTarget.CurrentPage;
        _dialogs.TemplatesToChoose = [Template("tareas-basicas.deckpack")];

        await _viewModel.InstallTemplatesCommand.ExecuteAsync(null);
        await _viewModel.InstallTemplatesCommand.ExecuteAsync(null);

        Assert.Equal(2, _viewModel.Items.Count);
    }

    [Fact]
    public async Task TheTemporaryFilesAreCleanedUpAfterwards()
    {
        // The pack has to be written to disk to be imported. Leaving those behind would grow a
        // folder in %TEMP% by the size of the whole shelf every time somebody browsed it.
        var scratch = Path.Combine(Path.GetTempPath(), "digitaldeck-plantillas");
        var before = Directory.Exists(scratch) ? Directory.GetDirectories(scratch).Length : 0;

        await OpenADeckAsync();
        _dialogs.TemplatesToChoose = [Template("valhalla.deckpack")];

        await _viewModel.InstallTemplatesCommand.ExecuteAsync(null);

        var after = Directory.Exists(scratch) ? Directory.GetDirectories(scratch).Length : 0;

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task TheButtonsArriveWorking()
    {
        await OpenADeckAsync();
        _dialogs.TemplatesToChoose = [Template("tareas-basicas.deckpack")];

        await _viewModel.InstallTemplatesCommand.ExecuteAsync(null);

        var page = _viewModel.SelectedPage!;
        var everything = await _repository.GetPageItemsAsync(page.Id);
        var buttons = everything.Where(i => i.Type == ItemType.Button).ToList();

        Assert.Equal(5, buttons.Count);
        Assert.All(buttons, button => Assert.Null(button.Action!.Validate()));
    }
}

/// <summary>
/// The question asked of packs picked off disk, which nothing else opens.
/// </summary>
public sealed class ImportFormatWindowTests : IClassFixture<WpfThread>
{
    private readonly WpfThread _ui;

    public ImportFormatWindowTests(WpfThread ui) => _ui = ui;

    private T Read<T>(int packCount, Func<ImportFormatWindow, T> read) =>
        _ui.Run(() =>
        {
            var window = new ImportFormatWindow(packCount);

            try
            {
                window.Show();
                return read(window);
            }
            finally
            {
                window.Close();
            }
        });

    [Fact]
    public void ItOpensOnTheSameAnswerAsTheTemplatePicker()
    {
        // Two screens asking one question have to agree about which answer is the usual one.
        Assert.Equal(PackImportTarget.NewPage, Read(3, window => window.Target));
    }

    [Fact]
    public void TheQuestionSaysHowManyItIsAbout()
    {
        // Picked off disk there is no list on screen to look at, so the count is the only sign of
        // how much this is about to do.
        var several = Read(3, window => ((TextBlock)window.FindName("CountText")!).Text);
        var one = Read(1, window => ((TextBlock)window.FindName("CountText")!).Text);

        Assert.Contains("3 paquetes", several);
        Assert.Contains("Un paquete", one);
    }
}

/// <summary>The picker window itself, which no view-model test would exercise.</summary>
public sealed class TemplatesWindowTests : IClassFixture<WpfThread>
{
    private readonly WpfThread _ui;

    public TemplatesWindowTests(WpfThread ui) => _ui = ui;

    private static IReadOnlyList<DeckTemplate> Three() =>
    [
        new("uno.deckpack", "Uno", 0, 4),
        new("dos.deckpack", "Dos", 3, 40),
        new("tres.deckpack", "Tres", 0, 9),
    ];

    [Fact]
    public void NothingIsTickedToStartWith()
    {
        var chosen = _ui.Run(() =>
        {
            var window = new TemplatesWindow(Three());

            try
            {
                window.Show();
                return window.Chosen;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Empty(chosen);
    }

    [Fact]
    public void TheListShowsHowBigEachOneIs()
    {
        // "40 botones en 3 grupos" is the difference between picking a shelf and gambling on one.
        Assert.Equal("4 botones", Three()[0].Summary);
        Assert.Equal("40 botones en 3 grupos", Three()[1].Summary);
    }

    [Fact]
    public void ThePageAnswerIsTheOneAlreadyTicked()
    {
        // Not a neutral default: these are shelves of a dozen or more plugins, which is a screen.
        // The person who wants one tile instead is the one making the less usual choice.
        var target = _ui.Run(() =>
        {
            var window = new TemplatesWindow(Three());

            try
            {
                window.Show();
                return window.Target;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(PackImportTarget.NewPage, target);
    }

    [Fact]
    public void AVersionWithNoTemplatesSaysSoInsteadOfShowingAnEmptyBox()
    {
        var (visible, canInstall) = _ui.Run(() =>
        {
            var window = new TemplatesWindow([]);

            try
            {
                window.Show();

                var empty = (TextBlock)window.FindName("EmptyText")!;
                var install = (Button)window.FindName("InstallButton")!;

                return (empty.Visibility, install.IsEnabled);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(Visibility.Visible, visible);
        Assert.False(canInstall);
    }
}
