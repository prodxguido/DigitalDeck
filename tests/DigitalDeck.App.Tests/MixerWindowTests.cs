using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using DigitalDeck.ViewModels;
using DigitalDeck.App.Views;
using DigitalDeck.Core.Model;
using Xunit;

namespace DigitalDeck.App.Tests;

/// <summary>
/// That the mixer window really builds, and that its faders are the size a finger needs.
/// </summary>
/// <remarks>
/// A XAML mistake — a resource key that does not exist, a template that does not parse — is a
/// runtime failure, and the compiler is perfectly happy with it. This is the only thing in the
/// suite that would catch one before someone opens the window on a touch screen mid-set.
/// </remarks>
public sealed class MixerWindowTests : IClassFixture<WpfThread>
{
    /// <summary>
    /// The smallest a touch target should be, in device-independent pixels.
    /// </summary>
    /// <remarks>
    /// Roughly 9mm on a typical screen, which is the figure both Microsoft's and Apple's guidance
    /// lands near. A mixer whose thumbs are mouse-sized is a mixer that gets used with a mouse.
    /// </remarks>
    private const double MinimumTouchTarget = 40;

    private readonly WpfThread _ui;

    public MixerWindowTests(WpfThread ui) => _ui = ui;

    private static LiveTrack Track(int index, string name, LiveTrackKind kind = LiveTrackKind.Normal) =>
        new(index, name, kind, 0x3C7DB5, 0.85, 0, "0.0 dB");

    [Fact]
    public void TheWindowBuildsWithChannelsOnIt()
    {
        var names = _ui.Run(() =>
        {
            var mixer = new FakeAbletonMixer();
            var window = new MixerWindow(mixer, new FakeAbletonClient());

            try
            {
                // Show, not just construct: the templates are not applied until it lays out, and an
                // unparseable one is exactly what this is here to catch.
                window.Show();

                mixer.Report(
                    Track(0, "Batería", LiveTrackKind.Group),
                    Track(-3, "A Reverb", LiveTrackKind.Return),
                    Track(-2, "Master", LiveTrackKind.Master));

                Settle(window);

                // Only the strips that ended up bound to a channel: the master pane keeps its
                // template around while collapsed, and an unbound fader is not one on screen.
                return Faders(window)
                    .Select(fader => (fader.DataContext as MixerChannelViewModel)?.Name)
                    .Where(name => name is not null)
                    .ToList();
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains("Batería", names);
        Assert.Contains("A Reverb", names);
        Assert.Contains("Master", names);
    }

    [Fact]
    public void EveryFaderIsBigEnoughToDragWithAFinger()
    {
        var thumbs = _ui.Run(() =>
        {
            var mixer = new FakeAbletonMixer();
            var window = new MixerWindow(mixer, new FakeAbletonClient());

            try
            {
                window.Show();
                mixer.Report(Track(0, "Bajo"));
                Settle(window);

                return Faders(window)
                    .Where(fader => fader.DataContext is MixerChannelViewModel)
                    .SelectMany(Descendants)
                    .OfType<System.Windows.Controls.Primitives.Thumb>()
                    .Select(thumb => (thumb.ActualWidth, thumb.ActualHeight))
                    .ToList();
            }
            finally
            {
                window.Close();
            }
        });

        Assert.NotEmpty(thumbs);

        foreach (var (width, height) in thumbs)
        {
            Assert.True(width >= MinimumTouchTarget, $"un fader de {width} de ancho es muy angosto");
            Assert.True(height >= 24, $"un fader de {height} de alto es muy fino");
        }
    }

    [Fact]
    public void LouderIsUp()
    {
        // A vertical Slider in WPF runs bottom-to-top only because its track is reversed. Get that
        // wrong and every fader in the mixer works upside down — which compiles perfectly, and
        // which nobody discovers until they are on stage.
        var (atSilence, atUnity) = _ui.Run(() =>
        {
            var mixer = new FakeAbletonMixer();
            var window = new MixerWindow(mixer, new FakeAbletonClient());

            try
            {
                window.Show();
                mixer.Report(Track(0, "Bajo"));
                Settle(window);

                var fader = Faders(window).First(f => f.DataContext is MixerChannelViewModel);
                var strip = (MixerChannelViewModel)fader.DataContext;

                strip.ApplyFromLive(0, "-inf dB");
                Settle(window);
                var bottom = ThumbTop(fader);

                strip.ApplyFromLive(0.85, "0.0 dB");
                Settle(window);
                var top = ThumbTop(fader);

                return (bottom, top);
            }
            finally
            {
                window.Close();
            }
        });

        // Y grows downwards on screen, so the louder position must sit at the smaller number.
        Assert.True(atUnity < atSilence, $"unidad en {atUnity}, silencio en {atSilence}");
    }

    [Fact]
    public void TheUnityMarkSitsWhereUnityIs()
    {
        // The one point of the dB scale the mixer draws, and the only one it can draw without
        // inventing Ableton's curve. A mark in the wrong place is worse than no mark at all.
        var (mark, thumb) = _ui.Run(() =>
        {
            var mixer = new FakeAbletonMixer();
            var window = new MixerWindow(mixer, new FakeAbletonClient());

            try
            {
                window.Show();
                mixer.Report(Track(0, "Bajo"));
                Settle(window);

                var fader = Faders(window).First(f => f.DataContext is MixerChannelViewModel);
                ((MixerChannelViewModel)fader.DataContext).ApplyFromLive(0.85, "0.0 dB");
                Settle(window);

                var line = Descendants(fader)
                    .OfType<FrameworkElement>()
                    .First(element => element.Name == "UnityMark");

                return (
                    line.TransformToAncestor(fader).Transform(new Point(0, 0)).Y,
                    ThumbTop(fader) + ThumbHeight(fader) / 2);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(Math.Abs(mark - thumb) < 2, $"la marca está en {mark} y la unidad en {thumb}");
    }

    private static double ThumbHeight(Slider fader) =>
        Descendants(fader).OfType<System.Windows.Controls.Primitives.Thumb>().First().ActualHeight;

    /// <summary>How far down the fader the thumb currently sits.</summary>
    private static double ThumbTop(Slider fader)
    {
        var thumb = Descendants(fader).OfType<System.Windows.Controls.Primitives.Thumb>().First();

        return thumb.TransformToAncestor(fader).Transform(new Point(0, 0)).Y;
    }

    /// <summary>
    /// Lets the queued work run, then lays out.
    /// </summary>
    /// <remarks>
    /// The view model marshals Live's updates onto the dispatcher rather than applying them where
    /// they arrive, so a roster reported from this thread is still sitting in the queue when the
    /// next line runs. Draining at a lower priority than the post is what makes it have happened.
    /// </remarks>
    private static void Settle(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
        window.UpdateLayout();
    }

    private static IEnumerable<Slider> Faders(DependencyObject root) =>
        Descendants(root).OfType<Slider>();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            yield return child;

            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
}
