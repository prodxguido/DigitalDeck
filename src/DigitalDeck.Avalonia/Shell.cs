using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Data.Packs;
using DigitalDeck.ViewModels;

namespace DigitalDeck.Avalonia;

/// <inheritdoc cref="IUiThread"/>
/// <remarks>
/// The whole of what the view models need from Avalonia's threading, and the mirror image of
/// <c>WpfUiThread</c>. Avalonia keeps one dispatcher for the whole application rather than one per
/// thread, so there is nothing to capture here.
/// </remarks>
public sealed class AvaloniaUiThread : IUiThread
{
    public bool IsCurrent => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}

/// <summary>
/// The windows this shell has, and honest silence for the ones it does not.
/// </summary>
/// <remarks>
/// Every method that answers "no" — cancelled, not confirmed, nothing chosen — is a window still
/// to port, and that is the safe shape: the view model treats it exactly as it treats a person who
/// pressed Escape, so nothing is saved and nothing is deleted by a dialog that never opened.
/// </remarks>
public sealed class AvaloniaDialogs : IDeckDialogs
{
    private readonly Func<IAbletonMixer> _mixer;
    private readonly Func<IAbletonClient> _ableton;
    private readonly Func<IRemoteScriptInstaller> _installer;

    /// <summary>The one live mixer window, or null when it is closed.</summary>
    private MixerWindow? _mixerWindow;

    public AvaloniaDialogs(
        Func<IAbletonMixer> mixer,
        Func<IAbletonClient> ableton,
        Func<IRemoteScriptInstaller> installer)
    {
        _mixer = mixer;
        _ableton = ableton;
        _installer = installer;
    }

    public string? AskText(string title, string prompt, string? initialValue = null)
    {
        var ask = new AskWindow(title, prompt, initialValue ?? string.Empty);

        Modal.Show(ask, Modal.MainWindow);

        return ask.Accepted ? ask.Text.Trim() : null;
    }

    public bool Confirm(string title, string message, bool destructive = false)
    {
        var ask = new AskWindow(title, message, cancellable: true);

        Modal.Show(ask, Modal.MainWindow);

        return ask.Accepted;
    }

    public void ShowError(string title, string message) =>
        Modal.Show(new AskWindow(title, message), Modal.MainWindow);

    public void ShowInfo(string title, string message) =>
        Modal.Show(new AskWindow(title, message), Modal.MainWindow);

    public bool EditItem(DeckItem item, IReadOnlyCollection<string> takenNames)
    {
        var editor = new ItemEditorWindow(item, takenNames, _ableton());

        Modal.Show(editor, Modal.MainWindow);

        return editor.Saved;
    }

    public bool EditPage(Page page)
    {
        var window = new PageWindow(page);

        Modal.Show(window, Modal.MainWindow);

        return window.Saved;
    }

    public TemplateInstall PickTemplates(IReadOnlyList<DeckTemplate> templates)
    {
        var window = new TemplatesWindow(templates);

        Modal.Show(window, Modal.MainWindow);

        return window.Result;
    }

    public PackImportTarget? AskImportTarget(int packCount)
    {
        var window = new ImportTargetWindow(packCount);

        Modal.Show(window, Modal.MainWindow);

        return window.Target;
    }

    public bool ShowRemoteScriptSetup()
    {
        var window = new RemoteScriptWindow(_installer());

        Modal.Show(window, Modal.MainWindow);

        return window.Installed;
    }

    public void ShowVoiceSettings() { }

    /// <remarks>
    /// Single instance, for the same sharp reason as in the WPF app: each open mixer asks Live to
    /// push every fader move, and two of them would double that traffic to show the same thing
    /// twice. Closing it drops the subscription.
    /// </remarks>
    public void ShowMixer()
    {
        if (_mixerWindow is { IsVisible: true } open)
        {
            open.Activate();
            return;
        }

        var view = new MixerViewModel(_mixer(), _ableton(), action => Dispatcher.UIThread.Post(action));

        var window = new MixerWindow { DataContext = view };

        window.Closed += (_, _) =>
        {
            view.Dispose();
            _mixerWindow = null;
        };

        _mixerWindow = window;

        window.Show();
        view.Open();
    }

