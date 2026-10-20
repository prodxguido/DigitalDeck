using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Voice;
using Xunit;

namespace DigitalDeck.Tests;

public sealed class VoiceAssistantTests : IDisposable
{
    /// <summary>Short enough that a test can sit through the deadline expiring.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(150);

    private static readonly VoiceTarget[] Deck =
    [
        new VoiceTarget("q", "Pro-Q 4"),
        new VoiceTarget("s", "Serum"),
        new VoiceTarget("c", "Compresor")
    ];

    private readonly FakeVoiceInput _input = new();
    private readonly FakeRecogniser _recogniser = new();
    private readonly VoiceAssistant _assistant;

    public VoiceAssistantTests() => _assistant = new VoiceAssistant(_input, _recogniser, Window);

    public void Dispose() => _assistant.Dispose();

    [Fact]
    public void PressingTheKeyOpensTheMicrophone()
    {
        _assistant.Listen(Deck);

        Assert.True(_input.IsCapturing);
        Assert.True(_assistant.IsListening);
    }

    /// <remarks>
    /// The point of restricting: the recogniser is told the answer set before a word is spoken.
    /// </remarks>
    [Fact]
    public void TheButtonsOnScreenBecomeTheVocabulary()
    {
        _assistant.Listen(Deck);

        // The names from the deck, plus the words someone might open a command with — the
        // recogniser needs both, and only the first half comes from what is on screen.
        Assert.Equal(
            ["pro q cuatro", "serum", "compresor"],
            _recogniser.Vocabulary.Except(VoicePhrase.CommandWords));
    }

    [Fact]
    public void SayingAButtonsNamePressesIt()
    {
        VoiceMatch? pressed = null;
        _assistant.Recognised += m => pressed = m;

        _assistant.Listen(Deck);
        _recogniser.WillHear("abrí pro q cuatro");
        _input.Speak();

        Assert.NotNull(pressed);
        Assert.Equal("q", pressed.Target.Id);
    }

    /// <remarks>
    /// The invariant this class is built around: every way out of listening closes the microphone.
    /// </remarks>
    [Theory]
    [InlineData("abrí serum")]      // understood
    [InlineData("qué hora es")]     // heard, matched nothing
    [InlineData("")]                // heard nothing
    public void TheMicrophoneAlwaysClosesAfterwards(string heard)
    {
        _assistant.Listen(Deck);
        _recogniser.WillHear(heard);
        _input.Speak();

        Assert.False(_input.IsCapturing);
        Assert.False(_assistant.IsListening);
    }

    [Fact]
    public void AudioThatIsStillMidSentenceKeepsTheWindowOpen()
    {
        _assistant.Listen(Deck);

        _recogniser.WillHear(null);
        _input.Speak();

        Assert.True(_assistant.IsListening);
        Assert.True(_input.IsCapturing);
    }

    [Fact]
    public void SomethingThatMatchesNoButtonIsReportedRatherThanGuessed()
    {
        VoiceMatch? pressed = null;
        _assistant.Recognised += m => pressed = m;

        _assistant.Listen(Deck);
        _recogniser.WillHear("qué hora es");
        _input.Speak();

        Assert.Null(pressed);
        Assert.Equal(VoiceAssistantState.Missed, _assistant.Status.State);

        // The transcript is kept so the user can see what it thought they said.
        Assert.Equal("qué hora es", _assistant.Status.Heard);
    }

    // ---------- The deadline ----------

    [Fact]
    public async Task TheWindowClosesOnItsOwnWhenNobodySaysAnything()
    {
        _assistant.Listen(Deck);

        await Task.Delay(Window * 4);

        Assert.False(_assistant.IsListening);
        Assert.False(_input.IsCapturing);
        Assert.Equal(VoiceAssistantState.Missed, _assistant.Status.State);
    }

    /// <remarks>
    /// A command said right up against the deadline has still been said. Discarding it because the
    /// speaker did not pause afterwards would be the most irritating possible way to fail.
    /// </remarks>
    [Fact]
    public async Task ACommandFinishedOnTheDeadlineStillCounts()
    {
        VoiceMatch? pressed = null;
        _assistant.Recognised += m => pressed = m;

        _recogniser.OnFlush = "compresor";
        _assistant.Listen(Deck);

        await Task.Delay(Window * 4);

        Assert.True(_recogniser.Flushed);
        Assert.NotNull(pressed);
        Assert.Equal("c", pressed.Target.Id);
    }

    // ---------- Pressing twice, and failing ----------

    /// <remarks>
    /// Two overlapping windows would leave one timer closing a microphone the other had reopened,
    /// so the second press stops rather than stacks.
    /// </remarks>
    [Fact]
    public void PressingTheKeyAgainWhileListeningCancels()
    {
        _assistant.Listen(Deck);
        _assistant.Listen(Deck);

        Assert.False(_assistant.IsListening);
        Assert.False(_input.IsCapturing);
        Assert.Equal(1, _input.Starts);
    }

    [Fact]
    public void AMicrophoneThatFailsMidwayEndsTheWindowWithSomethingToShow()
    {
        _assistant.Listen(Deck);
        _input.Fail(VoiceInputFault.AccessDenied, "Windows bloqueó el micrófono.");

        Assert.False(_assistant.IsListening);
        Assert.Equal(VoiceAssistantState.Missed, _assistant.Status.State);
        Assert.Equal("Windows bloqueó el micrófono.", _assistant.Status.Detail);
    }

    /// <remarks>
    /// Opening the microphone to listen for a vocabulary of nothing would be a light that goes on
    /// and a window that can only time out.
    /// </remarks>
    [Fact]
    public void AFolderWithNothingWorthSayingNeverOpensTheMicrophone()
    {
        _assistant.Listen([new VoiceTarget("blank", "   ")]);

        Assert.False(_input.IsCapturing);
        Assert.Equal(0, _input.Starts);
        Assert.Equal(VoiceAssistantState.Missed, _assistant.Status.State);
    }

    [Fact]
    public void CancellingClosesTheMicrophone()
    {
        _assistant.Listen(Deck);
        _assistant.Cancel();

        Assert.False(_input.IsCapturing);
        Assert.Equal(VoiceAssistantState.Idle, _assistant.Status.State);
    }

    [Fact]
    public void TheChosenMicrophoneIsTheOneOpened()
    {
        _assistant.Listen(Deck, "un-dispositivo");

        Assert.Equal("un-dispositivo", _input.LastDeviceId);
    }

    [Fact]
    public void EveryChangeIsAnnouncedSoTheOverlayCanFollowIt()
    {
        var seen = new List<VoiceAssistantState>();
        _assistant.StatusChanged += s => seen.Add(s.State);

        _assistant.Listen(Deck);
        _recogniser.WillHear("serum");
        _input.Speak();

        Assert.Equal([VoiceAssistantState.Listening, VoiceAssistantState.Understood], seen);
    }
}
