using DigitalDeck.Core.Model;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Integrations.Voice;

namespace DigitalDeck.App.Views;

/// <summary>
/// Turning the assistant on, choosing a microphone, and choosing the key.
/// </summary>
/// <remarks>
/// The level meter is the part that earns its place. Windows has two different "default"
/// microphones and a studio machine has a dozen inputs, most of them digital ones with nothing
/// plugged in — so the honest way to choose is to talk and watch the bar move, not to read a list
/// of names and guess. It runs through the same capture path the assistant uses, which means a
/// device that shows a level here is a device that will work.
/// </remarks>
public partial class VoiceSettingsWindow : Window
{
    private readonly VoiceService _voice;
    private readonly IVoiceInput _preview = new WasapiVoiceInput();

    private bool _loading = true;

    public VoiceSettingsWindow(VoiceService voice)
    {
        _voice = voice;

        InitializeComponent();

        _preview.SamplesCaptured += OnPreviewSamples;
        _preview.Faulted += OnPreviewFaulted;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnabledBox.IsChecked = _voice.IsEnabled;
        HotkeyBox.Text = _voice.Hotkey.ToString();

        var devices = _voice.GetDevices();
        DeviceBox.ItemsSource = devices;

        DeviceBox.SelectedItem =
            devices.FirstOrDefault(d => d.Id == _voice.DeviceId)
            ?? devices.FirstOrDefault(d => d.IsDefault)
            ?? devices.FirstOrDefault();

        DescribeState();

        _loading = false;

        StartPreview();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _preview.SamplesCaptured -= OnPreviewSamples;
        _preview.Faulted -= OnPreviewFaulted;
        _preview.Dispose();
    }

    private void DescribeState()
    {
        if (!_voice.HasModel)
        {
            EnabledBox.IsEnabled = false;

            if (_voice.IsInstallingModel)
            {
                StateDetail.Text = "Instalando el asistente de voz…";
                InstallModelButton.Visibility = Visibility.Visible;
                InstallModelButton.IsEnabled = false;
                return;
            }

            if (_voice.CanInstallModel)
            {
                StateDetail.Text =
                    "El asistente de voz todavía no está instalado. Ocupa unos 60 MB y ya viene adentro de la app.";
                InstallModelButton.Visibility = Visibility.Visible;
                InstallModelButton.IsEnabled = true;
                return;
            }

            StateDetail.Text =
                "Falta el modelo de voz. Descargá vosk-model-small-es de alphacephei.com/vosk/models "
                + "y descomprimilo en la carpeta voice de DigitalDeck.";
            InstallModelButton.Visibility = Visibility.Collapsed;
            return;
        }

        InstallModelButton.Visibility = Visibility.Collapsed;
        EnabledBox.IsEnabled = true;

        StateDetail.Text = _voice.IsEnabled
            ? $"Activo. Apretá {_voice.Hotkey} y decí el nombre de un botón de los que estén en pantalla."
            : "Apagado. Mientras está apagado, nada escucha el micrófono.";
    }

    private async void InstallModel_Click(object sender, RoutedEventArgs e)
    {
        InstallModelButton.IsEnabled = false;
        DescribeState();

        await _voice.InstallModelAsync();

        DescribeState();
    }

    // ---------- The switch ----------

    private async void Enabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        var wanted = EnabledBox.IsChecked == true;

        if (!await _voice.SetEnabledAsync(wanted) && wanted)
        {
            // It refused and has already said why. Putting the box back is what keeps the screen
            // from claiming the assistant is on when it is not.
            _loading = true;
            EnabledBox.IsChecked = false;
            _loading = false;
        }

        DescribeState();
    }

    // ---------- Microphone ----------

    private async void Device_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        await _voice.SetDeviceAsync((DeviceBox.SelectedItem as VoiceInputDevice)?.Id);

        StartPreview();
    }

    private void StartPreview()
    {
        LevelHint.Text = "Hablá para probarlo.";
        LevelBar.Width = 0;

        _preview.Start((DeviceBox.SelectedItem as VoiceInputDevice)?.Id);
    }

    private void OnPreviewSamples(short[] samples)
    {
        var peak = 0;

        foreach (var sample in samples)
            peak = Math.Max(peak, Math.Abs((int)sample));

        var level = peak / (double)short.MaxValue;

        Dispatcher.BeginInvoke(() =>
        {
            if (LevelBar.Parent is not FrameworkElement track) return;

            // Square-rooted so a quiet voice still visibly moves the bar. A linear meter on speech
            // sits near the floor and looks broken.
            LevelBar.Width = Math.Clamp(Math.Sqrt(level), 0, 1) * track.ActualWidth;

            if (level > 0.02) LevelHint.Text = "Te escucha.";
        });
    }

    private void OnPreviewFaulted(VoiceInputError error) =>
        Dispatcher.BeginInvoke(() => LevelHint.Text = error.Detail);

    // ---------- Hotkey ----------

    private async void Hotkey_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        // Alt combinations arrive as SystemKey, and reading Key there gives Key.System rather than
        // the key the user actually pressed.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // A modifier on its own is the user halfway through the combination, not their answer.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            return;

        var modifiers = HotkeyModifiers.None;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Windows;

        var spec = new HotkeySpec(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));

        // Round-tripped through the parser so only combinations this app can actually store and
        // read back are accepted — the layout-dependent keys are refused here rather than saved and
        // silently lost.
        if (!HotkeySpec.TryParse(spec.ToString(), out var usable))
        {
            HotkeyHint.Text = "Esa tecla no se puede usar como atajo. Probá con una letra, un número o una F.";
            return;
        }

        if (await _voice.SetHotkeyAsync(usable))
        {
            HotkeyBox.Text = usable.ToString();
            HotkeyHint.Text = "Listo.";
            DescribeState();
        }
        else
        {
            HotkeyHint.Text = $"{usable} ya la está usando otro programa. Probá otra.";
        }
    }

    // ---------- Chrome ----------

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
