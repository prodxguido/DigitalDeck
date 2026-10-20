using DigitalDeck.ViewModels;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using Xunit;

namespace DigitalDeck.App.Tests;

/// <summary>
/// The mixer window's behaviour, with no window in sight.
/// </summary>
/// <remarks>
/// Everything awkward about a touch mixer is in here rather than in the XAML on purpose: that a
/// fader ignores Live while a finger is on it, that a deleted track cannot leave a strip writing
/// to its neighbour, and that a set of groups can be folded down to fit are all things a
/// screenshot cannot check and a test can.
/// </remarks>
public class MixerViewModelTests : IDisposable
{
    private readonly FakeAbletonMixer _mixer = new();
    private readonly FakeAbletonClient _ableton = new();
    private readonly MixerViewModel _model;

    private DateTimeOffset _now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    public MixerViewModelTests()
    {
        // No marshalling: the test is the UI thread as far as this is concerned.
        _model = new MixerViewModel(_mixer, _ableton, action => action(), () => _now);
        _model.Open();
    }

    public void Dispose() => _model.Dispose();

    private void Advance(TimeSpan amount) => _now += amount;

    private static LiveTrack Track(
        int index,
        string name,
        LiveTrackKind kind = LiveTrackKind.Normal,
        double volume = 0.85,
        int depth = 0) =>
        new(index, name, kind, 0, volume, depth, "0.0 dB");

    /// <summary>A set shaped like the one that makes this hard: a group, its contents, and returns.</summary>
    private void ReportATypicalSet() => _mixer.Report(
        Track(0, "Batería", LiveTrackKind.Group),
        Track(1, "Bombo", depth: 1),
        Track(2, "Redoblante", depth: 1),
        Track(3, "Bajo"),
        Track(-3, "A Reverb", LiveTrackKind.Return),
        Track(-2, "Master", LiveTrackKind.Master));

    // ---------- Opening and closing ----------

    [Fact]
    public void OpeningSubscribesAndClosingUnsubscribes()
    {
        Assert.Equal(1, _mixer.Opened);

        _model.Close();

        Assert.Equal(1, _mixer.Closed);
    }

    [Fact]
    public void ClosingTwiceOnlyUnsubscribesOnce()
    {
        _model.Close();
        _model.Dispose();

        Assert.Equal(1, _mixer.Closed);
    }

    // ---------- The roster ----------

    [Fact]
    public void ChannelsReturnsAndTheMasterEndUpInTheirOwnPlaces()
    {
        ReportATypicalSet();

        Assert.Equal(
            new[] { "Batería", "Bombo", "Redoblante", "Bajo" },
            _model.Channels.Select(strip => strip.Name));

        Assert.Equal(new[] { "A Reverb" }, _model.Returns.Select(strip => strip.Name));
        Assert.Equal("Master", _model.Master!.Name);
    }

    [Fact]
    public void ASetWithNoReturnsDoesNotDrawTheReturnsPane()
    {
        _mixer.Report(Track(0, "Bajo"), Track(-2, "Master", LiveTrackKind.Master));

        Assert.False(_model.HasReturns);
        Assert.False(_model.ShowReturnsPane);
    }

    [Fact]
    public void ARenameInLiveKeepsTheSameStrip()
    {
        _mixer.Report(Track(0, "Bajo"));
        var before = _model.Channels[0];

        _mixer.Report(Track(0, "Bajo eléctrico"));

        Assert.Same(before, _model.Channels[0]);
        Assert.Equal("Bajo eléctrico", _model.Channels[0].Name);
    }

    [Fact]
    public void AChannelWithNoNameStillSaysSomething()
    {
        _mixer.Report(Track(0, string.Empty));

        Assert.Equal("sin nombre", _model.Channels[0].Name);
    }

    // ---------- Groups ----------

    [Fact]
    public void CompactingLeavesTheGroupFaderAndHidesWhatIsInsideIt()
    {
        ReportATypicalSet();

        _model.CompactGroups = true;

        // The group's own fader is what gets ridden, so it is the one that stays.
        Assert.Equal(new[] { "Batería", "Bajo" }, _model.Channels.Select(strip => strip.Name));
    }

    [Fact]
    public void OpeningTheGroupsAgainBringsThemBack()
    {
        ReportATypicalSet();

        _model.CompactGroups = true;
        _model.CompactGroups = false;

        Assert.Equal(4, _model.Channels.Count);
    }

    [Fact]
    public void ASetWithNoGroupsHasNothingToCompact()
    {
        _mixer.Report(Track(0, "Bajo"), Track(1, "Teclas"));

        Assert.False(_model.HasGroups);
    }

    [Fact]
    public void CompactingSurvivesARosterThatArrivesAfterwards()
    {
        ReportATypicalSet();
        _model.CompactGroups = true;

        ReportATypicalSet();

        Assert.Equal(new[] { "Batería", "Bajo" }, _model.Channels.Select(strip => strip.Name));
    }

    // ---------- Moving a fader ----------

    [Fact]
    public void MovingAStripReachesLiveUnderItsOwnIndex()
    {
        ReportATypicalSet();

        _model.Channels[3].Volume = 0.4;

        Assert.Equal((3, 0.4), _mixer.Moves.Single());
    }

    [Fact]
    public void MovingAReturnReachesLiveUnderTheNegativeIndex()
    {
        ReportATypicalSet();

        _model.Returns[0].Volume = 0.2;

        Assert.Equal((-3, 0.2), _mixer.Moves.Single());
    }

