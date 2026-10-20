using System.Text.Json;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Core.Serialization;
using Xunit;

namespace DigitalDeck.Tests;

/// <summary>
/// The Ableton payload as the database sees it, and the rules it enforces before anything is sent.
/// </summary>
public class AbletonActionTests
{
    private static ActionPayload RoundTrip(ActionPayload action)
    {
        // Exactly what the repository does with the item's data column.
        var json = JsonSerializer.Serialize(action, DeckJson.Compact);
        var restored = JsonSerializer.Deserialize<ActionPayload>(json, DeckJson.Compact);

        Assert.NotNull(restored);
        return restored!;
    }

    [Fact]
    public void AnAbletonActionSurvivesTheDataColumn()
    {
        var action = AbletonAction.For(AbletonCommand.SetVolume);
        action.TrackIndex = 2;
        action.Value = 0.4;

        var restored = Assert.IsType<AbletonAction>(RoundTrip(action));

        Assert.Equal(AbletonCommand.SetVolume, restored.Command);
        Assert.Equal(2, restored.TrackIndex);
        Assert.Equal(0.4, restored.Value, 6);
    }

    [Fact]
    public void ItIsStoredUnderItsOwnDiscriminator()
    {
        // The discriminator is what lets a new action type be added without a schema migration —
        // and what makes renaming one a breaking change for every button already saved.
        var json = JsonSerializer.Serialize<ActionPayload>(
            AbletonAction.For(AbletonCommand.Play), DeckJson.Compact);

        Assert.Contains("\"kind\":\"ableton\"", json);
        Assert.Contains("\"command\":\"play\"", json);
    }

    [Fact]
    public void ExistingActionTypesStillRoundTripAlongsideIt()
    {
        Assert.IsType<OpenUrlAction>(RoundTrip(new OpenUrlAction { Url = "https://ableton.com" }));
        Assert.IsType<OpenPathAction>(RoundTrip(new OpenPathAction { Path = @"D:\Samples" }));
    }

    [Fact]
    public void ButtonsSavedBeforeTheTrackFieldExistedStillLoad()
    {
        // Older rows have no track_index. The default has to be the selected track, not track 0,
        // or an upgrade would quietly repoint every button at the first track in the set.
        var restored = JsonSerializer.Deserialize<ActionPayload>(
            "{\"kind\":\"ableton\",\"command\":\"toggle_mute\"}", DeckJson.Compact);

        var action = Assert.IsType<AbletonAction>(restored);
        Assert.Equal(AbletonAction.SelectedTrack, action.TrackIndex);
    }

    // ---------- Validation ----------

    [Fact]
    public void AWellFormedActionHasNothingToComplainAbout()
    {
        Assert.Null(AbletonAction.For(AbletonCommand.Play).Validate());
        Assert.Null(AbletonAction.For(AbletonCommand.SetTempo).Validate());
    }

    [Theory]
    [InlineData(19)]
    [InlineData(1000)]
    [InlineData(0)]
    public void ATempoLiveWouldRefuseIsCaughtHere(double tempo)
    {
        var action = AbletonAction.For(AbletonCommand.SetTempo);
        action.Value = tempo;

        Assert.Contains("BPM", action.Validate());
    }

    [Fact]
    public void AVolumeOutsideTheNormalisedRangeIsCaught()
    {
        var action = AbletonAction.For(AbletonCommand.SetVolume);
        action.Value = 2.5;

        Assert.NotNull(action.Validate());
    }

    [Fact]
    public void ACommandFromAFutureVersionIsRefusedRatherThanCrashing()
    {
        // A .deckpack from a newer build, or a hand-edited database.
        var action = new AbletonAction { Command = (AbletonCommand)9999 };

        Assert.NotNull(action.Validate());
    }

    [Fact]
    public void ATrackIndexBelowMasterIsRefused()
    {
        var action = AbletonAction.For(AbletonCommand.ToggleMute);
        action.TrackIndex = -7;

        Assert.NotNull(action.Validate());
    }

    [Fact]
    public void AValueIsNotCheckedForACommandThatDoesNotUseOne()
    {
        // Play carries whatever number happened to be in the editor; it is simply never read.
        var action = AbletonAction.For(AbletonCommand.Play);
        action.Value = 9999;

        Assert.Null(action.Validate());
    }

    [Fact]
    public void AnAbletonButtonNeedsNoConfirmationToRun()
    {
        // Unlike a path button, nothing here starts a program, and Live's own undo covers the rest.
        Assert.False(AbletonAction.For(AbletonCommand.DeleteTrack).IsPotentiallyDangerous);
    }

    // ---------- Summary ----------

    [Fact]
    public void TheSummaryNamesTheTrackInHumanTerms()
    {
        var action = AbletonAction.For(AbletonCommand.ToggleMute);

        Assert.Contains("Mute", action.Summary);
        Assert.Contains("track seleccionada", action.Summary);

        action.TrackIndex = 0;
        Assert.Contains("track 1", action.Summary);

        action.TrackIndex = AbletonAction.MasterTrack;
        Assert.Contains("master", action.Summary);
    }

    [Fact]
    public void TheSummaryShowsTheValueWithItsUnit()
    {
        var action = AbletonAction.For(AbletonCommand.SetTempo);
        action.Value = 128;

        Assert.Contains("128", action.Summary);
        Assert.Contains("BPM", action.Summary);
    }

    [Fact]
    public void EveryCommandProducesASummaryWithoutThrowing()
    {
        foreach (var command in Enum.GetValues<AbletonCommand>())
            Assert.False(string.IsNullOrWhiteSpace(AbletonAction.For(command).Summary), command.ToString());
    }

    // ---------- Catalogue ----------

    [Fact]
    public void EveryCommandAppearsInExactlyOneGroup()
    {
        var described = AbletonCommands.All.Select(info => info.Command).ToList();

        Assert.Equal(Enum.GetValues<AbletonCommand>().Length, described.Count);
        Assert.Equal(described.Count, described.Distinct().Count());
    }

    [Fact]
    public void EveryGroupHasSomethingInItAndAName()
    {
        foreach (var group in Enum.GetValues<AbletonCommandGroup>())
        {
            Assert.NotEmpty(AbletonCommands.InGroup(group));
            Assert.False(string.IsNullOrWhiteSpace(AbletonCommands.GroupLabel(group)));
        }
    }

    [Fact]
    public void ACommandStartsWithADefaultValueThatIsAlreadyValid()
    {
        // Choosing a command in the editor must never leave the button in a state that refuses to
        // save until a number is typed.
        foreach (var command in Enum.GetValues<AbletonCommand>())
            Assert.Null(AbletonAction.For(command).Validate());
    }
}
