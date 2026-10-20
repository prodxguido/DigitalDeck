using DigitalDeck.Core.Abstractions;

namespace DigitalDeck.Core.Voice;

public enum VoiceAssistantState
{
    /// <summary>Not listening. The microphone is closed.</summary>
    Idle,

    /// <summary>The microphone is open and waiting for a command.</summary>
    Listening,

    /// <summary>Something was understood and the button is being pressed.</summary>
    Understood,

    /// <summary>Listening ended without a command anyone could act on.</summary>
    Missed
}

/// <param name="Detail">What to put on screen, written for the person who just spoke.</param>
/// <param name="Heard">The raw transcript, when there was one. Shown so a miss can be understood.</param>
public sealed record VoiceAssistantStatus(
    VoiceAssistantState State,
    string Detail,
    string? Heard = null,
    VoiceMatch? Match = null);

/// <summary>
/// One press of the hotkey, from opening the microphone to pressing a button.
/// </summary>
/// <remarks>
/// Push to talk rather than a wake word, and that is a decision about the room rather than about
/// the technology. This deck is used in front of monitor speakers: anything listening continuously
/// hears the mix, and a wake word spoken on a vocal track would fire it. Opening the microphone
/// only while a key is pressed removes that entire class of problem, costs one keystroke, and means
/// nothing is listening the rest of the time — which is also the honest answer to give someone
/// asking whether the app is recording them.
/// <para>
/// Every path out of <see cref="VoiceAssistantState.Listening"/> closes the microphone. That is the
/// invariant worth holding onto here; a bug that leaves it open is not a bug about voice control.
/// </para>
/// </remarks>
public sealed class VoiceAssistant : IDisposable
{
    /// <summary>How long a single press listens before giving up.</summary>
    /// <remarks>
    /// Long enough for "abrí el compresor de bus", short enough that a press nobody followed up on
    /// closes the microphone while the user still remembers pressing it.
    /// </remarks>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(5);

    private readonly IVoiceInput _input;
    private readonly IVoiceRecogniser _recogniser;
    private readonly TimeSpan _window;
    private readonly object _gate = new();

    private VoiceCommandMatcher? _matcher;
    private Timer? _deadline;
    private bool _disposed;

    public VoiceAssistant(IVoiceInput input, IVoiceRecogniser recogniser, TimeSpan? window = null)
    {
        _input = input;
        _recogniser = recogniser;
        _window = window ?? DefaultWindow;

        _input.SamplesCaptured += OnSamples;
        _input.Faulted += OnFaulted;
    }

    public VoiceAssistantStatus Status { get; private set; } =
        new(VoiceAssistantState.Idle, "En reposo.");

    /// <summary>Raised on every change of <see cref="Status"/>. May arrive on a background thread.</summary>
    public event Action<VoiceAssistantStatus>? StatusChanged;

    /// <summary>
    /// Raised when a command resolved to a button. The caller presses it.
    /// </summary>
    /// <remarks>
    /// An event rather than a callback the constructor takes, because what a button does is decided
    /// well above here — Core knows a tile was named, not that naming it loads a device into Live.
    /// </remarks>
    public event Action<VoiceMatch>? Recognised;

    public bool IsListening => Status.State == VoiceAssistantState.Listening;

    /// <summary>
    /// Opens the microphone and listens for one command naming any of <paramref name="targets"/>.
    /// </summary>
    /// <remarks>
    /// The targets are passed in per press rather than held, because the answer changes as the user
    /// navigates: the buttons on screen in this folder are the ones worth listening for, and
    /// offering the whole deck would put names in the vocabulary that the user cannot see.
    /// </remarks>
    public void Listen(IEnumerable<VoiceTarget> targets, string? deviceId = null)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // A second press while already listening cancels rather than stacks. Two overlapping
            // windows would leave one timer to close a microphone the other had reopened.
            if (Status.State == VoiceAssistantState.Listening)
            {
                StopLocked(new VoiceAssistantStatus(VoiceAssistantState.Idle, "Cancelado."));
                return;
            }

            _matcher = new VoiceCommandMatcher(targets);

            if (_matcher.IsEmpty)
            {
                Publish(new VoiceAssistantStatus(
                    VoiceAssistantState.Missed,
                    "No hay botones con nombre en esta carpeta."));

                return;
            }

            _recogniser.Reset();
            _recogniser.Restrict(_matcher.Vocabulary);

            Publish(new VoiceAssistantStatus(VoiceAssistantState.Listening, "Escuchando…"));

            _deadline = new Timer(_ => OnDeadline(), null, _window, Timeout.InfiniteTimeSpan);

            _input.Start(deviceId);
        }
    }

    /// <summary>Closes the microphone without acting on anything.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            if (Status.State != VoiceAssistantState.Listening) return;

            StopLocked(new VoiceAssistantStatus(VoiceAssistantState.Idle, "Cancelado."));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;

            _disposed = true;

            _input.SamplesCaptured -= OnSamples;
            _input.Faulted -= OnFaulted;

            _deadline?.Dispose();
            _deadline = null;

            _input.Stop();
        }
    }

    // ---------- The listening window ----------

    private void OnSamples(short[] samples)
    {
        VoiceMatch? recognised = null;

        lock (_gate)
        {
            if (Status.State != VoiceAssistantState.Listening) return;

            var heard = _recogniser.Feed(samples);

            // Still mid-sentence. The recogniser says so by returning nothing.
            if (heard is null) return;

            recognised = Settle(heard);
        }

        // Outside the lock: pressing the button can take as long as it likes — loading a device
        // into Live is a round trip — and holding the lock through it would block the capture
        // thread that is still delivering audio.
        if (recognised is not null) Recognised?.Invoke(recognised);
    }

    private void OnDeadline()
    {
        VoiceMatch? recognised = null;

        lock (_gate)
        {
            if (Status.State != VoiceAssistantState.Listening) return;

            // Flushed rather than discarded: a command finished right on the deadline has been said
            // in full, and throwing it away because the speaker did not pause afterwards is the
            // most annoying possible way to fail.
            recognised = Settle(_recogniser.Flush());
        }

        if (recognised is not null) Recognised?.Invoke(recognised);
    }

    private void OnFaulted(VoiceInputError error)
    {
        lock (_gate)
        {
            if (Status.State != VoiceAssistantState.Listening) return;

            StopLocked(new VoiceAssistantStatus(VoiceAssistantState.Missed, error.Detail));
        }
    }

    /// <summary>
    /// Decides what a transcript amounted to and closes the window either way. Must be called with
    /// <see cref="_gate"/> held.
    /// </summary>
    private VoiceMatch? Settle(string? heard)
    {
        var match = string.IsNullOrWhiteSpace(heard) ? null : _matcher?.Match(heard);

        if (match is null)
        {
            StopLocked(new VoiceAssistantStatus(
                VoiceAssistantState.Missed,
                string.IsNullOrWhiteSpace(heard)
                    ? "No se escuchó nada."
                    : "No encontré ningún botón que se llame así.",
                heard));

            return null;
        }

        StopLocked(new VoiceAssistantStatus(
            VoiceAssistantState.Understood,
            match.Target.Label,
            heard,
            match));

        return match;
    }

    /// <summary>Closes the microphone and publishes an outcome. Must be called with the lock held.</summary>
    private void StopLocked(VoiceAssistantStatus outcome)
    {
        _deadline?.Dispose();
        _deadline = null;

        _input.Stop();

        Publish(outcome);
    }

    private void Publish(VoiceAssistantStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }
}
