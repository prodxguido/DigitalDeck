using DigitalDeck.Core.Abstractions;
using DigitalDeck.Integrations.Voice;
using Xunit;
using Xunit.Abstractions;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// The half of voice input no unit test can reach: whether this machine's microphone opens at all,
/// and whether it keeps working with Live holding the interface.
/// </summary>
/// <remarks>
/// Read the output, not just the green tick. The rate check below is the one that matters most,
/// because a resampling mistake does not throw — it produces audio at the wrong speed that the
/// recogniser reads as gibberish, and every hour spent afterwards is spent blaming the model.
/// </remarks>
public sealed class VoiceInputSmokeTests : IDisposable
{
    /// <summary>Long enough to average out the buffer size, short enough to sit through.</summary>
    private static readonly TimeSpan CaptureWindow = TimeSpan.FromSeconds(4);

    private readonly ITestOutputHelper _output;
    private readonly WasapiVoiceInput _input = new();

    public VoiceInputSmokeTests(ITestOutputHelper output) => _output = output;

    public void Dispose() => _input.Dispose();

    [MicrophoneFact]
    public void WindowsOffersAtLeastOneMicrophone()
    {
        var devices = _input.GetDevices();

        foreach (var device in devices)
            _output.WriteLine($"{(device.IsDefault ? "* " : "  ")}{device.Name}  [{device.Id}]");

        Assert.NotEmpty(devices);

        // Both roles, because they disagree more often than anyone expects and the disagreement is
        // invisible from Settings: changing the default microphone there moves Console and leaves
        // Communications where it was. Picking the wrong one looks like the setting being ignored.
        using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();

        foreach (var role in new[] { NAudio.CoreAudioApi.Role.Console, NAudio.CoreAudioApi.Role.Communications })
        {
            try
            {
                using var device = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, role);
                _output.WriteLine($"default {role,-15} {device.FriendlyName}");
            }
            catch (Exception)
            {
                _output.WriteLine($"default {role,-15} (ninguno)");
            }
        }
    }

    /// <summary>
    /// Stopping from inside the capture callback, which is the ordinary way it happens.
    /// </summary>
    /// <remarks>
    /// The recogniser finishing a phrase is what closes the microphone, and it runs on the thread
    /// delivering the audio — so <see cref="IVoiceInput.Stop"/> gets called from inside
    /// <c>DataAvailable</c>. NAudio's teardown waits on that same thread, which deadlocks the
    /// moment it is done inline. This asserts it comes back at all; a regression here does not
    /// fail, it hangs.
    /// </remarks>
    [MicrophoneFact]
    public void StoppingFromInsideTheCallbackDoesNotDeadlock()
    {
        var stopped = new ManualResetEventSlim();

        _input.SamplesCaptured += _ =>
        {
            _input.Stop();
            stopped.Set();
        };

        _input.Start(Environment.GetEnvironmentVariable(MicrophoneFactAttribute.DeviceSwitch));

        Assert.True(
            stopped.Wait(TimeSpan.FromSeconds(10)),
            "No llegó ni un bloque de audio en 10 segundos.");

        // The teardown is deferred, so give it a moment before asking whether it happened.
        Assert.True(
            SpinWait.SpinUntil(() => !_input.IsCapturing, TimeSpan.FromSeconds(5)),
            "Stop() volvió pero la captura quedó abierta: se colgó la baja del dispositivo.");
    }

    /// <summary>
    /// What each microphone says about itself, before any of our code touches it.
    /// </summary>
    /// <remarks>
    /// This is the test that tells a capture bug apart from a quiet room. The peak here comes from
    /// the endpoint's own meter, so a device reading zero here and zero through
    /// <see cref="WasapiVoiceInput"/> is genuinely silent, while a device with signal here and
    /// silence there is ours to fix.
    /// </remarks>
    [MicrophoneFact]
    public void EveryMicrophoneReportsWhetherItIsMutedAndWhatItHears()
    {
        using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();

        foreach (var device in enumerator.EnumerateAudioEndPoints(
                     NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.DeviceState.Active))
        {
            using (device)
            {
                var peak = 0f;

                // Sampled rather than read once: the meter reports the current instant, and one
                // unlucky read lands between words.
                for (var i = 0; i < 20; i++)
                {
                    try
                    {
                        peak = Math.Max(peak, device.AudioMeterInformation.MasterPeakValue);
                    }
                    catch (Exception)
                    {
                        break;
                    }

                    Thread.Sleep(50);
                }

                string state;

                try
                {
                    var volume = device.AudioEndpointVolume;
                    state = volume.Mute ? "MUTEADO" : $"vol {volume.MasterVolumeLevelScalar:P0}";
                }
                catch (Exception ex)
                {
                    state = $"sin control de volumen ({ex.GetType().Name})";
                }

                _output.WriteLine($"{device.FriendlyName,-62} {state,-28} pico {peak:P1}");
            }
        }
    }

    /// <summary>
    /// Captures for a few seconds and reports what came back.
    /// </summary>
    /// <remarks>
    /// Run it twice — once with Live closed, once with Live open and playing through the same
    /// interface. Identical results are the evidence that shared-mode WASAPI does not fight ASIO
    /// for the device. A failure only on the second run means this interface's driver hides its
    /// inputs from Windows while ASIO holds them, and the answer for that machine is a different
    /// microphone rather than a change here.
    /// </remarks>
    [MicrophoneFact]
    public void TheMicrophoneDeliversSixteenKilohertzMono()
    {
        var chosen = Environment.GetEnvironmentVariable(MicrophoneFactAttribute.DeviceSwitch);

        var faults = new List<VoiceInputError>();
        var samples = 0L;
        var peak = 0;
        var chunks = 0;
        var gate = new object();

        // Timed from the first chunk to the last rather than from Start, because opening a device
        // takes a few hundred milliseconds that vary wildly between them. Counting that wait as
        // capture time makes a perfectly good stream look slow.
        var clock = new System.Diagnostics.Stopwatch();
        var firstChunkAt = TimeSpan.Zero;
        var lastChunkAt = TimeSpan.Zero;

        _input.Faulted += error =>
        {
            lock (gate) faults.Add(error);
        };

        _input.SamplesCaptured += chunk =>
        {
            lock (gate)
            {
                if (chunks == 0) firstChunkAt = clock.Elapsed;
                lastChunkAt = clock.Elapsed;

                chunks++;
                samples += chunk.Length;

                foreach (var sample in chunk)
                    peak = Math.Max(peak, Math.Abs((int)sample));
            }
        };

        _output.WriteLine($"Escuchando {CaptureWindow.TotalSeconds:0} segundos. Decí algo.");

        clock.Start();
        _input.Start(chosen);

        Thread.Sleep(CaptureWindow);

        _input.Stop();

        lock (gate)
        {
            foreach (var fault in faults)
                _output.WriteLine($"FALLO {fault.Fault}: {fault.Detail}");

            Assert.Empty(faults);
            Assert.True(chunks > 0, "No llegó ni un bloque de audio: el micrófono abrió pero no entregó nada.");

            // One chunk short: the span between the first and the last covers the samples that
            // arrived after the first, not the first one's own.
            var streamed = lastChunkAt - firstChunkAt;
            var counted = samples - (samples / chunks);
            var rate = counted / streamed.TotalSeconds;
            var level = peak / (double)short.MaxValue;

            _output.WriteLine($"Abrió en: {firstChunkAt.TotalMilliseconds:0} ms");
            _output.WriteLine($"Bloques:  {chunks}");
            _output.WriteLine($"Muestras: {samples} en {streamed.TotalSeconds:0.00}s de flujo");
            _output.WriteLine($"Ritmo:    {rate:0} Hz (esperado {IVoiceInput.SampleRate})");
            _output.WriteLine($"Pico:     {level:P1}");

            // Ten percent is generous for a stream measured between its own chunks. Anything
            // outside it is a wiring mistake rather than jitter — half or double the rate means the
            // channel count or the resampler is wrong.
            Assert.InRange(rate, IVoiceInput.SampleRate * 0.9, IVoiceInput.SampleRate * 1.1);

            // Reported rather than asserted: running this without saying anything is as likely as
            // a genuinely dead microphone, and failing on silence would cry wolf every time.
            //
            // Worth knowing anyway, because Windows does not always refuse a blocked microphone —
            // when the privacy setting denies desktop apps, some drivers hand over digital silence
            // instead of an error, which looks exactly like this.
            if (peak == 0)
                _output.WriteLine(
                    "Silencio absoluto: ni una muestra distinta de cero. Puede ser el micrófono "
                    + "muteado, el dispositivo equivocado, o Windows bloqueando el acceso en "
                    + "Configuración › Privacidad y seguridad › Micrófono.");
        }
    }
}
