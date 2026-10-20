using System.Runtime.InteropServices;
using DigitalDeck.Core.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace DigitalDeck.Integrations.Voice;

/// <summary>
/// Microphone capture through WASAPI in shared mode, resampled to what the recogniser reads.
/// </summary>
/// <remarks>
/// Shared mode is the whole point. It reaches the same hardware as Live without competing for it,
/// because WASAPI and ASIO are separate paths to the driver rather than two clients of one — so the
/// assistant keeps working with Live open, recording, on the same interface.
/// <para>
/// The one arrangement this cannot serve is a microphone wired into an interface whose driver hides
/// its inputs from Windows while ASIO holds them. There is no clever way around that from here; the
/// answer is any other microphone, which is why choosing the device is a setting.
/// </para>
/// </remarks>
public sealed class WasapiVoiceInput : IVoiceInput
{
    /// <summary>E_ACCESSDENIED, which for a microphone means the Windows privacy setting.</summary>
    private const int AccessDeniedHResult = unchecked((int)0x80070005);

    private readonly object _gate = new();

    private WasapiCapture? _capture;
    private VoiceSampleChain? _chain;
    private bool _stopping;
    private bool _disposed;

    public bool IsCapturing
    {
        get { lock (_gate) return _capture is not null; }
    }

    public event Action<short[]>? SamplesCaptured;
    public event Action<VoiceInputError>? Faulted;

    public IReadOnlyList<VoiceInputDevice> GetDevices()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();

            var defaultId = TryGetDefaultId(enumerator);
            var devices = new List<VoiceInputDevice>();

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                    devices.Add(new VoiceInputDevice(device.ID, device.FriendlyName, device.ID == defaultId));
            }

            // The default first, so the list opens on the one that is most likely right.
            return devices.OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name).ToList();
        }
        catch (Exception)
        {
            // The audio service can be genuinely absent — a session with no sound card, a remote
            // desktop. An empty list is the honest answer, and the UI can say so.
            return [];
        }
    }

    public void Start(string? deviceId = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            StopLocked();

            MMDevice? device = null;

            try
            {
                using var enumerator = new MMDeviceEnumerator();

                device = Resolve(enumerator, deviceId);

                if (device is null)
                {
                    Faulted?.Invoke(new VoiceInputError(
                        VoiceInputFault.DeviceMissing,
                        "No hay ningún micrófono disponible."));

                    return;
                }

                var capture = new WasapiCapture(device) { ShareMode = AudioClientShareMode.Shared };

                _chain = new VoiceSampleChain(capture.WaveFormat);

                capture.DataAvailable += OnDataAvailable;
                capture.RecordingStopped += OnRecordingStopped;

                _capture = capture;
                _stopping = false;

                capture.StartRecording();
            }
            catch (Exception ex)
            {
                StopLocked();
                Faulted?.Invoke(Describe(ex));
            }
            finally
            {
                device?.Dispose();
            }
        }
    }

    public void Stop()
    {
        lock (_gate) StopLocked();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;
            StopLocked();
        }
    }

    // ---------- Capture ----------

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        short[] samples;

        lock (_gate)
        {
            if (_chain is null || _stopping) return;

            samples = _chain.Add(e.Buffer, e.BytesRecorded);
            if (samples.Length == 0) return;
        }

        // Raised outside the lock: the recogniser runs on this thread, and holding the capture lock
        // through a whole decode would stall the next buffer behind it.
        SamplesCaptured?.Invoke(samples);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        bool expected;

        lock (_gate) expected = _stopping;

        if (expected || e.Exception is null) return;

        Stop();
        Faulted?.Invoke(Describe(e.Exception));
    }

    // ---------- Devices ----------

    private static MMDevice? Resolve(MMDeviceEnumerator enumerator, string? deviceId)
    {
        if (deviceId is { Length: > 0 })
        {
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                if (device.ID == deviceId) return device;

                device.Dispose();
            }

            // Falls through rather than failing: a saved device going away is ordinary — a headset
            // unplugged, a dock left at the office — and it should not cost the user the feature.
        }

        return TryGetDefault(enumerator);
    }

    /// <summary>The microphone Windows would pick, as the user understands that phrase.</summary>
    /// <remarks>
    /// <see cref="Role.Console"/>, not <see cref="Role.Communications"/>. Windows keeps two separate
    /// defaults for capture: the one in Settings › Sound › Input, which is Console, and a
    /// communications default that only appears in the old Sound control panel. Almost nobody sets
    /// the second, so honouring it means someone changes their default microphone, sees no
    /// difference here, and has no way to find out why.
    /// </remarks>
    private static MMDevice? TryGetDefault(MMDeviceEnumerator enumerator)
    {
        try
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? TryGetDefaultId(MMDeviceEnumerator enumerator)
    {
        using var device = TryGetDefault(enumerator);

        return device?.ID;
    }

    private static VoiceInputError Describe(Exception ex) => ex switch
    {
        COMException { ErrorCode: AccessDeniedHResult } => new VoiceInputError(
            VoiceInputFault.AccessDenied,
            "Windows bloqueó el acceso al micrófono. Activalo en Configuración › Privacidad y "
            + "seguridad › Micrófono, y permití que las aplicaciones de escritorio lo usen."),

        UnauthorizedAccessException => new VoiceInputError(
            VoiceInputFault.AccessDenied,
            "Windows bloqueó el acceso al micrófono. Revisalo en Configuración › Privacidad y "
            + "seguridad › Micrófono."),

        _ => new VoiceInputError(VoiceInputFault.Failed, $"No se pudo abrir el micrófono: {ex.Message}")
    };

    /// <summary>Tears capture down. Must be called with <see cref="_gate"/> held.</summary>
    private void StopLocked()
    {
        var capture = _capture;
        if (capture is null) return;

        _stopping = true;
        _capture = null;

        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;

        _chain = null;

        // Torn down on some other thread, deliberately. The ordinary way to reach this method is
        // from inside DataAvailable — the recogniser finishing a phrase is what closes the
        // microphone — and NAudio's teardown waits on the very capture thread that would be
        // calling it. Detaching the handlers above is what makes deferring safe: nothing else
        // arrives in the meantime.
        Task.Run(() =>
        {
            try
            {
                capture.StopRecording();
                capture.Dispose();
            }
            catch (Exception)
            {
                // Already gone. Nothing here is worth surfacing — the caller asked for it to stop.
            }
        });
    }
}
