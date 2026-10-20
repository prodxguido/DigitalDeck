using System.IO;
using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using DigitalDeck.Core;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Services;
using DigitalDeck.Data;
using DigitalDeck.Data.Packs;
using Xunit;

namespace DigitalDeck.App.Tests;

/// <summary>
/// Two settings the user reaches from the shell: opening at login, and the first-run tutorial.
/// </summary>
public sealed class StartupAndOnboardingTests : IDisposable
{
    private readonly string _root;
    private readonly IAppSettingsStore _settings;
    private readonly FakeStartupRegistration _startup = new();
    private readonly MainViewModel _viewModel;

    public StartupAndOnboardingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "digitaldeck-startup-tests", Guid.NewGuid().ToString("n"));

        var paths = new DeckPaths(_root);
        var database = new DeckDatabase(paths);
        database.Migrate();

        var repository = new SqliteDeckRepository(database);
        var icons = new IconStore(paths);
        var usage = new SqliteUsageTracker(database);

        _settings = new SqliteSettingsStore(database);

        var ableton = new FakeAbletonClient();

        _viewModel = new MainViewModel(
            repository,
            new ActionDispatcher(Array.Empty<IActionHandler>()),
            _settings,
            new SilentDialogs(),
            new DeckPackService(repository, icons),
            icons,
            ableton,
            ableton,
            new ImmediateUiThread(),
            new FakeRemoteScriptInstaller(),
            usage,
            new FrequentFolderService(repository, _settings, usage),
            _startup,
            new TemplateLibrary());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* A leftover temp folder is not worth failing a run over. */ }
    }

    // ---------- Opening at login ----------

    [Fact]
    public void TheMenuStartsShowingWhateverWindowsAlreadySays()
    {
        // Read from the system rather than from the app's own settings: the user can also change
        // this from the Task Manager, and a remembered copy would be a second, wrong answer.
        Assert.False(_viewModel.StartsWithWindows);
    }

    [Fact]
    public void TurningItOnRegistersIt()
    {
        _viewModel.StartsWithWindows = true;

        Assert.True(_startup.Read().Enabled);
        Assert.True(_viewModel.StartsWithWindows);
        Assert.Contains("se va a abrir solo", _viewModel.StatusMessage);
    }

    [Fact]
    public void TurningItOffAgainUnregistersIt()
    {
        _viewModel.StartsWithWindows = true;
        _viewModel.StartsWithWindows = false;

        Assert.False(_startup.Read().Enabled);
        Assert.Contains("ya no se abre solo", _viewModel.StatusMessage);
    }

    [Fact]
    public void SettingItToWhatItAlreadyIsTouchesNothing()
    {
        _viewModel.StartsWithWindows = false;

        Assert.Equal(0, _startup.Writes);
    }

    [Fact]
    public void ARefusalFromWindowsLeavesTheTickWhereItWas()
    {
        // Otherwise the menu would sit there claiming the deck opens at login while it does not.
        _startup.Refuse = "Windows no me deja cambiar el arranque automático en esta cuenta.";

        _viewModel.StartsWithWindows = true;

        Assert.False(_viewModel.StartsWithWindows);
        Assert.Equal(_startup.Refuse, _viewModel.StatusMessage);
    }

    // ---------- The tutorial ----------

    [Fact]
    public async Task TheTutorialOpensOnTheFirstRun()
    {
        await _viewModel.InitializeAsync();

        Assert.True(_viewModel.IsOnboardingOpen);
    }

    [Fact]
    public async Task AndDoesNotComeBackOnTheSecond()
    {
        // The bug this covers: it used to be remembered only when a "no volver a mostrar" box was
        // ticked, so reading the tutorial to the end and closing it earned you the same tutorial
        // on the next launch.
        await _viewModel.InitializeAsync();
        _viewModel.CloseOnboardingCommand.Execute(null);

        Assert.Equal("1", await _settings.GetAsync(SettingKeys.OnboardingSeen));

        _viewModel.IsOnboardingOpen = false;
        await _viewModel.InitializeAsync();

        Assert.False(_viewModel.IsOnboardingOpen);
    }

    [Fact]
    public async Task WalkingToTheEndCountsAsHavingSeenIt()
    {
        // Closing with the X and stepping off the last card are the same act, and only one of them
        // used to be remembered.
        await _viewModel.InitializeAsync();

        for (var step = 0; step < _viewModel.OnboardingSteps.Count; step++)
            _viewModel.NextOnboardingStepCommand.Execute(null);

        Assert.False(_viewModel.IsOnboardingOpen);
        Assert.Equal("1", await _settings.GetAsync(SettingKeys.OnboardingSeen));
    }

    [Fact]
    public async Task ItCanStillBeOpenedOnPurposeAfterwards()
    {
        // Which is what makes remembering it safe: the tutorial has its own button in the top bar.
        await _viewModel.InitializeAsync();
        _viewModel.CloseOnboardingCommand.Execute(null);

        _viewModel.ShowOnboardingCommand.Execute(null);

        Assert.True(_viewModel.IsOnboardingOpen);
        Assert.Equal(0, _viewModel.OnboardingStepIndex);
    }
}
