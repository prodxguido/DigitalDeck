using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using Xunit;

namespace DigitalDeck.Tests;

/// <summary>
/// The button that presses a key combination in another program.
/// </summary>
/// <remarks>
/// Both halves are required, and that is the point worth pinning. A combination with nowhere to go
/// would land on whatever has the focus, which after touching a tile is the deck itself — a button
/// that appears to do nothing, which is the worst way for this to fail.
/// </remarks>
public class HotkeyActionTests
{
    [Fact]
    public void AFullyConfiguredButtonIsValid()
    {
        var action = new HotkeyAction { Keys = "Ctrl+Shift+E", Program = "Photoshop" };

        Assert.Null(action.Validate());
        Assert.Equal(
            new HotkeySpec(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'E'), action.Spec);
    }

    [Fact]
    public void WithoutKeysItIsRefused()
    {
        var action = new HotkeyAction { Program = "Photoshop" };

        Assert.NotNull(action.Validate());
        Assert.Null(action.Spec);
    }

    [Fact]
    public void WithoutAProgramItIsRefused()
    {
        var action = new HotkeyAction { Keys = "Ctrl+S", Program = "   " };

        Assert.NotNull(action.Validate());
    }

    [Fact]
    public void AnUnreadableCombinationIsRefusedRatherThanStored()
    {
        // Punctuation keys move between keyboard layouts, so HotkeySpec will not take them and
        // neither will this: better a refusal in the editor than a button that misses on someone
        // else's keyboard.
        var action = new HotkeyAction { Keys = "Ctrl+ñ", Program = "Photoshop" };

        Assert.NotNull(action.Validate());
    }

    [Fact]
    public void TheSummarySaysBothHalves()
    {
        Assert.Equal(
            "Ctrl+Shift+E en Photoshop",
            new HotkeyAction { Keys = "Ctrl+Shift+E", Program = "Photoshop" }.Summary);

        // Half-built, as the editor shows it before anything is chosen.
        Assert.Equal("Atajo de teclado", new HotkeyAction().Summary);
    }

    [Fact]
    public void ItDoesNotCountAsDangerous()
    {
        // The flag means "could start a program". Nothing here starts anything, and a chord sent to
        // something that is not running does nothing at all.
        Assert.False(new HotkeyAction { Keys = "Ctrl+S", Program = "Photoshop" }.IsPotentiallyDangerous);
    }
}
