namespace DigitalDeck.ViewModels;

/// <summary>
/// One change to the deck, expressed as how to put the database back and how to do it again.
/// </summary>
/// <remarks>
/// Neither side touches the visible grid — both are plain repository calls against captured ids
/// and snapshots, and the caller reloads the current page afterwards. That split is what keeps an
/// edit undoable even after the user has navigated somewhere else: the database change still
/// applies, and the grid catches up wherever it happens to be looking.
/// </remarks>
public sealed class DeckEdit
{
    /// <summary>Shown in the status bar after undoing or redoing, e.g. "mover «Synth»".</summary>
    public required string Description { get; init; }

    public required Func<Task> Undo { get; init; }

    public required Func<Task> Redo { get; init; }
}
