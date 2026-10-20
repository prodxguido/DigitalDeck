using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Layout;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Ordering;
using DigitalDeck.Core.Services;
using DigitalDeck.Core.Voice;
using DigitalDeck.Data;
using DigitalDeck.Data.Packs;

namespace DigitalDeck.ViewModels;

/// <summary>What releasing a dragged tile at a given cell would do.</summary>
public enum DropKind
{
    None,
    Move,
    Swap,
    IntoFolder
}

/// <param name="Target">The tile being swapped with, or the folder being dropped into.</param>
public readonly record struct DropPreview(DropKind Kind, int Col, int Row, DeckItemViewModel? Target)
{
    public bool IsAllowed => Kind != DropKind.None;
}

/// <summary>
/// One card of the onboarding tutorial. <paramref name="AutoAdvance"/> steps have no "Siguiente":
/// they wait for the user to actually do the thing they describe, so the tutorial is followed
/// hands-on instead of read as a manual. <paramref name="AnchorTop"/> moves the card near the top
/// bar, where every action but the first and last one lives.
/// </summary>
public sealed record OnboardingStep(string Title, string Body, bool AutoAdvance, bool AnchorTop);

/// <summary>Drives the whole window: profiles, pages, folder navigation and tile editing.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDeckRepository _repository;
    private readonly IActionDispatcher _dispatcher;
    private readonly IAppSettingsStore _settings;
    private readonly IDeckDialogs _dialogs;
    private readonly DeckPackService _packs;
    private readonly IconStore _icons;
    private readonly IAbletonClient _ableton;
    private readonly IAbletonSong _song;
    private readonly IRemoteScriptInstaller _remoteScript;
    private readonly IUsageTracker _usage;
    private readonly FrequentFolderService _frequent;
    private readonly IStartupRegistration _startup;
    private readonly TemplateLibrary _templates;
    /// <summary>How long a status message stays on the bar before it clears itself.</summary>
    private static readonly TimeSpan StatusLinger = TimeSpan.FromSeconds(4);

    private readonly System.Threading.Timer _statusTimer;
    private readonly IUiThread _ui;

    private Task _pending = Task.CompletedTask;

    /// <summary>
    /// Deck edits, most recent first. A profile switch clears both — the ids an edit closes over
    /// still resolve across profiles (they are plain GUIDs), but jumping to a different deck and
    /// then undoing something you did in another one would be bewildering, not helpful.
    /// </summary>
    private readonly Stack<DeckEdit> _undoStack = new();
    private readonly Stack<DeckEdit> _redoStack = new();

    public MainViewModel(
        IDeckRepository repository,
        IActionDispatcher dispatcher,
        IAppSettingsStore settings,
        IDeckDialogs dialogs,
        DeckPackService packs,
        IconStore icons,
        IAbletonClient ableton,
        IAbletonSong song,
        IUiThread ui,
        IRemoteScriptInstaller remoteScript,
        IUsageTracker usage,
        FrequentFolderService frequent,
        IStartupRegistration startup,
        TemplateLibrary templates)
    {
        _repository = repository;
        _dispatcher = dispatcher;
        _settings = settings;
        _dialogs = dialogs;
        _packs = packs;
        _icons = icons;
        _ableton = ableton;
        _song = song;
        _remoteScript = remoteScript;
        _usage = usage;
        _frequent = frequent;
        _startup = startup;
        _templates = templates;
        _ui = ui;

        _startsWithWindows = _startup.Read().Enabled;

        // A plain timer rather than the toolkit's, so nothing here knows which toolkit is
        // drawing: it fires on a pool thread and hands the clearing back to the UI one.
        _statusTimer = new System.Threading.Timer(
            _ => _ui.Post(() => StatusMessage = null), null,
            Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        _ableton.StatusChanged += OnAbletonStatusChanged;
        _ableton.ScriptFailed += OnAbletonScriptFailed;
        ApplyAbletonStatus(_ableton.Status);

        _song.StateChanged += OnSongStateChanged;
        _song.TracksChanged += OnTrackStatesChanged;
        _songState = _song.State;
        _trackStates = _song.Tracks;

        Items.CollectionChanged += (_, e) =>
        {
            OnPropertyChanged(nameof(IsEmpty));

            // A tile built after Live already said what it is doing has to arrive lit. Without
            // this, opening a group while the set is playing shows a dark play button until
            // somebody presses stop.
            if (e.NewItems is null) return;

            foreach (DeckItemViewModel tile in e.NewItems) ApplyLight(tile);
        };
    }

    // ---------- Opening at login ----------

    private bool _startsWithWindows;

    /// <summary>
    /// Whether Windows opens the deck on its own after logging in.
    /// </summary>
    /// <remarks>
    /// Read once when the view model is built and written straight through on every change, rather
    /// than saved with everything else: the truth lives outside the app, where the user can also
    /// change it from the Task Manager, so keeping a copy of it would only create two answers.
    /// <para>
    /// A refusal from Windows puts the toggle back where it was instead of leaving the menu ticked
    /// for something that did not happen.
    /// </para>
    /// </remarks>
    public bool StartsWithWindows
    {
        get => _startsWithWindows;
        set
        {
            if (_startsWithWindows == value) return;

            var state = _startup.Set(value);

            _startsWithWindows = state.Enabled;
            OnPropertyChanged();

            if (state.Problem is { } problem) SetStatus(problem, isError: true);
            else SetStatus(state.Enabled
                ? "DigitalDeck se va a abrir solo cuando prendas la PC."
                : "DigitalDeck ya no se abre solo al prender la PC.");
        }
    }

    /// <summary>Flips the setting. Bound to the menu, which is the only way in.</summary>
    [RelayCommand]
    private void ToggleStartsWithWindows() => StartsWithWindows = !StartsWithWindows;

    public ObservableCollection<Profile> Profiles { get; } = new();
    public ObservableCollection<Page> Pages { get; } = new();
    public ObservableCollection<DeckItemViewModel> Items { get; } = new();
    public ObservableCollection<BreadcrumbEntry> Breadcrumbs { get; } = new();

    [ObservableProperty] private Profile? _selectedProfile;
    [ObservableProperty] private Page? _selectedPage;
    [ObservableProperty] private bool _isEditMode;
    [ObservableProperty] private bool _isMultiSelectMode;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _statusIsError;

    // ---------- Ableton indicator ----------

    [ObservableProperty] private AbletonConnectionState _abletonState;
    [ObservableProperty] private string _abletonLabel = "Live";
    [ObservableProperty] private string _abletonDetail = "Ableton Live no está conectado.";

    // ---------- Onboarding ----------

    public IReadOnlyList<OnboardingStep> OnboardingSteps { get; } = new[]
    {
        new OnboardingStep("Bienvenido a DigitalDeck",
            "Cada cuadrado es un botón: hacé click para dispararlo.",
            AutoAdvance: false, AnchorTop: false),
        new OnboardingStep("Modo edición",
            "Hacé click en «Editar», arriba a la derecha, para poder mover, agregar y borrar botones.",
            AutoAdvance: true, AnchorTop: true),
        new OnboardingStep("Agregar botones",
            "Ahora hacé click derecho en un espacio vacío para agregar un botón, un grupo o una carpeta de favoritos.",
            AutoAdvance: true, AnchorTop: true),
        new OnboardingStep("Tamaño de la grilla",
            "Elegí un tamaño en «GRILLA», arriba a la derecha, para cambiar cuántos cuadrados entran en la página.",
            AutoAdvance: true, AnchorTop: true),
        new OnboardingStep("Buscar",
            "Probá Ctrl+F, o el botón «Buscar» de arriba, para encontrar un botón por su nombre en todo el perfil.",
            AutoAdvance: true, AnchorTop: true),
        new OnboardingStep("Deshacer y rehacer",
            "Mientras editás, Ctrl+Z deshace y Ctrl+Y rehace el último cambio.",
            AutoAdvance: false, AnchorTop: true),
        new OnboardingStep("Páginas",
            "Abajo a la izquierda agregás y cambiás entre páginas del deck.",
            AutoAdvance: false, AnchorTop: false),
    };

    [ObservableProperty] private bool _isOnboardingOpen;
    [ObservableProperty] private int _onboardingStepIndex;

    public OnboardingStep CurrentOnboardingStep => OnboardingSteps[OnboardingStepIndex];
    public bool CanGoNextOnboardingStep => OnboardingStepIndex < OnboardingSteps.Count - 1;
    public bool CanGoPreviousOnboardingStep => OnboardingStepIndex > 0;
    public bool IsOnboardingWaitingForAction => CurrentOnboardingStep.AutoAdvance;
    public bool IsOnboardingManualAdvance => !CurrentOnboardingStep.AutoAdvance;
    public bool IsOnboardingAnchoredTop => CurrentOnboardingStep.AnchorTop;
    public string OnboardingProgressLabel => $"{OnboardingStepIndex + 1} / {OnboardingSteps.Count}";
    public string NextOnboardingLabel => CanGoNextOnboardingStep ? "Siguiente" : "Listo";

    partial void OnOnboardingStepIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentOnboardingStep));
        OnPropertyChanged(nameof(CanGoNextOnboardingStep));
        OnPropertyChanged(nameof(CanGoPreviousOnboardingStep));
        OnPropertyChanged(nameof(IsOnboardingWaitingForAction));
        OnPropertyChanged(nameof(IsOnboardingManualAdvance));
        OnPropertyChanged(nameof(IsOnboardingAnchoredTop));
        OnPropertyChanged(nameof(OnboardingProgressLabel));
        OnPropertyChanged(nameof(NextOnboardingLabel));
    }

    /// <summary>
    /// Moves an <see cref="OnboardingStep.AutoAdvance"/> step forward the moment the user actually
    /// does the thing it describes, instead of waiting for a "Siguiente" click.
    /// </summary>
    private void AdvanceOnboardingIfWaiting(int stepIndex)
    {
        if (!IsOnboardingOpen || OnboardingStepIndex != stepIndex) return;
        if (!OnboardingSteps[stepIndex].AutoAdvance) return;

        NextOnboardingStep();
    }

    [RelayCommand]
    private void ShowOnboarding()
    {
        OnboardingStepIndex = 0;
        IsOnboardingOpen = true;
    }

    [RelayCommand]
    private void NextOnboardingStep()
    {
        if (CanGoNextOnboardingStep) OnboardingStepIndex++;
        else CloseOnboarding();
    }

    [RelayCommand]
    private void PreviousOnboardingStep()
    {
        if (CanGoPreviousOnboardingStep) OnboardingStepIndex--;
    }

    /// <summary>
    /// Closes the tutorial and remembers that it has been seen.
    /// </summary>
    /// <remarks>
    /// Unconditionally, however it was closed. It used to be written only when a "no volver a
    /// mostrar" box was ticked, which meant reading the whole tutorial to the end and then meeting
    /// it again on the next launch — the box was doing the job the first run should have done by
    /// itself. Nothing is lost by dropping it: the tutorial has its own button in the top bar, so
    /// it is one click away for as long as anyone wants it.
    /// </remarks>
    [RelayCommand]
    private void CloseOnboarding()
    {
        IsOnboardingOpen = false;
        RunSafely(() => _settings.SetAsync(SettingKeys.OnboardingSeen, "1"));
    }

    private async Task ShowOnboardingIfFirstRunAsync()
    {
        var seen = await _settings.GetAsync(SettingKeys.OnboardingSeen).ConfigureAwait(true);
        if (seen is null) ShowOnboarding();
    }

    /// <summary>Folder currently being shown, or null at the page root.</summary>
    public string? CurrentFolderId => Breadcrumbs.Count > 0 ? Breadcrumbs[^1].ItemId : null;

    /// <summary>True when the open page or folder has nothing in it yet.</summary>
    public bool IsEmpty => Items.Count == 0 && SelectedPage is not null;

    public int Columns => SelectedPage?.Columns ?? 6;
    public int Rows => SelectedPage?.Rows ?? 4;

    public bool CanGoBack => Breadcrumbs.Count > 1;

    // ---------- Deleting several at once ----------

    /// <summary>True while the deck is being edited and the ticking has not started yet.</summary>
    public bool CanStartMultiSelect => IsEditMode && !IsMultiSelectMode;

    public int SelectedCount => Items.Count(i => i.IsSelected);

    /// <summary>Delete button caption, carrying the count so the click is never a surprise.</summary>
    public string DeleteSelectedLabel => SelectedCount == 0 ? "Eliminar" : $"Eliminar ({SelectedCount})";

    private bool HasSelection => SelectedCount > 0;

    // ---------- Loading ----------

    public async Task InitializeAsync()
    {
        await LoadProfilesAsync().ConfigureAwait(true);

        var lastProfileId = await _settings.GetAsync(SettingKeys.LastProfileId).ConfigureAwait(true);
        var profile = Profiles.FirstOrDefault(p => p.Id == lastProfileId) ?? Profiles.FirstOrDefault();

        SelectedProfile = profile;

        await AskAbletonUsageIfFirstRunAsync().ConfigureAwait(true);
        await ShowOnboardingIfFirstRunAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Asked once, before anything else: setting up the Ableton bridge is the one first-run choice
    /// that needs an actual answer, not a click through a card. Saying yes goes straight into
    /// installing the script; saying no leaves it for later — the Ableton button up top opens the
    /// same setup screen whenever it's needed.
    /// </summary>
    private async Task AskAbletonUsageIfFirstRunAsync()
    {
        var asked = await _settings.GetAsync(SettingKeys.AbletonOnboardingAsked).ConfigureAwait(true);
        if (asked is not null) return;

        await _settings.SetAsync(SettingKeys.AbletonOnboardingAsked, "1").ConfigureAwait(true);

        var willUseAbleton = _dialogs.Confirm(
            "¿Vas a usar Ableton Live?",
            "DigitalDeck puede disparar clips y escenas de Ableton Live con un script que se instala "
            + "adentro de Live. Te recomendamos decir que sí si pensás usarlo — se instala en un momento, "
            + "y si más tarde cambiás de idea, el botón «Live» de arriba siempre te lleva a esta misma pantalla.");

        if (willUseAbleton) _dialogs.ShowRemoteScriptSetup();
    }

    private async Task LoadProfilesAsync()
    {
        var profiles = await _repository.GetProfilesAsync().ConfigureAwait(true);

        Profiles.Clear();
        foreach (var profile in profiles) Profiles.Add(profile);
    }

    // Selecting a profile loads its pages, which selects a page, which loads that page's items.
    // The cascade is deliberate: every entry point into a new selection ends with the grid showing
    // the right contents, whether the change came from the menu, a restore, or an import.
    partial void OnSelectedProfileChanged(Profile? value)
    {
        ClearEditHistory();
        RunSafely(() => LoadPagesAsync(value));
    }

    private async Task LoadPagesAsync(Profile? profile)
    {
        Pages.Clear();
        SelectedPage = null;

        if (profile is null)
        {
            Items.Clear();
            Breadcrumbs.Clear();
            return;
        }

        await _settings.SetAsync(SettingKeys.LastProfileId, profile.Id).ConfigureAwait(true);

        var pages = await _repository.GetPagesAsync(profile.Id).ConfigureAwait(true);
        if (SelectedProfile?.Id != profile.Id) return;
        foreach (var page in pages) Pages.Add(page);

        var lastPageId = await _settings.GetAsync($"{SettingKeys.LastPageId}:{profile.Id}").ConfigureAwait(true)
            ?? await _settings.GetAsync(SettingKeys.LastPageId).ConfigureAwait(true);
        if (SelectedProfile?.Id != profile.Id) return;
        SelectedPage = Pages.FirstOrDefault(p => p.Id == lastPageId) ?? Pages.FirstOrDefault();
    }

    partial void OnSelectedPageChanged(Page? value)
    {
        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(Rows));

        Breadcrumbs.Clear();
        if (value is not null) Breadcrumbs.Add(new BreadcrumbEntry(null, value.Name));

        OnPropertyChanged(nameof(CurrentFolderId));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(IsEmpty));
        RunSafely(LoadItemsAsync);
    }

    private async Task LoadItemsAsync()
    {
        Items.Clear();

        if (SelectedPage is not { } page) return;
        var profileId = SelectedProfile?.Id;
        var folderId = CurrentFolderId;

        await _settings.SetAsync(SettingKeys.LastPageId, page.Id).ConfigureAwait(true);
        if (profileId is not null)
            await _settings.SetAsync($"{SettingKeys.LastPageId}:{profileId}", page.Id).ConfigureAwait(true);

        var children = await _repository
            .GetChildrenAsync(page.Id, folderId)
            .ConfigureAwait(true);

        if (SelectedPage?.Id != page.Id || CurrentFolderId != folderId) return;
        foreach (var child in children)
            Items.Add(new DeckItemViewModel(child, _icons));

        // One query for the whole page rather than one per folder: a page with ten groups on it would
        // otherwise cost ten extra round trips every time the grid is drawn.
        if (Items.Any(i => i.IsFolder))
        {
            var pageItems = await _repository
                .GetPageItemsAsync(SelectedPage.Id)
                .ConfigureAwait(true);

            foreach (var folder in Items.Where(i => i.IsFolder))
                folder.ChildCount = pageItems.Count(i => i.ParentId == folder.Id);
        }

        NotifySelectionChanged();
    }

    // ---------- Navigation ----------

    [RelayCommand]
    private async Task ActivateAsync(DeckItemViewModel? item)
    {
        if (item is null || IsEditMode) return;

        if (item.IsFolder)
        {
            // Rewritten on the way in rather than on every press: the ranking only has to be right
            // at the moment somebody looks at it, and a press should not cost a folder rebuild.
            if (await _frequent.IsFrequentFolderAsync(item.Id).ConfigureAwait(true))
                await _frequent.RebuildAsync(Columns, Rows).ConfigureAwait(true);

            Breadcrumbs.Add(new BreadcrumbEntry(item.Id, item.Label));
            OnPropertyChanged(nameof(CurrentFolderId));
            OnPropertyChanged(nameof(CanGoBack));
            await LoadItemsAsync().ConfigureAwait(true);
            return;
        }

        var result = await _dispatcher.DispatchAsync(item.Model.Action).ConfigureAwait(true);

        if (!result.Success)
        {
            SetStatus(result.Error ?? "No se pudo ejecutar la acción.", isError: true);
            return;
        }

        // Counted after the fact, and only when it worked. A button pointing at a device this Live
        // does not have would otherwise climb the ranking by failing.
        await _usage.RecordAsync(item.Model).ConfigureAwait(true);
    }

    // ---------- Voice ----------

    /// <summary>Where each name the assistant knows can be found, by tile id.</summary>
    private IReadOnlyDictionary<string, DeckSearchHit> _voiceIndex =
        new Dictionary<string, DeckSearchHit>(StringComparer.Ordinal);

    /// <summary>
    /// Every button in the profile that can be named out loud, and where each one lives.
    /// </summary>
    /// <remarks>
    /// The whole profile rather than the folder on screen, so a name is enough to reach a button
    /// without remembering which page it was filed under — the same reason the search exists.
    /// <para>
    /// Which forces a decision the on-screen version never had to make: across a few pages the same
    /// name turns up more than once, and the matcher refuses a tie rather than guessing. Left alone
    /// that would make «reverb» stop working the moment a second Reverb existed anywhere. So a name
    /// is claimed exactly once, and <b>what is on screen claims it first</b>: saying the name of a
    /// button you are looking at always presses that one, and the reach into the rest of the deck
    /// only applies to names that are not in front of you. Among the rest, the earliest page and
    /// the shallowest folder win, which is arbitrary but at least stays the same every time.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<VoiceTarget>> PrepareVoiceTargetsAsync()
    {
        if (SelectedProfile is not { } profile) return [];

        IReadOnlyList<DeckSearchHit> hits;

        try
        {
            // Re-read on every press, like the search does on every open: the deck is small, and a
            // stale vocabulary is worse than a few milliseconds.
            var items = await _repository.GetProfileItemsAsync(profile.Id).ConfigureAwait(true);
            hits = DeckSearch.All(items, Pages);
        }
        catch (Exception)
        {
            // A press that cannot read the deck should still hear what is in front of the user.
            hits = [];
        }

        var pageOrder = Pages
            .Select((page, index) => (page.Id, index))
            .ToDictionary(entry => entry.Id, entry => entry.index, StringComparer.Ordinal);

        var onScreen = Items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);

        var ranked = hits
            .OrderByDescending(hit => onScreen.Contains(hit.Item.Id))
            .ThenBy(hit => pageOrder.TryGetValue(hit.PageId, out var i) ? i : int.MaxValue)
            .ThenBy(hit => hit.FolderPath.Count)
            .ThenBy(hit => hit.Item.Label, StringComparer.CurrentCultureIgnoreCase);

        var index = new Dictionary<string, DeckSearchHit>(StringComparer.Ordinal);
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var targets = new List<VoiceTarget>();

        foreach (var hit in ranked)
        {
            var spoken = VoicePhrase.Normalise(hit.Item.Label);

            if (spoken.Length == 0 || !claimed.Add(spoken)) continue;

            index[hit.Item.Id] = hit;
            targets.Add(new VoiceTarget(hit.Item.Id, hit.Item.Label));
        }

        _voiceIndex = index;

        return targets;
    }

    /// <summary>
    /// Presses the tile the assistant resolved, going to it first when it is somewhere else.
    /// </summary>
    /// <remarks>
    /// Navigating rather than firing from where we stand, which is what the search does for the
    /// same reason: it leaves the user looking at the button that just ran. Without that, a spoken
    /// command would act on a folder they cannot see and give them no idea where it happened.
    /// </remarks>
    public async Task ActivateByIdAsync(string id)
    {
        var label = Items.FirstOrDefault(item => item.Id == id)?.Label
                    ?? (_voiceIndex.TryGetValue(id, out var known) ? known.Item.Label : null);

        if (label is null) return;

        // Said out loud rather than swallowed. ActivateAsync refuses in edit mode, which is right —
        // but the assistant has already told the user it understood, so going silent here reads as
        // the whole feature being broken rather than as one mode being the wrong one to be in.
        if (IsEditMode)
        {
            SetStatus($"«{label}» no se puede disparar en modo edición.", isError: true);
            return;
        }

        if (Items.FirstOrDefault(item => item.Id == id) is { } here)
        {
            await ActivateAsync(here).ConfigureAwait(true);
            return;
        }

        if (!_voiceIndex.TryGetValue(id, out var hit)) return;

        if (!await RevealAsync(hit).ConfigureAwait(true))
        {
            SetStatus($"«{label}» ya no está donde estaba.", isError: true);
            return;
        }

        // Looked up again after navigating, because the tile on screen now is a different view
        // model from the one the index remembers.
        if (Items.FirstOrDefault(item => item.Id == id) is { } there)
            await ActivateAsync(there).ConfigureAwait(true);
    }

    [RelayCommand]
    private void ShowVoiceSettings() => _dialogs.ShowVoiceSettings();

    [RelayCommand]
    private void ShowMixer() => _dialogs.ShowMixer();

    [RelayCommand]
    private void ShowRemoteScript() => _dialogs.ShowRemoteScriptSetup();

    [RelayCommand]
    private void ShowDuplicateNames() => _dialogs.ShowDuplicateNames();

    [RelayCommand]
    private async Task GoBackAsync()
    {
        if (Breadcrumbs.Count <= 1) return;

        Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);
        OnPropertyChanged(nameof(CurrentFolderId));
        OnPropertyChanged(nameof(CanGoBack));
        await LoadItemsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task NavigateToAsync(BreadcrumbEntry? entry)
    {
        if (entry is null) return;

        var index = Breadcrumbs.IndexOf(entry);
        if (index < 0 || index == Breadcrumbs.Count - 1) return;

        while (Breadcrumbs.Count > index + 1)
            Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);

        OnPropertyChanged(nameof(CurrentFolderId));
        OnPropertyChanged(nameof(CanGoBack));
        await LoadItemsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void ToggleEditMode() => IsEditMode = !IsEditMode;

    // Leaving edit mode has to take the ticking with it: a tile left ticked from a previous session
    // of editing would be deleted by a click the user thinks is starting a fresh selection.
    partial void OnIsEditModeChanged(bool value)
    {
        if (!value) ExitMultiSelect();
        OnPropertyChanged(nameof(CanStartMultiSelect));
        if (value) AdvanceOnboardingIfWaiting(1);
    }

    partial void OnIsMultiSelectModeChanged(bool value) => OnPropertyChanged(nameof(CanStartMultiSelect));

    [RelayCommand]
    private void SelectProfile(Profile? profile)
    {
        if (profile is not null) SelectedProfile = profile;
    }

    [RelayCommand]
    private void SelectPage(Page? page)
    {
        if (page is not null) SelectedPage = page;
    }

    // ---------- Search ----------

    /// <summary>
    /// Finds a button by name anywhere in the profile.
    /// </summary>
    /// <remarks>
    /// Past a couple of pages the tree stops being the fastest way in: the user remembers the name
    /// of the button, not the folder somebody filed it under. The search looks across every page at
    /// every depth, and picking a result both takes them there and fires it, so a forgotten button
    /// costs one keystroke instead of a hunt.
    /// </remarks>
    public ObservableCollection<DeckSearchResultViewModel> SearchResults { get; } = new();

    [ObservableProperty] private bool _isSearchOpen;
    [ObservableProperty] private string _searchQuery = string.Empty;

    /// <summary>Snapshot taken when the search opens, so typing does not re-read the deck per keystroke.</summary>
    private IReadOnlyList<DeckItem> _searchIndex = Array.Empty<DeckItem>();

    /// <summary>True once something was typed and nothing came back — the cue for the empty message.</summary>
    public bool HasNoSearchResults => IsSearchOpen && SearchQuery.Trim().Length > 0 && SearchResults.Count == 0;

    [RelayCommand]
    private async Task OpenSearchAsync()
    {
        SearchQuery = string.Empty;
        SearchResults.Clear();
        IsSearchOpen = true;
        AdvanceOnboardingIfWaiting(4);

        // Re-read on every open rather than kept in step with edits: the deck is small, and a stale
        // list is worse than a query that costs a few milliseconds.
        _searchIndex = SelectedProfile is { } profile
            ? await _repository.GetProfileItemsAsync(profile.Id).ConfigureAwait(true)
            : Array.Empty<DeckItem>();

        OnPropertyChanged(nameof(HasNoSearchResults));
    }

    [RelayCommand]
    private void CloseSearch()
    {
        IsSearchOpen = false;
        SearchQuery = string.Empty;
        SearchResults.Clear();
        _searchIndex = Array.Empty<DeckItem>();
        OnPropertyChanged(nameof(HasNoSearchResults));
    }

    [RelayCommand]
    private async Task ToggleSearchAsync()
    {
        if (IsSearchOpen) CloseSearch();
        else await OpenSearchAsync().ConfigureAwait(true);
    }

    partial void OnSearchQueryChanged(string value) => RefreshSearchResults(value);

    private void RefreshSearchResults(string query)
    {
        SearchResults.Clear();

        foreach (var hit in DeckSearch.Find(_searchIndex, Pages, query))
            SearchResults.Add(new DeckSearchResultViewModel(hit, _icons));

        OnPropertyChanged(nameof(HasNoSearchResults));
    }

    /// <summary>
    /// Takes the deck to the result and presses it.
    /// </summary>
    /// <remarks>
    /// Navigating first — instead of firing the action from the search — leaves the user standing
    /// where the button lives, which is how they learn the way there. In edit mode nothing is
    /// fired: the search becomes a way to jump to the tile you want to change.
    /// </remarks>
    [RelayCommand]
    private async Task OpenSearchResultAsync(DeckSearchResultViewModel? result)
    {
        if (result is null) return;

        var hit = result.Hit;
        CloseSearch();

        if (!await RevealAsync(hit).ConfigureAwait(true)) return;

        var tile = Items.FirstOrDefault(i => i.Id == hit.Item.Id);
        if (tile is null) return;

        if (IsEditMode)
        {
            SetStatus($"«{tile.Label}» está acá.");
            return;
        }

        await ActivateAsync(tile).ConfigureAwait(true);
    }

    /// <summary>Shows the page and folder a hit lives in. False when the page is gone.</summary>
    private async Task<bool> RevealAsync(DeckSearchHit hit)
    {
        var page = Pages.FirstOrDefault(p => p.Id == hit.PageId);
        if (page is null) return false;

        if (!ReferenceEquals(page, SelectedPage))
        {
            SelectedPage = page;

            // Selecting a page queues its own load; letting it finish first stops that load from
            // landing after the folder path below and putting the user back at the root.
            await SettleAsync().ConfigureAwait(true);
        }

        if (Breadcrumbs.Count == 0) return false;

        while (Breadcrumbs.Count > 1) Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);
        foreach (var step in hit.FolderPath) Breadcrumbs.Add(new BreadcrumbEntry(step.Id, step.Label));

        OnPropertyChanged(nameof(CurrentFolderId));
        OnPropertyChanged(nameof(CanGoBack));

        await LoadItemsAsync().ConfigureAwait(true);
        return true;
    }

    // ---------- Drag and drop ----------

    /// <summary>Works out what a drop at this cell would do, without changing anything.</summary>
    public DropPreview EvaluateDrop(DeckItemViewModel dragged, int col, int row)
    {
        if (SelectedPage is null) return new DropPreview(DropKind.None, col, row, null);

        var landedOn = Items.FirstOrDefault(i =>
            i.Id != dragged.Id && new GridRect(i.Col, i.Row, i.Width, i.Height).Contains(col, row));

        // Dropping onto a folder files the tile inside it — the fastest way to organise a page.
        if (landedOn is { IsFolder: true })
            return new DropPreview(DropKind.IntoFolder, landedOn.Col, landedOn.Row, landedOn);

        var siblings = Items.Select(i => i.Model).ToList();
        var placement = GridLayoutEngine.Place(siblings, dragged.Model, col, row, SelectedPage.Columns, SelectedPage.Rows);

        if (!placement.Success)
            return new DropPreview(DropKind.None, col, row, null);

        if (placement.SwapWith is null)
            return new DropPreview(DropKind.Move, placement.Target.Col, placement.Target.Row, null);

        var partner = Items.FirstOrDefault(i => i.Id == placement.SwapWith.Id);
        return new DropPreview(DropKind.Swap, placement.Target.Col, placement.Target.Row, partner);
    }

    /// <summary>Commits a drop previously described by <see cref="EvaluateDrop"/>.</summary>
    public async Task ApplyDropAsync(DeckItemViewModel dragged, DropPreview preview)
    {
        switch (preview.Kind)
        {
            case DropKind.Move:
            {
                var draggedId = dragged.Id;
                var parentId = dragged.Model.ParentId;
                var sortKey = dragged.Model.SortKey;
                var fromCol = dragged.Col;
                var fromRow = dragged.Row;
                var toCol = preview.Col;
                var toRow = preview.Row;

                dragged.Model.Col = toCol;
                dragged.Model.Row = toRow;
                await _repository.MoveItemAsync(draggedId, parentId, toCol, toRow, sortKey).ConfigureAwait(true);
                dragged.Refresh();

                PushEdit(new DeckEdit
                {
                    Description = $"mover «{dragged.Label}»",
                    Undo = () => _repository.MoveItemAsync(draggedId, parentId, fromCol, fromRow, sortKey),
                    Redo = () => _repository.MoveItemAsync(draggedId, parentId, toCol, toRow, sortKey)
                });
                break;
            }

            case DropKind.Swap when preview.Target is { } partner:
            {
                var draggedId = dragged.Id;
                var partnerId = partner.Id;
                var fromCol = dragged.Col;
                var fromRow = dragged.Row;
                var toCol = preview.Col;
                var toRow = preview.Row;

                dragged.Model.Col = toCol;
                dragged.Model.Row = toRow;
                partner.Model.Col = fromCol;
                partner.Model.Row = fromRow;

                await _repository
                    .SwapPositionsAsync(draggedId, toCol, toRow, partnerId, fromCol, fromRow)
                    .ConfigureAwait(true);

                dragged.Refresh();
                partner.Refresh();

                PushEdit(new DeckEdit
                {
                    Description = $"intercambiar «{dragged.Label}» y «{partner.Label}»",
                    Undo = () => _repository.SwapPositionsAsync(draggedId, fromCol, fromRow, partnerId, toCol, toRow),
                    Redo = () => _repository.SwapPositionsAsync(draggedId, toCol, toRow, partnerId, fromCol, fromRow)
                });
                break;
            }

            case DropKind.IntoFolder when preview.Target is { } folder:
                await MoveIntoFolderAsync(dragged, folder).ConfigureAwait(true);
                break;
        }
    }

    private async Task MoveIntoFolderAsync(DeckItemViewModel dragged, DeckItemViewModel folder)
    {
        if (SelectedPage is null) return;

        var contents = await _repository.GetChildrenAsync(SelectedPage.Id, folder.Id).ConfigureAwait(true);

        var slot = GridLayoutEngine.FindFirstFreeSlot(
            contents, dragged.Width, dragged.Height, SelectedPage.Columns, SelectedPage.Rows);

        if (slot is null)
        {
            SetStatus($"«{folder.Label}» no tiene lugar libre para ese botón.", isError: true);
            return;
        }

        var draggedId = dragged.Id;
        var oldParentId = dragged.Model.ParentId;
        var oldSortKey = dragged.Model.SortKey;
        var oldCol = dragged.Col;
        var oldRow = dragged.Row;

        var newSortKey = FractionalIndex.Last(contents.LastOrDefault()?.SortKey);
        var newCol = slot.Value.Col;
        var newRow = slot.Value.Row;
        var folderId = folder.Id;

        await _repository.MoveItemAsync(draggedId, folderId, newCol, newRow, newSortKey).ConfigureAwait(true);

        Items.Remove(dragged);
        SetStatus($"«{dragged.Label}» se movió a «{folder.Label}».");

        PushEdit(new DeckEdit
        {
            Description = $"mover «{dragged.Label}» a «{folder.Label}»",
            Undo = () => _repository.MoveItemAsync(draggedId, oldParentId, oldCol, oldRow, oldSortKey),
            Redo = () => _repository.MoveItemAsync(draggedId, folderId, newCol, newRow, newSortKey)
        });
    }

    // ---------- Undo / redo ----------

    /// <summary>
    /// Covers moving, swapping, filing into a folder, adding, deleting, editing, resizing and
    /// duplicating a tile — everything a drag or the edit-mode menus can do to a page. Deliberately
    /// does not cover creating or deleting a whole page or profile, or importing a pack: those are
    /// occasional and large enough that a confirmation dialog already stands in for undo.
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        if (_undoStack.Count == 0) return;

        var edit = _undoStack.Pop();
        await edit.Undo().ConfigureAwait(true);
        _redoStack.Push(edit);

        await LoadItemsAsync().ConfigureAwait(true);
        NotifyEditHistoryChanged();
        SetStatus($"Se deshizo: {edit.Description}.");
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private async Task RedoAsync()
    {
        if (_redoStack.Count == 0) return;

        var edit = _redoStack.Pop();
        await edit.Redo().ConfigureAwait(true);
        _undoStack.Push(edit);

        await LoadItemsAsync().ConfigureAwait(true);
        NotifyEditHistoryChanged();
        SetStatus($"Se rehizo: {edit.Description}.");
    }

    /// <summary>Records a completed edit. Doing anything new after an undo drops the redo branch.</summary>
    private void PushEdit(DeckEdit edit)
    {
        _undoStack.Push(edit);
        _redoStack.Clear();
        NotifyEditHistoryChanged();
    }

    private void ClearEditHistory()
    {
        if (_undoStack.Count == 0 && _redoStack.Count == 0) return;

        _undoStack.Clear();
        _redoStack.Clear();
        NotifyEditHistoryChanged();
    }

    private void NotifyEditHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// A value copy of everything an edit might need to put back, independent of whatever happens
    /// to the original afterwards.
    /// </summary>
    /// <remarks>
    /// An undo/redo closure that captured <c>item.Model</c> directly would be holding the same
    /// mutable object the view model keeps drawing from — the very next edit to that tile would
    /// silently rewrite what "before" or "after" means for an entry already sitting on the stack.
    /// Every closure below closes over one of these instead.
    /// </remarks>
    private static DeckItem Clone(DeckItem source) => new()
    {
        Id = source.Id,
        PageId = source.PageId,
        ParentId = source.ParentId,
        Type = source.Type,
        SortKey = source.SortKey,
        Col = source.Col,
        Row = source.Row,
        Width = source.Width,
        Height = source.Height,
        Label = source.Label,
        IconPath = source.IconPath,
        IconGlyph = source.IconGlyph,
        ColorHex = source.ColorHex,
        Action = source.Action
    };

    // ---------- Editing tiles ----------

    /// <summary>
    /// Names other tiles already answer to, so the editor can refuse to reuse one.
    /// </summary>
    /// <remarks>
    /// Normalised the way the assistant compares, which is what the rule is actually about: two
    /// tiles it could not tell apart. <c>ESTUDIO</c> and <c>Estudio</c> collide; <c>Reverb 1</c>
    /// and <c>Reverb 2</c> do not.
    /// <para>
    /// Only names typed by hand pass through here. The favourites folder is built from copies of
    /// tiles that live elsewhere, and an imported <c>.deckpack</c> arrives with whatever names it
    /// was saved with — applying this to either would break a feature to enforce a rule about the
    /// one place a person is actually choosing a name.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyCollection<string>> NamesInUseAsync(string? exceptItemId)
    {
        if (SelectedProfile is not { } profile) return [];

        try
        {
            var items = await _repository.GetProfileItemsAsync(profile.Id).ConfigureAwait(true);

            return items
                .Where(item => item.Id != exceptItemId)
                .Select(item => VoicePhrase.Normalise(item.Label))
                .Where(name => name.Length > 0)
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception)
        {
            // Not being able to read the deck is no reason to block someone from naming a button.
            return [];
        }
    }


    [RelayCommand]
    private async Task AddButtonAsync(string? cell) => await AddItemAsync(ItemType.Button, cell).ConfigureAwait(true);

    [RelayCommand]
    private async Task AddFolderAsync(string? cell) => await AddItemAsync(ItemType.Folder, cell).ConfigureAwait(true);

    /// <summary>
    /// Puts the "most used" folder on the page, in the cell asked for.
    /// </summary>
    /// <remarks>
    /// There is only ever one of these across the whole app — <see cref="FrequentFolderService"/>
    /// remembers a single id, not one per profile — so a second press finds the first one still
    /// standing and says where, rather than building a folder that would sit there never updating.
    /// Filled right away rather than left empty until first opened, so the button does not read as
    /// broken the moment it lands on the grid.
    /// </remarks>
    [RelayCommand]
    private async Task AddFrequentFolderAsync(string? cell)
    {
        if (SelectedPage is not { } page) return;

        var existingId = await _frequent.GetFolderIdAsync().ConfigureAwait(true);
        if (existingId is not null && await _repository.GetItemAsync(existingId).ConfigureAwait(true) is not null)
        {
            SetStatus(
                $"Ya existe una carpeta de «{FrequentFolderService.DefaultLabel}» en el deck; sólo puede haber una.",
                isError: true);
            return;
        }

        var siblings = Items.Select(i => i.Model).ToList();
        var requested = ParseCell(cell);

        var slot = requested is { } target
                   && GridLayoutEngine.CanPlace(siblings, new GridRect(target.Col, target.Row, 1, 1), Columns, Rows)
            ? new GridRect(target.Col, target.Row, 1, 1)
            : GridLayoutEngine.FindFirstFreeSlot(siblings, 1, 1, Columns, Rows);

        if (slot is null)
        {
            SetStatus("No queda espacio libre en esta página.", isError: true);
            return;
        }

        var sortKey = FractionalIndex.Last(siblings.OrderBy(s => s.SortKey, StringComparer.Ordinal).LastOrDefault()?.SortKey);

        var folder = DeckItem.NewFolder(page.Id, CurrentFolderId, sortKey, FrequentFolderService.DefaultLabel);
        folder.Col = slot.Value.Col;
        folder.Row = slot.Value.Row;
        folder.ColorHex = FrequentFolderService.DefaultColorHex;
        folder.IconGlyph = FrequentFolderService.DefaultIcon;

        await _repository.AddItemAsync(folder).ConfigureAwait(true);
        await _settings.SetAsync(SettingKeys.FrequentFolderId, folder.Id).ConfigureAwait(true);
        await _frequent.RebuildAsync(Columns, Rows).ConfigureAwait(true);

        Items.Add(new DeckItemViewModel(folder, _icons));
        SetStatus($"Se agregó «{FrequentFolderService.DefaultLabel}».");
    }

    private async Task AddItemAsync(ItemType type, string? cell)
    {
        if (SelectedPage is null) return;

        var siblings = Items.Select(i => i.Model).ToList();
        var requested = ParseCell(cell);

        var slot = requested is { } target
                   && GridLayoutEngine.CanPlace(siblings, new GridRect(target.Col, target.Row, 1, 1), Columns, Rows)
            ? new GridRect(target.Col, target.Row, 1, 1)
            : GridLayoutEngine.FindFirstFreeSlot(siblings, 1, 1, Columns, Rows);

        if (slot is null)
        {
            SetStatus("No queda espacio libre en esta página.", isError: true);
            return;
        }

        var sortKey = FractionalIndex.Last(siblings.OrderBy(s => s.SortKey, StringComparer.Ordinal).LastOrDefault()?.SortKey);

        var item = type == ItemType.Folder
            ? DeckItem.NewFolder(SelectedPage.Id, CurrentFolderId, sortKey, "Grupo")
            : DeckItem.NewButton(SelectedPage.Id, CurrentFolderId, sortKey, "Botón");

        item.Col = slot.Value.Col;
        item.Row = slot.Value.Row;

        if (!_dialogs.EditItem(item, await NamesInUseAsync(exceptItemId: null).ConfigureAwait(true))) return;

        await _repository.AddItemAsync(item).ConfigureAwait(true);
        Items.Add(new DeckItemViewModel(item, _icons));
        AdvanceOnboardingIfWaiting(2);

        var added = Clone(item);
        PushEdit(new DeckEdit
        {
            Description = $"agregar «{added.Label}»",
            Undo = () => _repository.DeleteItemAsync(added.Id),
            Redo = () => _repository.AddItemAsync(Clone(added))
        });
    }

    [RelayCommand]
    private async Task EditItemAsync(DeckItemViewModel? item)
    {
        if (item is null) return;

        var before = Clone(item.Model);
        var taken = await NamesInUseAsync(item.Id).ConfigureAwait(true);

        if (!_dialogs.EditItem(item.Model, taken)) return;
        var after = Clone(item.Model);

        await _repository.UpdateItemAsync(item.Model).ConfigureAwait(true);
        item.Refresh();

        PushEdit(new DeckEdit
        {
            Description = $"editar «{after.Label}»",
            Undo = () => _repository.UpdateItemAsync(Clone(before)),
            Redo = () => _repository.UpdateItemAsync(Clone(after))
        });
    }

    [RelayCommand]
    private async Task DeleteItemAsync(DeckItemViewModel? item)
    {
        if (item is null) return;

        await DeleteAsync("Eliminar", new[] { item }).ConfigureAwait(true);
    }

    // ---------- Deleting several at once ----------

    /// <summary>
    /// Turns left click into ticking instead of editing, so a batch can be picked off the grid.
    /// </summary>
    /// <remarks>
    /// Deleting tiles one at a time means a context menu and a confirmation for each one, which is
    /// the wrong shape for clearing out a page. Here the clicks pick, and one confirmation covers
    /// the lot.
    /// </remarks>
    [RelayCommand]
    private void StartMultiSelect()
    {
        if (!IsEditMode) return;

        ClearSelection();
        IsMultiSelectMode = true;
        SetStatus("Tocá los botones que querés eliminar y después confirmá.");
    }

    [RelayCommand]
    private void CancelMultiSelect() => ExitMultiSelect();

    /// <summary>Ticks or unticks one tile. Called by the view when a tile is clicked.</summary>
    [RelayCommand]
    private void ToggleSelection(DeckItemViewModel? item)
    {
        if (item is null || !IsMultiSelectMode) return;

        item.IsSelected = !item.IsSelected;
        NotifySelectionChanged();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteSelectedAsync()
    {
        var chosen = Items.Where(i => i.IsSelected).ToList();
        if (chosen.Count == 0) return;

        if (!await DeleteAsync("Eliminar varios", chosen).ConfigureAwait(true)) return;

        ExitMultiSelect();
        SetStatus(chosen.Count == 1
            ? "Se eliminó 1 elemento."
            : $"Se eliminaron {chosen.Count} elementos.");
    }

    /// <summary>Confirms once and then removes every item, from storage and from the grid.</summary>
    private async Task<bool> DeleteAsync(string title, IReadOnlyList<DeckItemViewModel> items)
    {
        if (items.Count == 0) return false;
        if (!_dialogs.Confirm(title, DescribeDeletion(items), destructive: true)) return false;

        // A folder takes its whole subtree with it when it goes, so undoing has to bring the whole
        // subtree back — captured before anything is deleted, and by value, so nothing later can
        // reach into this snapshot and change what "back" means.
        var restore = new List<DeckItem>();
        foreach (var item in items)
        {
            var subtree = await _repository.GetSubtreeAsync(item.Id).ConfigureAwait(true);
            restore.AddRange(subtree.Select(Clone));
        }

        var topLevelIds = items.Select(i => i.Id).ToList();

        foreach (var item in items)
        {
            await _repository.DeleteItemAsync(item.Id).ConfigureAwait(true);
            Items.Remove(item);
        }

        NotifySelectionChanged();

        PushEdit(new DeckEdit
        {
            Description = items.Count == 1
                ? $"eliminar «{items[0].Label}»"
                : $"eliminar {items.Count} elementos",
            // AddItemsAsync sorts parents before children itself, so the snapshot order does not
            // have to.
            Undo = () => _repository.AddItemsAsync(restore.Select(Clone).ToList()),
            Redo = async () =>
            {
                foreach (var id in topLevelIds)
                    await _repository.DeleteItemAsync(id).ConfigureAwait(false);
            }
        });

        return true;
    }

    /// <summary>
    /// Spells out what is about to disappear, counting folders separately.
    /// </summary>
    /// <remarks>
    /// A folder takes its contents with it, and after a batch selection the user cannot be expected
    /// to remember which of the ticked tiles were folders — so the warning counts them.
    /// </remarks>
    private static string DescribeDeletion(IReadOnlyList<DeckItemViewModel> items)
    {
        if (items.Count == 1)
        {
            var only = items[0];
            return only.IsFolder
                ? $"Se va a eliminar «{only.Label}» y todo lo que contiene. Esta acción no se puede deshacer."
                : $"Se va a eliminar «{only.Label}».";
        }

        var folders = items.Count(i => i.IsFolder);
        var contents = folders switch
        {
            0 => string.Empty,
            1 => " Uno es un grupo, así que también se va todo lo que tiene adentro.",
            _ => $" {folders} son grupos, así que también se va todo lo que tienen adentro."
        };

        return $"Se van a eliminar {items.Count} elementos.{contents} Esta acción no se puede deshacer.";
    }

    private void ExitMultiSelect()
    {
        IsMultiSelectMode = false;
        ClearSelection();
    }

    private void ClearSelection()
    {
        foreach (var item in Items) item.IsSelected = false;
        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(DeleteSelectedLabel));
        DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task DuplicateItemAsync(DeckItemViewModel? item)
    {
        if (item is null || SelectedPage is null) return;

        var siblings = Items.Select(i => i.Model).ToList();
        var slot = GridLayoutEngine.FindFirstFreeSlot(siblings, item.Width, item.Height, Columns, Rows);

        if (slot is null)
        {
            SetStatus("No queda espacio libre para la copia.", isError: true);
            return;
        }

        var sortKey = FractionalIndex.Last(siblings.OrderBy(s => s.SortKey, StringComparer.Ordinal).LastOrDefault()?.SortKey);

        // A folder's contents come along, so the copy is genuinely independent.
        var source = await _repository.GetSubtreeAsync(item.Id).ConfigureAwait(true);
        var idMap = source.ToDictionary(i => i.Id, _ => Guid.NewGuid().ToString("n"));

        var clones = source.Select(original => new DeckItem
        {
            Id = idMap[original.Id],
            PageId = original.PageId,
            ParentId = original.Id == item.Id
                ? item.Model.ParentId
                : original.ParentId is not null && idMap.TryGetValue(original.ParentId, out var mapped) ? mapped : null,
            Type = original.Type,
            SortKey = original.Id == item.Id ? sortKey : original.SortKey,
            Col = original.Id == item.Id ? slot.Value.Col : original.Col,
            Row = original.Id == item.Id ? slot.Value.Row : original.Row,
            Width = original.Width,
            Height = original.Height,
            Label = original.Id == item.Id ? $"{original.Label} (copia)" : original.Label,
            IconPath = original.IconPath,
            IconGlyph = original.IconGlyph,
            ColorHex = original.ColorHex,
            Action = original.Action
        }).ToList();

        await _repository.AddItemsAsync(clones).ConfigureAwait(true);

        var root = clones.First(c => c.Id == idMap[item.Id]);
        Items.Add(new DeckItemViewModel(root, _icons));

        var rootId = root.Id;
        var snapshot = clones.Select(Clone).ToList();

        PushEdit(new DeckEdit
        {
            Description = $"duplicar «{item.Label}»",
            Undo = () => _repository.DeleteItemAsync(rootId),
            Redo = () => _repository.AddItemsAsync(snapshot.Select(Clone).ToList())
        });
    }

    [RelayCommand]
    private async Task ResizeItemAsync(string? parameter)
    {
        if (SelectedItem is not { } item || parameter is null) return;

        var parts = parameter.Split(',');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var width) || !int.TryParse(parts[1], out var height))
            return;

        var siblings = Items.Select(i => i.Model).ToList();

        if (!GridLayoutEngine.CanResize(siblings, item.Model, width, height, Columns, Rows))
        {
            SetStatus("No hay espacio para agrandar el botón ahí.", isError: true);
            return;
        }

        var before = Clone(item.Model);

        item.Model.Width = width;
        item.Model.Height = height;

        await _repository.UpdateItemAsync(item.Model).ConfigureAwait(true);
        item.Refresh();

        var after = Clone(item.Model);
        PushEdit(new DeckEdit
        {
            Description = $"cambiar el tamaño de «{after.Label}»",
            Undo = () => _repository.UpdateItemAsync(Clone(before)),
            Redo = () => _repository.UpdateItemAsync(Clone(after))
        });
    }

    /// <summary>Tile the context menu is acting on; set by the view before opening the menu.</summary>
    public DeckItemViewModel? SelectedItem { get; set; }

    // ---------- Profiles and pages ----------

    [RelayCommand]
    private async Task NewProfileAsync()
    {
        if (_dialogs.AskText("Nuevo perfil", "Nombre del perfil") is not { Length: > 0 } name) return;

        var profile = await _repository.CreateProfileAsync(name).ConfigureAwait(true);
        Profiles.Add(profile);
        SelectedProfile = profile;
    }

    [RelayCommand]
    private async Task RenameProfileAsync()
    {
        if (SelectedProfile is not { } profile) return;
        if (_dialogs.AskText("Renombrar perfil", "Nombre del perfil", profile.Name) is not { Length: > 0 } name) return;

        await _repository.RenameProfileAsync(profile.Id, name).ConfigureAwait(true);
        profile.Name = name;

        var index = Profiles.IndexOf(profile);
        if (index >= 0)
        {
            // Replacing the instance is what makes the combo box redraw the new name.
            Profiles[index] = profile;
            SelectedProfile = profile;
        }
    }

    [RelayCommand]
    private async Task DuplicateProfileAsync()
    {
        if (SelectedProfile is not { } profile) return;
        if (_dialogs.AskText("Duplicar perfil", "Nombre de la copia", $"{profile.Name} (copia)") is not { Length: > 0 } name) return;

        var copy = await _repository.DuplicateProfileAsync(profile.Id, name).ConfigureAwait(true);
        Profiles.Add(copy);
        SelectedProfile = copy;
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is not { } profile) return;

        if (Profiles.Count <= 1)
        {
            SetStatus("Tiene que quedar al menos un perfil.", isError: true);
            return;
        }

        if (!_dialogs.Confirm("Eliminar perfil",
                $"Se va a eliminar «{profile.Name}» con todas sus páginas y botones. Esta acción no se puede deshacer.",
                destructive: true))
            return;

        await _repository.DeleteProfileAsync(profile.Id).ConfigureAwait(true);
        Profiles.Remove(profile);
        SelectedProfile = Profiles.FirstOrDefault();
    }

    [RelayCommand]
    private async Task NewPageAsync()
    {
        if (SelectedProfile is not { } profile) return;
        if (_dialogs.AskText("Nueva página", "Nombre de la página", $"Página {Pages.Count + 1}") is not { Length: > 0 } name) return;

        var page = await _repository.CreatePageAsync(profile.Id, name, Columns, Rows).ConfigureAwait(true);
        Pages.Add(page);
        SelectedPage = page;
    }

    /// <summary>
    /// Opens the page settings editor.
    /// </summary>
    /// <remarks>
    /// Takes the page rather than always reading <see cref="SelectedPage"/>, so the tab strip's own
    /// right-click menu can act on whichever tab was clicked without first switching to it — the
    /// menu on the hamburger button still calls this with no argument and gets the old behaviour.
    /// </remarks>
    [RelayCommand]
    private async Task EditPageAsync(Page? target = null)
    {
        if ((target ?? SelectedPage) is not { } page) return;
        if (!_dialogs.EditPage(page)) return;

        await _repository.UpdatePageAsync(page).ConfigureAwait(true);
        var stranded = await RepackPageAsync(page).ConfigureAwait(true);

        var index = Pages.IndexOf(page);
        if (index >= 0) Pages[index] = page;

        if (ReferenceEquals(page, SelectedPage))
        {
            OnPropertyChanged(nameof(Columns));
            OnPropertyChanged(nameof(Rows));
            SelectedPage = page;
            await LoadItemsAsync().ConfigureAwait(true);
        }

        if (stranded > 0)
            SetStatus($"Quedaron {stranded} botones sin lugar en la grilla nueva.", isError: true);
    }

    /// <summary>
    /// After a page's grid changed, pulls any tile now hanging outside it — at the page root or in
    /// any of its folders — back into the first free cell. Tiles that still fit stay exactly where
    /// they were. Returns how many could not be placed because the grid is now too small for
    /// everything on it.
    /// </summary>
    private async Task<int> RepackPageAsync(Page page)
    {
        var items = await _repository.GetPageItemsAsync(page.Id).ConfigureAwait(true);

        var stranded = 0;

        foreach (var group in items.GroupBy(item => item.ParentId))
        {
            var siblings = group.ToList();

            foreach (var moved in GridLayoutEngine.RepackIntoBounds(siblings, page.Columns, page.Rows))
                await _repository.UpdateItemAsync(moved).ConfigureAwait(true);

            stranded += siblings.Count(s =>
                !GridLayoutEngine.BoundsOf(s).FitsWithin(page.Columns, page.Rows));
        }

        return stranded;
    }

    /// <summary>Same reasoning as <see cref="EditPageAsync"/>: acts on whichever tab asked, not only the open one.</summary>
    /// <summary>
    /// Common grid sizes, offered as one-click buttons on the breadcrumb bar of every page and
    /// every group — a page with only a handful of buttons reads better small, and this is meant
    /// to be the fast path there, with "Configurar página" left for anything in between.
    /// </summary>
    public static IReadOnlyList<GridSizePreset> GridSizePresets { get; } = new[]
    {
        new GridSizePreset(4, 3),
        new GridSizePreset(6, 4),
        new GridSizePreset(8, 5),
        new GridSizePreset(10, 6)
    };

    /// <summary>
    /// Applies one of the preset sizes to the page being looked at.
    /// </summary>
    /// <remarks>
    /// The grid belongs to the page, not to whichever folder happens to be open — every folder on
    /// a page shares its size, the same way they share its columns and rows today — so this reaches
    /// <see cref="SelectedPage"/> directly rather than needing to know which folder is on screen.
    /// </remarks>
    [RelayCommand]
    private async Task SetGridSizeAsync(string? size)
    {
        if (SelectedPage is not { } page) return;
        if (ParseSize(size) is not (int columns, int rows)) return;
        if (columns == page.Columns && rows == page.Rows) return;

        // Same warning EditPageAsync's dialog gives for a shrink — a tile outside the new bounds
        // is a tile nobody can reach, and a one-click preset must not do that silently.
        if ((columns < page.Columns || rows < page.Rows)
            && !_dialogs.Confirm("Achicar la grilla",
                $"Reducir «{page.Name}» a {columns}×{rows} puede dejar botones fuera de la vista. " +
                $"Movelos antes de achicar de {page.Columns}×{page.Rows}.",
                destructive: true))
            return;

        page.Columns = columns;
        page.Rows = rows;

        await _repository.UpdatePageAsync(page).ConfigureAwait(true);
        var stranded = await RepackPageAsync(page).ConfigureAwait(true);

        OnPropertyChanged(nameof(Columns));
        OnPropertyChanged(nameof(Rows));

        var index = Pages.IndexOf(page);
        if (index >= 0) Pages[index] = page;

        await LoadItemsAsync().ConfigureAwait(true);
        SetStatus(stranded > 0
            ? $"«{page.Name}» ahora es de {columns}×{rows}. Quedaron {stranded} botones sin lugar."
            : $"«{page.Name}» ahora es de {columns}×{rows}.");
        AdvanceOnboardingIfWaiting(3);
    }

    private static (int Columns, int Rows)? ParseSize(string? size)
    {
        if (size is null) return null;

        var parts = size.Split('x');
        return parts.Length == 2 && int.TryParse(parts[0], out var c) && int.TryParse(parts[1], out var r)
            ? (c, r)
            : null;
    }

    [RelayCommand]
    private async Task DeletePageAsync(Page? target = null)
    {
        if ((target ?? SelectedPage) is not { } page) return;

        if (Pages.Count <= 1)
        {
            SetStatus("El perfil tiene que conservar al menos una página.", isError: true);
            return;
        }

        if (!_dialogs.Confirm("Eliminar página",
                $"Se va a eliminar «{page.Name}» con todos sus botones. Esta acción no se puede deshacer.",
                destructive: true))
            return;

        await _repository.DeletePageAsync(page.Id).ConfigureAwait(true);
        Pages.Remove(page);

        // Deleting a page nobody was looking at should not change what is on screen.
        if (ReferenceEquals(page, SelectedPage)) SelectedPage = Pages.FirstOrDefault();
    }

    // ---------- Backup ----------

    [RelayCommand]
    private async Task ExportProfileAsync()
    {
        if (SelectedProfile is not { } profile) return;

        var suggested = Sanitise(profile.Name) + DeckPackService.FileExtension;
        if (_dialogs.PickSaveFile(DeckPackService.FileDialogFilter, "Exportar perfil", suggested) is not { } destination)
            return;

        await _packs.ExportProfileAsync(profile.Id, destination).ConfigureAwait(true);
        SetStatus($"Perfil exportado a {System.IO.Path.GetFileName(destination)}.");
    }

    [RelayCommand]
    private async Task ExportFolderAsync(DeckItemViewModel? item)
    {
        if (item is not { IsFolder: true }) return;

        var suggested = Sanitise(item.Label) + DeckPackService.FileExtension;
        if (_dialogs.PickSaveFile(DeckPackService.FileDialogFilter, "Exportar grupo", suggested) is not { } destination)
            return;

        await _packs.ExportFolderAsync(item.Id, destination).ConfigureAwait(true);
        SetStatus($"Grupo exportado a {System.IO.Path.GetFileName(destination)}.");
    }

    [RelayCommand]
    private async Task ImportProfileAsync()
    {
        if (_dialogs.PickOpenFile(DeckPackService.FileDialogFilter, "Importar paquete") is not { } source) return;

        var profile = await _packs.ImportAsync(source).ConfigureAwait(true);

        Profiles.Add(profile);
        SelectedProfile = profile;
        SetStatus($"Se importó «{profile.Name}».");
    }

    /// <summary>
    /// Brings a pack in as a group on the page being looked at, instead of as a profile of its own.
    /// </summary>
    /// <remarks>
    /// Which of the two imports is right depends on what the pack is for. A backup is a whole deck
    /// and wants to arrive as one. A template is a shelf of plugins, and a shelf is only worth
    /// anything next to the other shelves — filed as a separate profile it would sit behind a
    /// switch that takes everything else away to get to it, which is the opposite of what a
    /// template is for.
    /// </remarks>
    [RelayCommand]
    private async Task ImportGroupAsync()
    {
        if (SelectedPage is not { } page) return;

        var sources = _dialogs.PickOpenFiles(DeckPackService.FileDialogFilter, "Importar grupos acá");
        if (sources.Count == 0) return;

        await ImportGroupsAsync(page, sources).ConfigureAwait(true);
    }

    /// <summary>
    /// Brings packs in from a file, asking first what they should turn into.
    /// </summary>
    /// <remarks>
    /// A pack does not say what it is for: the same file can be a backup, a shelf of plugins
    /// somebody put together, or a single group sent over. Guessing means being wrong about half
    /// of them, and it costs something either way — a shelf filed as a group is a press away from
    /// everything on it, and a group given a whole page is a tab holding four buttons. The menu
    /// that says «acá» still means acá; this one asks.
    /// </remarks>
    [RelayCommand]
    private async Task ImportPacksAsync()
    {
        if (SelectedProfile is null) return;

        var sources = _dialogs.PickOpenFiles(DeckPackService.FileDialogFilter, "Importar paquetes");
        if (sources.Count == 0) return;

        if (_dialogs.AskImportTarget(sources.Count) is not { } target) return;

        await BringInAsync(sources, target).ConfigureAwait(true);
    }

    /// <summary>
    /// Installs the templates that travel inside the app.
    /// </summary>
    /// <remarks>
    /// The same import as a file picked off disk, and deliberately so: the templates are ordinary
    /// packs, and giving them a path of their own would be a second importer to keep working.
    /// They are written to a temporary file first only because importing reads a path.
    /// <para>
    /// Where they land is asked in the picker rather than settled here. Most of these are shelves
    /// of fifteen or twenty plugins, which is a page and not a group — but somebody filling one
    /// page with the three brands they actually use is doing something just as sensible, and only
    /// they know which of the two they are doing.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private async Task InstallTemplatesAsync()
    {
        if (SelectedProfile is null) return;

        var chosen = _dialogs.PickTemplates(_templates.List());
        if (chosen.Templates.Count == 0) return;

        var scratch = Path.Combine(
            Path.GetTempPath(), "digitaldeck-plantillas", Guid.NewGuid().ToString("n"));

        try
        {
            var files = new List<string>();

            foreach (var template in chosen.Templates) files.Add(_templates.ExtractTo(template, scratch));

            await BringInAsync(files, chosen.Target).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            SetStatus($"No pude preparar las plantillas: {ex.Message}", isError: true);
        }
        finally
        {
            try { Directory.Delete(scratch, recursive: true); }
            catch (IOException) { /* A temporary folder left behind is not worth a message. */ }
        }
    }

    /// <summary>Sends a batch of packs wherever the user said they should go.</summary>
    private Task BringInAsync(IReadOnlyList<string> sources, PackImportTarget target)
    {
        if (target == PackImportTarget.NewPage)
            return SelectedProfile is { } profile ? ImportAsPagesAsync(profile, sources) : Task.CompletedTask;

        return SelectedPage is { } page ? ImportGroupsAsync(page, sources) : Task.CompletedTask;
    }

    /// <summary>
    /// Brings a batch of packs in as pages of their own, and says how it went.
    /// </summary>
    /// <remarks>
    /// The tab strip moves to the first page that arrived. Installing nine shelves and being left
    /// looking at the page you started on gives no sign that anything happened — and the first is
    /// the one to land on rather than the last, because that is where what you just installed
    /// begins.
    /// </remarks>
    private async Task ImportAsPagesAsync(Profile profile, IReadOnlyList<string> sources)
    {
        var added = new List<Page>();
        var failed = new List<string>();

        foreach (var source in sources)
        {
            try
            {
                added.AddRange(await _packs.ImportAsPagesAsync(source, profile.Id).ConfigureAwait(true));
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
            {
                failed.Add($"{Path.GetFileName(source)}: {ex.Message}");
            }
        }

        foreach (var page in added) Pages.Add(page);

        if (added.Count > 0) SelectedPage = added[0];

        SetStatus(Summarise(added.Count, "página", "páginas", failed), isError: failed.Count > 0);
    }

    /// <summary>Places a batch of packs on a page as groups, and says how it went.</summary>
    private async Task ImportGroupsAsync(Page page, IReadOnlyList<string> sources)
    {
        var added = new List<DeckItem>();
        var failed = new List<string>();

        // One pack at a time and in the order picked, not in parallel: each import reads the
        // current contents of the page to find a free cell, so two running together could both
        // see the same empty cell and land on top of each other.
        foreach (var source in sources)
        {
            try
            {
                added.AddRange(await _packs
                    .ImportIntoPageAsync(source, page.Id, CurrentFolderId)
                    .ConfigureAwait(true));
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
            {
                // A full page and a file that is not a pack are both things the user can act on,
                // so they are collected here rather than stopping the rest of the batch — one bad
                // file in a folder full of templates should not cost the other nine.
                failed.Add($"{Path.GetFileName(source)}: {ex.Message}");
            }
        }

        foreach (var item in added) Items.Add(new DeckItemViewModel(item, _icons));

        SetStatus(Summarise(added.Count, "grupo", "grupos", failed), isError: failed.Count > 0);
    }

    private static string Summarise(
        int addedCount,
        string singular,
        string plural,
        IReadOnlyList<string> failed)
    {
        var added = addedCount switch
        {
            0 => null,
            1 => $"Se agregó 1 {singular}.",
            _ => $"Se agregaron {addedCount} {plural}."
        };

        if (failed.Count == 0) return added ?? "No se agregó nada.";

        var problem = failed.Count == 1
            ? $"No se pudo con {failed[0]}"
            : $"No se pudo con {failed.Count}: {string.Join(" · ", failed)}";

        return added is null ? problem : $"{added} {problem}";
    }

    // ---------- Ableton ----------

    /// <summary>
    /// Puts the full explanation in the status bar.
    /// </summary>
    /// <remarks>
    /// The indicator is a coloured dot with one word next to it, which is enough to notice but not
    /// enough to act on. Clicking it is how the user finds out that the port is taken or the script
    /// needs reinstalling.
    /// </remarks>
    [RelayCommand]
    /// <summary>
    /// The indicator was clicked.
    /// </summary>
    /// <remarks>
    /// When the script is missing or stale there is something to do about it, and a line in the
    /// status bar is not it: the setup screen opens instead. Everything else — Live simply closed,
    /// the port taken by something else — is a sentence, so it stays a sentence.
    /// </remarks>
    private void ShowAbletonStatus()
    {
        if (!_ableton.Status.IsUsable && _remoteScript.Inspect().NeedsAttention)
        {
            _dialogs.ShowRemoteScriptSetup();
            return;
        }

        SetStatus(AbletonDetail, isError: AbletonState is AbletonConnectionState.VersionMismatch
                                                       or AbletonConnectionState.Unavailable);
    }

    private void OnAbletonStatusChanged(AbletonStatus status)
    {
        // Arrives on the socket's thread. CheckAccess rather than an unconditional post so that a
        // change raised on this thread is applied before the caller looks at it.
        OnUi(() => ApplyAbletonStatus(status));
    }

    private void ApplyAbletonStatus(AbletonStatus status)
    {
        AbletonState = status.State;
        AbletonDetail = status.Detail;
        AbletonLabel = status.State switch
        {
            AbletonConnectionState.Connected => "Live",
            AbletonConnectionState.Connecting => "Buscando…",
            AbletonConnectionState.VersionMismatch => "Script viejo",
            AbletonConnectionState.Unavailable => "Sin puerto",
            _ => "Sin Live"
        };
    }

    // ---------- What Live is doing, on the buttons ----------

    private LiveSongState _songState;
    private LiveTracksState _trackStates;

    private void OnSongStateChanged(LiveSongState state)
    {
        // Arrives on the socket's thread, like the status above.
        OnUi(() => ApplySongState(state));
    }

    private void ApplySongState(LiveSongState state)
    {
        _songState = state;

        foreach (var tile in Items) ApplyLight(tile);
    }

    private void OnTrackStatesChanged(LiveTracksState tracks)
    {
        OnUi(() => ApplyTrackStates(tracks));
    }

    private void ApplyTrackStates(LiveTracksState tracks)
    {
        _trackStates = tracks;

        foreach (var tile in Items) ApplyLight(tile);
    }

    private void ApplyLight(DeckItemViewModel tile) =>
        tile.IsLit = LiveButtonLight.IsLit(tile.Model.Action, _songState, _trackStates);

    private void OnAbletonScriptFailed(string message)
    {
        OnUi(() => SetStatus(message, isError: true));
    }

    // ---------- Helpers ----------

    public void SetStatus(string message, bool isError = false)
    {
        StatusMessage = message;
        StatusIsError = isError;

        _statusTimer.Change(StatusLinger, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Runs something on the UI thread, straight away when that is already where we are.
    /// </summary>
    /// <remarks>
    /// Directly rather than always posting: several of these arrive on the bridge's socket thread
    /// and several do not, and a change raised on the UI thread has to be applied before the caller
    /// looks at the result.
    /// </remarks>
    private void OnUi(Action action)
    {
        if (_ui.IsCurrent) action();
        else _ui.Post(action);
    }

    /// <summary>
    /// Runs work started by a selection change, surfacing any failure instead of tearing the app
    /// down. The task is kept rather than fired and forgotten so a failure can never go unobserved
    /// and so tests can wait for a load to finish.
    /// </summary>
    private void RunSafely(Func<Task> work) => _pending = ExecuteSafelyAsync(work);

    private async Task ExecuteSafelyAsync(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("Algo salió mal", ex.Message);
        }
    }

    /// <summary>Waits for the load cascade to come to rest. Used by tests.</summary>
    internal async Task SettleAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var pending = _pending;
            await pending.ConfigureAwait(true);

            // Loading a profile starts a page load, which starts an item load; keep going until
            // no new work was queued while waiting.
            if (ReferenceEquals(pending, _pending)) return;
        }
    }

    private static (int Col, int Row)? ParseCell(string? cell)
    {
        if (cell is null) return null;

        var parts = cell.Split(',');
        return parts.Length == 2 && int.TryParse(parts[0], out var col) && int.TryParse(parts[1], out var row)
            ? (col, row)
            : null;
    }

    private static string Sanitise(string name)
    {
        var cleaned = new string(name
            .Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)
            .ToArray())
            .Trim();

        return cleaned.Length == 0 ? "digitaldeck" : cleaned;
    }
}
