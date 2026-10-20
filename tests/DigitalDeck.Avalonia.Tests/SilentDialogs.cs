using DigitalDeck.Core.Model;
using DigitalDeck.Data.Packs;
using DigitalDeck.ViewModels;

namespace DigitalDeck.Avalonia.Tests;

/// <summary>Dialogs that never open a window, for tests that are not about dialogs.</summary>
/// <remarks>
/// The real <c>AvaloniaDialogs</c> opens modals, and a modal pushes a dispatcher frame that runs
/// until somebody closes the window. In a test there is nobody to close it, so the run hangs
/// instead of failing — which is exactly what happened the first time these tests used the real
/// one. Anything that opens a window belongs in a test that drives that window directly.
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
