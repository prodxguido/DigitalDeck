namespace DigitalDeck.Core.Abstractions;

/// <summary>
/// Turns 16 kHz mono samples into words.
/// </summary>
/// <remarks>
/// Core stops at "something turns audio into text". What sits behind it — an offline model, a
/// service, a stub in a test — is nobody else's business, and that boundary is what lets the
/// assistant's whole flow be proved without a model on disk.
/// </remarks>
public interface IVoiceRecogniser : IDisposable
{
    /// <summary>
    /// Narrows what may be recognised to this list of phrases.
    /// </summary>
    /// <remarks>
    /// The single largest accuracy win available to this feature, and the reason the interface has
    /// this method at all rather than just taking audio. Asking "which of these forty phrases was
    /// that, or none" is a different problem from "what did they say", and a recogniser that can be
    /// told the answer set gets the right one far more often — particularly for plugin names, which
    /// are brand words a Spanish model has never seen.
    /// <para>
    /// An implementation that cannot be narrowed is free to ignore this; the matcher above it
    /// tolerates a loose transcript.
    /// </para>
    /// </remarks>
    void Restrict(IReadOnlyList<string> vocabulary);

    /// <summary>
    /// Pushes audio in, and returns a finished phrase when the speaker has stopped.
    /// </summary>
    /// <returns>The transcript once an utterance ends, or null while one is still being spoken.</returns>
    string? Feed(short[] samples);

    /// <summary>
    /// Ends the current utterance early and returns whatever was heard.
    /// </summary>
    /// <remarks>
    /// Needed because the listening window can close before the speaker pauses. Without it, a
    /// command said right up against the timeout would be discarded after the user had already
    /// finished saying it.
    /// </remarks>
    string? Flush();

    /// <summary>Throws away any partial utterance, ready for a fresh one.</summary>
    void Reset();
}
