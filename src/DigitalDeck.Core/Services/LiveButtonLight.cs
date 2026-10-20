using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.Core.Services;

/// <summary>
/// Decides whether a button should be lit, given what Live is doing.
/// </summary>
/// <remarks>
/// A deck whose buttons all look the same whatever Live is doing makes the person read Live to know
/// whether the click is on — which is the screen the deck exists to avoid looking at. This is the
/// whole rule, kept in Core and free of WPF so it can be tested without a window.
/// <para>
/// Deliberately conservative: only commands whose state the Remote Script actually watches can
/// light up, and only when Live has said so. Anything else stays dark rather than guessing. In
/// particular the Session record is not here — <c>session_record</c> has no observer, so a lit
/// button would be showing the last thing the app happened to hear rather than the truth.
/// </para>
/// </remarks>
public static class LiveButtonLight
{
    /// <summary>True when this button is showing a state Live is currently in.</summary>
    /// <param name="tracks">
    /// Every channel's switches. Left out, nothing about a track lights up — which is what a caller
    /// that only knows about the transport should get.
    /// </param>
    public static bool IsLit(
        ActionPayload? action,
        LiveSongState state,
        LiveTracksState tracks = default) =>
        action is AbletonAction ableton && IsLit(ableton, state, tracks);

    private static bool IsLit(AbletonAction action, LiveSongState state, LiveTracksState tracks) =>
        action.Command switch
    {
        // The transport. Stop is not here on purpose: a stop button lit whenever the set is not
        // playing would be lit almost all the time, which is a light that says nothing.
        AbletonCommand.Play or AbletonCommand.Continue or AbletonCommand.TogglePlay =>
            state.IsPlaying == true,

        AbletonCommand.ToggleRecord => state.IsRecording == true,
        AbletonCommand.ToggleMetronome => state.MetronomeOn == true,

        // The set/unset pair light when Live is already in the state they put it in — so a pair of
        // "click on" and "click off" buttons shows which one of the two is the current one.
        AbletonCommand.SetRecord => state.IsRecording == action.State,
        AbletonCommand.SetMetronome => state.MetronomeOn == action.State,

        // The three switches of one channel, read the same way.
        AbletonCommand.ToggleMute => Track(action, tracks)?.Mute == true,
        AbletonCommand.ToggleSolo => Track(action, tracks)?.Solo == true,
        AbletonCommand.ToggleArm => Track(action, tracks)?.Arm == true,

        AbletonCommand.SetMute => Track(action, tracks)?.Mute == action.State,
        AbletonCommand.SetSolo => Track(action, tracks)?.Solo == action.State,
        AbletonCommand.SetArm => Track(action, tracks)?.Arm == action.State,

        _ => false
    };

    /// <summary>
    /// The channel a button acts on, or null when there is nothing to show.
    /// </summary>
    /// <remarks>
    /// A button configured against "the selected track" — which is the default, and what most of
    /// them end up carrying — follows the selection in Live rather than a number. When Live has not
    /// said which one that is, the honest answer is none: a mute button lit from the wrong channel
    /// is worse than one that is dark.
    /// </remarks>
    private static LiveTrackState? Track(AbletonAction action, LiveTracksState tracks)
    {
        var index = action.TrackIndex == AbletonAction.SelectedTrack
            ? tracks.Selected
            : action.TrackIndex;

        return index == LiveTracksState.Unknown ? null : tracks.Find(index);
    }
}
