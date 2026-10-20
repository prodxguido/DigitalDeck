using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using DigitalDeck.Core.Abstractions;

namespace DigitalDeck.App.Views;

/// <summary>
/// The touch mixer: one fader per channel of the open Live set.
/// </summary>
/// <remarks>
/// Its own window and never part of the grid: it is meant to be dragged onto a touch screen and
/// left there while the deck stays on the other monitor. Single-instance is enforced by
/// <see cref="Services.DeckDialogs"/>.
/// <para>
/// Almost nothing happens in this file. What does is the one thing a view model cannot know: which
/// fader a hand is currently on. WPF gives that away through the thumb's drag events and through
/// the touch and mouse presses that a move-to-point tap arrives as — and until the strip is told,
/// every echo Live sends back would be fighting the finger.
/// </para>
/// </remarks>
public partial class MixerWindow : Window
{
    private readonly IAbletonClient _ableton;
    private readonly WindowPlacementService? _placement;
    private readonly MixerViewModel _model;

    /// <param name="placement">
    /// Where to put the window, remembered under a key of its own. Optional so a test can build the
    /// window without a settings store behind it.
    /// </param>
    public MixerWindow(
        IAbletonMixer mixer,
        IAbletonClient ableton,
        WindowPlacementService? placement = null)
    {
        _ableton = ableton;
        _placement = placement;

        InitializeComponent();

        _model = new MixerViewModel(mixer, ableton, action => Dispatcher.BeginInvoke(action));
        DataContext = _model;

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Handled events too: a Slider marks the thumb's drag as handled once it has acted on it,
        // and this is a bystander that only wants to know it happened.
        Root.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnGrabbed), true);
        Root.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnReleased), true);
        Root.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnPressed), true);
        Root.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnLifted), true);
        Root.AddHandler(UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(OnTouched), true);
        Root.AddHandler(UIElement.PreviewTouchUpEvent, new EventHandler<TouchEventArgs>(OnUntouched), true);

        _ableton.StatusChanged += OnStatusChanged;
        ApplyStatus(_ableton.Status);

        _model.Open();

        // Back onto the screen it was dragged to. The whole point of this window is that it lives
        // on the touch monitor, and having to move it there every launch would undo that.
        if (_placement is not null)
            await _placement.RestoreAsync(this, SettingKeys.MixerPlacement);
    }

    /// <summary>
    /// Remembers where the window ended up, while there is still a window to ask.
    /// </summary>
    /// <remarks>
    /// On Closing rather than Closed, which is not a detail: by the time Closed runs the native
    /// handle is gone, and a placement service with no handle to look up has nothing to save. The
    /// call is not awaited because everything it reads off the window it reads before its first
    /// await — only the write to the settings store happens afterwards.
    /// </remarks>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_placement is null) return;

        try
        {
            _ = _placement.SaveAsync(this, false, null, SettingKeys.MixerPlacement);
        }
        catch (Exception)
        {
            // Losing the remembered monitor is not a reason to refuse to close.
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _ableton.StatusChanged -= OnStatusChanged;
        _model.Dispose();
    }

    // ---------- Which fader has a hand on it ----------

    private void OnGrabbed(object sender, DragStartedEventArgs e) => Hold(e.OriginalSource, true);

    private void OnReleased(object sender, DragCompletedEventArgs e) => Hold(e.OriginalSource, false);

    private void OnPressed(object sender, MouseButtonEventArgs e) => Hold(e.OriginalSource, true);

    private void OnLifted(object sender, MouseButtonEventArgs e) => Hold(e.OriginalSource, false);

    private void OnTouched(object? sender, TouchEventArgs e) => Hold(e.OriginalSource, true);

    private void OnUntouched(object? sender, TouchEventArgs e) => Hold(e.OriginalSource, false);

    /// <summary>Marks the strip the event came from as held, or lets it go.</summary>
    private static void Hold(object? source, bool held)
    {
        if (FaderOf(source)?.DataContext is MixerChannelViewModel strip) strip.IsHeld = held;
    }

    /// <summary>The slider an event started inside, or null when it did not start in one.</summary>
    private static Slider? FaderOf(object? source)
    {
        for (var node = source as DependencyObject; node is not null; node = Above(node))
            if (node is Slider slider)
                return slider;

        return null;
    }

    /// <summary>
    /// Walks up out of a control template as well as out of the visual tree.
    /// </summary>
    /// <remarks>
    /// The thumb of a templated slider is not reachable by <c>VisualTreeHelper</c> alone from every
    /// event source — a run of text inside a content presenter is not a visual child of anything
    /// useful. Falling back to the logical parent is what makes the walk end at the slider rather
    /// than at null.
    /// </remarks>
    private static DependencyObject? Above(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);

    // ---------- The bridge's state ----------

    private void OnStatusChanged(AbletonStatus status) =>
        Dispatcher.BeginInvoke(() => ApplyStatus(status));

    private void ApplyStatus(AbletonStatus status)
    {
        LiveDot.Fill = Brush(status.State switch
        {
            AbletonConnectionState.Connected => "AccentBrush",
            AbletonConnectionState.Connecting => "TextFaintBrush",
            _ => "DangerBrush"
        });

        LiveStateText.Text = status.State switch
        {
            AbletonConnectionState.Connected => "Live conectado",
            AbletonConnectionState.Connecting => "Buscando a Live…",
            AbletonConnectionState.VersionMismatch => "El Remote Script está desactualizado",
            AbletonConnectionState.Unavailable => "No pude abrir el puente",
            _ => "Live no está conectado"
        };
    }

    // ---------- Panes ----------

    private void Compact_Click(object sender, RoutedEventArgs e) =>
        _model.CompactGroups = !_model.CompactGroups;

    private void Returns_Click(object sender, RoutedEventArgs e) =>
        _model.ShowReturns = !_model.ShowReturns;

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}
