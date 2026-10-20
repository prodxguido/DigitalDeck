namespace DigitalDeck.Core.Abstractions;

/// <summary>
/// The words a recogniser is able to produce at all.
/// </summary>
/// <remarks>
/// A restricted recogniser is handed a grammar built from the button names, but it can only put
/// words from its own model into an answer: anything else is dropped while the grammar is being
/// built, and the button it came from goes quiet for good. Nothing says so at the time, which is
/// what makes it worth asking about up front rather than discovering by talking to a deck that
/// ignores you.
/// <para>
/// Core stops at "something can say whether a word is pronounceable". Where the list comes from —
/// a model on disk, a fake in a test — belongs to the implementation.
/// </para>
/// </remarks>
public interface IVoiceLexicon
{
    /// <summary>How many words the model can produce. Useful for saying so in the UI.</summary>
    int Count { get; }

    /// <summary>True when the recogniser can return this exact word.</summary>
    /// <param name="word">A single lower-case word, already stripped of punctuation.</param>
    bool Knows(string word);

    /// <summary>
    /// True when the model has the word under some accentuation, even if not this one.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Knows"/> because the two failures need different advice. A word the
    /// model has never seen is a naming problem the user can fix by renaming the button. A word the
    /// model only knows accented — it has <c>compresión</c> but not <c>compresion</c> — is ours:
    /// the grammar is built from a form with the accents stripped, so the model never gets offered
    /// the spelling it actually holds. Telling a user to rename their way out of that would be
    /// advice that cannot work.
    /// </remarks>
    bool KnowsIgnoringAccents(string word);
}
