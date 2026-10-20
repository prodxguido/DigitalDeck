using DigitalDeck.Core.Model.Actions;
using Xunit;

namespace DigitalDeck.Tests;

/// <summary>
/// The commands that put Live in a known state, rather than flipping whatever it was in.
/// </summary>
/// <remarks>
/// These were in the Remote Script from the start and unreachable from any button: an audit found
/// thirteen such addresses, built and tested, that the app never offered. The distinction they add
/// is not cosmetic — a toggle leaves the set wherever it happened to be, which is the wrong thing
/// for a button whose job is "arm this track" or "metronome on before the take".
/// </remarks>
public class AbletonSetCommandTests
{
    [Theory]
    [InlineData(AbletonCommand.SetMute)]
    [InlineData(AbletonCommand.SetSolo)]
    [InlineData(AbletonCommand.SetArm)]
    [InlineData(AbletonCommand.SetLoop)]
    [InlineData(AbletonCommand.SetMetronome)]
    [InlineData(AbletonCommand.SetRecord)]
    [InlineData(AbletonCommand.SetSessionRecord)]
    public void EverySettingCommandAsksForAStateAndSaysWhichOne(AbletonCommand command)
    {
        var info = AbletonCommands.Describe(command);

        Assert.True(info.NeedsState, $"{command} tendría que pedir encendido o apagado");

        var on = AbletonAction.For(command);
        var off = AbletonAction.For(command);
        off.State = false;

        Assert.Contains("encender", on.Summary);
        Assert.Contains("apagar", off.Summary);
    }

    [Fact]
    public void ANewSettingCommandDefaultsToTurningThingsOn()
    {
        // Far and away the common case: metronome on, arm on, loop on. Defaulting to off would
        // make the usual button a two-step configuration.
        Assert.True(AbletonAction.For(AbletonCommand.SetArm).State);
    }

    [Fact]
    public void TheToggleAndTheSettingFormBothStillExist()
    {
        // Keeping both is the point. A pad you watch wants the toggle; a macro step wants the one
        // that ends in a known state.
        Assert.True(AbletonCommands.Describe(AbletonCommand.ToggleMute).NeedsTrack);
        Assert.False(AbletonCommands.Describe(AbletonCommand.ToggleMute).NeedsState);

        Assert.True(AbletonCommands.Describe(AbletonCommand.SetMute).NeedsTrack);
        Assert.True(AbletonCommands.Describe(AbletonCommand.SetMute).NeedsState);
    }

    // ---------- Sends ----------

    [Fact]
    public void ASendIsNamedByItsLetterTheWayLivesMixerDoes()
    {
        Assert.Equal("send A", AbletonAction.DescribeSend(0));
        Assert.Equal("send B", AbletonAction.DescribeSend(1));
        Assert.Equal("send D", AbletonAction.DescribeSend(3));
    }

    [Fact]
    public void SettingASendNeedsTheTrackTheSendAndTheAmount()
    {
        var info = AbletonCommands.Describe(AbletonCommand.SetSend);

        Assert.True(info.NeedsTrack);
        Assert.True(info.NeedsSend);
        Assert.True(info.NeedsValue);

        var action = AbletonAction.For(AbletonCommand.SetSend);
        action.SendIndex = 1;

        Assert.Contains("send B", action.Summary);
        Assert.Null(action.Validate());
    }

    [Fact]
    public void ASendBeforeAIsRefused()
    {
        var action = AbletonAction.For(AbletonCommand.SetSend);
        action.SendIndex = -1;

        Assert.NotNull(action.Validate());
    }

    // ---------- Scenes ----------

    [Fact]
    public void TheSceneCommandsThatNeededNoNumberDoNotAskForOne()
    {
        Assert.False(AbletonCommands.Describe(AbletonCommand.FireSelectedScene).NeedsScene);
        Assert.False(AbletonCommands.Describe(AbletonCommand.CreateScene).NeedsScene);

        Assert.True(AbletonCommands.Describe(AbletonCommand.SelectScene).NeedsScene);
    }

    [Fact]
    public void EveryCommandInTheCatalogueIsDescribedExactlyOnce()
    {
        // The catalogue is what the editor builds itself from, so a command missing from it would
        // simply never appear as a chip — invisible rather than broken.
        var described = AbletonCommands.All.Select(info => info.Command).ToList();

        Assert.Equal(described.Count, described.Distinct().Count());
        Assert.Equal(Enum.GetValues<AbletonCommand>().Length, described.Count);
    }
}
