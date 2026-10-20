using DigitalDeck.Core.Voice;
using Xunit;

namespace DigitalDeck.Tests;

public sealed class VoiceCommandMatcherTests
{
    private static VoiceCommandMatcher Deck(params string[] labels) =>
        new(labels.Select((label, i) => new VoiceTarget($"id{i}", label)));

    [Fact]
    public void SayingTheNameFindsTheButton()
    {
        var match = Deck("Pro-Q 4", "Serum", "Reverb").Match("abrí Pro-Q 4");

        Assert.NotNull(match);
        Assert.Equal("Pro-Q 4", match.Target.Label);
    }

    /// <remarks>
    /// The whole reason this matches on distance rather than equality: a Spanish model handed an
    /// English plugin name returns the nearest Spanish-sounding thing, and it is never spelled the
    /// way the tile is.
    /// </remarks>
    [Theory]
    [InlineData("serun")]
    [InlineData("serum")]
    [InlineData("cerum")]
    public void AMisheardNameStillLands(string heard)
    {
        var match = Deck("Serum", "Reverb", "Compresor").Match(heard);

        Assert.NotNull(match);
        Assert.Equal("Serum", match.Target.Label);
    }

    [Fact]
    public void SayingMoreThanTheNameStillFindsIt()
    {
        var match = Deck("Compresor", "Reverb").Match("poneme el compresor de bus");

        Assert.NotNull(match);
        Assert.Equal("Compresor", match.Target.Label);
    }

    // ---------- Refusing ----------

    /// <remarks>
    /// The case this class exists for. Both are plausible, so pressing either is a coin flip, and a
    /// coin flip that loads a plugin into a track is worse than doing nothing.
    /// </remarks>
    [Fact]
    public void TwoNamesThatAreEquallyCloseAreRefusedRatherThanGuessed()
    {
        var match = Deck("Reverb 1", "Reverb 2").Match("reverb");

        Assert.Null(match);
    }

    [Fact]
    public void SomethingThatIsNotAButtonAtAllMatchesNothing()
    {
        var match = Deck("Pro-Q 4", "Serum", "Compresor").Match("qué hora es");

        Assert.Null(match);
    }

    [Fact]
    public void AnEmptyDeckMatchesNothing() =>
        Assert.Null(Deck().Match("reverb"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SilenceMatchesNothing(string? heard) =>
        Assert.Null(Deck("Reverb").Match(heard));

    /// <remarks>
    /// A near-tie between two tiles with the <i>same</i> name is not ambiguous in any way the user
    /// would notice — the ranking folder is built from copies of tiles that live elsewhere, so this
    /// arrangement is normal rather than exotic.
    /// </remarks>
    [Fact]
    public void TwoTilesWithTheSameNameAreNotATie()
    {
        var match = new VoiceCommandMatcher([
            new VoiceTarget("a", "Reverb"),
            new VoiceTarget("b", "Reverb")
        ]).Match("reverb");

        Assert.NotNull(match);
        Assert.Equal("Reverb", match.Target.Label);
    }

    /// <remarks>
    /// A version number is the whole difference between two tiles here, and saying it has to be
    /// enough to tell them apart.
    /// </remarks>
    [Fact]
    public void SayingTheVersionPicksTheRightOne()
    {
        var deck = Deck("Pro-Q 2", "Pro-Q 4");

        Assert.Equal("Pro-Q 4", deck.Match("abrí pro q cuatro")?.Target.Label);
        Assert.Equal("Pro-Q 2", deck.Match("abrí pro q dos")?.Target.Label);
    }

    // ---------- Vocabulary ----------

    [Fact]
    public void TheVocabularyIsEveryNameInItsSpokenForm()
    {
        var vocabulary = Deck("Pro-Q 4", "Compresión").Vocabulary;

        Assert.Contains("pro q cuatro", vocabulary);
        Assert.Contains("compresion", vocabulary);
    }

    /// <remarks>
    /// The bug this is here to stop coming back: someone says "abrí estudio" at a folder called
    /// Estudio and nothing happens. A restricted recogniser can only return words it was handed, so
    /// with a grammar of nothing but button names the verb has nowhere to land — and the decoder
    /// does not politely drop just that word, it can fail to place the whole sentence. The button
    /// was named perfectly, the matcher would have stripped the verb happily, and the transcript
    /// never arrives to be stripped.
    /// </remarks>
    [Fact]
    public void TheVocabularyIncludesTheWordsPeopleStartACommandWith()
    {
        var vocabulary = Deck("Estudio").Vocabulary;

        Assert.Contains("estudio", vocabulary);
        Assert.Contains("abri", vocabulary);
        Assert.Contains("poneme", vocabulary);
        Assert.Contains("el", vocabulary);
    }

    /// <remarks>
    /// And the other half of it: what the recogniser is now allowed to return still has to resolve
    /// to the button once the verb is taken back off.
    /// </remarks>
    [Fact]
    public void AVerbTheRecogniserWasAllowedToHearIsStillStrippedBeforeMatching()
    {
        var match = Deck("Estudio", "Serum").Match("abri estudio");

        Assert.NotNull(match);
        Assert.Equal("Estudio", match.Target.Label);
    }

    /// <remarks>
    /// The command words must not become buttons themselves. They are in the vocabulary so the
    /// recogniser can hear them, not so someone can press one by saying "el".
    /// </remarks>
    [Fact]
    public void ACommandWordOnItsOwnPressesNothing() =>
        Assert.Null(Deck("Estudio", "Serum").Match("poneme"));

    [Fact]
    public void TheVocabularyDoesNotRepeatANameThatAppearsTwice()
    {
        var names = NamesIn(new VoiceCommandMatcher([
            new VoiceTarget("a", "Reverb"),
            new VoiceTarget("b", "Reverb")
        ]));

        Assert.Equal(["reverb"], names);
    }

    [Fact]
    public void ATileWithNoUsableNameIsLeftOutRatherThanMatchingEverything() =>
        Assert.Equal(["reverb"], NamesIn(Deck("Reverb", "   ", "---")));

    /// <summary>
    /// The vocabulary minus the command words, which is the part that comes from the deck.
    /// </summary>
    /// <remarks>
    /// Subtracted rather than counted, so these keep asserting what they are about — which button
    /// names went in — instead of breaking every time a word is added to the command list.
    /// </remarks>
    private static List<string> NamesIn(VoiceCommandMatcher matcher) =>
        matcher.Vocabulary.Except(VoicePhrase.CommandWords).ToList();

    [Fact]
    public void ConfidenceIsReportedSoItCanBeShown()
    {
        var exact = Deck("Serum").Match("serum");

        Assert.NotNull(exact);
        Assert.Equal(1, exact.Confidence);
    }
}
