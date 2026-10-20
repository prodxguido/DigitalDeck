using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Integrations.Ableton;
using DigitalDeck.Integrations.Ableton.Osc;
using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// How the bridge decides whether Live is there, and what it does when it is not.
/// </summary>
public class AbletonAdapterTests : IDisposable
{
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(2);

    private readonly FakeOscTransport _transport = new();
    private readonly AbletonAdapter _adapter;
    private readonly List<AbletonStatus> _statuses = new();

    private DateTimeOffset _now = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    public AbletonAdapterTests()
    {
        // ManualHeartbeat leaves the timer off so the test drives Tick itself; a background timer
        // would put unpredictable pings into the transport's list.
        _adapter = new AbletonAdapter(_transport, AbletonAdapter.ManualHeartbeat, () => _now);
        _adapter.StatusChanged += _statuses.Add;
    }

    public void Dispose() => _adapter.Dispose();

    private void Advance(TimeSpan amount) => _now += amount;

    // ---------- Coming up ----------

    [Fact]
    public void StartingLooksForLiveAndSaysSo()
    {
        _adapter.Start();

        Assert.Equal(AbletonConnectionState.Connecting, _adapter.Status.State);
        Assert.Equal(new[] { AbletonCommandMap.PingAddress }, _transport.SentAddresses);
    }

    [Fact]
    public void AnswerFromTheScriptConnects()
    {
        _adapter.Start();
        _transport.ReceivePong();

        Assert.Equal(AbletonConnectionState.Connected, _adapter.Status.State);
        Assert.Equal("12.3.0", _adapter.Status.LiveVersion);
        Assert.Equal("2.0.0", _adapter.Status.ScriptVersion);
        Assert.Contains("12.3.0", _adapter.Status.Detail);
    }

    [Fact]
    public void APortThatCannotBeOpenedIsReportedAsHopelessRatherThanAsSearching()
    {
        // Nothing will change by waiting, so the indicator must not sit there pretending to look.
        _transport.FailToOpen = true;
        _transport.LastError = "el puerto 11001 está ocupado";

        _adapter.Start();

        Assert.Equal(AbletonConnectionState.Unavailable, _adapter.Status.State);
        Assert.Contains("11001", _adapter.Status.Detail);
        Assert.Empty(_transport.Sent);
    }

    [Fact]
    public void StartingTwiceDoesNotDoubleUpTheHandshake()
    {
        _adapter.Start();
        _adapter.Start();

        Assert.Single(_transport.Sent);
    }

    // ---------- Version ----------

    [Fact]
    public void AScriptFromAnotherMajorVersionIsCalledOutInsteadOfBeingUsed()
    {
        _adapter.Start();
        _transport.ReceivePong(scriptVersion: "1.4.0");

        Assert.Equal(AbletonConnectionState.VersionMismatch, _adapter.Status.State);
        Assert.Contains("1.4.0", _adapter.Status.Detail);
    }

    [Fact]
    public void ANewerPatchOfTheSameMajorVersionIsFine()
    {
        // The major is where a breaking change to the address space gets announced; anything else
        // is additive, and refusing it would mean a reinstall for no reason.
        _adapter.Start();
        _transport.ReceivePong(scriptVersion: "2.7.3");

        Assert.Equal(AbletonConnectionState.Connected, _adapter.Status.State);
    }

    [Fact]
    public void AnAnswerWithNoVersionAtAllIsNotTrusted()
    {
        _adapter.Start();
        _transport.Receive(new OscMessage(AbletonCommandMap.PongAddress));

        Assert.Equal(AbletonConnectionState.VersionMismatch, _adapter.Status.State);
    }

    // ---------- Staying up ----------

    [Fact]
    public void ASingleLostDatagramDoesNotDropTheConnection()
    {
        _adapter.Start();
        _transport.ReceivePong();

        Advance(Heartbeat);
        _adapter.Tick();

        Assert.Equal(AbletonConnectionState.Connected, _adapter.Status.State);
    }

    [Fact]
    public void SeveralUnansweredPingsInARowDropTheConnection()
    {
        _adapter.Start();
        _transport.ReceivePong();

        Advance(Heartbeat * 4);
        _adapter.Tick();

        Assert.Equal(AbletonConnectionState.Disconnected, _adapter.Status.State);
        Assert.Contains("perdió", _adapter.Status.Detail);
    }

    [Fact]
    public void AnyMessageAtAllCountsAsProofLiveIsStillThere()
    {
        // The script publishes state as it changes. Insisting on a pong would make the indicator
        // drop out during a session that is plainly working.
        _adapter.Start();
        _transport.ReceivePong();

        Advance(Heartbeat * 2);
        _transport.Receive(new OscMessage("/live/song/get/tempo", 128.0f));

        Advance(Heartbeat * 2);
        _adapter.Tick();

        Assert.Equal(AbletonConnectionState.Connected, _adapter.Status.State);
    }

    [Fact]
    public void ComingBackAfterLiveRestartsReconnects()
    {
        _adapter.Start();
        _transport.ReceivePong();

        Advance(Heartbeat * 4);
        _adapter.Tick();
        Assert.Equal(AbletonConnectionState.Disconnected, _adapter.Status.State);

        _transport.ReceivePong();
        Assert.Equal(AbletonConnectionState.Connected, _adapter.Status.State);
    }

    [Fact]
    public void EveryTickSendsAPing()
    {
        _adapter.Start();
        _adapter.Tick();
        _adapter.Tick();

        Assert.Equal(3, _transport.SentAddresses.Count(address => address == AbletonCommandMap.PingAddress));
    }

