using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;

namespace DigitalDeck.ViewModels;

/// <summary>
/// One channel strip: a fader, its name, and the reading Live prints under it.
/// </summary>
/// <remarks>
/// <see cref="Volume"/> is the fader's own travel, 0..1, exactly as Live's API takes it. That is
/// not a simplification: Live's fader is linear in this number too — unity sits near 0.85 because
/// that is where the travel puts it — so a straight vertical slider feels the same under the finger
/// as the one on the other screen. What is not linear is the decibel behind the position, and that
/// number comes from Live in <see cref="Display"/> rather than from a conversion here.
/// </remarks>
public sealed class MixerChannelViewModel : ObservableObject
{
    /// <summary>
    /// How long a fader keeps ignoring Live after the finger comes off.
    /// </summary>
    /// <remarks>
    /// Live echoes back every move the app makes — it has no idea which side a value came from —
    /// and the last of those echoes is still in flight when the user lets go. Without a moment of
    /// deafness the fader visibly springs back to a position from a few frames ago.
    /// </remarks>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);

    private readonly Action<MixerChannelViewModel, double> _moved;
    private readonly Func<DateTimeOffset> _clock;

    private LiveTrack _track;
    private double _volume;
    private string _display;
    private bool _isHeld;
    private DateTimeOffset _releasedAt = DateTimeOffset.MinValue;

    public MixerChannelViewModel(
        LiveTrack track,
        Action<MixerChannelViewModel, double> moved,
        Func<DateTimeOffset>? clock = null)
    {
        _track = track;
        _moved = moved;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _volume = track.Volume;
        _display = track.Display;
    }

    public int Index => _track.Index;

    public string Name => _track.Name.Length == 0 ? "sin nombre" : _track.Name;

    public LiveTrackKind Kind => _track.Kind;

    public int Depth => _track.Depth;

    public bool IsGroup => _track.Kind == LiveTrackKind.Group;

    public bool IsMaster => _track.Kind == LiveTrackKind.Master;

    /// <summary>
    /// How far in this channel is drawn, in pixels, for a track that lives inside a group.
    /// </summary>
    /// <remarks>
    /// A number rather than the toolkit's margin type: this class is shared by whatever is drawing,
    /// and every toolkit spells a margin differently. Turning it into one is the view's job.
    /// </remarks>
    public double IndentPixels => _track.Depth * 9.0;

    /// <summary>
    /// The channel's colour as Live reports it, <c>0xRRGGBB</c>, or null when it has none.
    /// </summary>
    /// <remarks>
    /// The raw number for the same reason as <see cref="IndentPixels"/>. Null rather than a default
    /// colour so the view can fall back to its own idea of "no colour" — the master and the returns
    /// have none, and painting them an invented one would say something untrue about the set.
    /// </remarks>
    public int? ColorRgb => _track.Color == 0 ? null : _track.Color;

    /// <summary>The fader position, 0..1. Setting it is what a finger on the strip does.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);

            if (Math.Abs(_volume - clamped) < 0.0001) return;

            _volume = clamped;
            OnPropertyChanged();

            _moved(this, clamped);
        }
    }

    /// <summary>What Live prints under the fader, or the raw position when this Live would not say.</summary>
    public string Display => _display.Length > 0 ? _display : _volume.ToString("0.00");

    /// <summary>Set while the user has hold of this fader, so Live's echo cannot fight the finger.</summary>
    public bool IsHeld
    {
        get => _isHeld;
        set
        {
            if (_isHeld == value) return;

            _isHeld = value;
            if (!value) _releasedAt = _clock();

            OnPropertyChanged();
        }
    }

    /// <summary>Takes a move made inside Live, unless the user is the one moving this fader.</summary>
    public void ApplyFromLive(double value, string display)
    {
        if (_isHeld || _clock() - _releasedAt < Settle) return;

        _display = display;
        OnPropertyChanged(nameof(Display));

        var clamped = Math.Clamp(value, 0, 1);

        if (Math.Abs(_volume - clamped) < 0.0001) return;

        // Straight to the field: going through the setter would send this back where it came from.
        _volume = clamped;
        OnPropertyChanged(nameof(Volume));
    }

    /// <summary>Called after the app itself moved the fader, to keep the reading honest.</summary>
    public void ShowSentReading(string display)
    {
        if (display.Length == 0 || display == _display) return;

        _display = display;
        OnPropertyChanged(nameof(Display));
    }

    /// <summary>
    /// Re-points this strip at a fresh roster entry without rebuilding it.
    /// </summary>
    /// <returns>False when the entry is not the same channel, and the strip has to be replaced.</returns>
    public bool Rebind(LiveTrack track)
    {
        if (track.Index != _track.Index || track.Kind != _track.Kind) return false;

        var before = _track;
        _track = track;

        if (before.Name != track.Name) OnPropertyChanged(nameof(Name));
        if (before.Color != track.Color) OnPropertyChanged(nameof(ColorRgb));
        if (before.Depth != track.Depth) OnPropertyChanged(nameof(IndentPixels));

        // The volume in a roster is a snapshot from Live, so it is applied under the same rule as
        // any other thing Live says: not while a finger is on this fader.
        ApplyFromLive(track.Volume, track.Display);

        return true;
    }
}
