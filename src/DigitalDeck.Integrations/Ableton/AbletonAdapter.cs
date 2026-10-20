using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Integrations.Ableton.Osc;

namespace DigitalDeck.Integrations.Ableton;

/// <summary>
/// Talks to the DigitalDeck Remote Script running inside Ableton Live.
/// </summary>
/// <remarks>
/// UDP has no notion of a connection, so "is Live there?" has to be inferred. The rule here is:
/// anything at all arriving from the script counts as proof of life, and a ping goes out on a
/// timer in case nothing else has. Live is declared gone only after several pings in a row go
/// unanswered — a single dropped datagram must not make the indicator flicker.
/// <para>
/// Commands are sent and not awaited, because there is nothing to await: the script carries them
/// out on Live's own thread and only speaks up if something went wrong. That is why a press is
/// refused up front when the bridge is down, and why failures arrive later through
/// <see cref="ScriptFailed"/> instead of as a return value.
/// </para>
/// </remarks>
public sealed class AbletonAdapter : IAbletonClient, IAbletonMixer, IAbletonSong, IDisposable
{
    /// <summary>Bridge version this build of the app was written against.</summary>
    public const string ExpectedScriptVersion = "2.6.0";

    /// <summary>Pass as the heartbeat interval to drive <see cref="Tick"/> by hand, as tests do.</summary>
    public static readonly TimeSpan ManualHeartbeat = TimeSpan.Zero;

    private static readonly TimeSpan DefaultHeartbeat = TimeSpan.FromSeconds(2);

    /// <summary>Missed pings tolerated before Live is declared gone.</summary>
    private const int MissesBeforeDisconnected = 3;

    /// <summary>How long one attempt at a question waits before the datagram is presumed lost.</summary>
    private static readonly TimeSpan DefaultQueryTimeout = TimeSpan.FromMilliseconds(1200);

    /// <summary>
    /// Attempts made at a question before giving up.
    /// </summary>
    /// <remarks>
    /// UDP drops datagrams and there is no layer underneath to notice. Resending is safe because
    /// every question here is a read: asking twice costs one more walk of a bounded tree, while
    /// not asking twice costs the user a picker that mysteriously came up empty.
    /// </remarks>
    private const int QueryAttempts = 2;

    private readonly IOscTransport _transport;
    private readonly TimeSpan _heartbeat;
    private readonly TimeSpan _queryTimeout;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private readonly List<PendingQuery> _queries = new();
    private readonly AbletonMixerState _mixer;
    private readonly AbletonSongState _song;

    private System.Threading.Timer? _timer;
    private DateTimeOffset? _lastHeard;
    private AbletonStatus _status = new(AbletonConnectionState.Disconnected, "Live no está conectado.");
    private bool _started;
    private bool _disposed;

    public AbletonAdapter(
        IOscTransport transport,
        TimeSpan? heartbeat = null,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? queryTimeout = null,
        TimeSpan? faderThrottle = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _heartbeat = heartbeat ?? DefaultHeartbeat;
        _queryTimeout = queryTimeout ?? DefaultQueryTimeout;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _mixer = new AbletonMixerState(_transport.Send, faderThrottle, _clock);
        _song = new AbletonSongState(_transport.Send);
    }

    public AbletonStatus Status
    {
        get { lock (_gate) return _status; }
    }

    public event Action<AbletonStatus>? StatusChanged;

