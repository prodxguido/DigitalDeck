using System.Globalization;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Integrations.Ableton;
using DigitalDeck.Integrations.Ableton.Osc;
using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// The mixer half of the bridge: the roster it reads, and the throttle in front of the faders.
/// </summary>
/// <remarks>
/// The throttle is driven through <see cref="AbletonMixerState.Pump"/> with a clock the test moves,
/// for the same reason the heartbeat is: a real timer would make "how many datagrams did a drag
/// produce" a question about the machine the tests ran on.
/// </remarks>
public class AbletonMixerTests : IDisposable
{
    private static readonly TimeSpan Throttle = TimeSpan.FromMilliseconds(40);

    private readonly List<OscMessage> _sent = new();
    private readonly AbletonMixerState _mixer;

    private DateTimeOffset _now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    public AbletonMixerTests()
    {
        // ManualThrottle leaves the background timer off, so nothing lands in _sent except what the
        // test asked for. The coalescing window itself still applies.
        _mixer = new AbletonMixerState(_sent.Add, AbletonMixerState.ManualThrottle, () => _now);
    }

    public void Dispose() => _mixer.Dispose();

    private void Advance(TimeSpan amount) => _now += amount;

    private IReadOnlyList<OscMessage> Volumes =>
        _sent.Where(message => message.Address == "/live/track/set/volume").ToList();

    private static OscMessage Roster(params string[] lines) =>
        new(AbletonCommandMap.MixerTracksAddress, lines.Length, string.Join('\n', lines));

    /// <summary>
    /// One roster line as the script writes it.
    /// </summary>
    /// <remarks>
    /// Invariant on purpose: Python's <c>%g</c> always writes a dot, and a test that formatted with
    /// the machine's own culture would pass in English and fail in Spanish for a reason that has
    /// nothing to do with the code under it.
    /// </remarks>
    private static string Line(
        int index, string name, string kind, int colour, double volume, int depth, string display) =>
        string.Join('\t', index, name, kind, colour,
            volume.ToString("0.####", CultureInfo.InvariantCulture), depth, display);

    // ---------- The roster ----------

    [Fact]
    public void TheRosterBecomesChannels()
    {
        IReadOnlyList<LiveTrack>? announced = null;
        _mixer.TracksChanged += tracks => announced = tracks;

        _mixer.TryHandle(Roster(
            Line(0, "Drums", "group", 0x3C7DB5, 0.85, 0, "0.0 dB"),
            Line(1, "Bass", "normal", 0x0, 0.5, 1, "-6.0 dB"),
            Line(-3, "A Reverb", "return", 0x0, 0.4, 0, "-9.0 dB"),
            Line(-2, "Master", "master", 0, 0.85, 0, "0.0 dB")));

        Assert.NotNull(announced);
        Assert.Equal(4, announced!.Count);

        Assert.Equal(LiveTrackKind.Group, announced[0].Kind);
        Assert.Equal(LiveTrackKind.Return, announced[2].Kind);
        Assert.Equal(LiveTrackKind.Master, announced[3].Kind);

        Assert.Equal(1, announced[1].Depth);
        Assert.Equal(0x3C7DB5, announced[0].Color);
        Assert.Equal("-6.0 dB", announced[1].Display);
        Assert.Equal(0.5, announced[1].Volume, 3);
    }

    [Fact]
    public void ARosterFromAnOlderScriptStillDrawsAFlatMixer()
    {
        // Before 2.5.0 the line stopped at the volume. Losing the indentation and the reading is
        // the right amount of harm; refusing to show the set at all is not.
        IReadOnlyList<LiveTrack>? announced = null;
        _mixer.TracksChanged += tracks => announced = tracks;

        _mixer.TryHandle(Roster(string.Join('\t', 0, "Drums", "normal", 0, "0.85")));

        Assert.Equal(0, announced![0].Depth);
        Assert.Equal(string.Empty, announced[0].Display);
        Assert.Equal(0.85, announced[0].Volume, 3);
    }

    [Fact]
    public void ALineThatMakesNoSenseCostsOneFaderAndNotTheWindow()
    {
        IReadOnlyList<LiveTrack>? announced = null;
        _mixer.TracksChanged += tracks => announced = tracks;

        _mixer.TryHandle(Roster(
            "esto no es un canal",
            Line(1, "Bass", "normal", 0, 0.5, 0, "-6.0 dB")));

        Assert.Single(announced!);
        Assert.Equal("Bass", announced![0].Name);
    }

    [Fact]
    public void AnUnknownKindIsDrawnAsAnOrdinaryChannel()
    {
        IReadOnlyList<LiveTrack>? announced = null;
        _mixer.TracksChanged += tracks => announced = tracks;

        _mixer.TryHandle(Roster(Line(0, "Algo", "chain", 0, 0.5, 0, "")));

        Assert.Equal(LiveTrackKind.Normal, announced![0].Kind);
    }

    [Fact]
    public void AMessageThatIsNotTheMixersIsLeftAlone() =>
        Assert.False(_mixer.TryHandle(new OscMessage("/live/song/get/tempo", 120f)));

    // ---------- What Live pushes ----------

