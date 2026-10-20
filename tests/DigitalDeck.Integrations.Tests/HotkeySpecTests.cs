using DigitalDeck.Core.Model;
using DigitalDeck.Integrations.Windows;
using Xunit;

namespace DigitalDeck.Integrations.Tests;

public sealed class HotkeySpecTests
{
    [Fact]
    public void ACombinationSurvivesBeingSavedAndReadBack()
    {
        Assert.True(HotkeySpec.TryParse(HotkeySpec.Default.ToString(), out var read));

        Assert.Equal(HotkeySpec.Default, read);
    }

    [Theory]
    [InlineData("Ctrl+Alt+Space")]
    [InlineData("ctrl+alt+space")]
    [InlineData("CTRL + ALT + SPACE")]
    [InlineData("Alt+Ctrl+Space")]
    [InlineData("Control+Alt+Space")]
    public void ItIsReadTheSameHoweverItWasWritten(string text)
    {
        Assert.True(HotkeySpec.TryParse(text, out var spec));

        Assert.Equal(HotkeySpec.Default, spec);
    }

    /// <remarks>
    /// One spelling out, whatever went in, so the settings table and the screen never disagree
    /// about the same combination.
    /// </remarks>
    [Fact]
    public void ItIsAlwaysWrittenTheSameWay() =>
        Assert.Equal("Ctrl+Alt+Space", HotkeySpec.Default.ToString());

    [Theory]
    [InlineData("F13", "F13")]
    [InlineData("Shift+F5", "Shift+F5")]
    [InlineData("Win+K", "Win+K")]
    [InlineData("Ctrl+Shift+7", "Ctrl+Shift+7")]
    public void TheKeysWorthOfferingAllRoundTrip(string text, string expected)
    {
        Assert.True(HotkeySpec.TryParse(text, out var spec));

        Assert.Equal(expected, spec.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+Alt")]        // modifiers with nothing to modify
    [InlineData("Ctrl+A+B")]        // two keys
    [InlineData("Ctrl+F25")]        // past the last function key
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+Ñ")]          // moves between keyboard layouts, so it is not offered
    [InlineData("Ctrl+;")]
    public void WhatCannotBeMeantIsRefused(string? text) =>
        Assert.False(HotkeySpec.TryParse(text, out _));

    /// <remarks>
    /// A key with no modifier is legal as far as Windows is concerned, and claiming it takes that
    /// key away from every other program on the machine. It has to be possible — someone with a
    /// spare F-key should be able to use it — so this only records that it works.
    /// </remarks>
    [Fact]
    public void AKeyOnItsOwnIsAllowed()
    {
        Assert.True(HotkeySpec.TryParse("F13", out var spec));

        Assert.Equal(HotkeyModifiers.None, spec.Modifiers);
    }

    [Fact]
    public void AnUnsetCombinationIsNotValid()
    {
        var empty = default(HotkeySpec);

        Assert.False(empty.IsValid);
        Assert.Equal(string.Empty, empty.ToString());
    }

    /// <remarks>
    /// Space on its own is Live's play and stop. Taking it system-wide would be the single most
    /// disruptive thing this feature could do to the application it exists to help.
    /// </remarks>
    [Fact]
    public void TheDefaultDoesNotStealPlayAndStopFromLive()
    {
        Assert.NotEqual(HotkeyModifiers.None, HotkeySpec.Default.Modifiers);
        Assert.True(HotkeySpec.Default.IsValid);
    }
}
