using DigitalDeck.Core.Abstractions;
using DigitalDeck.Integrations.Ableton.Osc;

namespace DigitalDeck.Integrations.Ableton;

/// <summary>
/// The transport half of the bridge: the cache behind <see cref="IAbletonSong"/>.
/// </summary>
/// <remarks>
/// Split out of <see cref="AbletonAdapter"/> for the same reason <see cref="AbletonMixerState"/> is:
/// it holds state that changes while nobody asked. There is no throttle here and there does not need
/// to be — these four values move when a person presses something in Live, not with the audio.
/// <para>
/// The script watches them for its whole life, so nothing has to be subscribed. What the app does
/// have to do is <see cref="Ask"/> once when the bridge comes up: the observers only speak when a
/// value <em>changes</em>, so a deck that started while Live was already playing would otherwise sit
/// there dark until somebody pressed stop. And <see cref="Forget"/> when it goes down, because the
/// last thing Live said stops being true the moment it is no longer there to contradict it.
/// </para>
/// </remarks>
public sealed class AbletonSongState
{
    private readonly Action<OscMessage> _send;
    private readonly object _gate = new();

    private LiveSongState _state = LiveSongState.Unknown;
    private LiveTracksState _tracks = LiveTracksState.Empty;

    public AbletonSongState(Action<OscMessage> send)
    {
        _send = send ?? throw new ArgumentNullException(nameof(send));
    }

    public LiveSongState State
    {
        get { lock (_gate) return _state; }
    }

    public LiveTracksState Tracks
    {
        get { lock (_gate) return _tracks; }
    }

    public event Action<LiveSongState>? StateChanged;

    public event Action<LiveTracksState>? TracksChanged;

    /// <summary>Asks Live for all four values at once. Called when the bridge reaches Connected.</summary>
    public void Ask()
    {
        _send(new OscMessage(AbletonCommandMap.SongIsPlayingAddress));
        _send(new OscMessage(AbletonCommandMap.SongTempoAddress));
        _send(new OscMessage(AbletonCommandMap.SongMetronomeAddress));
        _send(new OscMessage(AbletonCommandMap.SongRecordAddress));
        _send(new OscMessage(AbletonCommandMap.TrackStatesAddress));
    }

    /// <summary>Drops everything back to unknown, so no button keeps showing a set that is gone.</summary>
    public void Forget()
    {
        bool hadSong;
        bool hadTracks;

        lock (_gate)
        {
            hadSong = _state.IsKnown;
            hadTracks = _tracks.Tracks is not null;

            _state = LiveSongState.Unknown;
            _tracks = LiveTracksState.Empty;
        }

        if (hadSong) StateChanged?.Invoke(LiveSongState.Unknown);
        if (hadTracks) TracksChanged?.Invoke(LiveTracksState.Empty);
    }

    /// <summary>Takes one inbound message if it is the transport's. True when it was.</summary>
    public bool TryHandle(OscMessage message)
    {
        var updated = message.Address switch
        {
            AbletonCommandMap.SongIsPlayingAddress =>
                (LiveSongState?)With(state => state with { IsPlaying = message.GetInt(0) != 0 }),
            AbletonCommandMap.SongRecordAddress =>
                With(state => state with { IsRecording = message.GetInt(0) != 0 }),
            AbletonCommandMap.SongMetronomeAddress =>
                With(state => state with { MetronomeOn = message.GetInt(0) != 0 }),
            AbletonCommandMap.SongTempoAddress =>
                With(state => state with { Tempo = message.GetDouble(0) }),
            _ => null
        };

        if (updated is { } state)
        {
            StateChanged?.Invoke(state);
            return true;
        }

        if (message.Address != AbletonCommandMap.TrackStatesAddress) return false;

        var tracks = AbletonCommandMap.ParseTrackStates(message);

        lock (_gate) _tracks = tracks;

        TracksChanged?.Invoke(tracks);
        return true;
    }

    private LiveSongState With(Func<LiveSongState, LiveSongState> change)
    {
        lock (_gate) return _state = change(_state);
    }
}