    [Fact]
    public void AFaderMovedInsideLiveComesThroughWithItsReading()
    {
        LiveVolume? heard = null;
        _mixer.VolumeChanged += volume => heard = volume;

        Assert.True(_mixer.TryHandle(
            new OscMessage(AbletonCommandMap.TrackVolumeAddress, 2, 0.3f, "-16.0 dB")));

        Assert.Equal(2, heard!.Value.TrackIndex);
        Assert.Equal(0.3, heard.Value.Value, 3);
        Assert.Equal("-16.0 dB", heard.Value.Display);
    }

    [Fact]
    public void APushFromAnOlderScriptSimplyHasNoReading()
    {
        LiveVolume? heard = null;
        _mixer.VolumeChanged += volume => heard = volume;

        _mixer.TryHandle(new OscMessage(AbletonCommandMap.TrackVolumeAddress, 2, 0.3f));

        Assert.Equal(string.Empty, heard!.Value.Display);
    }

    // ---------- Opening and closing ----------

    [Fact]
    public void OpeningAsksLiveToPush()
    {
        _mixer.Open();

        Assert.Equal(new[] { AbletonCommandMap.MixerSubscribeAddress }, _sent.Select(m => m.Address));
        Assert.Equal(1, _sent[0].GetInt(0));
        Assert.True(_mixer.IsOpen);
    }

    [Fact]
    public void ClosingAsksLiveToStop()
    {
        _mixer.Open();
        _sent.Clear();

        _mixer.Close();

        Assert.Equal(AbletonCommandMap.MixerSubscribeAddress, _sent[0].Address);
        Assert.Equal(0, _sent[0].GetInt(0));
        Assert.False(_mixer.IsOpen);
    }

    [Fact]
    public void ReconnectingAsksAgainOnlyForAWindowThatIsStillOpen()
    {
        // The listeners live inside the script and die with it, so a window open across a restart
        // of Live has to re-ask. A window that was closed must not quietly reopen the tap.
        _mixer.Resubscribe();
        Assert.Empty(_sent);

        _mixer.Open();
        _sent.Clear();

        _mixer.Resubscribe();
        Assert.Single(_sent);
    }

    [Fact]
    public void AFaderCannotBeMovedBeforeTheWindowOpens()
    {
        _mixer.SetVolume(1, 0.5);

        Assert.Empty(Volumes);
    }

    // ---------- The throttle ----------

    [Fact]
    public void TheFirstMoveOfADragGoesOutStraightAway()
    {
        _mixer.Open();
        _mixer.SetVolume(1, 0.5);

        var move = Assert.Single(Volumes);
        Assert.Equal(1, move.GetInt(0));
        Assert.Equal(0.5, move.GetDouble(1), 3);
    }

    [Fact]
    public void MovesInsideOneWindowAreCoalescedIntoTheLatest()
    {
        _mixer.Open();
        _mixer.SetVolume(1, 0.50);

        // A finger produces one of these a frame. Every one of them is a datagram Live has to pick
        // up on its own thread, so all but the newest are worth nothing.
        _mixer.SetVolume(1, 0.55);
        _mixer.SetVolume(1, 0.60);
        _mixer.SetVolume(1, 0.65);

        Assert.Single(Volumes);

        Advance(Throttle);
        _mixer.Pump();

        Assert.Equal(2, Volumes.Count);
        Assert.Equal(0.65, Volumes[1].GetDouble(1), 3);
    }

    [Fact]
    public void ThePositionTheUserLetGoOnAlwaysLeaves()
    {
        // The one failure a mixer is not forgiven: the fader on screen and the fader in Live
        // disagreeing because the last move of a drag was the one the throttle ate.
        _mixer.Open();
        _mixer.SetVolume(1, 0.50);
        _mixer.SetVolume(1, 0.92);

        Advance(Throttle);
        _mixer.Pump();

        Assert.Equal(0.92, Volumes.Last().GetDouble(1), 3);
    }

    [Fact]
    public void TwoFadersMovedTogetherBothGetThere()
    {
        _mixer.Open();
        _mixer.SetVolume(1, 0.5);
        _mixer.SetVolume(2, 0.4);

        Advance(Throttle);
        _mixer.Pump();

        Assert.Equal(new[] { 1, 2 }, Volumes.Select(move => move.GetInt(0)));
    }

    [Fact]
    public void PumpingWithNothingWaitingSendsNothing()
    {
        _mixer.Open();
        _sent.Clear();

        Advance(Throttle);
        _mixer.Pump();

        Assert.Empty(_sent);
    }

    [Fact]
    public void AFaderIsHeldInsideTheRangeLiveAccepts()
    {
        _mixer.Open();
        _mixer.SetVolume(1, 4.2);

        Assert.Equal(1.0, Volumes[0].GetDouble(1), 3);
    }

    [Fact]
    public void ClosingDropsWhateverWasStillWaiting()
    {
        _mixer.Open();
        _mixer.SetVolume(1, 0.5);
        _mixer.SetVolume(1, 0.9);

        _mixer.Close();
        Advance(Throttle);
        _mixer.Pump();

        // Only the first move, which had already gone. The one still queued belongs to a window
        // that is no longer there.
        Assert.Single(Volumes);
    }
}
