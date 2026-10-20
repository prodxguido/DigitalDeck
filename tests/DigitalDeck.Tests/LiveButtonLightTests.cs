using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Core.Services;
using Xunit;

namespace DigitalDeck.Tests;

/// <summary>
/// Which buttons light up for which state of the set.
/// </summary>
/// <remarks>
/// The rule that matters most here is the one about not knowing. A deck that comes up before Live
/// does has no idea whether anything is playing, and a play button lit on that guess is worse than
/// one that stays dark: the person believes it.
/// </remarks>
public class LiveButtonLightTests
{
    private static AbletonAction Button(AbletonCommand command, bool state = true) =>
        new() { Command = command, State = state };

    [Fact]
    public void NothingIsLitBeforeLiveHasSaidAnything()
    {
        var unknown = LiveSongState.Unknown;

        Assert.False(LiveButtonLight.IsLit(Button(AbletonCommand.TogglePlay), unknown));
        Assert.False(LiveButtonLight.IsLit(Button(AbletonCommand.ToggleMetronome), unknown));
        Assert.False(LiveButtonLight.IsLit(Button(AbletonCommand.ToggleRecord), unknown));
        Assert.False(unknown.IsKnown);
    }

    [Fact]
    public void TheTransportButtonsFollowTheSet()
    {
        var playing = new LiveSongState(IsPlaying: true);

        Assert.True(LiveButtonLight.IsLit(Button(AbletonCommand.TogglePlay), playing));
        Assert.True(LiveButtonLight.IsLit(Button(AbletonCommand.Play), playing));
        Assert.True(LiveButtonLight.IsLit(Button(AbletonCommand.Continue), playing));

        Assert.False(LiveButtonLight.IsLit(
            Button(AbletonCommand.TogglePlay), new LiveSongState(IsPlaying: false)));
    }

    [Fact]
    public void StopIsNeverLit()
    {
        // It would be lit almost all the time, which is a light that says nothing.
        Assert.False(LiveButtonLight.IsLit(
            Button(AbletonCommand.Stop), new LiveSongState(IsPlaying: false)));
        Assert.False(LiveButtonLight.IsLit(
            Button(AbletonCommand.Stop), new LiveSongState(IsPlaying: true)));
    }

    [Fact]
    public void TheClickAndTheRecordFollowTheirOwnValue()
    {
        var state = new LiveSongState(MetronomeOn: true, IsRecording: false);

        Assert.True(LiveButtonLight.IsLit(Button(AbletonCommand.ToggleMetronome), state));
        Assert.False(LiveButtonLight.IsLit(Button(AbletonCommand.ToggleRecord), state));
    }

    [Fact]
    public void AButtonThatSetsAValueIsLitWhenLiveIsAlreadyThere()
    {
        // A pair of "click on" and "click off" buttons: whichever one describes the set is the lit
        // one, so the pair reads as a switch rather than as two identical keys.
        var on = Button(AbletonCommand.SetMetronome, state: true);
        var off = Button(AbletonCommand.SetMetronome, state: false);

        Assert.True(LiveButtonLight.IsLit(on, new LiveSongState(MetronomeOn: true)));
        Assert.False(LiveButtonLight.IsLit(off, new LiveSongState(MetronomeOn: true)));

        Assert.False(LiveButtonLight.IsLit(on, new LiveSongState(MetronomeOn: false)));
        Assert.True(LiveButtonLight.IsLit(off, new LiveSongState(MetronomeOn: false)));
    }

    [Fact]
    public void TheSessionRecordStaysDarkBecauseNobodyWatchesIt()
    {
        // session_record has no observer in the Remote Script, so anything shown for it would be a
        // guess. Dark is the honest answer until one exists.
        var state = new LiveSongState(IsRecording: true, IsPlaying: true);

        Assert.False(LiveButtonLight.IsLit(Button(AbletonCommand.ToggleSessionRecord), state));
    }

    // ---------- The switches of one channel ----------

    private static LiveTracksState Set(int selected, params LiveTrackState[] tracks) =>
        new(tracks, selected);

