using DigitalDeck.Core.Model;
using DigitalDeck.Data.Packs;

namespace DigitalDeck.ViewModels;

/// <summary>
/// Everything the view model needs to ask the user, kept behind an interface so the view model
/// never touches a Window type.
/// </summary>
public interface IDeckDialogs
{
    /// <summary>Single-line text prompt. Returns null when cancelled.</summary>
    string? AskText(string title, string prompt, string? initialValue = null);

    /// <summary>Yes/no question. <paramref name="destructive"/> styles it as a warning.</summary>
    bool Confirm(string title, string message, bool destructive = false);

    void ShowError(string title, string message);

    void ShowInfo(string title, string message);

    /// <summary>Opens the button editor. Returns true when the user saved.</summary>
    /// <param name="takenNames">
    /// Names other tiles in the profile already answer to, normalised the way the voice assistant
    /// compares them. The editor refuses to save a name from this list.
    /// </param>
    bool EditItem(DeckItem item, IReadOnlyCollection<string> takenNames);

    /// <summary>Opens the page settings editor. Returns true when the user saved.</summary>
    bool EditPage(Page page);

    /// <summary>
    /// Shows the templates that ship inside the app and returns the ones ticked, along with what
    /// the user wants them to become.
    /// </summary>
    /// <remarks>
    /// A picker and not an installer: where a page or a group actually lands depends on the
    /// profile, page and folder being looked at, which the view model knows and a dialog has no
    /// business being told. It answers the question, not the placement.
    /// </remarks>
    TemplateInstall PickTemplates(IReadOnlyList<DeckTemplate> templates);

    /// <summary>
    /// Asks what a batch of packs picked off disk should turn into. Null when cancelled.
    /// </summary>
    /// <param name="packCount">How many were picked, so the question can say so.</param>
    PackImportTarget? AskImportTarget(int packCount);

    /// <summary>
    /// Opens the screen that installs the Remote Script into Ableton. True when it ended up installed.
    /// </summary>
    bool ShowRemoteScriptSetup();

    /// <summary>Opens the voice assistant's settings: the switch, the microphone and the key.</summary>
    void ShowVoiceSettings();

    /// <summary>
    /// Opens the mixer window — one fader per channel of the open Live set, sized for a finger. A
    /// companion window rather than a modal dialog: already open means bring it forward.
    /// </summary>
    void ShowMixer();

    /// <summary>
    /// Opens the list of buttons the voice assistant cannot tell apart, so one of each pair can be
    /// renamed out of the tie.
    /// </summary>
    void ShowDuplicateNames();

    string? PickOpenFile(string filter, string title);

    /// <summary>Same picker, several files at once. Empty when cancelled.</summary>
    IReadOnlyList<string> PickOpenFiles(string filter, string title);

    string? PickSaveFile(string filter, string title, string suggestedName);
}
