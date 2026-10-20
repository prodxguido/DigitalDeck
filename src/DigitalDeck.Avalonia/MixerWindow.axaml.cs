using Avalonia.Controls;

namespace DigitalDeck.Avalonia;

/// <summary>
/// The touch mixer: one fader per channel of the open Live set.
/// </summary>
/// <remarks>
/// Almost nothing in this file, and that is the point: <c>MixerViewModel</c> and
/// <c>MixerChannelViewModel</c> are shared with the WPF app and never had a line of toolkit in
/// them, so porting the window was porting the XAML and nothing else.
/// </remarks>
public partial class MixerWindow : Window
{
    public MixerWindow() => InitializeComponent();
}
