using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DigitalDeck.Core.Voice;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;

namespace DigitalDeck.App.Views;

/// <summary>
/// The small panel that says what the assistant is doing, over whatever the user is working in.
/// </summary>
/// <remarks>
/// The whole reason this is a separate window rather than something inside the deck: someone on one
/// monitor is looking at Ableton, not at us. Showing the state on the deck's own window would mean
/// they never see it.
/// <para>
/// Which makes not stealing focus the entire job. A window that activates itself here pulls the
/// user out of Live every time they speak — losing their selection, their edit, their playhead
/// focus. That would be far worse than having no overlay, so it is enforced twice: WPF's
/// <c>ShowActivated="False"</c> for the first show, and <c>WS_EX_NOACTIVATE</c> on the handle for
/// every time after.
/// </para>
/// </remarks>
public partial class VoiceOverlayWindow : Window
{
    /// <summary>How long a finished message stays up before fading out.</summary>
    private static readonly TimeSpan Linger = TimeSpan.FromSeconds(3);

    private readonly DispatcherTimer _hide;
    private readonly IWindowEffects _effects;

    public VoiceOverlayWindow(IWindowEffects effects)
    {
        _effects = effects;

        InitializeComponent();

        _hide = new DispatcherTimer { Interval = Linger };
        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            Hide();
        };

        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e) =>
        _effects.MakeOverlay(new WindowInteropHelper(this).Handle);

    /// <summary>Puts the panel in the bottom-right corner of the monitor the deck is on.</summary>
    public void PlaceNear(MonitorInfo monitor)
    {
        // The work area rather than the full bounds, so it sits above the taskbar instead of behind
        // it. Physical pixels are converted through the window's own scale factor, since the monitor
        // the overlay lands on may not be the one WPF sized it against.
        var scale = VisualTreeHelper.GetDpi(this);

        var width = ActualWidth * scale.DpiScaleX;
        var height = ActualHeight * scale.DpiScaleY;

        Left = (monitor.WorkArea.Right - width) / scale.DpiScaleX;
        Top = (monitor.WorkArea.Bottom - height) / scale.DpiScaleY;
    }

    /// <summary>Shows the current state, and starts the countdown once there is nothing left to do.</summary>
    public void Render(VoiceAssistantStatus status)
    {
        _hide.Stop();

        Detail.Text = status.Detail;

        Pip.Fill = status.State switch
        {
            VoiceAssistantState.Listening => Brush("AccentBrush"),
            VoiceAssistantState.Understood => Brush("AccentBrush"),
            _ => Brush("DangerBrush")
        };

        // The transcript only earns its place on a miss: when it worked, the button's name says
        // everything, and when it did not, seeing "pro que cuatro" is the whole explanation.
        var heard = status.State == VoiceAssistantState.Missed ? status.Heard : null;

        Heard.Text = string.IsNullOrWhiteSpace(heard) ? string.Empty : $"Escuché: {heard}";
        Heard.Visibility = string.IsNullOrWhiteSpace(Heard.Text) ? Visibility.Collapsed : Visibility.Visible;

        if (status.State == VoiceAssistantState.Idle)
        {
            Hide();
            return;
        }

        if (!IsVisible) Show();

        if (status.State != VoiceAssistantState.Listening) _hide.Start();
    }

    private Brush Brush(string key) =>
        TryFindResource(key) as Brush ?? Brushes.Gray;

}
