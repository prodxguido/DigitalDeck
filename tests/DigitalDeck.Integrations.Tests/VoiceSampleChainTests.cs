using DigitalDeck.Core.Abstractions;
using DigitalDeck.Integrations.Voice;
using NAudio.Wave;
using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// Proves the conversion the microphone smoke test cannot: that a known tone comes out the other
/// side at the right speed, with its pitch intact.
/// </summary>
/// <remarks>
/// Counting zero crossings is what makes this worth writing. A test that only checked how many
/// samples came out would pass on audio that had been resampled to the correct <i>length</i> while
/// playing back at the wrong <i>speed</i> — which is exactly the failure that reaches a user as "the
/// recogniser is useless" rather than as a crash.
/// </remarks>
public sealed class VoiceSampleChainTests
{
    private const double ToneHz = 440;

    [Fact]
    public void FortyEightKilohertzStereoComesOutAsSixteenKilohertzMono()
    {
        var output = Push(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2), seconds: 1);

        // A second in, a second out. Wrong channel handling shows up here as double or half.
        Assert.InRange(output.Count, 15_600, 16_400);

        // And still 440 Hz, which is what says the resampler changed the rate rather than just the
        // sample count.
        Assert.InRange(CrossingsPerSecond(output), ToneHz * 2 * 0.95, ToneHz * 2 * 1.05);
    }

    [Fact]
    public void FortyFourPointOneKilohertzIsResampledToo()
    {
        var output = Push(WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2), seconds: 1);

        Assert.InRange(output.Count, 15_600, 16_400);
        Assert.InRange(CrossingsPerSecond(output), ToneHz * 2 * 0.95, ToneHz * 2 * 1.05);
    }

    [Fact]
    public void AnInterfaceWithManyInputsIsNarrowedToTheFirstRatherThanMixed()
    {
        var output = Push(WaveFormat.CreateIeeeFloatWaveFormat(48_000, 8), seconds: 1);

        Assert.InRange(output.Count, 15_600, 16_400);
        Assert.InRange(CrossingsPerSecond(output), ToneHz * 2 * 0.95, ToneHz * 2 * 1.05);
    }

    [Fact]
    public void AlreadySixteenKilohertzMonoIsLeftAlone()
    {
        var output = Push(WaveFormat.CreateIeeeFloatWaveFormat(IVoiceInput.SampleRate, 1), seconds: 1);

        // Nothing to resample, so nothing should be lost: this is the one case that should come
        // back sample for sample.
        Assert.InRange(output.Count, 15_900, 16_100);
        Assert.InRange(CrossingsPerSecond(output), ToneHz * 2 * 0.95, ToneHz * 2 * 1.05);
    }

    [Fact]
    public void SilenceInIsSilenceOut()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
        var chain = new VoiceSampleChain(format);

        var quiet = new byte[48_000 / 10 * format.Channels * sizeof(float)];
        var output = new List<short>();

        for (var i = 0; i < 10; i++)
            output.AddRange(chain.Add(quiet, quiet.Length));

        Assert.NotEmpty(output);
        Assert.All(output, sample => Assert.Equal(0, sample));
    }

    /// <summary>Feeds a tone through the chain in the small buffers a sound card really delivers.</summary>
    private static List<short> Push(WaveFormat format, int seconds)
    {
        var chain = new VoiceSampleChain(format);
        var output = new List<short>();

        var framesPerChunk = format.SampleRate / 100;   // 10 ms, close to what WASAPI hands over
        var chunks = seconds * 100;
        var frame = 0;

        for (var c = 0; c < chunks; c++)
        {
            var buffer = new float[framesPerChunk * format.Channels];

            for (var f = 0; f < framesPerChunk; f++, frame++)
            {
                var value = (float)Math.Sin(2 * Math.PI * ToneHz * frame / format.SampleRate);

                // The same tone on every channel: a downmix that averaged them would then be
                // indistinguishable from one that picked one, and either is correct here.
                for (var ch = 0; ch < format.Channels; ch++)
                    buffer[(f * format.Channels) + ch] = value * 0.5f;
            }

            var bytes = new byte[buffer.Length * sizeof(float)];
            Buffer.BlockCopy(buffer, 0, bytes, 0, bytes.Length);

            output.AddRange(chain.Add(bytes, bytes.Length));
        }

        return output;
    }

    /// <summary>
    /// How often the signal changes sign per second of output, which for a sine is twice its
    /// frequency — a pitch measurement that needs no FFT.
    /// </summary>
    private static double CrossingsPerSecond(List<short> samples)
    {
        // The edges of a resampled buffer ring a little; trimming them keeps that out of the count.
        const int Margin = 200;

        var from = Math.Min(Margin, samples.Count / 4);
        var to = samples.Count - from;

        var crossings = 0;
        var previous = 0;

        for (var i = from; i < to; i++)
        {
            var sign = Math.Sign(samples[i]);
            if (sign == 0) continue;

            if (previous != 0 && sign != previous) crossings++;

            previous = sign;
        }

        var counted = to - from;

        return crossings / (counted / (double)IVoiceInput.SampleRate);
    }
}