    [Fact]
    public void MovingTheMasterReachesTheMaster()
    {
        ReportATypicalSet();

        _model.Master!.Volume = 0.7;

        Assert.Equal((-2, 0.7), _mixer.Moves.Single());
    }

    // ---------- Live pushing back ----------

    [Fact]
    public void AMoveMadeInLiveArrivesOnTheRightStrip()
    {
        ReportATypicalSet();

        _mixer.Push(3, 0.25, "-14.0 dB");

        Assert.Equal(0.25, _model.Channels[3].Volume, 3);
        Assert.Equal("-14.0 dB", _model.Channels[3].Display);
    }

    [Fact]
    public void TakingAMoveFromLiveDoesNotSendItStraightBack()
    {
        ReportATypicalSet();

        _mixer.Push(3, 0.25);

        Assert.Empty(_mixer.Moves);
    }

    [Fact]
    public void AFaderUnderAFingerIgnoresLive()
    {
        // Live echoes back every move the app makes, having no idea which side it came from. Taking
        // those echoes mid-gesture is the fader fighting the finger.
        ReportATypicalSet();
        var strip = _model.Channels[3];

        strip.IsHeld = true;
        strip.Volume = 0.4;

        _mixer.Push(3, 0.85);

        Assert.Equal(0.4, strip.Volume, 3);
    }

    [Fact]
    public void AFaderKeepsIgnoringLiveForAMomentAfterTheFingerComesOff()
    {
        // The last echo of a drag is still in flight when the user lets go. Without this the fader
        // visibly springs back to a position from a few frames ago.
        ReportATypicalSet();
        var strip = _model.Channels[3];

        strip.IsHeld = true;
        strip.Volume = 0.4;
        strip.IsHeld = false;

        _mixer.Push(3, 0.85);

        Assert.Equal(0.4, strip.Volume, 3);
    }

    [Fact]
    public void OnceTheMomentHasPassedLiveIsListenedToAgain()
    {
        ReportATypicalSet();
        var strip = _model.Channels[3];

        strip.IsHeld = true;
        strip.IsHeld = false;

        Advance(TimeSpan.FromMilliseconds(400));
        _mixer.Push(3, 0.2);

        Assert.Equal(0.2, strip.Volume, 3);
    }

    [Fact]
    public void ARosterArrivingMidGestureIsNotAppliedOverTheFinger()
    {
        ReportATypicalSet();
        var strip = _model.Channels[3];

        strip.IsHeld = true;
        strip.Volume = 0.4;

        // A rename somewhere else in the set republishes the whole roster, volumes included.
        _mixer.Report(
            Track(0, "Batería", LiveTrackKind.Group),
            Track(1, "Bombo", depth: 1),
            Track(2, "Redoblante", depth: 1),
            Track(3, "Bajo", volume: 0.85),
            Track(-3, "A Reverb", LiveTrackKind.Return),
            Track(-2, "Master", LiveTrackKind.Master));

        Assert.Equal(0.4, strip.Volume, 3);
    }

    // ---------- Tracks coming and going ----------

    [Fact]
    public void ADeletedTrackRenumbersTheStripsAfterIt()
    {
        ReportATypicalSet();

        // "Bombo" is gone; what was index 3 is now index 2.
        _mixer.Report(
            Track(0, "Batería", LiveTrackKind.Group),
            Track(1, "Redoblante", depth: 1),
            Track(2, "Bajo"),
            Track(-2, "Master", LiveTrackKind.Master));

        var bass = _model.Channels.Single(strip => strip.Name == "Bajo");
        bass.Volume = 0.3;

        Assert.Equal((2, 0.3), _mixer.Moves.Single());
    }

    [Fact]
    public void ARosterReleasesAnyFaderAFingerWasOn()
    {
        // An index is a position, not an identity. A gesture still counted as live across a
        // renumbering would land on whoever moved into that slot.
        ReportATypicalSet();
        var strip = _model.Channels[3];
        strip.IsHeld = true;

        ReportATypicalSet();

        Assert.False(strip.IsHeld);
    }

    [Fact]
    public void ATrackThatWasAddedGetsAStripOfItsOwn()
    {
        _mixer.Report(Track(0, "Bajo"));
        _mixer.Report(Track(0, "Bajo"), Track(1, "Teclas"));

        Assert.Equal(new[] { "Bajo", "Teclas" }, _model.Channels.Select(strip => strip.Name));
    }

    // ---------- Nothing to show ----------

    [Fact]
    public void AnEmptyMixerRepeatsWhateverTheBridgeSaidIsWrong()
    {
        // "No encuentro a Live" and "el script está desactualizado" send someone to very different
        // places, and flattening both into "no hay canales" sends them nowhere.
        _ableton.Report(
            AbletonConnectionState.VersionMismatch, "El Remote Script está desactualizado.");

        Assert.True(_model.IsEmpty);
        Assert.Equal("El Remote Script está desactualizado.", _model.EmptyMessage);
    }

    [Fact]
    public void AConnectedLiveThatHasNotAnsweredYetSaysSoInsteadOfBlamingTheBridge()
    {
        _ableton.Report(AbletonConnectionState.Connected, "Conectado.");

        Assert.Contains("todavía no mandó los canales", _model.EmptyMessage);
    }

    [Fact]
    public void OnceThereAreChannelsItIsNotEmpty()
    {
        ReportATypicalSet();

        Assert.False(_model.IsEmpty);
    }
}
