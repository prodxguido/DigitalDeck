using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DigitalDeck.Core.Layout;
using DigitalDeck.Core.Model;
using Xunit;

namespace DigitalDeck.Avalonia.Tests;

/// <summary>
/// Putting the deck back on the screen it was left on.
/// </summary>
/// <remarks>
/// The whole point of the app is to live on a second screen, so this is not cosmetic: a window
/// restored onto a monitor that is no longer connected is invisible, and a person who cannot find
/// their deck assumes it did not start.
/// <para>
/// Avalonia's headless platform reports one screen of its own, which is enough to drive the real
/// thing rather than a stand-in.
/// </para>
/// </remarks>
public class PlacementTests
{
    private static MonitorInfo Screen(string name, int left, int top, int width, int height) =>
        new(name,
            new MonitorRect(left, top, left + width, top + height),
            new MonitorRect(left, top, left + width, top + height - 40),
            IsPrimary: left == 0);

    [AvaloniaFact]
    public void WithNothingRememberedTheWindowOpensCentredOnItsScreen()
    {
        var window = new Window { Width = 400, Height = 300 };

        try
        {
            window.Show();

            var screens = AvaloniaPlacement.Monitors(window);

            Assert.NotEmpty(screens);

            // Nada guardado: centrada, y sin pedir pantalla completa.
            Assert.False(AvaloniaPlacement.Restore(window, null));

            var expected = WindowPlacement.CentredIn(screens[0]);

            Assert.Equal(expected.Width, (int)window.Width);
            Assert.Equal(expected.Left, window.Position.X);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void WhereTheWindowEndedUpIsWhatGetsWrittenDown()
    {
        var window = new Window { Width = 400, Height = 300 };

        try
        {
            window.Show();
            AvaloniaPlacement.Restore(window, null);

            var captured = AvaloniaPlacement.Capture(window);

            Assert.NotNull(captured);
            Assert.Equal(window.Position.X, captured.Left);
            Assert.Equal((int)window.Width, captured.Width);
            Assert.False(captured.Fullscreen);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AWindowRememberedOnAScreenThatIsGoneComesBackOnOneThatIsThere()
    {
        // El caso que importa de verdad: el monitor guardado no está, y la ventana tiene que
        // aparecer igual, en una pantalla que sí existe.
        var window = new Window { Width = 400, Height = 300 };

        try
        {
            window.Show();

            var gone = Screen("pantalla-que-no-esta", 9000, 9000, 2560, 1440);
            var saved = WindowPlacement.Of(gone, new MonitorRect(9100, 9100, 10260, 9820), fullscreen: false);

            AvaloniaPlacement.Restore(window, saved);

            var here = AvaloniaPlacement.Monitors(window)[0];

            Assert.InRange(window.Position.X, here.WorkArea.Left, here.WorkArea.Right);
            Assert.InRange(window.Position.Y, here.WorkArea.Top, here.WorkArea.Bottom);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void WhatWasSavedOnOneShellIsReadableByTheOther()
    {
        // La misma clave y la misma forma que escribe la app de WPF: un deck que se mueve entre
        // los dos shells se acuerda de su posición en vez de olvidarla una vez.
        var monitor = Screen(@"\\.\DISPLAY2", 1920, 0, 2560, 1440);
        var bounds = new MonitorRect(2000, 100, 3160, 820);

        var written = WindowPlacement.Of(monitor, bounds, fullscreen: false).ToJson();
        var read = WindowPlacement.Read(written);

        Assert.NotNull(read);
        Assert.Equal(monitor.DeviceName, read.DeviceName);
        Assert.Equal(bounds, read.Bounds);
    }
}
