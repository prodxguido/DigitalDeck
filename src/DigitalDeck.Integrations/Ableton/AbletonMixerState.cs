using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Integrations.Ableton.Osc;

namespace DigitalDeck.Integrations.Ableton;

/// <summary>
/// The mixer half of the bridge: the roster of channels, and the throttle in front of the faders.
/// </summary>
/// <remarks>
/// Split out of <see cref="AbletonAdapter"/> rather than folded into it because it is the only part
/// of the bridge holding state that changes while nobody asked — everything else there is a
/// question, an answer, or a heartbeat.
/// <para>
/// The throttle is the reason this exists at all. A finger dragging a fader produces a position
/// every frame, and each one of those is a datagram Live has to pick up on its own thread inside a
/// tick that is also draining everything else. So moves are coalesced: the newest value for a
/// channel replaces the one that has not gone out yet, and the last thing the user did always
/// leaves — a throttle that swallowed the final position would leave the fader on screen and the
/// fader in Live disagreeing, which is the one failure a mixer does not get forgiven for.
/// </para>
/// </remarks>
public sealed class AbletonMixerState : IDisposable
{
    /// <summary>Pass as the interval to drive <see cref="Pump"/> by hand, as tests do.</summary>
    public static readonly TimeSpan ManualThrottle = TimeSpan.Zero;

    /// <summary>
    /// How close together two moves of the same fader may go out.
    /// </summary>
    /// <remarks>
    /// 40ms is four of the Remote Script's 10ms ticks, so a drag never has more than one move
    /// waiting for it, while staying well under the ~100ms where a fader starts to feel like it is
    /// being dragged through mud.
    /// </remarks>
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(40);

    private readonly Action<OscMessage> _send;
    private readonly TimeSpan _interval;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private readonly Dictionary<int, double> _pending = new();

    private System.Threading.Timer? _timer;
    private DateTimeOffset _lastSent = DateTimeOffset.MinValue;
    private IReadOnlyList<LiveTrack> _tracks = Array.Empty<LiveTrack>();
    private bool _open;
    private bool _disposed;

    public AbletonMixerState(
        Action<OscMessage> send,
        TimeSpan? interval = null,
        Func<DateTimeOffset>? clock = null)
    {
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _interval = interval ?? DefaultInterval;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<LiveTrack> Tracks
    {
        get { lock (_gate) return _tracks; }
    }

    /// <summary>True while Live has been asked to push. Read when the bridge comes back up.</summary>
    public bool IsOpen
    {
        get { lock (_gate) return _open; }
    }

    public event Action<IReadOnlyList<LiveTrack>>? TracksChanged;

    public event Action<LiveVolume>? VolumeChanged;

    // ---------- The subscription ----------

    public void Open()
    {
        lock (_gate) _open = true;

        Resubscribe();
    }

    public void Close()
    {
        lock (_gate)
        {
            _open = false;
            _pending.Clear();
        }

        StopTimer();
        _send(AbletonCommandMap.MixerSubscribeRequest(false));
    }

    /// <summary>
    /// Asks Live to push again, without changing whether the window thinks it is open.
    /// </summary>
    /// <remarks>
    /// Called when the bridge reconnects. The listeners inside Live went away with the script, so a
    /// window that was open across a restart of Live would otherwise sit there showing whatever was
    /// true before, with no way of finding out it is now wrong.
    /// </remarks>
    public void Resubscribe()
    {
        if (!IsOpen) return;

        _send(AbletonCommandMap.MixerSubscribeRequest(true));
    }

    // ---------- Moving a fader ----------

    public void SetVolume(int trackIndex, double value)
    {
        var clamped = Math.Clamp(value, 0, 1);

        lock (_gate)
        {
            if (!_open) return;

            _pending[trackIndex] = clamped;
        }

        Pump();
    }

    /// <summary>
    /// Sends whatever is waiting, if enough time has passed; otherwise arms the timer that will.
    /// </summary>
    /// <remarks>Public so the throttle can be tested without waiting on a real timer.</remarks>
    public void Pump()
    {
        KeyValuePair<int, double>[] going;

        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                StopTimerLocked();
                return;
            }

            var window = _interval > TimeSpan.Zero ? _interval : DefaultInterval;
            var now = _clock();

            if (now - _lastSent < window)
            {
                ArmLocked(window - (now - _lastSent));
                return;
            }

            going = _pending.ToArray();
            _pending.Clear();
            _lastSent = now;

            // Kept armed for one more window: whatever arrives during it must not sit in the
            // dictionary waiting for a move that never comes because the finger stopped.
            ArmLocked(window);
        }

        foreach (var (index, value) in going)
            _send(AbletonCommandMap.SetVolumeRequest(index, value));
    }

    // ---------- What Live says back ----------

    /// <summary>Takes one inbound message if it is the mixer's. True when it was.</summary>
    public bool TryHandle(OscMessage message)
    {
        if (message.Address == AbletonCommandMap.MixerTracksAddress)
        {
            var tracks = AbletonCommandMap.ParseRoster(message);

            lock (_gate) _tracks = tracks;

            TracksChanged?.Invoke(tracks);
            return true;
        }

        if (message.Address == AbletonCommandMap.TrackVolumeAddress)
        {
            VolumeChanged?.Invoke(new LiveVolume(message.GetInt(0), message.GetDouble(1))
            {
                // Scripts before 2.5.0 send only the position. An empty reading costs the label
                // under one fader until the next roster arrives, which is the right amount of harm.
                Display = message.GetString(2) ?? string.Empty
            });
            return true;
        }

        return false;
    }

    // ---------- Timer plumbing ----------

    private void ArmLocked(TimeSpan due)
    {
        if (_interval <= TimeSpan.Zero || _disposed) return;

        if (_timer is null) _timer = new System.Threading.Timer(_ => Pump(), null, due, Timeout.InfiniteTimeSpan);
        else _timer.Change(due, Timeout.InfiniteTimeSpan);
    }

    private void StopTimer()
    {
        lock (_gate) StopTimerLocked();
    }

    private void StopTimerLocked()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            _pending.Clear();
            StopTimerLocked();
        }
    }
}
