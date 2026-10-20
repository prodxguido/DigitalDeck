using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Integrations.Ableton;
using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// Zoom is the one part of the bridge Live's own OSC vocabulary has no word for, so both halves of
/// it are ours: the addresses, the axis names, and the trick that turns a stepwise API into a
/// preset. These cover the seams where the two languages have to agree.
/// </summary>
public class AbletonZoomTests
{
    private static AbletonAction Zoom(AbletonCommand command, double value)
    {
        var action = AbletonAction.For(command);
        action.Value = value;
        return action;
    }

    [Theory]
    [InlineData(AbletonCommand.ZoomTimeIn, "time", 4)]
    [InlineData(AbletonCommand.ZoomTimeOut, "time", -4)]
    [InlineData(AbletonCommand.ZoomTracksIn, "tracks", 4)]
    [InlineData(AbletonCommand.ZoomTracksOut, "tracks", -4)]
    public void TheDirectionTravelsAsTheSignOfTheStepCount(
        AbletonCommand command, string axis, int expected)
    {
        var message = AbletonCommandMap.ToMessage(Zoom(command, 4));

        Assert.Equal(AbletonCommandMap.ZoomAddress, message.Address);
        Assert.Equal(axis, message.GetString(0));
        Assert.Equal(expected, message.GetInt(1));
    }

    [Theory]
    [InlineData(AbletonCommand.ZoomTimePreset, "time")]
    [InlineData(AbletonCommand.ZoomTracksPreset, "tracks")]
    public void APresetGoesToItsOwnAddressWithTheLevelItHolds(AbletonCommand command, string axis)
    {
        var message = AbletonCommandMap.ToMessage(Zoom(command, 12));

        Assert.Equal(AbletonCommandMap.ZoomPresetAddress, message.Address);
        Assert.Equal(axis, message.GetString(0));
        Assert.Equal(12, message.GetInt(1));
    }

    [Fact]
    public void ANotchCountTravelsAsAWholeNumber()
    {
        // The script loops on it. A float here would be a type the codec has to guess at for a
        // value that was never fractional in the first place.
        var message = AbletonCommandMap.ToMessage(Zoom(AbletonCommand.ZoomTimeIn, 3));

        Assert.IsType<int>(message.Arguments[1]);
    }

    [Fact]
    public void EachPresetButtonHoldsItsOwnLevel()
    {
        // The point of the feature: several buttons, same command, different place to land.
        var wide = AbletonCommandMap.ToMessage(Zoom(AbletonCommand.ZoomTimePreset, 2));
        var close = AbletonCommandMap.ToMessage(Zoom(AbletonCommand.ZoomTimePreset, 30));

        Assert.Equal(2, wide.GetInt(1));
        Assert.Equal(30, close.GetInt(1));
    }

    [Fact]
    public void ALevelOutsideWhatTheScriptCanReachIsRefusedBeforeItIsSent()
    {
        Assert.NotNull(Zoom(AbletonCommand.ZoomTimePreset, AbletonCommands.ZoomLevelMaximum + 1).Validate());
        Assert.Null(Zoom(AbletonCommand.ZoomTimePreset, 0).Validate());
        Assert.Null(Zoom(AbletonCommand.ZoomTimePreset, AbletonCommands.ZoomLevelMaximum).Validate());
    }

    [Fact]
    public void ZeroStepsIsNotAZoomButton()
    {
        // A step button that moves nothing is a button that looks broken, so the editor refuses it.
        Assert.NotNull(Zoom(AbletonCommand.ZoomTimeIn, 0).Validate());
    }

    [Fact]
    public void TheAxisNamesAreTheOnesTheScriptAnswersTo()
    {
        var view = ScriptSource("handlers/view.py");

        Assert.Contains($"TIME = \"{AbletonCommandMap.TimeAxis}\"", view, StringComparison.Ordinal);
        Assert.Contains($"TRACKS = \"{AbletonCommandMap.TracksAxis}\"", view, StringComparison.Ordinal);
        Assert.Contains($"\"{AbletonCommandMap.ZoomAddress}\"", view, StringComparison.Ordinal);
        Assert.Contains($"\"{AbletonCommandMap.ZoomPresetAddress}\"", view, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHighestLevelTheEditorOffersIsOneTheScriptCanActuallyReach()
    {
        // Two constants in two languages, like the port pair. A preset counts up from the floor the
        // script bottoms out at, so an editor offering a level past that floor would hand the user
        // buttons that all land on the same place and no way to tell why.
        var view = ScriptSource("handlers/view.py");

        Assert.Contains(
            $"ZOOM_FLOOR_STEPS = {(int)AbletonCommands.ZoomLevelMaximum}",
            view,
            StringComparison.Ordinal);
    }

    /// <summary>One file of the Remote Script the app carries, as text.</summary>
    private static string ScriptSource(string relativePath)
    {
        var installer = new RemoteScriptInstaller(liveIsRunning: () => false);
        var bundle = installer.BundledFiles;

        var key = bundle.Keys.FirstOrDefault(name =>
            name.Replace('\\', '/').EndsWith(relativePath, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(key);
        return bundle[key];
    }
}