    [Fact]
    public void TheSameStatusIsNotAnnouncedTwice()
    {
        // The indicator binds to this; a stream of identical notifications would repaint for nothing.
        _adapter.Start();
        _transport.ReceivePong();
        _transport.ReceivePong();

        Assert.Equal(2, _statuses.Count);
        Assert.Equal(AbletonConnectionState.Connecting, _statuses[0].State);
        Assert.Equal(AbletonConnectionState.Connected, _statuses[1].State);
    }

    // ---------- Sending ----------

    [Fact]
    public async Task AConnectedBridgeSendsTheCommand()
    {
        _adapter.Start();
        _transport.ReceivePong();
        _transport.Sent.Clear();

        var result = await _adapter.ExecuteAsync(AbletonAction.For(AbletonCommand.Play));

        Assert.True(result.Success);
        Assert.Equal(new[] { "/live/song/start_playing" }, _transport.SentAddresses);
    }

    [Fact]
    public async Task PressingAButtonWithLiveClosedFailsWithAnExplanationInsteadOfSilence()
    {
        _adapter.Start();

        var result = await _adapter.ExecuteAsync(AbletonAction.For(AbletonCommand.Play));

        Assert.False(result.Success);
        Assert.Contains("Ableton Live", result.Error);
        Assert.DoesNotContain("/live/song/start_playing", _transport.SentAddresses);
    }

    [Fact]
    public async Task NothingIsSentWhileTheScriptVersionIsWrong()
    {
        // Half the addresses might still work, but guessing which is worse than saying so.
        _adapter.Start();
        _transport.ReceivePong(scriptVersion: "1.0.0");
        _transport.Sent.Clear();

        var result = await _adapter.ExecuteAsync(AbletonAction.For(AbletonCommand.Play));

        Assert.False(result.Success);
        Assert.Contains("Remote Script", result.Error);
        Assert.Empty(_transport.Sent);
    }

    [Fact]
    public async Task AnInvalidActionIsRefusedBeforeItReachesTheWire()
    {
        _adapter.Start();
        _transport.ReceivePong();
        _transport.Sent.Clear();

        var action = AbletonAction.For(AbletonCommand.SetTempo);
        action.Value = 5000;

        var result = await _adapter.ExecuteAsync(action);

        Assert.False(result.Success);
        Assert.Empty(_transport.Sent);
    }

    // ---------- Loading a device ----------

    [Fact]
    public async Task LoadingADeviceSendsTheUriTheCategoryAndTheName()
    {
        _adapter.Start();
        _transport.ReceivePong();
        _transport.Sent.Clear();

        var result = await _adapter.LoadDeviceAsync(new AbletonLoadDeviceAction
        {
            Name = "Chorus Echo.adg",
            Uri = "query:AudioFx#Space:FileId_5479",
            Category = AbletonBrowserCategories.AudioEffects
        });

        Assert.True(result.Success);

        var message = Assert.Single(_transport.Sent);
        Assert.Equal(AbletonCommandMap.LoadAddress, message.Address);
        Assert.Equal("query:AudioFx#Space:FileId_5479", message.GetString(0));
        Assert.Equal("audio_effects", message.GetString(1));
        Assert.Equal("Chorus Echo.adg", message.GetString(2));
    }

    [Fact]
    public async Task ADeviceWithNoUriStillTravelsSoTheScriptCanFallBackToTheName()
    {
        _adapter.Start();
        _transport.ReceivePong();
        _transport.Sent.Clear();

        await _adapter.LoadDeviceAsync(new AbletonLoadDeviceAction { Name = "Reverb" });

        var message = Assert.Single(_transport.Sent);
        Assert.Equal(string.Empty, message.GetString(0));
        Assert.Equal("Reverb", message.GetString(2));
    }

    [Fact]
    public async Task LoadingWithLiveClosedFailsWithAnExplanation()
    {
        _adapter.Start();

        var result = await _adapter.LoadDeviceAsync(new AbletonLoadDeviceAction { Name = "Reverb" });

        Assert.False(result.Success);
        Assert.Contains("Ableton Live", result.Error);
        Assert.DoesNotContain(AbletonCommandMap.LoadAddress, _transport.SentAddresses);
    }

    [Fact]
    public async Task ADeviceWithNothingToLookForIsRefusedBeforeTheWire()
    {
        _adapter.Start();
        _transport.ReceivePong();
        _transport.Sent.Clear();

        var result = await _adapter.LoadDeviceAsync(new AbletonLoadDeviceAction { Name = "  " });

        Assert.False(result.Success);
        Assert.Empty(_transport.Sent);
    }

    // ---------- Failures that arrive late ----------

    [Fact]
    public void AnErrorFromInsideLiveIsSurfacedRatherThanDropped()
    {
        // Commands are fire and forget, so this is the only way the user ever learns that Live
        // refused one.
        var failures = new List<string>();
        _adapter.ScriptFailed += failures.Add;

        _adapter.Start();
        _transport.ReceivePong();
        _transport.Receive(new OscMessage(
            AbletonCommandMap.ErrorAddress, "/live/track/set/mute", "there is no track 99"));

        var failure = Assert.Single(failures);
        Assert.Contains("/live/track/set/mute", failure);
        Assert.Contains("no track 99", failure);
    }

    [Fact]
    public void DisposingReleasesTheTransport()
    {
        _adapter.Start();
        _adapter.Dispose();

        Assert.True(_transport.Disposed);
    }
}