    private static AbletonAction TrackButton(AbletonCommand command, int track, bool state = true) =>
        new() { Command = command, TrackIndex = track, State = state };

    [Fact]
    public void AMuteButtonFollowsItsOwnChannel()
    {
        var tracks = Set(
            selected: 0,
            new LiveTrackState(0, Mute: false, Solo: false, Arm: false),
            new LiveTrackState(1, Mute: true, Solo: false, Arm: false));

        Assert.False(LiveButtonLight.IsLit(
            TrackButton(AbletonCommand.ToggleMute, 0), LiveSongState.Unknown, tracks));
        Assert.True(LiveButtonLight.IsLit(
            TrackButton(AbletonCommand.ToggleMute, 1), LiveSongState.Unknown, tracks));
    }

    [Fact]
    public void SoloAndArmAreReadTheSameWay()
    {
        var tracks = Set(selected: 0, new LiveTrackState(2, Mute: false, Solo: true, Arm: true));

        Assert.True(LiveButtonLight.IsLit(
            TrackButton(AbletonCommand.ToggleSolo, 2), LiveSongState.Unknown, tracks));
        Assert.True(LiveButtonLight.IsLit(
            TrackButton(AbletonCommand.ToggleArm, 2), LiveSongState.Unknown, tracks));
        Assert.False(LiveButtonLight.IsLit(
            TrackButton(AbletonCommand.ToggleMute, 2), LiveSongState.Unknown, tracks));
    }

    [Fact]
    public void AButtonOnTheSelectedTrackFollowsTheSelection()
    {
        // The default a button carries, and the one that makes the light worth having: the same
        // key means a different channel depending on where the person is working.
        var button = TrackButton(AbletonCommand.ToggleMute, AbletonAction.SelectedTrack);

        var muted = Set(
            selected: 1,
            new LiveTrackState(0, Mute: false, Solo: false, Arm: false),
            new LiveTrackState(1, Mute: true, Solo: false, Arm: false));

        Assert.True(LiveButtonLight.IsLit(button, LiveSongState.Unknown, muted));

        var elsewhere = muted with { Selected = 0 };

        Assert.False(LiveButtonLight.IsLit(button, LiveSongState.Unknown, elsewhere));
    }

    [Fact]
    public void NothingLightsWhileTheSelectionIsUnknown()
    {
        var button = TrackButton(AbletonCommand.ToggleMute, AbletonAction.SelectedTrack);
        var tracks = Set(
            LiveTracksState.Unknown, new LiveTrackState(0, Mute: true, Solo: false, Arm: false));

        Assert.False(LiveButtonLight.IsLit(button, LiveSongState.Unknown, tracks));
    }

    [Fact]
    public void AChannelLiveHasNotMentionedStaysDark()
    {
        var tracks = Set(selected: 0, new LiveTrackState(0, Mute: true, Solo: false, Arm: false));

        Assert.False(LiveButtonLight.IsLit(
            TrackButton(AbletonCommand.ToggleMute, 7), LiveSongState.Unknown, tracks));
        Assert.False(LiveButtonLight.IsLit(
            TrackButton(AbletonCommand.ToggleMute, 0), LiveSongState.Unknown, LiveTracksState.Empty));
    }

    [Fact]
    public void ASetButtonOnAChannelLightsWhenLiveIsAlreadyThere()
    {
        var tracks = Set(selected: 0, new LiveTrackState(0, Mute: false, Solo: false, Arm: false));

        Assert.True(LiveButtonLight.IsLit(
            TrackButton(AbletonCommand.SetMute, 0, state: false), LiveSongState.Unknown, tracks));
        Assert.False(LiveButtonLight.IsLit(
            TrackButton(AbletonCommand.SetMute, 0, state: true), LiveSongState.Unknown, tracks));
    }

    [Fact]
    public void ButtonsThatAreNotAboutLiveAreNeverLit()
    {
        var state = new LiveSongState(IsPlaying: true, MetronomeOn: true, IsRecording: true);

        Assert.False(LiveButtonLight.IsLit(new OpenUrlAction { Url = "https://example.com" }, state));
        Assert.False(LiveButtonLight.IsLit(null, state));
    }
}
