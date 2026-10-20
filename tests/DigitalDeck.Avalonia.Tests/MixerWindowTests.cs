using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using DigitalDeck.Core.Model;
using DigitalDeck.ViewModels;
using Xunit;

namespace DigitalDeck.Avalonia.Tests;

/// <summary>
/// That the touch mixer really builds, and that its faders are the size a finger needs.
/// </summary>
/// <remarks>
/// The twin of the WPF window's test, and it can be the twin because the view models underneath
/// are literally the same objects: porting this window was porting the XAML and nothing else.
/// </remarks>
public class MixerWindowTests
{
    /// <summary>
    /// The smallest a touch target should be, in device-independent pixels.
    /// </summary>
    /// <remarks>
    /// Roughly 9mm on a typical screen, which is the figure both Microsoft's and Apple's guidance
    /// lands near. A mixer whose faders are mouse-sized is a mixer that gets used with a mouse.
    /// </remarks>
    private const double MinimumTouchTarget = 40;

    private static LiveTrack Track(int index, string name, LiveTrackKind kind = LiveTrackKind.Normal) =>
        new(index, name, kind, 0x3C7DB5, 0.85, 0, "0.0 dB");

    private static (MixerWindow Window, MixerViewModel Deck) Open()
    {
        var mixer = new FakeMixer();

        // Sin marshalling: acá el test es el hilo de UI. Y Open() porque Live sólo empuja mientras
        // la ventana lo pidió — igual que en la app.
        var view = new MixerViewModel(mixer, new NoAbletonForTests(), action => action());
        view.Open();

        var window = new MixerWindow { DataContext = view };

        window.Show();

        mixer.Report(
            Track(0, "Batería", LiveTrackKind.Group),
            Track(1, "Bajo"),
            Track(-3, "A Reverb", LiveTrackKind.Return),
            Track(-2, "Master", LiveTrackKind.Master));

        window.UpdateLayout();

        return (window, view);
    }

    private static IReadOnlyList<Slider> Faders(Visual root) =>
        root.GetVisualDescendants().OfType<Slider>().ToList();

    [AvaloniaFact]
    public void TheWindowBuildsWithAStripPerChannel()
    {
        var (window, deck) = Open();

        try
        {
            // Two ordinary channels, the return, and the master.
            Assert.Equal(2, deck.Channels.Count);
            Assert.Single(deck.Returns);
            Assert.NotNull(deck.Master);

            Assert.NotEmpty(Faders(window));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EveryFaderIsBigEnoughToDragWithAFinger()
    {
        var (window, _) = Open();

        try
        {
            Assert.All(Faders(window), fader =>
                Assert.True(fader.Bounds.Width >= MinimumTouchTarget,
                    $"un fader de {fader.Bounds.Width} px es angosto para un dedo"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MovingAFaderReachesLive()
    {
        // The point of the window: the value goes back out to the set, not just onto the screen.
        var mixer = new FakeMixer();
        var view = new MixerViewModel(mixer, new NoAbletonForTests(), action => action());
        view.Open();

        var window = new MixerWindow { DataContext = view };

        try
        {
            window.Show();
            mixer.Report(Track(0, "Bajo"));
            window.UpdateLayout();

            view.Channels[0].Volume = 0.42;

            Assert.Contains((0, 0.42), mixer.Moves);
        }
        finally
        {
            window.Close();
        }
    }
}
