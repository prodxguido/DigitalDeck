using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;

namespace DigitalDeck.Avalonia.Tests;

/// <summary>Live's mixer with no Live behind it, driven by hand.</summary>
public sealed class FakeMixer : IAbletonMixer
{
    public IReadOnlyList<LiveTrack> Tracks { get; private set; } = Array.Empty<LiveTrack>();

    /// <summary>Every move the window asked for, in order, as (index, value).</summary>
    public List<(int Index, double Value)> Moves { get; } = new();

    public event Action<IReadOnlyList<LiveTrack>>? TracksChanged;

    public event Action<LiveVolume>? VolumeChanged;

    public void Open() { }

    public void Close() { }

    public void SetVolume(int trackIndex, double value) => Moves.Add((trackIndex, value));

    /// <summary>Pretends Live reported its channels.</summary>
    public void Report(params LiveTrack[] tracks)
    {
        Tracks = tracks;
        TracksChanged?.Invoke(tracks);
    }

    /// <summary>Pretends someone moved a fader inside Live.</summary>
    public void Push(int index, double value, string display = "") =>
        VolumeChanged?.Invoke(new LiveVolume(index, value) { Display = display });
}
