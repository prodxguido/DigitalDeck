namespace DigitalDeck.Core.Abstractions;

/// <param name="Id">
/// Opaque handle for the device, stable enough to store in settings. Windows can hand the same
/// microphone a different friendly name after a driver update, so the id is what gets saved and
/// the name is only ever shown.
/// </param>
/// <param name="IsDefault">True for the microphone Windows would pick on its own.</param>
public sealed record VoiceInputDevice(string Id, string Name, bool IsDefault);

/// <summary>Why listening stopped, when it stopped on its own.</summary>
public enum VoiceInputFault
{
    /// <summary>The device is gone — unplugged, or disabled since it was chosen.</summary>
    DeviceMissing,

    /// <summary>Windows refused the microphone. Almost always the privacy setting, not a bug.</summary>
    AccessDenied,

    /// <summary>Something else went wrong while capturing.</summary>
    Failed
}

/// <param name="Detail">Human-readable explanation, shown to the user as-is.</param>
public readonly record struct VoiceInputError(VoiceInputFault Fault, string Detail);

/// <summary>
/// A microphone, delivered as the one format the recogniser can read.
/// </summary>
/// <remarks>
/// Core stops at "there is a microphone and it produces samples": nothing above this line knows
/// about WASAPI, channel counts or sample rates. That boundary is what lets the recogniser and the
/// command matching above it be tested from a .wav file with no sound card in the room.
/// <para>
/// Deliberately <b>not</b> ASIO. An ASIO driver is single-client, so asking for one while Live
/// holds it would fail exactly when it is needed; WASAPI reaches the same hardware down a separate
/// path that does not collide with Live. The low latency ASIO exists for is worth nothing here —
/// speech recognition wants 16 kHz mono, and every bit of that machinery would be resampled away.
/// </para>
/// </remarks>
public interface IVoiceInput : IDisposable
{
    /// <summary>What <see cref="SamplesCaptured"/> delivers, and what the recogniser expects.</summary>
    const int SampleRate = 16_000;

    /// <summary>Microphones Windows currently offers, best candidate first.</summary>
    IReadOnlyList<VoiceInputDevice> GetDevices();

    bool IsCapturing { get; }

    /// <summary>
    /// One chunk of 16 kHz mono PCM. Arrives on a background thread, roughly ten times a second.
    /// </summary>
    event Action<short[]>? SamplesCaptured;

    /// <summary>
    /// Raised when capture stops for a reason of its own. Opening a microphone fails for ordinary,
    /// explainable reasons — it is unplugged, or Windows privacy has the app blocked — so those
    /// arrive here as something to show the user rather than as an exception to crash on.
    /// </summary>
    event Action<VoiceInputError>? Faulted;

    /// <summary>
    /// Begins listening. Safe to call while already listening, which restarts on the new device.
    /// </summary>
    /// <param name="deviceId">
    /// A <see cref="VoiceInputDevice.Id"/>, or null for whatever Windows calls the default. An id
    /// that no longer matches a present device falls back to the default rather than failing: the
    /// stored choice going stale is normal, and it should not cost the user the feature.
    /// </param>
    void Start(string? deviceId = null);

    void Stop();
}
