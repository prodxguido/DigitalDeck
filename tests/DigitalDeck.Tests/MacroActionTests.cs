using System.Text.Json;
using DigitalDeck.Core.Model.Actions;
using Xunit;

namespace DigitalDeck.Tests;

/// <summary>
/// A button that does several things in a row: what it accepts, what it refuses, and that it still
/// says the same thing after a trip through the database.
/// </summary>
public sealed class MacroActionTests
{
    private static MacroAction Morning() => new()
    {
        Steps =
        {
            new MacroStep { Action = new OpenUrlAction { Url = "https://youtube.com/playlist?list=x" } },
            new MacroStep { Action = new OpenUrlAction { Url = "https://lanacion.com.ar" }, DelayMs = 1500 },
            new MacroStep { Action = new OpenPathAction { Path = @"C:\Ableton\Live.exe", Acknowledged = true } }
        }
    };

    [Fact]
    public void ARoutineWithStepsIsValid()
    {
        Assert.Null(Morning().Validate());
    }

    [Fact]
    public void AnEmptyRoutineIsRefused()
    {
        Assert.NotNull(new MacroAction().Validate());
    }

    [Fact]
    public void ABrokenStepIsNamedByItsPosition()
    {
        var macro = Morning();
        macro.Steps[1].Action = new OpenUrlAction { Url = "no-es-una-url" };

        Assert.Contains("Paso 2", macro.Validate());
    }

    [Fact]
    public void AStepThatRunsAProgramHasToBeAcknowledged()
    {
        var macro = Morning();
        ((OpenPathAction)macro.Steps[2].Action).Acknowledged = false;

        Assert.Contains("Paso 3", macro.Validate());
    }

    /// <summary>
    /// The reason nesting is refused: a macro inside a macro is a cycle waiting to be written, and
    /// the dispatcher would follow it forever.
    /// </summary>
    [Fact]
    public void ARoutineCannotContainAnotherRoutine()
    {
        var macro = new MacroAction { Steps = { new MacroStep { Action = Morning() } } };

        Assert.NotNull(macro.Validate());
    }

    [Fact]
    public void AnAbsurdWaitIsRefused()
    {
        var macro = Morning();
        macro.Steps[0].DelayMs = MacroStep.MaxDelayMs + 1;

        Assert.Contains("Paso 1", macro.Validate());
    }

    /// <summary>
    /// The steps are declared as the abstract payload, so each one has to carry its own «kind»
    /// through the round trip — otherwise a saved routine comes back as three empty actions.
    /// </summary>
    [Fact]
    public void EveryStepKeepsItsKindThroughTheDatabase()
    {
        var json = JsonSerializer.Serialize<ActionPayload>(Morning());
        var back = Assert.IsType<MacroAction>(JsonSerializer.Deserialize<ActionPayload>(json));

        Assert.Collection(back.Steps,
            first => Assert.Equal("https://youtube.com/playlist?list=x", Assert.IsType<OpenUrlAction>(first.Action).Url),
            second => Assert.Equal(1500, second.DelayMs),
            third => Assert.Equal(@"C:\Ableton\Live.exe", Assert.IsType<OpenPathAction>(third.Action).Path));

        Assert.Null(back.Validate());
    }
}
