namespace DigitalDeck.Data.Packs;

/// <summary>
/// The shape a pack takes when it comes in.
/// </summary>
/// <remarks>
/// A pack does not say what it is for — the same file can be a backup of a whole deck, a shelf of
/// plugins, or a group somebody sent over. Which of these it should become is the user's call and
/// nobody else's, so it is asked rather than guessed.
/// </remarks>
public enum PackImportTarget
{
    /// <summary>A page of its own, next to the pages the profile already has.</summary>
    NewPage,

    /// <summary>One more group on the page being looked at, wherever there is room.</summary>
    CurrentPage
}
