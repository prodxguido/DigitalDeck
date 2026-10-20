using System.Net;
using System.Net.Sockets;
using DigitalDeck.Core.Abstractions;

namespace DigitalDeck.Integrations.Ableton.Osc;

/// <summary>
/// The loopback UDP socket the app uses to talk to the Remote Script.
/// </summary>
/// <remarks>
/// Bound to <see cref="IPAddress.Loopback"/> and never to Any. That is a deliberate security
/// boundary, not a default: an OSC endpoint reachable from the network would let anything on the
/// LAN start playback, change tempo or delete tracks in the user's set. Outgoing datagrams go to
/// loopback for the same reason.
/// </remarks>
public sealed class UdpOscTransport : IOscTransport
{
    /// <summary>
    /// Where the Remote Script listens.
    /// </summary>
    /// <remarks>
    /// Deliberately not AbletonOSC's 11000. Live loads both scripts happily — Control Surface has
    /// several slots — but two processes cannot bind one UDP port, so sharing the pair leaves
    /// whichever started second silently deaf. See <see cref="AbletonPorts"/>.
    /// </remarks>
    public const int DefaultSendPort = 11010;

    /// <summary>Where the Remote Script sends its replies.</summary>
    public const int DefaultReceivePort = 11011;

    private readonly IPEndPoint _destination;
    private readonly int _receivePort;
    private readonly object _gate = new();

    private UdpClient? _client;
    private CancellationTokenSource? _cancellation;
    private Task? _listener;
    private bool _disposed;

    public UdpOscTransport(int receivePort = DefaultReceivePort, int sendPort = DefaultSendPort)
    {
        _receivePort = receivePort;
        _destination = new IPEndPoint(IPAddress.Loopback, sendPort);
    }

    public bool IsOpen
    {
        get { lock (_gate) return _client is not null; }
    }

    public string? LastError { get; private set; }

    public event Action<OscMessage>? MessageReceived;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _client is not null) return;

            try
            {
                _client = new UdpClient(new IPEndPoint(IPAddress.Loopback, _receivePort));
                LastError = null;
            }
            catch (SocketException ex)
            {
                // Almost always another OSC client already holding the port — AbletonOSC's own
                // tooling, or a second copy of DigitalDeck.
                _client = null;
                LastError =
                    $"No se pudo abrir el puerto UDP {_receivePort} ({ex.SocketErrorCode}). " +
                    "¿Hay otra instancia de DigitalDeck o de AbletonOSC escuchando?";
                return;
            }

            _cancellation = new CancellationTokenSource();
            _listener = Task.Run(() => ListenAsync(_client, _cancellation.Token));
        }
    }

    public void Send(OscMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        UdpClient? client;
        lock (_gate) client = _client;

        if (client is null) return;

        try
        {
            var datagram = OscCodec.Encode(message);
            client.Send(datagram, datagram.Length, _destination);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // Live going away mid-send is ordinary. The heartbeat is what notices, not this.
            LastError = ex.Message;
        }
    }

    private async Task ListenAsync(UdpClient client, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult received;

            try
            {
                received = await client.ReceiveAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                // A refused ICMP from a script that just went away surfaces here. Keep listening:
                // the script comes back when Live restarts, and the socket is still ours.
                continue;
            }

            if (!OscCodec.TryDecode(received.Buffer, out var message, out _) || message is null)
                continue;

            try
            {
                MessageReceived?.Invoke(message);
            }
            catch (Exception)
            {
                // A throwing subscriber must not kill the receive loop; without it the app would
                // silently stop hearing from Live with no way back short of a restart.
            }
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        UdpClient? client;
        Task? listener;

        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            cancellation = _cancellation;
            client = _client;
            listener = _listener;

            _cancellation = null;
            _client = null;
            _listener = null;
        }

        cancellation?.Cancel();
        client?.Dispose();

        // The receive loop is already unblocked by the disposed socket; this only keeps a stray
        // callback from arriving after Dispose returns.
        try
        {
            listener?.Wait(TimeSpan.FromMilliseconds(250));
        }
        catch (AggregateException)
        {
        }

        cancellation?.Dispose();
    }
}