    public event Action<string>? ScriptFailed;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _started) return;

            _started = true;
        }

        _transport.MessageReceived += OnMessageReceived;
        _transport.Start();

        if (!_transport.IsOpen)
        {
            SetStatus(new AbletonStatus(
                AbletonConnectionState.Unavailable,
                _transport.LastError ?? "No se pudo abrir el puerto para hablar con Live."));
            return;
        }

        SetStatus(new AbletonStatus(AbletonConnectionState.Connecting, "Buscando a Ableton Live…"));
        SendPing();

        if (_heartbeat > TimeSpan.Zero)
            _timer = new System.Threading.Timer(_ => Tick(), null, _heartbeat, _heartbeat);
    }

    /// <summary>
    /// One heartbeat: send a ping, and downgrade the status if the script has gone quiet.
    /// </summary>
    /// <remarks>Public so the connection logic can be tested without waiting on a real timer.</remarks>
    public void Tick()
    {
        if (!_transport.IsOpen) return;

        var now = _clock();
        DateTimeOffset? lastHeard;

        lock (_gate) lastHeard = _lastHeard;

        var silence = _heartbeat > TimeSpan.Zero ? _heartbeat : DefaultHeartbeat;
        var deadline = silence * MissesBeforeDisconnected;

        if (lastHeard is null || now - lastHeard.Value > deadline)
        {
            var state = Status.State;

            if (state is AbletonConnectionState.Connected or AbletonConnectionState.VersionMismatch)
            {
                SetStatus(new AbletonStatus(
                    AbletonConnectionState.Disconnected,
                    "Se perdió la conexión con Live."));
            }
            else if (state == AbletonConnectionState.Disconnected)
            {
                SetStatus(new AbletonStatus(
                    AbletonConnectionState.Connecting,
                    "Buscando a Ableton Live…"));
            }
        }

        SendPing();
    }

    public Task<ActionResult> ExecuteAsync(AbletonAction action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ct.ThrowIfCancellationRequested();

        if (action.Validate() is { } problem)
            return Task.FromResult(ActionResult.Failed(problem));

        var status = Status;

        if (!status.IsUsable)
            return Task.FromResult(ActionResult.Failed(Explain(status)));

        OscMessage message;

        try
        {
            message = AbletonCommandMap.ToMessage(action);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Task.FromResult(ActionResult.Failed(ex.Message));
        }

        _transport.Send(message);
        return Task.FromResult(ActionResult.Ok());
    }

    public Task<ActionResult> LoadDeviceAsync(AbletonLoadDeviceAction action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ct.ThrowIfCancellationRequested();

        if (action.Validate() is { } problem)
            return Task.FromResult(ActionResult.Failed(problem));

        var status = Status;

        if (!status.IsUsable)
            return Task.FromResult(ActionResult.Failed(Explain(status)));

        // Whether Live actually has this item is only knowable inside Live, so a failure comes
        // back later through ScriptFailed rather than as this result.
        _transport.Send(AbletonCommandMap.ToMessage(action));
        return Task.FromResult(ActionResult.Ok());
    }

    // ---------- The mixer ----------
    //
    // Everything here is delegated to AbletonMixerState, which holds the roster and the throttle.
    // It lives on this class rather than beside it because the transport has one owner: two objects
    // subscribing to the same socket would each have to know which messages belonged to the other.

    public IReadOnlyList<LiveTrack> Tracks => _mixer.Tracks;

    public event Action<IReadOnlyList<LiveTrack>>? TracksChanged
    {
        add => _mixer.TracksChanged += value;
        remove => _mixer.TracksChanged -= value;
    }

    public event Action<LiveVolume>? VolumeChanged
    {
        add => _mixer.VolumeChanged += value;
        remove => _mixer.VolumeChanged -= value;
    }

    public void Open() => _mixer.Open();

    public void Close() => _mixer.Close();

    public void SetVolume(int trackIndex, double value) => _mixer.SetVolume(trackIndex, value);

    // ---------- What the set is doing ----------
    //
    // Delegated to AbletonSongState, for the same reason as the mixer above: the transport has one
    // owner. Implemented explicitly because this class already has a Status, and a second,
    // unrelated "state" on its public surface would read as another word for the same thing.

    LiveSongState IAbletonSong.State => _song.State;

    event Action<LiveSongState>? IAbletonSong.StateChanged
    {
        add => _song.StateChanged += value;
        remove => _song.StateChanged -= value;
    }

    LiveTracksState IAbletonSong.Tracks => _song.Tracks;

    event Action<LiveTracksState>? IAbletonSong.TracksChanged
    {
        add => _song.TracksChanged += value;
        remove => _song.TracksChanged -= value;
    }

    // ---------- Questions ----------

    public async Task<AbletonBrowserListing> ListAsync(
        string category,
        string path,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(category);
        path ??= string.Empty;

        var reply = await AskAsync(
            AbletonCommandMap.ListRequest(category, path),
            AbletonCommandMap.ListAddress,
            AbletonCommandMap.ListKey(category, path),
            ct).ConfigureAwait(false);

        return reply.Message is null
            ? AbletonBrowserListing.Failed(category, path, reply.Error ?? UnansweredQuestion)
            : AbletonCommandMap.ParseListing(reply.Message);
    }

    public async Task<AbletonBrowserListing> SearchAsync(
        string category,
        string query,
        int limit = 100,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(query);

        if (query.Trim().Length == 0)
            return AbletonBrowserListing.Failed(category, query, "Escribí algo para buscar.");

        var reply = await AskAsync(
            AbletonCommandMap.SearchRequest(category, query, limit),
            AbletonCommandMap.SearchAddress,
            AbletonCommandMap.SearchKey(category, query),
            ct).ConfigureAwait(false);

        return reply.Message is null
            ? AbletonBrowserListing.Failed(category, query, reply.Error ?? UnansweredQuestion)
            : AbletonCommandMap.ParseListing(reply.Message);
    }

    public async Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken ct = default)
    {
        var reply = await AskAsync(
            AbletonCommandMap.CategoriesRequest(),
            AbletonCommandMap.CategoriesAddress,
            string.Empty,
            ct).ConfigureAwait(false);

        return reply.Message is null
            ? Array.Empty<string>()
            : AbletonCommandMap.ParseCategories(reply.Message);
    }

    private const string UnansweredQuestion =
        "Live no contestó a tiempo. Puede estar ocupado con una biblioteca grande; probá de nuevo.";

    /// <summary>
    /// Asks one question and waits for the answer that belongs to it.
    /// </summary>
    /// <remarks>
    /// Correlation is by reply address plus the echo the script sends back, since the protocol
    /// carries no request id. When only one question is outstanding on an address the echo is not
    /// even consulted — see <see cref="TryAnswer"/> — which is what keeps a harmless difference in
    /// how two languages lower-case a string from turning into a reply nobody claims.
    /// </remarks>
    private async Task<QueryReply> AskAsync(
        OscMessage request,
        string replyAddress,
        string key,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var status = Status;

        if (!status.IsUsable)
            return new QueryReply(null, Explain(status));

        var pending = new PendingQuery(replyAddress, key);

        lock (_gate) _queries.Add(pending);

        try
        {
            for (var attempt = 0; attempt < QueryAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                _transport.Send(request);

                using var expiry = CancellationTokenSource.CreateLinkedTokenSource(ct);

                var waiting = Task.Delay(_queryTimeout, expiry.Token);
                var finished = await Task.WhenAny(pending.Completion.Task, waiting).ConfigureAwait(false);

                // Stops the timer from outliving the answer it was guarding.
                expiry.Cancel();

                if (finished == pending.Completion.Task)
                    return await pending.Completion.Task.ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            return new QueryReply(null, UnansweredQuestion);
        }
        finally
        {
            lock (_gate) _queries.Remove(pending);
        }
    }

    /// <summary>Hands a reply to the question that asked for it. True when one was waiting.</summary>
    private bool TryAnswer(OscMessage message)
    {
        var key = AbletonCommandMap.KeyOf(message);

        if (key is null) return false;

        PendingQuery? match;

        lock (_gate)
        {
            var candidates = _queries.Where(query => query.Address == message.Address).ToList();

            // One outstanding question on this address means the answer is unambiguously its own,
            // whatever the echo says. Only when several are in flight does the key have to decide.
            match = candidates.Count == 1
                ? candidates[0]
                : candidates.FirstOrDefault(query => query.Key == key);
        }

        return match is not null && match.Completion.TrySetResult(new QueryReply(message, null));
    }

    /// <summary>
    /// Fails the oldest question waiting on <paramref name="address"/>, if there is one.
    /// </summary>
    /// <remarks>
    /// The script answers a question it refuses with an error rather than with silence, so there
    /// is no reason to make the caller sit through two timeouts to learn what it already said.
    /// </remarks>
    private bool TryFailQuery(string address, string reason)
    {
        PendingQuery? match;

        lock (_gate) match = _queries.FirstOrDefault(query => query.Address == address);

        return match is not null && match.Completion.TrySetResult(new QueryReply(null, reason));
    }

    /// <summary>A question that has gone out and is waiting for its answer.</summary>
    private sealed class PendingQuery
    {
        public PendingQuery(string address, string key)
        {
            Address = address;
            Key = key;
        }

        /// <summary>The address the answer will arrive on, which is the one it was asked on.</summary>
        public string Address { get; }

        public string Key { get; }

        // Asynchronous continuations: the answer is completed from the transport's receive loop,
        // and a UI continuation must not run inside it.
        public TaskCompletionSource<QueryReply> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly record struct QueryReply(OscMessage? Message, string? Error);

    /// <summary>What to tell the user when a press could not be sent.</summary>
    public static string Explain(AbletonStatus status) => status.State switch
    {
        AbletonConnectionState.Connected => "Live está conectado.",
        AbletonConnectionState.Connecting => "Todavía no encuentro a Ableton Live.",
        AbletonConnectionState.VersionMismatch =>
            $"El Remote Script instalado es la versión {status.ScriptVersion}, y esta app necesita " +
            $"la {ExpectedScriptVersion}. Reinstalalo y reiniciá Live.",
        AbletonConnectionState.Unavailable => status.Detail,
        _ => "Ableton Live no está conectado. Abrilo y activá DigitalDeck en Control Surface."
    };

    // ---------- Inbound ----------

    private void OnMessageReceived(OscMessage message)
    {
        // Any message is proof the script is alive, whatever it says. Relying on pongs alone would
        // make the indicator lag behind a session that is plainly working.
        lock (_gate) _lastHeard = _clock();

        if (message.Address == AbletonCommandMap.PongAddress)
        {
            OnPong(message);
            return;
        }

        if (message.Address == AbletonCommandMap.ErrorAddress)
        {
            var address = message.GetString(0) ?? "una acción";
            var reason = message.GetString(1) ?? "Live la rechazó.";

            // A question waiting on that address gets the refusal as its answer. Reporting it as a
            // failed button as well would put "Ableton rechazó /digitaldeck/browser/list" in the
            // status bar every time someone opened a folder that turned out to be empty.
            if (TryFailQuery(address, reason)) return;

            // A script from before this address existed answers "unknown address" to it, and that
            // is not something to put in red on the status bar: the deck works, it just cannot
            // light its buttons until the script is reinstalled — which the app already offers.
            if (address == AbletonCommandMap.TrackStatesAddress) return;

            ScriptFailed?.Invoke($"Ableton rechazó {address}: {reason}");
            return;
        }

        if (TryAnswer(message)) return;

        // State the script publishes as it changes: the mixer window, and the lit buttons.
        if (_mixer.TryHandle(message)) return;
        if (_song.TryHandle(message)) return;

        // Anything else still counts as the liveness signal handled above, and nothing more.
    }

    private void OnPong(OscMessage message)
    {
        var scriptVersion = message.GetString(0);
        var liveVersion = message.GetString(1);

        // Scripts before 2.3.0 do not send these. Zero means "it did not say", not port zero.
        var listens = message.GetInt(3);
        var replies = message.GetInt(4);

        var ports = listens > 0 && replies > 0 ? new AbletonPorts(listens, replies) : (AbletonPorts?)null;

        if (!IsCompatible(scriptVersion))
        {
            SetStatus(new AbletonStatus(
                AbletonConnectionState.VersionMismatch,
                $"El Remote Script es la versión {scriptVersion ?? "desconocida"} y esta app " +
                $"necesita la {ExpectedScriptVersion}.",
                scriptVersion,
                liveVersion,
                ports));
            return;
        }

        var where = ports is { } pair ? $" (puertos {pair})" : string.Empty;

        SetStatus(new AbletonStatus(
            AbletonConnectionState.Connected,
            liveVersion is null
                ? $"Conectado a Ableton Live{where}."
                : $"Conectado a Ableton Live {liveVersion}{where}.",
            scriptVersion,
            liveVersion,
            ports));
    }

    /// <summary>
    /// Same major version, which is where a breaking change to the address space is announced.
    /// </summary>
    private static bool IsCompatible(string? scriptVersion)
    {
        if (string.IsNullOrWhiteSpace(scriptVersion)) return false;

        var theirs = scriptVersion.Split('.')[0];
        var ours = ExpectedScriptVersion.Split('.')[0];

        return string.Equals(theirs, ours, StringComparison.Ordinal);
    }

    private void SendPing() => _transport.Send(new OscMessage(AbletonCommandMap.PingAddress));

    private void SetStatus(AbletonStatus status)
    {
        lock (_gate)
        {
            if (_status == status) return;

            _status = status;
        }

        // A reconnect means the script was restarted, and the listeners the mixer window asked for
        // died with it. Asking again here is what stops an open window from sitting there showing
        // a set that has since been closed.
        //
        // The transport is the mirror image: the script watches it without being asked, but only
        // speaks when a value changes, so the current one has to be asked for once. And when the
        // bridge goes down, what Live last said stops being true — see AbletonSongState.Forget.
        if (status.State == AbletonConnectionState.Connected)
        {
            _mixer.Resubscribe();
            _song.Ask();
        }
        else
        {
            _song.Forget();
        }

        StatusChanged?.Invoke(status);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
        }

        _timer?.Dispose();
        _timer = null;

        _mixer.Dispose();

        _transport.MessageReceived -= OnMessageReceived;
        _transport.Dispose();
    }
}
