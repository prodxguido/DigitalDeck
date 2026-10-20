using System.IO;
using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using DigitalDeck.Core;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Core.Ordering;
using DigitalDeck.Core.Services;
using DigitalDeck.Data;
using DigitalDeck.Data.Packs;
using Microsoft.Data.Sqlite;
using Xunit;

// WPF brings System.Windows.Shapes.Path into scope, which would otherwise make a bare "Path"
// ambiguous with the file-system one this file means.
using Path = System.IO.Path;

namespace DigitalDeck.App.Tests;

/// <summary>
/// Drives the view model the way the window does, without a window. These cover the wiring between
/// a selection change and what ends up on the grid — the part that a passing storage test says
/// nothing about.
/// </summary>
public class MainViewModelTests : IDisposable
{
    private readonly string _root;
    private readonly IDeckRepository _repository;
    private readonly RecordingDialogs _dialogs = new();
    private readonly FakeRemoteScriptInstaller _remoteScript = new();
    private readonly FakeAbletonClient _ableton = new();
    private readonly MainViewModel _viewModel;
    private readonly IAppSettingsStore _settings;
    private readonly FrequentFolderService _frequent;
    private readonly FakeStartupRegistration _startup;
    private readonly IUsageTracker _usage;

    public MainViewModelTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "digitaldeck-vm-tests", Guid.NewGuid().ToString("n"));

        var paths = new DeckPaths(_root);
        var database = new DeckDatabase(paths);
        database.Migrate();

        _repository = new SqliteDeckRepository(database);

        var icons = new IconStore(paths);
        var settings = new SqliteSettingsStore(database);
        var dispatcher = new ActionDispatcher(Array.Empty<IActionHandler>());
        var usage = new SqliteUsageTracker(database);

        _settings = settings;
        _usage = usage;
        _frequent = new FrequentFolderService(_repository, settings, usage);
        _startup = new FakeStartupRegistration();

        _viewModel = new MainViewModel(
            _repository,
            dispatcher,
            settings,
            _dialogs,
            new DeckPackService(_repository, icons),
            icons,
            _ableton,
            _ableton,
            new ImmediateUiThread(),
            _remoteScript,
            usage,
            _frequent,
            _startup,
            new TemplateLibrary());
    }

    [Fact]
    public async Task StartingUpSelectsAProfileAndShowsItsItems()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();

        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        Assert.Null(_dialogs.LastError);
        Assert.Equal("Estudio", _viewModel.SelectedProfile?.Name);
        Assert.Equal("Inicio", _viewModel.SelectedPage?.Name);
        Assert.Contains(_viewModel.Items, i => i.Label == "PRODUCCIÓN");
        Assert.Single(_viewModel.Breadcrumbs);
        Assert.False(_viewModel.CanGoBack);
    }

    [Fact]
    public async Task PressingAFolderShowsWhatIsInsideIt()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var folder = _viewModel.Items.Single(i => i.Label == "PRODUCCIÓN");

        await _viewModel.ActivateCommand.ExecuteAsync(folder);

        Assert.Null(_dialogs.LastError);
        Assert.Equal(folder.Id, _viewModel.CurrentFolderId);
        Assert.True(_viewModel.CanGoBack);
        Assert.Equal(new[] { "Inicio", "PRODUCCIÓN" }, _viewModel.Breadcrumbs.Select(b => b.Label).ToArray());
        Assert.Equal(new[] { "MIXING", "Splice" }, _viewModel.Items.Select(i => i.Label).OrderBy(l => l).ToArray());
    }

    [Fact]
    public async Task GoingBackReturnsToTheParentFolder()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var folder = _viewModel.Items.Single(i => i.Label == "PRODUCCIÓN");
        await _viewModel.ActivateCommand.ExecuteAsync(folder);
        await _viewModel.GoBackCommand.ExecuteAsync(null);

        Assert.Null(_viewModel.CurrentFolderId);
        Assert.False(_viewModel.CanGoBack);
        Assert.Contains(_viewModel.Items, i => i.Label == "PRODUCCIÓN");
    }

    [Fact]
    public async Task PressingAFolderDoesNothingWhileEditing()
    {
        // In edit mode a press opens the editor instead of navigating, so the grid must stay put.
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        _viewModel.IsEditMode = true;
        var folder = _viewModel.Items.Single(i => i.Label == "PRODUCCIÓN");

        await _viewModel.ActivateCommand.ExecuteAsync(folder);

        Assert.Null(_viewModel.CurrentFolderId);
        Assert.Single(_viewModel.Breadcrumbs);
    }

    [Fact]
    public async Task ImportingATemplateAddsAGroupToThePageInsteadOfANewProfile()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var profilesBefore = _viewModel.Profiles.Count;
        var stayedOn = _viewModel.SelectedProfile;

        _dialogs.OpenFilesAnswer = new[] { TemplateFile("valhalla.deckpack") };

        await _viewModel.ImportGroupCommand.ExecuteAsync(null);

        Assert.Null(_dialogs.LastError);
        Assert.Equal(profilesBefore, _viewModel.Profiles.Count);
        Assert.Same(stayedOn, _viewModel.SelectedProfile);

        // On the grid straight away, not only in the database.
        Assert.Contains(_viewModel.Items, i => i.Label == "VALHALLA" && i.IsFolder);
    }

    [Fact]
    public async Task AddingAFavoritesFolderFillsItRightAway()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        // The seeder already ships with one, inside ABLETON — removed here so this test can prove
        // the creation path itself, from a deck that genuinely has none yet.
        var ableton = _viewModel.Items.Single(i => i.Label == "ABLETON");
        await _viewModel.ActivateCommand.ExecuteAsync(ableton);
        var shipped = _viewModel.Items.Single(i => i.Label == "Favoritos");
        await _repository.DeleteItemAsync(shipped.Id);
        await _settings.RemoveAsync(SettingKeys.FrequentFolderId);
        await _viewModel.GoBackCommand.ExecuteAsync(null);

        // Something to rank. Recorded directly rather than through ActivateCommand: the test
        // dispatcher has no handlers registered, so a real press would fail before ever reaching
        // the usage tracker, and this test is about the folder filling, not about dispatch.
        var starter = _viewModel.Items.First(i => !i.IsFolder);
        await _usage.RecordAsync(starter.Model);

        await _viewModel.AddFrequentFolderCommand.ExecuteAsync(null);

        Assert.Null(_dialogs.LastError);
        var folder = _viewModel.Items.Single(i => i.Label == "Favoritos");
        Assert.True(folder.IsFolder);

        await _viewModel.ActivateCommand.ExecuteAsync(folder);

        Assert.Contains(_viewModel.Items, i => i.Label == starter.Label);
    }

    [Fact]
    public async Task OnlyOneFavoritesFolderCanExistAtATime()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        // The seeded deck already ships with one, inside ABLETON.
        var before = _viewModel.Items.Count;

        await _viewModel.AddFrequentFolderCommand.ExecuteAsync(null);

        Assert.Null(_dialogs.LastError);
        Assert.True(_viewModel.StatusIsError, "status=" + _viewModel.StatusMessage);
        Assert.Contains("Favoritos", _viewModel.StatusMessage);

        // Refused, not silently ignored: nothing new landed on the page.
        Assert.Equal(before, _viewModel.Items.Count);
    }

    [Fact]
    public async Task PickingAPresetResizesThePageInPlace()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var page = _viewModel.SelectedPage!;

        await _viewModel.SetGridSizeCommand.ExecuteAsync("8x5");

        Assert.Null(_dialogs.LastError);
        Assert.Equal(8, _viewModel.Columns);
        Assert.Equal(5, _viewModel.Rows);

        var stored = await _repository.GetPageAsync(page.Id);
        Assert.Equal((8, 5), (stored!.Columns, stored.Rows));
    }

    [Fact]
    public async Task ShrinkingThroughAPresetAsksFirstJustLikeTheFullEditorDoes()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        // The seeded page is 6×4; every button on it sits inside a 4×3 grid already, so shrinking
        // to it does not strand anything — this is purely about the confirmation appearing.
        _dialogs.ConfirmAnswer = false;

        await _viewModel.SetGridSizeCommand.ExecuteAsync("4x3");

        Assert.Equal(6, _viewModel.Columns);
        Assert.Equal(4, _viewModel.Rows);

        _dialogs.ConfirmAnswer = true;
        await _viewModel.SetGridSizeCommand.ExecuteAsync("4x3");

        Assert.Equal(4, _viewModel.Columns);
        Assert.Equal(3, _viewModel.Rows);
    }

    [Fact]
    public async Task PickingTheSizeAlreadyOnScreenDoesNothing()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        await _viewModel.SetGridSizeCommand.ExecuteAsync("6x4");

        // No confirmation, because nothing needed confirming.
        Assert.Null(_dialogs.LastError);
        Assert.Equal(6, _viewModel.Columns);
        Assert.Equal(4, _viewModel.Rows);
    }

    [Fact]
    public async Task DeletingAPageThatIsNotOpenLeavesTheOpenOneAlone()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var open = _viewModel.SelectedPage!;
        var other = await _repository.CreatePageAsync(open.ProfileId, "Otra", open.Columns, open.Rows);
        _viewModel.Pages.Add(other);

        // The context menu on a tab passes that tab's Page directly, without switching to it first.
        await _viewModel.DeletePageCommand.ExecuteAsync(other);

        Assert.Null(_dialogs.LastError);
        Assert.DoesNotContain(other, _viewModel.Pages);
        Assert.Same(open, _viewModel.SelectedPage);
    }

    [Fact]
    public async Task DeletingTheOpenPageMovesToWhatIsLeft()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var open = _viewModel.SelectedPage!;
        var other = await _repository.CreatePageAsync(open.ProfileId, "Otra", open.Columns, open.Rows);
        _viewModel.Pages.Add(other);

        await _viewModel.DeletePageCommand.ExecuteAsync(open);

        Assert.Null(_dialogs.LastError);
        Assert.DoesNotContain(open, _viewModel.Pages);
        Assert.NotSame(open, _viewModel.SelectedPage);
        Assert.NotNull(_viewModel.SelectedPage);
    }

    [Fact]
    public async Task ImportingSeveralPacksAtOnceAddsAllOfThem()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        _dialogs.OpenFilesAnswer = new[]
        {
            TemplateFile("valhalla.deckpack"),
            TemplateFile("d16.deckpack"),
            TemplateFile("soundtoys.deckpack")
        };

        await _viewModel.ImportGroupCommand.ExecuteAsync(null);

        Assert.Null(_dialogs.LastError);
        Assert.False(_viewModel.StatusIsError);
        Assert.Contains("3", _viewModel.StatusMessage);

        Assert.Contains(_viewModel.Items, i => i.Label == "VALHALLA");
        Assert.Contains(_viewModel.Items, i => i.Label == "D16");
        Assert.Contains(_viewModel.Items, i => i.Label == "SOUNDTOYS");

        // Each landed on its own cell, not stacked on top of one another.
        var placed = new[] { "VALHALLA", "D16", "SOUNDTOYS" }
            .Select(label => _viewModel.Items.Single(i => i.Label == label))
            .Select(i => (i.Col, i.Row))
            .ToList();

        Assert.Equal(placed.Count, placed.Distinct().Count());
    }

    [Fact]
    public async Task PacksPickedOffDiskAreAskedAboutAndCanBecomePages()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var startedOn = _viewModel.SelectedPage!;
        var pagesBefore = _viewModel.Pages.Count;

        _dialogs.OpenFilesAnswer = new[]
        {
            TemplateFile("valhalla.deckpack"),
            TemplateFile("d16.deckpack")
        };

        _dialogs.ImportTargetAnswer = PackImportTarget.NewPage;

        await _viewModel.ImportPacksCommand.ExecuteAsync(null);
        await _viewModel.SettleAsync();

        // Asked once, about everything picked — not once per file.
        Assert.Equal(1, _dialogs.ImportTargetsAsked);
        Assert.Equal(2, _dialogs.PacksAskedAbout);

        Assert.Null(_dialogs.LastError);
        Assert.Equal(pagesBefore + 2, _viewModel.Pages.Count);
        Assert.Equal("Valhalla DSP", _viewModel.SelectedPage!.Name);
        Assert.NotSame(startedOn, _viewModel.SelectedPage);

        // Unwrapped: the tab shows the plugins, not one folder to press.
        Assert.Contains(_viewModel.Items, i => i.Label == "VintageVerb");
        Assert.DoesNotContain(_viewModel.Items, i => i.IsFolder);
    }

    [Fact]
    public async Task TheSamePacksCanStillArriveAsAGroupInstead()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var pagesBefore = _viewModel.Pages.Count;

        _dialogs.OpenFilesAnswer = new[] { TemplateFile("valhalla.deckpack") };
        _dialogs.ImportTargetAnswer = PackImportTarget.CurrentPage;

        await _viewModel.ImportPacksCommand.ExecuteAsync(null);
        await _viewModel.SettleAsync();

        Assert.Equal(pagesBefore, _viewModel.Pages.Count);
        Assert.Contains(_viewModel.Items, i => i.Label == "VALHALLA" && i.IsFolder);
    }

    [Fact]
    public async Task BackingOutOfTheQuestionImportsNothing()
    {
        // The question comes after the file picker, so it is the last place to change your mind.
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var pagesBefore = _viewModel.Pages.Count;
        var itemsBefore = _viewModel.Items.Count;

        _dialogs.OpenFilesAnswer = new[] { TemplateFile("valhalla.deckpack") };
        _dialogs.ImportTargetAnswer = null;

        await _viewModel.ImportPacksCommand.ExecuteAsync(null);
        await _viewModel.SettleAsync();

        Assert.Equal(pagesBefore, _viewModel.Pages.Count);
        Assert.Equal(itemsBefore, _viewModel.Items.Count);
    }

    [Fact]
    public async Task OneBadFileInABatchDoesNotStopTheRest()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var notAPack = Path.Combine(Path.GetTempPath(), "no-es-un-paquete.txt");
        await File.WriteAllTextAsync(notAPack, "esto no es un deckpack");

        try
        {
            _dialogs.OpenFilesAnswer = new[] { TemplateFile("valhalla.deckpack"), notAPack };

            await _viewModel.ImportGroupCommand.ExecuteAsync(null);

            Assert.Null(_dialogs.LastError);
            Assert.Contains(_viewModel.Items, i => i.Label == "VALHALLA");

            // Told about it rather than silently dropped, and still not an error dialog for
            // something the user picked by mistake.
            Assert.True(_viewModel.StatusIsError);
            Assert.Contains("no-es-un-paquete.txt", _viewModel.StatusMessage);
        }
        finally
        {
            File.Delete(notAPack);
        }
    }

    [Fact]
    public async Task ImportingIntoAFullPageSaysSoInsteadOfThrowing()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var page = _viewModel.SelectedPage!;
        var taken = _viewModel.Items.Select(i => (i.Col, i.Row)).ToHashSet();

        var filler = new List<DeckItem>();
        for (var row = 0; row < page.Rows; row++)
        for (var col = 0; col < page.Columns; col++)
        {
            if (!taken.Add((col, row))) continue;

            var button = DeckItem.NewButton(page.Id, null, FractionalIndex.Last(), $"{col},{row}");
            button.Col = col;
            button.Row = row;
            filler.Add(button);
        }

        await _repository.AddItemsAsync(filler);
        await _viewModel.SettleAsync();

        _dialogs.OpenFilesAnswer = new[] { TemplateFile("valhalla.deckpack") };
        await _viewModel.ImportGroupCommand.ExecuteAsync(null);

        // A page with no room is something the user can fix, so it is a line in the status bar and
        // not an error dialog about something having gone wrong.
        Assert.Null(_dialogs.LastError);
        Assert.True(_viewModel.StatusIsError);
        Assert.Contains("VALHALLA", _viewModel.StatusMessage);
    }

    [Fact]
    public async Task SearchingFindsAButtonFiledInsideAFolder()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        await _viewModel.OpenSearchCommand.ExecuteAsync(null);
        _viewModel.SearchQuery = "splice";

        var result = Assert.Single(_viewModel.SearchResults);
        Assert.Equal("Splice", result.Label);

        // The path is the point: the button was never on the page the user was looking at.
        Assert.StartsWith("Inicio / PRODUCCIÓN", result.Location);
        Assert.DoesNotContain(_viewModel.Items, i => i.Label == "Splice");
    }

    [Fact]
    public async Task PickingASearchResultOpensTheFolderItLivesIn()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        // Edit mode so the result is only revealed: firing it would need a real action handler.
        _viewModel.IsEditMode = true;

        await _viewModel.OpenSearchCommand.ExecuteAsync(null);
        _viewModel.SearchQuery = "splice";

        await _viewModel.OpenSearchResultCommand.ExecuteAsync(_viewModel.SearchResults[0]);

        Assert.Null(_dialogs.LastError);
        Assert.False(_viewModel.IsSearchOpen);
        Assert.Equal(new[] { "Inicio", "PRODUCCIÓN" }, _viewModel.Breadcrumbs.Select(b => b.Label).ToArray());
        Assert.Contains(_viewModel.Items, i => i.Label == "Splice");
    }

    [Fact]
    public async Task ClosingTheSearchLeavesNothingBehind()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        await _viewModel.OpenSearchCommand.ExecuteAsync(null);
        _viewModel.SearchQuery = "splice";
        _viewModel.CloseSearchCommand.Execute(null);

        Assert.False(_viewModel.IsSearchOpen);
        Assert.Empty(_viewModel.SearchResults);
        Assert.Equal(string.Empty, _viewModel.SearchQuery);
        Assert.False(_viewModel.HasNoSearchResults);
    }

    [Fact]
    public async Task DraggingATileOntoAFreeCellMovesIt()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var tile = _viewModel.Items.Single(i => i.Label == "YouTube");

        var preview = _viewModel.EvaluateDrop(tile, 5, 3);
        Assert.Equal(DropKind.Move, preview.Kind);

        await _viewModel.ApplyDropAsync(tile, preview);

        var stored = await _repository.GetItemAsync(tile.Id);
        Assert.Equal((5, 3), (stored!.Col, stored.Row));
    }

    [Fact]
    public async Task DraggingATileOntoAnotherOfTheSameSizeSwapsThem()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var dragged = _viewModel.Items.Single(i => i.Label == "YouTube");
        var target = _viewModel.Items.Single(i => i.Label == "Documentos");

        var from = (dragged.Col, dragged.Row);
        var to = (target.Col, target.Row);

        var preview = _viewModel.EvaluateDrop(dragged, target.Col, target.Row);
        Assert.Equal(DropKind.Swap, preview.Kind);

        await _viewModel.ApplyDropAsync(dragged, preview);

        var storedDragged = await _repository.GetItemAsync(dragged.Id);
        var storedTarget = await _repository.GetItemAsync(target.Id);

        Assert.Equal(to, (storedDragged!.Col, storedDragged.Row));
        Assert.Equal(from, (storedTarget!.Col, storedTarget.Row));
    }

    [Fact]
    public async Task DraggingATileOntoAFolderFilesItInside()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var dragged = _viewModel.Items.Single(i => i.Label == "YouTube");
        var folder = _viewModel.Items.Single(i => i.Label == "WEB");

        var preview = _viewModel.EvaluateDrop(dragged, folder.Col, folder.Row);
        Assert.Equal(DropKind.IntoFolder, preview.Kind);

        await _viewModel.ApplyDropAsync(dragged, preview);

        var stored = await _repository.GetItemAsync(dragged.Id);

        Assert.Equal(folder.Id, stored!.ParentId);
        Assert.DoesNotContain(_viewModel.Items, i => i.Id == dragged.Id);
    }

    // ---------- Undo / redo ----------

    [Fact]
    public async Task UndoingAMergeIntoAFolderPutsTheTileBackWhereItWas()
    {
        // The exact scenario this whole feature exists for: drop one tile onto a folder, and get
        // it back out without having to open the folder and drag it back by hand.
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var dragged = _viewModel.Items.Single(i => i.Label == "YouTube");
        var folder = _viewModel.Items.Single(i => i.Label == "WEB");
        var from = (dragged.Col, dragged.Row);

        var preview = _viewModel.EvaluateDrop(dragged, folder.Col, folder.Row);
        await _viewModel.ApplyDropAsync(dragged, preview);

        Assert.True(_viewModel.CanUndo);
        Assert.Contains(_viewModel.Items, i => i.Label == "WEB");
        Assert.DoesNotContain(_viewModel.Items, i => i.Label == "YouTube");

        await _viewModel.UndoCommand.ExecuteAsync(null);

        Assert.Null(_dialogs.LastError);
        Assert.False(_viewModel.CanUndo);
        Assert.True(_viewModel.CanRedo);

        var restored = _viewModel.Items.Single(i => i.Label == "YouTube");
        Assert.Null(restored.Model.ParentId);
        Assert.Equal(from, (restored.Col, restored.Row));

        var stored = await _repository.GetItemAsync(dragged.Id);
        Assert.Null(stored!.ParentId);

        await _viewModel.RedoCommand.ExecuteAsync(null);

        Assert.False(_viewModel.CanRedo);
        Assert.DoesNotContain(_viewModel.Items, i => i.Label == "YouTube");

        var restoredFolder = await _repository.GetItemAsync(dragged.Id);
        Assert.Equal(folder.Id, restoredFolder!.ParentId);
    }

    [Fact]
    public async Task UndoingASwapPutsBothTilesBack()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var dragged = _viewModel.Items.Single(i => i.Label == "YouTube");
        var target = _viewModel.Items.Single(i => i.Label == "Documentos");
        var draggedFrom = (dragged.Col, dragged.Row);
        var targetFrom = (target.Col, target.Row);

        var preview = _viewModel.EvaluateDrop(dragged, target.Col, target.Row);
        await _viewModel.ApplyDropAsync(dragged, preview);

        await _viewModel.UndoCommand.ExecuteAsync(null);

        var storedDragged = await _repository.GetItemAsync(dragged.Id);
        var storedTarget = await _repository.GetItemAsync(target.Id);

        Assert.Equal(draggedFrom, (storedDragged!.Col, storedDragged.Row));
        Assert.Equal(targetFrom, (storedTarget!.Col, storedTarget.Row));
    }

    [Fact]
    public async Task UndoingADeleteBringsBackAWholeFolderWithItsContents()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var folder = _viewModel.Items.Single(i => i.Label == "PRODUCCIÓN");
        var subtreeBefore = await _repository.GetSubtreeAsync(folder.Id);

        await _viewModel.DeleteItemCommand.ExecuteAsync(folder);

        Assert.DoesNotContain(_viewModel.Items, i => i.Label == "PRODUCCIÓN");
        Assert.True(_viewModel.CanUndo);

        await _viewModel.UndoCommand.ExecuteAsync(null);

        Assert.Contains(_viewModel.Items, i => i.Label == "PRODUCCIÓN");

        var subtreeAfter = await _repository.GetSubtreeAsync(folder.Id);
        Assert.Equal(subtreeBefore.Count, subtreeAfter.Count);
        Assert.Equal(
            subtreeBefore.Select(i => i.Label).OrderBy(l => l),
            subtreeAfter.Select(i => i.Label).OrderBy(l => l));

        await _viewModel.RedoCommand.ExecuteAsync(null);
        Assert.DoesNotContain(_viewModel.Items, i => i.Label == "PRODUCCIÓN");
    }

    [Fact]
    public async Task UndoingAnAddRemovesTheTileRedoingBringsItBack()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        await _viewModel.AddButtonCommand.ExecuteAsync(null);

        var added = _viewModel.Items.Single(i => i.Label == "Botón");

        await _viewModel.UndoCommand.ExecuteAsync(null);
        Assert.DoesNotContain(_viewModel.Items, i => i.Id == added.Id);
        Assert.Null(await _repository.GetItemAsync(added.Id));

        await _viewModel.RedoCommand.ExecuteAsync(null);
        Assert.Contains(_viewModel.Items, i => i.Id == added.Id);
        Assert.NotNull(await _repository.GetItemAsync(added.Id));
    }

    [Fact]
    public async Task UndoingAnEditRestoresTheOldLabelEvenAfterAFurtherEdit()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var tile = _viewModel.Items.Single(i => i.Label == "YouTube");

        // Two edits in a row, proving the snapshot for the first is not the same mutable object
        // the second edit goes on to change.
        _dialogs.NextLabel = "Primero";
        await _viewModel.EditItemCommand.ExecuteAsync(tile);

        _dialogs.NextLabel = "Segundo";
        await _viewModel.EditItemCommand.ExecuteAsync(tile);

        Assert.Equal("Segundo", _viewModel.Items.Single(i => i.Id == tile.Id).Label);

        await _viewModel.UndoCommand.ExecuteAsync(null);
        Assert.Equal("Primero", _viewModel.Items.Single(i => i.Id == tile.Id).Label);

        await _viewModel.UndoCommand.ExecuteAsync(null);
        Assert.Equal("YouTube", _viewModel.Items.Single(i => i.Id == tile.Id).Label);

        await _viewModel.RedoCommand.ExecuteAsync(null);
        Assert.Equal("Primero", _viewModel.Items.Single(i => i.Id == tile.Id).Label);
    }

    [Fact]
    public async Task DoingSomethingNewAfterAnUndoDropsTheRedoBranch()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var tile = _viewModel.Items.Single(i => i.Label == "YouTube");
        var preview = _viewModel.EvaluateDrop(tile, 5, 3);
        await _viewModel.ApplyDropAsync(tile, preview);

        await _viewModel.UndoCommand.ExecuteAsync(null);
        Assert.True(_viewModel.CanRedo);

        var other = _viewModel.Items.Single(i => i.Label == "Documentos");
        var otherPreview = _viewModel.EvaluateDrop(other, 5, 3);
        await _viewModel.ApplyDropAsync(other, otherPreview);

        Assert.False(_viewModel.CanRedo);
    }

    [Fact]
    public async Task SwitchingProfileClearsTheHistory()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var tile = _viewModel.Items.Single(i => i.Label == "YouTube");
        var preview = _viewModel.EvaluateDrop(tile, 5, 3);
        await _viewModel.ApplyDropAsync(tile, preview);
        Assert.True(_viewModel.CanUndo);

        var second = await _repository.CreateProfileAsync("Otro perfil");
        _viewModel.Profiles.Add(second);
        _viewModel.SelectedProfile = second;
        await _viewModel.SettleAsync();

        Assert.False(_viewModel.CanUndo);
        Assert.False(_viewModel.CanRedo);
    }

    [Fact]
    public async Task SwitchingProfileLoadsThatProfilesGrid()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var other = await _repository.CreateProfileAsync("Mastering");
        var otherPage = (await _repository.GetPagesAsync(other.Id))[0];

        var item = DeckItem.NewButton(otherPage.Id, null, "V", "Solo en Mastering");
        await _repository.AddItemAsync(item);

        _viewModel.Profiles.Add(other);
        _viewModel.SelectProfileCommand.Execute(other);
        await _viewModel.SettleAsync();

        Assert.Null(_dialogs.LastError);
        Assert.Equal("Mastering", _viewModel.SelectedProfile?.Name);
        Assert.Equal(new[] { "Solo en Mastering" }, _viewModel.Items.Select(i => i.Label).ToArray());
    }

    // ---------- What Live is doing, on the buttons ----------

    [Fact]
    public async Task ButtonsArriveLitWithWhatLiveIsAlreadyDoing()
    {
        // Reported before there is a single tile on screen. This is the deck that was opened while
        // the set was already playing: nothing will change in Live to announce it, so a button that
        // waited to be told would stay dark for the rest of the session.
        _ableton.ReportSong(new LiveSongState(IsPlaying: true, MetronomeOn: false));

        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var other = await _repository.CreateProfileAsync("Transporte");
        var page = (await _repository.GetPagesAsync(other.Id))[0];

        var play = DeckItem.NewButton(page.Id, null, "V", "Reproducir");
        play.Action = new AbletonAction { Command = AbletonCommand.TogglePlay };
        await _repository.AddItemAsync(play);

        var click = DeckItem.NewButton(page.Id, null, "W", "Metrónomo");
        click.Action = new AbletonAction { Command = AbletonCommand.ToggleMetronome };
        await _repository.AddItemAsync(click);

        var folder = DeckItem.NewFolder(page.Id, null, "X", "Un grupo");
        await _repository.AddItemAsync(folder);

        _viewModel.Profiles.Add(other);
        _viewModel.SelectProfileCommand.Execute(other);
        await _viewModel.SettleAsync();

        Assert.True(Tile("Reproducir").IsLit);
        Assert.False(Tile("Metrónomo").IsLit);
        Assert.False(Tile("Un grupo").IsLit);
    }

    [Fact]
    public async Task AMuteButtonLightsUpForWhicheverTrackIsSelected()
    {
        _ableton.ReportTracks(
            selected: 1,
            new LiveTrackState(0, Mute: false, Solo: false, Arm: false),
            new LiveTrackState(1, Mute: true, Solo: false, Arm: false));

        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var other = await _repository.CreateProfileAsync("Canales");
        var page = (await _repository.GetPagesAsync(other.Id))[0];

        // The default a button carries: whichever track is selected, not a number.
        var selected = DeckItem.NewButton(page.Id, null, "V", "Mute");
        selected.Action = new AbletonAction { Command = AbletonCommand.ToggleMute };
        await _repository.AddItemAsync(selected);

        var first = DeckItem.NewButton(page.Id, null, "W", "Mute 1");
        first.Action = new AbletonAction { Command = AbletonCommand.ToggleMute, TrackIndex = 0 };
        await _repository.AddItemAsync(first);

        _viewModel.Profiles.Add(other);
        _viewModel.SelectProfileCommand.Execute(other);
        await _viewModel.SettleAsync();

        Assert.True(Tile("Mute").IsLit);
        Assert.False(Tile("Mute 1").IsLit);
    }

    private DeckItemViewModel Tile(string label) =>
        _viewModel.Items.Single(item => item.Label == label);

    // ---------- Deleting several at once ----------

    [Fact]
    public async Task TickingSeveralTilesAndConfirmingRemovesAllOfThem()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        _viewModel.IsEditMode = true;
        _viewModel.StartMultiSelectCommand.Execute(null);

        var youTube = _viewModel.Items.Single(i => i.Label == "YouTube");
        var documents = _viewModel.Items.Single(i => i.Label == "Documentos");

        _viewModel.ToggleSelectionCommand.Execute(youTube);
        _viewModel.ToggleSelectionCommand.Execute(documents);

        Assert.Equal(2, _viewModel.SelectedCount);
        Assert.Equal("Eliminar (2)", _viewModel.DeleteSelectedLabel);

        await _viewModel.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.Null(await _repository.GetItemAsync(youTube.Id));
        Assert.Null(await _repository.GetItemAsync(documents.Id));
        Assert.DoesNotContain(_viewModel.Items, i => i.Id == youTube.Id || i.Id == documents.Id);
        Assert.False(_viewModel.IsMultiSelectMode);
    }

    [Fact]
    public async Task ClickingATickedTileAgainTakesItOutOfTheBatch()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        _viewModel.IsEditMode = true;
        _viewModel.StartMultiSelectCommand.Execute(null);

        var tile = _viewModel.Items.Single(i => i.Label == "YouTube");

        _viewModel.ToggleSelectionCommand.Execute(tile);
        _viewModel.ToggleSelectionCommand.Execute(tile);

        Assert.False(tile.IsSelected);
        Assert.Equal(0, _viewModel.SelectedCount);
        Assert.False(_viewModel.DeleteSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task SayingNoToTheWarningLeavesEveryTileWhereItWas()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        _viewModel.IsEditMode = true;
        _viewModel.StartMultiSelectCommand.Execute(null);
        _viewModel.ToggleSelectionCommand.Execute(_viewModel.Items.Single(i => i.Label == "YouTube"));

        _dialogs.ConfirmAnswer = false;
        await _viewModel.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.Contains(_viewModel.Items, i => i.Label == "YouTube");
        // Still armed, so a mistaken click on the delete button costs nothing but a second look.
        Assert.True(_viewModel.IsMultiSelectMode);
        Assert.Equal(1, _viewModel.SelectedCount);
    }

    [Fact]
    public async Task TheWarningCountsTheFoldersInTheBatch()
    {
        // A folder takes its contents with it, and after ticking a dozen tiles nobody remembers
        // which ones were folders.
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        _viewModel.IsEditMode = true;
        _viewModel.StartMultiSelectCommand.Execute(null);
        _viewModel.ToggleSelectionCommand.Execute(_viewModel.Items.Single(i => i.Label == "PRODUCCIÓN"));
        _viewModel.ToggleSelectionCommand.Execute(_viewModel.Items.Single(i => i.Label == "WEB"));
        _viewModel.ToggleSelectionCommand.Execute(_viewModel.Items.Single(i => i.Label == "YouTube"));

        await _viewModel.DeleteSelectedCommand.ExecuteAsync(null);

        Assert.Contains("3 elementos", _dialogs.LastConfirmMessage);
        Assert.Contains("2 son grupos", _dialogs.LastConfirmMessage);
    }

    [Fact]
    public async Task DeletingAFolderInABatchTakesItsContentsWithIt()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        var folder = _viewModel.Items.Single(i => i.Label == "PRODUCCIÓN");
        var inside = (await _repository.GetChildrenAsync(_viewModel.SelectedPage!.Id, folder.Id)).ToList();
        Assert.NotEmpty(inside);

        _viewModel.IsEditMode = true;
        _viewModel.StartMultiSelectCommand.Execute(null);
        _viewModel.ToggleSelectionCommand.Execute(folder);

        await _viewModel.DeleteSelectedCommand.ExecuteAsync(null);

        foreach (var child in inside)
            Assert.Null(await _repository.GetItemAsync(child.Id));
    }

    [Fact]
    public async Task LeavingEditModeUnticksEverything()
    {
        // Otherwise a tile ticked before stepping out of edit mode would still be armed on the way
        // back in, and the next click would delete more than the user picked.
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        _viewModel.IsEditMode = true;
        _viewModel.StartMultiSelectCommand.Execute(null);

        var tile = _viewModel.Items.Single(i => i.Label == "YouTube");
        _viewModel.ToggleSelectionCommand.Execute(tile);

        _viewModel.ToggleEditModeCommand.Execute(null);

        Assert.False(_viewModel.IsEditMode);
        Assert.False(_viewModel.IsMultiSelectMode);
        Assert.False(tile.IsSelected);
        Assert.Equal(0, _viewModel.SelectedCount);
    }

    [Fact]
    public async Task TickingIsOnlyOfferedWhileEditing()
    {
        await new DeckSeeder(_repository, _settings).SeedIfEmptyAsync();
        await _viewModel.InitializeAsync();
        await _viewModel.SettleAsync();

        Assert.False(_viewModel.CanStartMultiSelect);

        _viewModel.StartMultiSelectCommand.Execute(null);
        Assert.False(_viewModel.IsMultiSelectMode);

        _viewModel.IsEditMode = true;
        Assert.True(_viewModel.CanStartMultiSelect);

        _viewModel.StartMultiSelectCommand.Execute(null);
        Assert.True(_viewModel.IsMultiSelectMode);

        // The button that armed it steps aside once it is armed, leaving Eliminar and Cancelar.
        Assert.False(_viewModel.CanStartMultiSelect);
    }

    // ---------- Ableton indicator ----------

    [Fact]
    public void TheIndicatorStartsOutSayingLiveIsNotThere()
    {
        Assert.Equal(AbletonConnectionState.Disconnected, _viewModel.AbletonState);
        Assert.Equal("Sin Live", _viewModel.AbletonLabel);
    }

    [Fact]
    public void ConnectingToLiveIsReflectedInTheIndicator()
    {
        _ableton.Report(AbletonConnectionState.Connected, "Conectado a Ableton Live 12.3.0", "12.3.0");

        Assert.Equal(AbletonConnectionState.Connected, _viewModel.AbletonState);
        Assert.Equal("Live", _viewModel.AbletonLabel);
        Assert.Contains("12.3.0", _viewModel.AbletonDetail);
    }

    [Theory]
    [InlineData(AbletonConnectionState.Connecting, "Buscando…")]
    [InlineData(AbletonConnectionState.VersionMismatch, "Script viejo")]
    [InlineData(AbletonConnectionState.Unavailable, "Sin puerto")]
    public void EachWayOfNotBeingConnectedGetsItsOwnLabel(AbletonConnectionState state, string expected)
    {
        // Three different problems needing three different fixes — open Live, reinstall the script,
        // free the port — so one shared "disconnected" label would send the user looking in the
        // wrong place.
        _ableton.Report(state, "detalle");

        Assert.Equal(expected, _viewModel.AbletonLabel);
    }

    [Fact]
    public void ClickingTheIndicatorExplainsItselfInTheStatusBar()
    {
        _ableton.Report(AbletonConnectionState.Unavailable, "El puerto 11001 está ocupado.");

        _viewModel.ShowAbletonStatusCommand.Execute(null);

        Assert.Equal("El puerto 11001 está ocupado.", _viewModel.StatusMessage);
        Assert.True(_viewModel.StatusIsError);
    }

    [Fact]
    public void WithNoScriptInstalledTheIndicatorOffersToInstallItInsteadOfExplaining()
    {
        // Telling someone the bridge is down is useless when the reason is that the half of it that
        // runs inside Live was never copied there. That has a fix, and the click is where it lives.
        _remoteScript.Status = RemoteScriptStatus.NotInstalled;
        _ableton.Report(AbletonConnectionState.Disconnected, "Ableton Live no está conectado.");

        _viewModel.ShowAbletonStatusCommand.Execute(null);

        Assert.Equal(1, _dialogs.RemoteScriptSetups);
    }

    [Fact]
    public void WithTheScriptInPlaceTheIndicatorGoesBackToJustExplaining()
    {
        // Live closed, or the port taken: nothing to install, so nothing to open a window for.
        _remoteScript.Status = RemoteScriptStatus.Installed;
        _ableton.Report(AbletonConnectionState.Unavailable, "El puerto 11001 está ocupado.");

        _viewModel.ShowAbletonStatusCommand.Execute(null);

        Assert.Equal(0, _dialogs.RemoteScriptSetups);
        Assert.Equal("El puerto 11001 está ocupado.", _viewModel.StatusMessage);
    }

    [Fact]
    public void LiveSimplyBeingClosedIsNotShownAsAnError()
    {
        // Configuring the deck with Ableton shut is entirely normal; painting it red would be noise.
        _ableton.Report(AbletonConnectionState.Connecting, "Buscando a Ableton Live…");

        _viewModel.ShowAbletonStatusCommand.Execute(null);

        Assert.False(_viewModel.StatusIsError);
    }

    [Fact]
    public void AFailureThatArrivesAfterTheCommandWasSentStillReachesTheStatusBar()
    {
        // Commands are fire and forget, so without this the button would look like it worked.
        _ableton.ReportFailure("Ableton rechazó /live/track/set/mute: there is no track 99");

        Assert.Contains("no track 99", _viewModel.StatusMessage);
        Assert.True(_viewModel.StatusIsError);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Leftover temp files are not worth failing a test over.
        }
    }

    /// <summary>Stands in for the real dialogs and records anything the view model tried to report.</summary>
    /// <summary>Walks up from the test binary to a template in the repository.</summary>
    private static string TemplateFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "plantillas", fileName);
            if (File.Exists(candidate)) return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"No se encontró la plantilla «{fileName}».");
    }

    private sealed class RecordingDialogs : IDeckDialogs
    {
        public string? LastError { get; private set; }

        public string? AskText(string title, string prompt, string? initialValue = null) => initialValue;

        public bool ConfirmAnswer { get; set; } = true;

        public string LastConfirmMessage { get; private set; } = string.Empty;

        public bool Confirm(string title, string message, bool destructive = false)
        {
            LastConfirmMessage = message;
            return ConfirmAnswer;
        }

        public void ShowError(string title, string message) => LastError = $"{title}: {message}";

        public void ShowInfo(string title, string message) { }

        /// <summary>Label the next edit sets, or null to leave whatever the caller already put there.</summary>
        public string? NextLabel { get; set; }

        public bool EditItem(DeckItem item, IReadOnlyCollection<string> takenNames)
        {
            if (NextLabel is not null) item.Label = NextLabel;
            return true;
        }

        public bool EditPage(Page page) => true;

        public int RemoteScriptSetups { get; private set; }

        public bool ShowRemoteScriptSetup()
        {
            RemoteScriptSetups++;
            return false;
        }

        public void ShowVoiceSettings() => VoiceSettingsShown++;


        public TemplateInstall PickTemplates(IReadOnlyList<DeckTemplate> templates) =>
            TemplateInstall.None;

        /// <summary>What the import question answers, or null for a cancelled one.</summary>
        public PackImportTarget? ImportTargetAnswer { get; set; } = PackImportTarget.NewPage;

        public int ImportTargetsAsked { get; private set; }

        public PackImportTarget? AskImportTarget(int packCount)
        {
            ImportTargetsAsked++;
            PacksAskedAbout = packCount;

            return ImportTargetAnswer;
        }

        /// <summary>How many packs the last question was about, which is part of what it says.</summary>
        public int PacksAskedAbout { get; private set; }

        public void ShowMixer() => MixerShown++;

        public int MixerShown { get; private set; }


        public int VoiceSettingsShown { get; private set; }

        public void ShowDuplicateNames() => DuplicateNamesShown++;

        public int DuplicateNamesShown { get; private set; }

        /// <summary>What the next "open" dialog answers with, or null for a cancelled one.</summary>
        public string? OpenFileAnswer { get; set; }

        public string? PickOpenFile(string filter, string title) => OpenFileAnswer;

        /// <summary>What the next multi-file "open" dialog answers with. Empty means cancelled.</summary>
        public IReadOnlyList<string> OpenFilesAnswer { get; set; } = Array.Empty<string>();

        public IReadOnlyList<string> PickOpenFiles(string filter, string title) => OpenFilesAnswer;

        public string? PickSaveFile(string filter, string title, string suggestedName) => null;
    }
}
