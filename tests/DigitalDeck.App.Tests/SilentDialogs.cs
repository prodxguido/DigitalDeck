using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using DigitalDeck.Core.Model;
using DigitalDeck.Data.Packs;

namespace DigitalDeck.App.Tests;

/// <summary>
/// Dialogs that never ask anything, for tests that are not about dialogs.
/// </summary>
/// <remarks>
/// Its own file rather than nested inside one test class: more than one of them needs a view model
/// and none of them needs a second copy of seventeen empty methods.
/// </remarks>
public sealed class SilentDialogs : IDeckDialogs
{
    public string? AskText(string title, string prompt, string? initialValue = null) => null;

    public bool Confirm(string title, string message, bool destructive = false) => false;

    public void ShowError(string title, string message) { }

    public void ShowInfo(string title, string message) { }

    public bool EditItem(DeckItem item, IReadOnlyCollection<string> takenNames) => false;

    public bool EditPage(Page page) => false;

    /// <summary>What <see cref="PickTemplates"/> answers with. Empty stands for a cancelled dialog.</summary>
    public IReadOnlyList<DeckTemplate> TemplatesToChoose { get; set; } = [];

    /// <summary>
    /// Which of the two destinations the picker comes back with. Starts on the one the real window
    /// starts on, so a test that does not care gets what a user who does not care would get.
    /// </summary>
    public PackImportTarget TargetToChoose { get; set; } = PackImportTarget.NewPage;

    /// <summary>What the app offered, so a test can check the list without opening a window.</summary>
    public IReadOnlyList<DeckTemplate> OfferedTemplates { get; private set; } = [];

    public TemplateInstall PickTemplates(IReadOnlyList<DeckTemplate> templates)
    {
        OfferedTemplates = templates;

        return new TemplateInstall(TemplatesToChoose, TargetToChoose);
    }

    public PackImportTarget? AskImportTarget(int packCount) => TargetToChoose;

    public bool ShowRemoteScriptSetup() => false;

    public void ShowVoiceSettings() { }


    public void ShowMixer() { }

    public void ShowDuplicateNames() { }

    public string? PickOpenFile(string filter, string title) => null;

    public IReadOnlyList<string> PickOpenFiles(string filter, string title) => [];

    public string? PickSaveFile(string filter, string title, string suggestedName) => null;
}
