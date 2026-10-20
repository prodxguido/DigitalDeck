using DigitalDeck.Core.Voice;
using Xunit;

namespace DigitalDeck.Tests;

public sealed class VoicePhraseTests
{
    [Theory]
    [InlineData("Pro-Q 4", "pro q cuatro")]
    [InlineData("EQ8", "eq ocho")]
    [InlineData("Serum", "serum")]
    [InlineData("Compresión", "compresion")]
    [InlineData("  Reverb  de   sala ", "reverb de sala")]
    [InlineData("Auto-Filter", "auto filter")]
    public void ALabelIsReducedToWhatSomeoneWouldSay(string label, string expected) =>
        Assert.Equal(expected, VoicePhrase.Normalise(label));

    /// <remarks>
    /// A label writes a version number against the name and nobody says it that way, so the digits
    /// have to come off the word as well as turn into words.
    /// </remarks>
    [Theory]
    [InlineData("OTT2", "ott dos")]
    [InlineData("Delay 1/4", "delay uno cuatro")]
    [InlineData("Sala 12", "sala doce")]
    [InlineData("Canal 45", "canal cuarenta y cinco")]
    public void NumbersAreSpelledOutTheWayTheyAreSpoken(string label, string expected) =>
        Assert.Equal(expected, VoicePhrase.Normalise(label));

    /// <remarks>
    /// Past a hundred a number stops being a quantity and starts being a name, and it gets read out
    /// rather than counted.
    /// </remarks>
    [Fact]
    public void ALongNumberIsReadDigitByDigit() =>
        Assert.Equal("uno nueve ocho cuatro", VoicePhrase.Normalise("1984"));

    [Theory]
    [InlineData("abrí Pro-Q 4", "pro q cuatro")]
    [InlineData("poneme el compresor", "compresor")]
    [InlineData("dale reverb", "reverb")]
    [InlineData("cargá la sala grande", "sala grande")]
    [InlineData("reverb", "reverb")]
    public void TheWordsThatOpenACommandComeOff(string spoken, string expected) =>
        Assert.Equal(expected, VoicePhrase.NormaliseSpoken(spoken));

    /// <remarks>
    /// The case that makes stripping openers dangerous: a button whose own name is one of them.
    /// Taking the last word off would leave nothing to match against at all.
    /// </remarks>
    [Fact]
    public void APhraseMadeEntirelyOfOpenersKeepsItsLastWord() =>
        Assert.Equal("abrir", VoicePhrase.NormaliseSpoken("abrir"));

    [Fact]
    public void OpenersAreOnlyStrippedFromTheFront() =>
        Assert.Equal("camara de eco", VoicePhrase.NormaliseSpoken("abrí la cámara de eco"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-–—")]
    public void NothingInIsNothingOut(string? text)
    {
        Assert.Equal(string.Empty, VoicePhrase.Normalise(text));
        Assert.Equal(string.Empty, VoicePhrase.NormaliseSpoken(text));
    }
}
