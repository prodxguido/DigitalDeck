using DigitalDeck.Integrations.Ableton.Osc;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// A transport with no socket behind it.
/// </summary>
/// <remarks>
/// Everything interesting about the adapter — when it decides Live is gone, how it reacts to a
/// script from the wrong version, what it refuses to send — is timing and state, not networking.
/// Driving it through this instead of a real UDP pair keeps those tests instant and deterministic,
/// and lets them run on a machine with no Ableton on it.
/// </remarks>
public sealed class FakeOscTransport : IOscTransport
{
    public List<OscMessage> Sent { get; } = new();

    public bool IsOpen { get; private set; }

    public string? LastError { get; set; }

    public bool FailToOpen { get; set; }

    public bool Disposed { get; private set; }

    public event Action<OscMessage>? MessageReceived;

    public void Start()
    {
        if (FailToOpen)
        {
            IsOpen = false;
            LastError ??= "el puerto está ocupado";
            return;
        }

        IsOpen = true;
    }

    /// <summary>
    /// Answers a message the moment it is sent, as the script would.
    /// </summary>
    /// <remarks>
    /// Returning null stands for a datagram that never arrived, which is the case the retry exists
    /// for and the one no real network would reproduce on demand.
    /// </remarks>
    public Func<OscMessage, OscMessage?>? Answer { get; set; }

    public void Send(OscMessage message)
    {
        Sent.Add(message);

        if (Answer?.Invoke(message) is { } reply)
            Receive(reply);
    }

    /// <summary>Pretends the Remote Script said something.</summary>
    public void Receive(OscMessage message) => MessageReceived?.Invoke(message);

    /// <summary>Pretends the script answered a handshake.</summary>
    public void ReceivePong(string scriptVersion = "2.0.0", string liveVersion = "12.3.0") =>
        Receive(new OscMessage("/digitaldeck/pong", scriptVersion, liveVersion, "12"));

    public IReadOnlyList<string> SentAddresses => Sent.Select(message => message.Address).ToList();

    public void Dispose()
    {
        Disposed = true;
        IsOpen = false;
    }
}
