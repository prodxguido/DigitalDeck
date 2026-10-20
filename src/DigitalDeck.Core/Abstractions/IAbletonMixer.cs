using DigitalDeck.Core.Model;

namespace DigitalDeck.Core.Abstractions;

/// <summary>One channel's fader moving, wherever the move came from.</summary>
public readonly record struct LiveVolume(int TrackIndex, double Value)
{
    /// <summary>
    /// What Live prints under the fader at this position, or empty when it would not say. See
    /// <see cref="LiveTrack.Display"/> for why this is Live's answer rather than a conversion here.
    /// </summary>
    public string Display { get; init; } = "";
}

/// <summary>
/// Live's mixer as a set of faders the app can read and move.
/// </summary>
/// <remarks>
/// Core stops here for the same reason it does at <see cref="IAbletonClient"/>: it knows there are
/// channels with volumes on them, and nothing about OSC, indices counted downwards from the master,
/// or how often a datagram may be sent. That boundary is what lets the mixer's view model be tested
/// without a socket.
/// <para>
/// Live only pushes while <see cref="Open"/> has been called, so a deck with the mixer window
/// closed costs nothing. <see cref="Close"/> is not optional housekeeping — leaving it subscribed
/// means every fader move in Live keeps crossing the bridge with nowhere to land.
/// </para>
/// </remarks>
public interface IAbletonMixer
{
    /// <summary>The channels as last reported, or empty before Live has answered.</summary>
    IReadOnlyList<LiveTrack> Tracks { get; }

    /// <summary>
    /// Raised when the set of channels changes — one added, removed or renamed. Carries the whole
    /// roster, because an index only means anything relative to the rest of it. May arrive on a
    /// background thread.
    /// </summary>
    event Action<IReadOnlyList<LiveTrack>>? TracksChanged;

    /// <summary>
    /// Raised when a fader moves inside Live. May arrive on a background thread.
    /// </summary>
    /// <remarks>
    /// This also fires for moves the app itself just made — Live does not distinguish, and asking
    /// it to would mean the app trusting its own echo over the truth. Whoever is drawing a fader
    /// the user has hold of is the one that has to ignore it; see the mixer view model.
    /// </remarks>
    event Action<LiveVolume>? VolumeChanged;

    /// <summary>Asks Live for the roster and to start pushing changes. Safe to call more than once.</summary>
    void Open();

    /// <summary>Tells Live to stop pushing. Safe to call when it never started.</summary>
    void Close();

    /// <summary>
    /// Moves one fader. Sent on a throttle, so calling this on every pixel of a drag is fine and
    /// the value that lands last is the one the user let go on.
    /// </summary>
    void SetVolume(int trackIndex, double value);
}