    public void ShowDuplicateNames() { }

    public string? PickOpenFile(string filter, string title) =>
        PickOpenFiles(filter, title, several: false).FirstOrDefault();

    public IReadOnlyList<string> PickOpenFiles(string filter, string title) =>
        PickOpenFiles(filter, title, several: true);

    private static IReadOnlyList<string> PickOpenFiles(string filter, string title, bool several)
    {
        if (Modal.MainWindow?.StorageProvider is not { CanOpen: true } storage) return [];

        var files = Modal.Wait(storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = several,
            FileTypeFilter = FileFilter.Parse(filter)
        }));

        // Only files that are really on disk: a picker can hand back something from a cloud
        // provider that has no local path, and the deck reads these with File.OpenRead.
        return files
            .Select(file => file.TryGetLocalPath())
            .Where(path => path is { Length: > 0 })
            .Select(path => path!)
            .ToList();
    }

    public string? PickSaveFile(string filter, string title, string suggestedName)
    {
        if (Modal.MainWindow?.StorageProvider is not { CanSave: true } storage) return null;

        var file = Modal.Wait(storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = FileFilter.DefaultExtension(filter),
            FileTypeChoices = FileFilter.Parse(filter)
        }));

        return file?.TryGetLocalPath();
    }
}

/// <summary>
/// Live's menu commands that are pressed as keystrokes rather than sent over the bridge.
/// </summary>
/// <remarks>
/// Export, Save and Save As are not on Live's API, so the Windows app reaches them by bringing
/// Live forward and typing the shortcut. Doing that needs a way to press keys into another
/// application, which is the one piece of this that every desktop spells differently — on Windows
/// it is <c>SendInput</c>, on macOS it is a Quartz event with the accessibility permission behind
/// it. Until that exists here, those three buttons say so instead of doing nothing.
/// </remarks>
public sealed class NoLiveKeyboard : ILiveKeyboard
{
    public Task SendAsync(LiveShortcut shortcut, CancellationToken ct = default) =>
        throw new InvalidOperationException(
            "Mandarle teclas a Live todavía no está portado a este shell.");
}

/// <summary>Opening at login, on a platform whose way of doing it is not written yet.</summary>
public sealed class NoStartup : IStartupRegistration
{
    public StartupState Read() => new(false, null);

    public StartupState Set(bool enabled) => new(false, null);
}

/// <summary>Dialogs that never open a window, for when there is no UI to open one into.</summary>
/// <remarks>
/// The <c>--smoke</c> run has no windowing platform at all: it builds the deck, reports what it
/// loaded and leaves. Every one of these answers the way a person who pressed Escape would, which
/// is what keeps a headless run from saving or deleting anything.
/// </remarks>
public sealed class SilentDialogs : IDeckDialogs
{
    public string? AskText(string title, string prompt, string? initialValue = null) => null;

    public bool Confirm(string title, string message, bool destructive = false) => false;

    public void ShowError(string title, string message) { }

    public void ShowInfo(string title, string message) { }

    public bool EditItem(DeckItem item, IReadOnlyCollection<string> takenNames) => false;

    public bool EditPage(Page page) => false;

    public TemplateInstall PickTemplates(IReadOnlyList<DeckTemplate> templates) =>
        new([], PackImportTarget.NewPage);

    public PackImportTarget? AskImportTarget(int packCount) => null;

    public bool ShowRemoteScriptSetup() => false;

    public void ShowVoiceSettings() { }

    public void ShowMixer() { }

    public void ShowDuplicateNames() { }

    public string? PickOpenFile(string filter, string title) => null;

    public IReadOnlyList<string> PickOpenFiles(string filter, string title) => [];

    public string? PickSaveFile(string filter, string title, string suggestedName) => null;
}
