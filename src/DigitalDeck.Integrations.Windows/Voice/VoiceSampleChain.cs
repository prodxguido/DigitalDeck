using DigitalDeck.Core.Abstractions;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DigitalDeck.Integrations.Voice;

/// <summary>
/// Turns whatever a sound card produces into the 16 kHz mono PCM the recogniser reads.
/// </summary>
/// <remarks>
/// Split out from <see cref="WasapiVoiceInput"/> so it can be proved without a microphone, which
/// matters more here than anywhere else in the feature: both steps fail <i>quietly</i>. A stereo
/// stream read as mono halves the apparent pitch, and 48 kHz audio handed over as 16 kHz is
/// recognised as nothing at all. Neither throws. Both look like a bad model, and the hours that
/// follow get spent replacing one.
/// </remarks>
public sealed class VoiceSampleChain
{
    private readonly BufferedWaveProvider _incoming;
    private readonly ISampleProvider _output;
    private readonly float[] _scratch = new float[4096];

    public VoiceSampleChain(WaveFormat sourceFormat)
    {
        _incoming = new BufferedWaveProvider(sourceFormat)
        {
            // Padding a short buffer with zeros would feed the recogniser silence nobody spoke;
            // better to hand over less and let it wait for the rest.
            ReadFully = false,
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(2)
        };

        _output = Build(_incoming);
    }

    /// <summary>
    /// Pushes one buffer in and takes out whatever is ready, which may be nothing.
    /// </summary>
    /// <returns>16 kHz mono samples, or an empty array when the chain is still filling.</returns>
    public short[] Add(byte[] buffer, int count)
    {
        _incoming.AddSamples(buffer, 0, count);

        List<short>? drained = null;
        short[]? single = null;

        // Drained in a loop rather than read once: a caller that hands over a large buffer would
        // otherwise leave most of it sitting inside the resampler until the next call, and the
        // recogniser would run permanently behind the microphone.
        while (true)
        {
            var read = _output.Read(_scratch, 0, _scratch.Length);
            if (read == 0) break;

            var chunk = new short[read];

            for (var i = 0; i < read; i++)
                chunk[i] = ToPcm16(_scratch[i]);

            // The overwhelmingly common case is one pass, so it does not pay a list for that.
            if (single is null && drained is null)
            {
                single = chunk;
                continue;
            }

            drained ??= [.. single!];
            single = null;
            drained.AddRange(chunk);
        }

        return single ?? (drained is null ? [] : [.. drained]);
    }

    private static ISampleProvider Build(BufferedWaveProvider incoming)
    {
        ISampleProvider samples = incoming.ToSampleProvider();

        samples = samples.WaveFormat.Channels switch
        {
            1 => samples,
            2 => new StereoToMonoSampleProvider(samples) { LeftVolume = 0.5f, RightVolume = 0.5f },

            // An interface can present eight inputs. Mixing them together would fold in everything
            // else plugged into it, so take the first and leave the rest alone.
            _ => FirstChannelOf(samples)
        };

        return samples.WaveFormat.SampleRate == IVoiceInput.SampleRate
            ? samples
            : new WdlResamplingSampleProvider(samples, IVoiceInput.SampleRate);
    }

    private static ISampleProvider FirstChannelOf(ISampleProvider source)
    {
        var single = new MultiplexingSampleProvider([source], 1);
        single.ConnectInputToOutput(0, 0);

        return single;
    }

    private static short ToPcm16(float sample) => (short)(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
}
