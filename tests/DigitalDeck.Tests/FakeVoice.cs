using DigitalDeck.Core.Abstractions;

namespace DigitalDeck.Tests;

/// <summary>A microphone that is whatever the test says it is.</summary>
public sealed class FakeVoiceInput : IVoiceInput
{
    public bool IsCapturing { get; private set; }

    /// <summary>How many times the microphone has been opened, to catch it being opened twice.</summary>
    public int Starts { get; private set; }

    public string? LastDeviceId { get; private set; }

    public event Action<short[]>? SamplesCaptured;
    public event Action<VoiceInputError>? Faulted;

    public IReadOnlyList<VoiceInputDevice> GetDevices() =>
        [new VoiceInputDevice("fake", "Micrófono de prueba", IsDefault: true)];

    public void Start(string? deviceId = null)
    {
        IsCapturing = true;
        Starts++;
        LastDeviceId = deviceId;
    }

    public void Stop() => IsCapturing = false;

    public void Dispose() => Stop();

    /// <summary>Delivers a buffer as the real one would, on whatever thread the test is on.</summary>
    public void Speak() => SamplesCaptured?.Invoke(new short[160]);

    public void Fail(VoiceInputFault fault, string detail) =>
        Faulted?.Invoke(new VoiceInputError(fault, detail));
}

/// <summary>A recogniser that returns what the test told it to, when the test says so.</summary>
public sealed class FakeRecogniser : IVoiceRecogniser
{
    private readonly Queue<string?> _answers = new();

    public IReadOnlyList<string> Vocabulary { get; private set; } = [];

    public int Resets { get; private set; }

    public bool Flushed { get; private set; }

    /// <summary>What the next <see cref="Feed"/> returns. Null stands for "still talking".</summary>
    public void WillHear(string? transcript) => _answers.Enqueue(transcript);

    /// <summary>What <see cref="Flush"/> returns when the window closes.</summary>
    public string? OnFlush { get; set; }

    public void Restrict(IReadOnlyList<string> vocabulary) => Vocabulary = vocabulary;

    public string? Feed(short[] samples) => _answers.Count > 0 ? _answers.Dequeue() : null;

    public string? Flush()
    {
        Flushed = true;
        return OnFlush;
    }

    public void Reset() => Resets++;

    public void Dispose() { }
}
