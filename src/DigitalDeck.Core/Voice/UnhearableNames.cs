using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Services;

namespace DigitalDeck.Core.Voice;

/// <summary>Why the recogniser cannot produce a word.</summary>
public enum UnhearableReason
{
    /// <summary>The model has never seen it. Renaming the button is the only way out.</summary>
    NotInModel,

    /// <summary>
    /// The model has it, but only spelled with an accent. Renaming cannot help, because the form
    /// handed to the recogniser is stripped of accents on the way in.
    /// </summary>
    AccentedOnly
}

/// <param name="Word">The word as the recogniser is offered it: lower case, no accents.</param>
public sealed record UnhearableWord(string Word, UnhearableReason Reason);

/// <param name="Tile">The button, and where it lives.</param>
/// <param name="Words">The parts of its name the recogniser cannot produce.</param>
public sealed record UnhearableName(DeckSearchHit Tile, IReadOnlyList<UnhearableWord> Words);

/// <summary>
/// Finds the buttons the voice assistant can never fire, because their names contain words its
/// model cannot say.
/// </summary>
/// <remarks>
/// The companion to <see cref="NameCollisions"/>: that one finds names the assistant cannot tell
/// apart, this one finds names it cannot hear at all. Both fail the same way from the outside — the
/// button simply never responds — and both are invisible until somebody goes looking, which is why
/// they are worth reporting in the same place.
/// </remarks>
public static class UnhearableNames
{
    /// <summary>Every tile with at least one word the recogniser cannot produce.</summary>
    public static IReadOnlyList<UnhearableName> Find(
        IReadOnlyList<DeckSearchHit> hits,
        IVoiceLexicon lexicon)
    {
        var found = new List<UnhearableName>();

        // Names repeat across a profile — the favourites folder is a copy of tiles that live
        // elsewhere — and reporting the same word twice would pad the list without adding anything.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var hit in hits)
        {
            var spoken = VoicePhrase.Normalise(hit.Item.Label);

            if (spoken.Length == 0 || !seen.Add(spoken)) continue;

            var words = Check(hit.Item.Label, lexicon);

            if (words.Count > 0) found.Add(new UnhearableName(hit, words));
        }

        return found;
    }

    /// <summary>
    /// The words of one label the recogniser cannot produce, in the order they are spoken.
    /// </summary>
    /// <remarks>
    /// Checked against the normalised form rather than the label as typed, because that is the form
    /// the grammar is actually built from — punctuation gone, digits spelled out. A button called
    /// <c>Pro-Q 4</c> is offered as <c>pro q cuatro</c>, and it is those three words that either
    /// exist or do not.
    /// </remarks>
    public static IReadOnlyList<UnhearableWord> Check(string? label, IVoiceLexicon lexicon)
    {
        var spoken = VoicePhrase.Normalise(label);

        if (spoken.Length == 0) return [];

        var missing = new List<UnhearableWord>();

        foreach (var word in spoken.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (lexicon.Knows(word)) continue;

            missing.Add(new UnhearableWord(
                word,
                lexicon.KnowsIgnoringAccents(word)
                    ? UnhearableReason.AccentedOnly
                    : UnhearableReason.NotInModel));
        }

        return missing;
    }
}
