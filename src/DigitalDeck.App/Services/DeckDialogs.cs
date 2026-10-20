using System.Windows;
using DigitalDeck.Integrations.Ableton;
using DigitalDeck.ViewModels;
using DigitalDeck.App.Views;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core;
using DigitalDeck.Data;
using DigitalDeck.Data.Packs;
using Microsoft.Win32;

namespace DigitalDeck.App.Services;

/// <inheritdoc cref="IDeckDialogs"/>
public sealed class DeckDialogs : IDeckDialogs
{
    private readonly IconStore _icons;
    private readonly IAbletonClient _ableton;
    private readonly IAbletonMixer _abletonMixer;
    private readonly WindowPlacementService _placement;
    private readonly IRemoteScriptInstaller _remoteScript;
    private readonly IMonitors _monitors;
    private readonly AbletonPortSettings _ports;
    private readonly VoiceService _voice;
    private readonly IDeckRepository _repository;
    private readonly DeckPaths _paths;

    /// <summary>The one live mixer window, or null when it is closed. See <see cref="ShowMixer"/>.</summary>
    private MixerWindow? _mixer;

    public DeckDialogs(
        IconStore icons,
        IAbletonClient ableton,
        IAbletonMixer abletonMixer,
        IRemoteScriptInstaller remoteScript,
        IMonitors monitors,
        AbletonPortSettings ports,
        VoiceService voice,
        IDeckRepository repository,
        WindowPlacementService placement,
        DeckPaths paths)
    {
        _icons = icons;
        _ableton = ableton;
        _abletonMixer = abletonMixer;
        _remoteScript = remoteScript;
        _monitors = monitors;
        _ports = ports;
        _voice = voice;
        _repository = repository;
        _placement = placement;
        _paths = paths;
    }

    /// <summary>
    /// Every dialog opens the same way: owned by the shell, and kept inside the usable screen.
    /// </summary>
    /// <remarks>
    /// The second part is not optional here. These windows size themselves to their content, so one
    /// that grows when a panel opens will happily grow straight under the taskbar unless something
    /// pulls it back.
    /// </remarks>
    private T Prepare<T>(T window) where T : Window
    {
        window.Owner = Owner;
        WindowFit.KeepOnScreen(window, _monitors);

        return window;
    }

    /// <summary>
    /// A companion window: owned by the shell, and free to go wherever the user drags it.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="Prepare"/>. Keeping a window on screen means re-checking its
    /// position every time it moves, and the monitor it is "on" is the one holding most of it — so
    /// halfway across a boundary the window still counts as being on the screen it is leaving, and
    /// gets pulled back to that edge. For a dialog that grows under the taskbar that is exactly
    /// right. For a window whose whole purpose is to be dragged onto the touch screen it is a trap
    /// with no way out, and these size themselves rather than growing with their content, so none
    /// of what that guard protects against applies to them.
    /// </remarks>
    private T PrepareCompanion<T>(T window) where T : Window
    {
        window.Owner = Owner;

        return window;
    }

    /// <summary>The window dialogs should centre on — resolved late so it survives a restart of the shell.</summary>
    private static Window? Owner => Application.Current?.MainWindow;

    public string? AskText(string title, string prompt, string? initialValue = null)
    {
        var window = Prepare(new TextPromptWindow(title, prompt, initialValue));
        return window.ShowDialog() == true ? window.Value : null;
    }

    public bool Confirm(string title, string message, bool destructive = false) =>
        MessageBox.Show(
            Owner!, message, title,
            MessageBoxButton.OKCancel,
            destructive ? MessageBoxImage.Warning : MessageBoxImage.Question,
            destructive ? MessageBoxResult.Cancel : MessageBoxResult.OK) == MessageBoxResult.OK;

    public void ShowError(string title, string message) =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string title, string message) =>
        MessageBox.Show(Owner!, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool EditItem(DeckItem item, IReadOnlyCollection<string> takenNames) =>
        Prepare(new ItemEditorWindow(item, _icons, _ableton, takenNames)).ShowDialog() == true;

    public bool EditPage(Page page) =>
        Prepare(new PageEditorWindow(page)).ShowDialog() == true;

    public TemplateInstall PickTemplates(IReadOnlyList<DeckTemplate> templates)
    {
        var window = Prepare(new TemplatesWindow(templates));

        return window.ShowDialog() == true
            ? new TemplateInstall(window.Chosen, window.Target)
            : TemplateInstall.None;
    }

    public PackImportTarget? AskImportTarget(int packCount)
    {
        var window = Prepare(new ImportFormatWindow(packCount));

        return window.ShowDialog() == true ? window.Target : null;
    }

    public bool ShowRemoteScriptSetup()
    {
        var window = Prepare(new RemoteScriptWindow(_remoteScript, _ports));
        window.ShowDialog();

        return window.Installed;
    }

    public void ShowVoiceSettings() => Prepare(new VoiceSettingsWindow(_voice)).ShowDialog();

    public void ShowMixer()
    {
        // Single instance, and for a sharp reason: each open mixer asks Live to push every fader
        // move, and two of them would double that traffic to show the same thing twice.
        if (_mixer is { IsLoaded: true } open)
        {
            if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }

        var window = PrepareCompanion(new MixerWindow(_abletonMixer, _ableton, _placement));
        window.Closed += (_, _) => _mixer = null;
        _mixer = window;
        window.Show();
    }

    public void ShowDuplicateNames()
    {
        if ((Owner as MainWindow)?.DataContext is not MainViewModel { SelectedProfile: { } profile }) return;

        Prepare(new DuplicateNamesWindow(_repository, profile.Id)).ShowDialog();
    }

    public string? PickOpenFile(string filter, string title)
    {
        var dialog = new OpenFileDialog { Filter = filter, Title = title, CheckFileExists = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public IReadOnlyList<string> PickOpenFiles(string filter, string title)
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            Title = title,
            CheckFileExists = true,
            Multiselect = true
        };

        return dialog.ShowDialog(Owner) == true ? dialog.FileNames : Array.Empty<string>();
    }

    public string? PickSaveFile(string filter, string title, string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            Title = title,
            FileName = suggestedName,
            OverwritePrompt = true
        };

        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }
}
