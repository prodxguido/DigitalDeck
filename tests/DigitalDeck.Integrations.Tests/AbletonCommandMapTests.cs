using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Integrations.Ableton;
using DigitalDeck.Integrations.Ableton.Osc;
using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// The join between the catalogue Core publishes and the addresses that go on the wire.
/// </summary>
public class AbletonCommandMapTests
{
    public static TheoryData<AbletonCommand> EveryCommand()
    {
        var data = new TheoryData<AbletonCommand>();

        foreach (var command in Enum.GetValues<AbletonCommand>())
            data.Add(command);

        return data;
    }

    /// <summary>Only the commands that go on the wire; the keyboard-driven ones never reach ToMessage.</summary>
    public static TheoryData<AbletonCommand> EveryWireCommand()
    {
        var data = new TheoryData<AbletonCommand>();

        foreach (var command in Enum.GetValues<AbletonCommand>())
            if (!AbletonCommands.Describe(command).ViaKeyboard)
                data.Add(command);

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryWireCommand))]
    public void EveryCommandInTheCatalogueHasAnAddress(AbletonCommand command)
    {
        // The failure this exists to catch: a command added to Core and forgotten here, which
        // would reach the wire only when a user finally configured a button with it.
        var message = AbletonCommandMap.ToMessage(AbletonAction.For(command));

        Assert.StartsWith("/", message.Address);
    }

    [Theory]
    [MemberData(nameof(EveryCommand))]
    public void EveryKeyboardCommandMapsToAShortcut(AbletonCommand command)
    {
        if (!AbletonCommands.Describe(command).ViaKeyboard) return;

        // Reachable and typed — the handler routes these to ILiveKeyboard instead of the wire.
        var shortcut = AbletonCommandMap.ToShortcut(command);
        Assert.True(Enum.IsDefined(shortcut));

        Assert.Throws<InvalidOperationException>(() => AbletonCommandMap.ToMessage(AbletonAction.For(command)));
    }

    [Theory]
    [MemberData(nameof(EveryCommand))]
    public void EveryCommandIsDescribedForTheEditor(AbletonCommand command)
    {
        var info = AbletonCommands.Describe(command);

        Assert.False(string.IsNullOrWhiteSpace(info.Label));
        Assert.True(!info.NeedsValue || info.ValueLabel is not null,
                    $"{command} takes a number but the editor has no label for it.");
    }

    [Fact]
    public void NoTwoCommandsShareAnAddress()
    {
        var messages = Enum.GetValues<AbletonCommand>()
            .Where(command => !AbletonCommands.Describe(command).ViaKeyboard)
            .Select(command => Describe(AbletonCommandMap.ToMessage(AbletonAction.For(command))))
            .ToList();

        // Two commands that send the identical message are two names for one thing, which is far
        // more likely to be a copy-paste slip than a decision. Sharing an address is fine and
        // deliberate — the zoom pairs differ only by which axis and which sign they carry — so it
        // is the whole message that has to be distinct, not the address on its own.
        Assert.Equal(messages.Count, messages.Distinct().Count());
    }

    /// <summary>Address and arguments as one comparable line.</summary>
    private static string Describe(OscMessage message) =>
        message.Address + " " + string.Join(" ", message.Arguments);

    [Fact]
    public void TheTrackIndexIsCarriedThrough()
    {
        var action = AbletonAction.For(AbletonCommand.ToggleMute);
        action.TrackIndex = 3;

        var message = AbletonCommandMap.ToMessage(action);

        Assert.Equal("/live/track/toggle/mute", message.Address);
        Assert.Equal(3, message.GetInt(0));
    }

    [Fact]
    public void TheSelectedTrackTravelsAsMinusOne()
    {
        // The script resolves it; the app never has to know which track is selected.
        var message = AbletonCommandMap.ToMessage(AbletonAction.For(AbletonCommand.ToggleSolo));

        Assert.Equal(AbletonAction.SelectedTrack, message.GetInt(0));
    }

    [Fact]
    public void NumbersGoOutAsFloatsBecauseThatIsWhatLiveExpects()
    {
        var action = AbletonAction.For(AbletonCommand.SetTempo);
        action.Value = 128;

        var message = AbletonCommandMap.ToMessage(action);

        Assert.Equal("/live/song/set/tempo", message.Address);
        Assert.IsType<float>(message.Arguments[0]);
        Assert.Equal(128, message.GetDouble(0));
    }

    [Fact]
    public void AClipCarriesBothItsTrackAndItsScene()
    {
        var action = AbletonAction.For(AbletonCommand.FireClip);
        action.TrackIndex = 1;
        action.SceneIndex = 4;

        var message = AbletonCommandMap.ToMessage(action);

        Assert.Equal("/live/clip_slot/fire", message.Address);
        Assert.Equal(1, message.GetInt(0));
        Assert.Equal(4, message.GetInt(1));
    }

    [Fact]
    public void AVolumeCarriesTheTrackFirstAndTheValueSecond()
    {
        var action = AbletonAction.For(AbletonCommand.SetVolume);
        action.TrackIndex = 2;
        action.Value = 0.5;

        var message = AbletonCommandMap.ToMessage(action);

        Assert.Equal(2, message.GetInt(0));
        Assert.Equal(0.5, message.GetDouble(1), 4);
    }

    [Fact]
    public void ACommandThatIsNotInTheEnumIsRefused()
    {
        var action = new AbletonAction { Command = (AbletonCommand)9999 };

        Assert.Throws<ArgumentOutOfRangeException>(() => AbletonCommandMap.ToMessage(action));
    }
}
