namespace DigitalDeck.Integrations.Ableton.Osc;

/// <summary>
/// Where OSC messages go and come from.
/// </summary>
/// <remarks>
/// An interface rather than the UDP class directly so the adapter's connection logic — heartbeats,
/// timeouts, version checks — can be tested without opening a socket or having Live installed.
/// </remarks>
public interface IOscTransport : IDisposable
{
    /// <summary>False when the socket could not be opened; <see cref="LastError"/> says why.</summary>
    bool IsOpen { get; }

    /// <summary>Why the transport is not open, or null.</summary>
    string? LastError { get; }

    /// <summary>
    /// Raised for every message that arrives. Fired on a background thread — a subscriber that
    /// touches the UI has to marshal.
    /// </summary>
    event Action<OscMessage>? MessageReceived;

    /// <summary>Opens the socket and starts listening. Safe to call more than once.</summary>
    void Start();

    /// <summary>Sends one message. Fire and forget: UDP cannot tell us it arrived.</summary>
    void Send(OscMessage message);
}
