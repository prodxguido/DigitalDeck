using DigitalDeck.Core.Abstractions;
using DigitalDeck.Integrations.Ableton;
using DigitalDeck.Integrations.Ableton.Osc;
using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// What the set is doing, as the bridge learns it: asked for once, then told.
/// </summary>
public class AbletonSongTests
{
    private readonly List<OscMessage> _sent = new();
    private readonly AbletonSongState _song;

    public AbletonSongTests() => _song = new AbletonSongState(_sent.Add);

    private static OscMessage Says(string address, params object[] args) => new(address, args);

    // ---------- The cache ----------

    [Fact]
    public void EachValueArrivesOnItsOwnAddressAndTheRestIsKept()
    {
        Assert.True(_song.TryHandle(Says(AbletonCommandMap.SongIsPlayingAddress, 1)));
        Assert.True(_song.TryHandle(Says(AbletonCommandMap.SongTempoAddress, 128.5)));

        Assert.Equal(true, _song.State.IsPlaying);
        Assert.Equal(128.5, _song.State.Tempo);

        // A second message about one value must not blank the others.
        Assert.True(_song.TryHandle(Says(AbletonCommandMap.SongMetronomeAddress, 1)));

        Assert.Equal(true, _song.State.IsPlaying);
        Assert.Equal(128.5, _song.State.Tempo);
        Assert.Equal(true, _song.State.MetronomeOn);
    }

    [Fact]
    public void ZeroIsOffAndNotUnknown()
    {
        _song.TryHandle(Says(AbletonCommandMap.SongRecordAddress, 0));

        Assert.Equal(false, _song.State.IsRecording);
        Assert.True(_song.State.IsKnown);
    }

    [Fact]
    public void EveryChangeIsAnnounced()
    {
        var heard = new List<LiveSongState>();
        _song.StateChanged += heard.Add;

        _song.TryHandle(Says(AbletonCommandMap.SongIsPlayingAddress, 1));
        _song.TryHandle(Says(AbletonCommandMap.SongIsPlayingAddress, 0));

        Assert.Equal(new bool?[] { true, false }, heard.Select(state => state.IsPlaying));
    }

    [Fact]
    public void SomebodyElsesMessageIsLeftAlone()
    {
        Assert.False(_song.TryHandle(Says(AbletonCommandMap.MixerTracksAddress, 0, "")));
        Assert.False(_song.State.IsKnown);
    }

    // ---------- Every channel's switches ----------

    private static OscMessage States(int selected, params string[] lines) =>
        new(AbletonCommandMap.TrackStatesAddress, selected, lines.Length, string.Join('\n', lines));

    private static string Line(int index, bool mute, bool solo, bool arm) =>
        string.Join('\t', index, mute ? 1 : 0, solo ? 1 : 0, arm ? 1 : 0);

    [Fact]
    public void TheStatesOfEveryChannelArriveTogetherWithTheSelection()
    {
        Assert.True(_song.TryHandle(States(
            1,
            Line(0, mute: true, solo: false, arm: false),
            Line(1, mute: false, solo: true, arm: true),
            Line(-2, mute: false, solo: false, arm: false))));

        Assert.Equal(1, _song.Tracks.Selected);
        Assert.Equal(3, _song.Tracks.Tracks!.Count);

        Assert.True(_song.Tracks.Find(0)!.Value.Mute);
        Assert.True(_song.Tracks.Find(1)!.Value.Arm);
        Assert.False(_song.Tracks.Find(-2)!.Value.Solo);
        Assert.Null(_song.Tracks.Find(9));
    }

    [Fact]
    public void EveryStateChangeIsAnnounced()
    {
        var heard = new List<LiveTracksState>();
        _song.TracksChanged += heard.Add;

        _song.TryHandle(States(0, Line(0, mute: true, solo: false, arm: false)));

        Assert.Single(heard);
        Assert.True(heard[0].Find(0)!.Value.Mute);
    }

    // ---------- Asking and forgetting ----------

    [Fact]
    public void AskingWantsAllFourAtOnce()
    {
        _song.Ask();

        Assert.Equal(
            new[]
            {
                AbletonCommandMap.SongIsPlayingAddress,
                AbletonCommandMap.SongTempoAddress,
                AbletonCommandMap.SongMetronomeAddress,
                AbletonCommandMap.SongRecordAddress,
                AbletonCommandMap.TrackStatesAddress
            },
            _sent.Select(message => message.Address));
    }

    [Fact]
    public void ForgettingGoesBackToNotKnowingAndSaysSo()
    {
        _song.TryHandle(Says(AbletonCommandMap.SongIsPlayingAddress, 1));

        var heard = new List<LiveSongState>();
        _song.StateChanged += heard.Add;

        _song.Forget();

        Assert.False(_song.State.IsKnown);
        Assert.Equal(new[] { LiveSongState.Unknown }, heard);
    }

    [Fact]
    public void ForgettingDropsTheChannelsToo()
    {
        _song.TryHandle(States(0, Line(0, mute: true, solo: false, arm: false)));

        var heard = new List<LiveTracksState>();
        _song.TracksChanged += heard.Add;

        _song.Forget();

        Assert.Null(_song.Tracks.Tracks);
        Assert.Equal(LiveTracksState.Unknown, _song.Tracks.Selected);
        Assert.Single(heard);
    }

    [Fact]
    public void ForgettingWhatWasNeverKnownSaysNothing()
    {
        var heard = 0;
        _song.StateChanged += _ => heard++;

        _song.Forget();

        Assert.Equal(0, heard);
    }
}

/// <summary>
/// The same thing through the bridge itself, which is what wires asking and forgetting to the
/// connection coming up and going down.
/// </summary>
public class AbletonSongBridgeTests : IDisposable
{
    private readonly FakeOscTransport _transport = new();
    private readonly AbletonAdapter _adapter;

    private DateTimeOffset _now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    public AbletonSongBridgeTests() =>
        _adapter = new AbletonAdapter(_transport, AbletonAdapter.ManualHeartbeat, () => _now);

    public void Dispose() => _adapter.Dispose();

    private IAbletonSong Song => _adapter;

    [Fact]
    public void ConnectingAsksWhatTheSetIsDoing()
    {
        _adapter.Start();
        _transport.ReceivePong();

        Assert.Contains(AbletonCommandMap.SongIsPlayingAddress, _transport.SentAddresses);
        Assert.Contains(AbletonCommandMap.SongTempoAddress, _transport.SentAddresses);
    }

    [Fact]
    public void WhatLiveAnnouncesReachesTheSongFace()
    {
        _adapter.Start();
        _transport.ReceivePong();
        _transport.Receive(new OscMessage(AbletonCommandMap.SongIsPlayingAddress, 1));

        Assert.Equal(true, Song.State.IsPlaying);
    }

    [Fact]
    public void LosingLiveForgetsWhatItSaid()
    {
        // A button lit because the set was playing when Live closed would go on saying so forever.
        _adapter.Start();
        _transport.ReceivePong();
        _transport.Receive(new OscMessage(AbletonCommandMap.SongIsPlayingAddress, 1));

        _now += TimeSpan.FromMinutes(1);
        _adapter.Tick();

        Assert.Equal(AbletonConnectionState.Disconnected, _adapter.Status.State);
        Assert.False(Song.State.IsKnown);
    }
}
