using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;

namespace DigitalDeck.ViewModels;

/// <summary>
/// The mixer window's model: the channels of the open Live set, and what the faders do to them.
/// </summary>
/// <remarks>
/// Nothing here touches a Window or a Slider, which is the point — the awkward parts of a mixer are
/// the ones a screenshot cannot check. That a fader ignores Live while a finger is on it, that a
/// deleted track does not leave the strip beside it writing to the wrong channel, and that groups
/// fold away are all decided in this file and testable without a socket or a screen.
/// </remarks>
public sealed class MixerViewModel : ObservableObject, IDisposable
{
    private readonly IAbletonMixer _mixer;
    private readonly IAbletonClient _ableton;
    private readonly Action<Action> _toUi;
    private readonly Func<DateTimeOffset> _clock;
    private readonly List<MixerChannelViewModel> _strips = new();

    private bool _showReturns = true;
    private bool _compactGroups;
    private bool _opened;
    private bool _disposed;

    /// <param name="toUi">
    /// Marshals onto the thread the collections are bound to. Live's updates arrive on the
    /// transport's receive loop, and an ObservableCollection changed from there throws.
    /// </param>
    public MixerViewModel(
        IAbletonMixer mixer,
        IAbletonClient ableton,
        Action<Action>? toUi = null,
        Func<DateTimeOffset>? clock = null)
    {
        _mixer = mixer ?? throw new ArgumentNullException(nameof(mixer));
        _ableton = ableton ?? throw new ArgumentNullException(nameof(ableton));
        _toUi = toUi ?? (action => action());
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The tracks and groups, in Live's own order, with folded-away ones left out.</summary>
    public ObservableCollection<MixerChannelViewModel> Channels { get; } = new();

    /// <summary>The returns, kept apart so the whole pane can be folded away.</summary>
    public ObservableCollection<MixerChannelViewModel> Returns { get; } = new();

    public MixerChannelViewModel? Master { get; private set; }

    /// <summary>
    /// Whether the returns are on screen.
    /// </summary>
    /// <remarks>
    /// A pane rather than a permanent column: returns get set once and then left alone for the rest
    /// of a session, and on a touch screen the width they take is width the channels do not get.
    /// </remarks>
    public bool ShowReturns
    {
        get => _showReturns;
        set
        {
            if (!SetProperty(ref _showReturns, value)) return;

            OnPropertyChanged(nameof(ReturnsLabel));
            OnPropertyChanged(nameof(ShowReturnsPane));
        }
    }

    public string ReturnsLabel => _showReturns ? "Ocultar retornos" : "Mostrar retornos";

    /// <summary>Whether the returns pane is drawn — never for a set that has none.</summary>
    public bool ShowReturnsPane => _showReturns && HasReturns;

    /// <summary>
    /// Whether the contents of groups are hidden, leaving only each group's own fader.
    /// </summary>
    /// <remarks>
    /// A set built out of groups is the one that does not fit on a tablet, and it is also the one
    /// where the group fader is what actually gets ridden. All-or-nothing on purpose: a per-group
    /// state is one more thing to keep straight across a roster Live can renumber underneath it.
    /// </remarks>
    public bool CompactGroups
    {
        get => _compactGroups;
        set
        {
            if (!SetProperty(ref _compactGroups, value)) return;

            OnPropertyChanged(nameof(CompactLabel));
            RefreshChannels();
        }
    }

    public string CompactLabel => _compactGroups ? "Abrir grupos" : "Compactar grupos";

    public bool HasGroups => _strips.Any(strip => strip.IsGroup);

    public bool HasReturns => Returns.Count > 0;

    public bool IsEmpty => Channels.Count == 0 && Master is null;

    /// <summary>
    /// What to say instead of faders when there are none.
    /// </summary>
    /// <remarks>
    /// A disconnected bridge already carries its own explanation, so it is repeated here rather
    /// than restated: "no encuentro a Live" and "el script está desactualizado" send someone to
    /// very different places, and a window that flattened both into "no hay canales" would send
    /// them nowhere.
    /// </remarks>
    public string EmptyMessage
    {
        get
        {
            var status = _ableton.Status;

            return status.IsUsable
                ? "Live está conectado pero todavía no mandó los canales. Un segundo…"
                : status.Detail;
        }
    }

    // ---------- Opening and closing ----------

    public void Open()
    {
        if (_opened || _disposed) return;

        _opened = true;

        _mixer.TracksChanged += OnTracksChanged;
        _mixer.VolumeChanged += OnVolumeChanged;
        _ableton.StatusChanged += OnStatusChanged;

        // Whatever the bridge already knew, so a reopened window is not blank while Live answers.
        Rebuild(_mixer.Tracks);

        _mixer.Open();
    }

    public void Close()
    {
        if (!_opened) return;

        _opened = false;

        _mixer.TracksChanged -= OnTracksChanged;
        _mixer.VolumeChanged -= OnVolumeChanged;
        _ableton.StatusChanged -= OnStatusChanged;

        // Live holds a listener per channel while subscribed. Leaving it on for a window nobody can
        // see is a message per fader move crossing the bridge with nowhere to land.
        _mixer.Close();
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        Close();
    }

    // ---------- What Live says ----------

    private void OnStatusChanged(AbletonStatus status) =>
        _toUi(() => OnPropertyChanged(nameof(EmptyMessage)));

    private void OnTracksChanged(IReadOnlyList<LiveTrack> tracks) => _toUi(() => Rebuild(tracks));

    private void OnVolumeChanged(LiveVolume volume) => _toUi(() =>
    {
        foreach (var strip in _strips)
        {
            if (strip.Index != volume.TrackIndex) continue;

            strip.ApplyFromLive(volume.Value, volume.Display);
            return;
        }
    });

    /// <summary>
    /// Takes a whole roster and makes the strips match it.
    /// </summary>
    /// <remarks>
    /// Strips are reused where the channel at an index is still the same kind of thing, so a rename
    /// in Live does not throw away a fader under someone's finger. What is never carried over is
    /// who is holding what: an index is a position, and a track leaving the set renumbers
    /// everything to its right, so a gesture in flight is released rather than allowed to land on a
    /// neighbour.
    /// </remarks>
    private void Rebuild(IReadOnlyList<LiveTrack> tracks)
    {
        foreach (var strip in _strips) strip.IsHeld = false;

        var rebuilt = new List<MixerChannelViewModel>();

        foreach (var track in tracks)
        {
            var existing = _strips.FirstOrDefault(
                strip => strip.Index == track.Index && strip.Kind == track.Kind);

            rebuilt.Add(existing is not null && existing.Rebind(track)
                ? existing
                : new MixerChannelViewModel(track, Move, _clock));
        }

        _strips.Clear();
        _strips.AddRange(rebuilt);

        Returns.Clear();

        foreach (var strip in _strips.Where(strip => strip.Kind == LiveTrackKind.Return))
            Returns.Add(strip);

        Master = _strips.FirstOrDefault(strip => strip.IsMaster);

        RefreshChannels();

        OnPropertyChanged(nameof(Master));
        OnPropertyChanged(nameof(HasReturns));
        OnPropertyChanged(nameof(ShowReturnsPane));
        OnPropertyChanged(nameof(HasGroups));
    }

    private void RefreshChannels()
    {
        Channels.Clear();

        foreach (var strip in Visible()) Channels.Add(strip);

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    /// <summary>
    /// The strips to draw, in Live's order, with the contents of folded groups left out.
    /// </summary>
    /// <remarks>
    /// Live lists a group's contents straight after it and deeper than it, so "everything until the
    /// depth comes back up" is what belongs to a group. Asking the script for parentage as well
    /// would be a second thing that can disagree with the order.
    /// </remarks>
    private IEnumerable<MixerChannelViewModel> Visible()
    {
        var hidingBelow = int.MaxValue;

        foreach (var strip in _strips)
        {
            if (strip.Kind is LiveTrackKind.Return or LiveTrackKind.Master) continue;

            if (strip.Depth > hidingBelow) continue;

            hidingBelow = _compactGroups && strip.IsGroup ? strip.Depth : int.MaxValue;

            yield return strip;
        }
    }

    // ---------- What the finger does ----------

    private void Move(MixerChannelViewModel strip, double value) =>
        _mixer.SetVolume(strip.Index, value);
}
