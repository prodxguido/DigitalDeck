namespace DigitalDeck.Core.Abstractions;

/// <summary>
/// What the open Live set is doing right now, as far as the app has been told.
/// </summary>
/// <remarks>
/// Every field is nullable, and null is not the same as off. Before Live has answered — the bridge
/// still coming up, an older script, a value this version of Live has no listener for — the honest
/// answer is "I do not know", and a button that lights up on a guess is worse than one that stays
/// dark. See <see cref="LiveButtonLight"/> for what that means on screen.
/// </remarks>
/// <param name="IsPlaying">Whether the transport is running.</param>
/// <param name="IsRecording">Whether the Arrangement record is armed (<c>record_mode</c>, not the Session grid).</param>
/// <param name="MetronomeOn">Whether the click is on.</param>
/// <param name="Tempo">The set's tempo in BPM.</param>
public readonly record struct LiveSongState(
    bool? IsPlaying = null,
    bool? IsRecording = null,
    bool? MetronomeOn = null,
    double? Tempo = null)
{
    /// <summary>Nothing known yet. What the app holds before Live has said a word.</summary>
    public static LiveSongState Unknown => default;

    /// <summary>True once Live has answered about anything at all.</summary>
    public bool IsKnown => IsPlaying is not null
        || IsRecording is not null
        || MetronomeOn is not null
        || Tempo is not null;
}

/// <summary>One channel's three switches, as Live last reported them.</summary>
/// <param name="Index">
/// The channel, in the index space the mixer uses: 0 upwards for the tracks of the set, -2 for the
/// master, -3 downwards for the returns.
/// </param>
public readonly record struct LiveTrackState(int Index, bool Mute, bool Solo, bool Arm);

/// <summary>
/// Every channel's switches at once, and which channel is selected in Live.
/// </summary>
/// <remarks>
/// The two travel together because they are read together: a button is usually configured against
/// "the selected track" rather than a number, so knowing that track 2 is muted says nothing until
/// you also know which track the button means. They arrive in one message for the same reason.
/// </remarks>
/// <param name="Selected">
/// The selected channel's index, or <see cref="Unknown"/> when Live has not said — which is also
/// what the app stores on a button that means "whichever one is selected".
/// </param>
public readonly record struct LiveTracksState(IReadOnlyList<LiveTrackState>? Tracks, int Selected)
{
    /// <summary>No selection known. The same -1 an unconfigured button carries.</summary>
    public const int Unknown = -1;

    public static LiveTracksState Empty => new(null, Unknown);

    /// <summary>The state of one channel, or null when Live has said nothing about it.</summary>
    public LiveTrackState? Find(int index)
    {
        if (Tracks is null) return null;

        foreach (var track in Tracks)
            if (track.Index == index)
                return track;

        return null;
    }
}

/// <summary>
/// The transport half of the bridge: what Live is doing, pushed as it changes.
/// </summary>
/// <remarks>
/// Separate from <see cref="IAbletonMixer"/> because the two are not paid for the same way. The
/// mixer's listeners cost a datagram per fader move and are only attached while its window is open;
/// these four values change when a person presses something, so the Remote Script watches them for
/// its whole life and every deck on screen can read them for free.
/// </remarks>
public interface IAbletonSong
{
    /// <summary>The last thing Live said, or <see cref="LiveSongState.Unknown"/>.</summary>
    LiveSongState State { get; }

    /// <summary>Raised whenever any of it changes. May arrive on a background thread.</summary>
    event Action<LiveSongState>? StateChanged;

    /// <summary>Every channel's mute, solo and arm, and which one is selected.</summary>
    LiveTracksState Tracks { get; }

    /// <summary>Raised when any channel's switches, or the selection, change. May be off-thread.</summary>
    event Action<LiveTracksState>? TracksChanged;
}
