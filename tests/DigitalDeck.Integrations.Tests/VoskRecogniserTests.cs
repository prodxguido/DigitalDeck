using DigitalDeck.Core;
using DigitalDeck.Integrations.Voice;
using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// A test that needs a speech model unpacked in the user's DigitalDeck folder. Skipped when there
/// is none, so a machine that has never enabled voice still runs a green suite.
/// </summary>
public sealed class ModelFactAttribute : FactAttribute
{
    public ModelFactAttribute()
    {
        if (VoskRecogniser.FindModel(new DeckPaths().VoiceDirectory) is null)
            Skip = "No hay un modelo de voz instalado.";
    }
}

/// <summary>
/// What only the real model can answer: that the native library loads, and that silence does not
/// press buttons.
/// </summary>
/// <remarks>
/// The second half is the one worth having. A grammar-restricted recogniser is being asked "which
/// of these forty phrases was that", and the failure mode of that question is answering it when the
/// honest answer is "none of them" — which is how a cough loads a plugin.
/// </remarks>
public sealed class VoskRecogniserTests
{
    private static readonly string[] Vocabulary = ["pro q cuatro", "serum", "compresor", "reverb"];

    private static VoskRecogniser Open() =>
        new(VoskRecogniser.FindModel(new DeckPaths().VoiceDirectory)!);

    [ModelFact]
    public void TheModelLoadsAndTakesAGrammar()
    {
        using var recogniser = Open();

        recogniser.Restrict(Vocabulary);
        recogniser.Reset();
    }

    /// <remarks>
    /// Four seconds of nothing, which is what the microphone delivers when someone presses the key
    /// and then changes their mind.
    /// </remarks>
    [ModelFact]
    public void SilenceIsNeverHeardAsAButton()
    {
        using var recogniser = Open();
        recogniser.Restrict(Vocabulary);

        var heard = FeedAndFlush(recogniser, Silence(seconds: 4));

        Assert.Null(heard);
    }

    /// <remarks>
    /// Noise rather than silence, because a studio is never actually silent and a recogniser that
    /// only rejects digital zero rejects nothing useful.
    /// </remarks>
    [ModelFact]
    public void RoomNoiseIsNeverHeardAsAButton()
    {
        using var recogniser = Open();
        recogniser.Restrict(Vocabulary);

        var heard = FeedAndFlush(recogniser, Noise(seconds: 4, amplitude: 0.02));

        Assert.Null(heard);
    }

    /// <remarks>
    /// The grammar is rebuilt on every press, since the buttons on screen change as the user
    /// navigates. Rebuilding it repeatedly must not leak or fail.
    /// </remarks>
    [ModelFact]
    public void TheGrammarCanBeRebuiltOverAndOver()
    {
        using var recogniser = Open();

        for (var i = 0; i < 20; i++)
            recogniser.Restrict([$"boton {i}", "serum", "reverb"]);

        Assert.Null(FeedAndFlush(recogniser, Silence(seconds: 1)));
    }

    [ModelFact]
    public void AMissingModelSaysSoRatherThanFailingLater() =>
        Assert.Throws<DirectoryNotFoundException>(() => new VoskRecogniser(Path.GetTempPath()));

    // ---------- Helpers ----------

    private static string? FeedAndFlush(VoskRecogniser recogniser, IEnumerable<short[]> audio)
    {
        foreach (var chunk in audio)
        {
            if (recogniser.Feed(chunk) is { } heard) return heard;
        }

        return recogniser.Flush();
    }

    private static IEnumerable<short[]> Silence(int seconds)
    {
        for (var i = 0; i < seconds * 10; i++)
            yield return new short[1600];   // 100 ms at 16 kHz
    }

    private static IEnumerable<short[]> Noise(int seconds, double amplitude)
    {
        var random = new Random(20260830);

        for (var i = 0; i < seconds * 10; i++)
        {
            var chunk = new short[1600];

            for (var s = 0; s < chunk.Length; s++)
                chunk[s] = (short)((random.NextDouble() * 2 - 1) * amplitude * short.MaxValue);

            yield return chunk;
        }
    }
}
