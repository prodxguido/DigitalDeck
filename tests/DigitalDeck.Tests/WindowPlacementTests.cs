using DigitalDeck.Core.Layout;
using DigitalDeck.Core.Model;
using Xunit;

namespace DigitalDeck.Tests;

/// <summary>
/// What gets remembered about where a window was, and where one opens when nothing was.
/// </summary>
/// <remarks>
/// The stored shape is checked by name here on purpose. Those property names are already written
/// into people's settings, and renaming one would not fail anything — it would silently forget
/// every window position ever saved, on every machine, at once.
/// </remarks>
public class WindowPlacementTests
{
    private static MonitorInfo Screen(int left, int top, int width, int height, int taskbar = 40) =>
        new("\\\\.\\DISPLAY1",
            new MonitorRect(left, top, left + width, top + height),
            new MonitorRect(left, top, left + width, top + height - taskbar),
            IsPrimary: true);

    [Fact]
    public void AWindowOpensCentredOnTheWorkArea()
    {
        var monitor = Screen(0, 0, 1920, 1080);

        var bounds = WindowPlacement.CentredIn(monitor);

        Assert.Equal(WindowPlacement.PreferredWidth, bounds.Width);
        Assert.Equal(WindowPlacement.PreferredHeight, bounds.Height);

        // Igual de aire a cada lado.
        Assert.Equal(bounds.Left - monitor.WorkArea.Left, monitor.WorkArea.Right - bounds.Right);
    }

    [Fact]
    public void ItNeverOpensUnderTheTaskbar()
    {
        // El área de trabajo y no la pantalla entera: una ventana que abre debajo de la barra es
        // una ventana que la persona tiene que ir a buscar.
        var monitor = Screen(0, 0, 1920, 1080, taskbar: 60);

        var bounds = WindowPlacement.CentredIn(monitor);

        Assert.True(bounds.Bottom <= monitor.WorkArea.Bottom);
    }

    [Fact]
    public void OnASmallScreenItShrinksInsteadOfHangingOff()
    {
        var monitor = Screen(0, 0, 800, 600);

        var bounds = WindowPlacement.CentredIn(monitor);

        Assert.True(bounds.Width < WindowPlacement.PreferredWidth);
        Assert.True(bounds.Left >= monitor.WorkArea.Left);
        Assert.True(bounds.Right <= monitor.WorkArea.Right);
    }

    [Fact]
    public void ItOpensOnTheScreenItWasGiven()
    {
        // El segundo monitor, a la derecha: la ventana tiene que caer ahí y no en el primero.
        var second = Screen(1920, 0, 2560, 1440);

        var bounds = WindowPlacement.CentredIn(second);

        Assert.True(bounds.Left >= 1920, $"cayó en {bounds.Left}, fuera de la pantalla que se le dio");
    }

    // ---------- Lo que queda guardado ----------

    [Fact]
    public void WhatIsSavedComesBackTheSame()
    {
        var monitor = Screen(1920, 0, 2560, 1440);
        var bounds = new MonitorRect(2000, 100, 3160, 820);

        var stored = WindowPlacement.Of(monitor, bounds, fullscreen: true);
        var read = WindowPlacement.Read(stored.ToJson());

        Assert.NotNull(read);
        Assert.Equal(monitor.DeviceName, read.DeviceName);
        Assert.Equal(monitor.Bounds, read.MonitorRect);
        Assert.Equal(bounds, read.Bounds);
        Assert.True(read.Fullscreen);
    }

    [Fact]
    public void TheStoredNamesAreTheOnesAlreadyOnPeoplesMachines()
    {
        // Escrito a mano como lo escribió una build vieja. Si esto deja de leerse, todo el mundo
        // pierde la posición de su ventana y nada falla para avisarlo.
        const string saved = """
            {"DeviceName":"\\\\.\\DISPLAY2","MonitorLeft":1920,"MonitorTop":0,"MonitorWidth":2560,
             "MonitorHeight":1440,"Left":2000,"Top":100,"Width":1160,"Height":720,"Fullscreen":false}
            """;

        var read = WindowPlacement.Read(saved);

        Assert.NotNull(read);
        Assert.Equal("\\\\.\\DISPLAY2", read.DeviceName);
        Assert.Equal(new MonitorRect(1920, 0, 4480, 1440), read.MonitorRect);
        Assert.Equal(1160, read.Width);
        Assert.False(read.Fullscreen);
    }

    [Fact]
    public void SomethingUnreadableIsJustNoMemory()
    {
        // Un ajuste escrito por otra build es una ventana que abre centrada, no una app que no arranca.
        Assert.Null(WindowPlacement.Read(null));
        Assert.Null(WindowPlacement.Read(""));
        Assert.Null(WindowPlacement.Read("   "));
        Assert.Null(WindowPlacement.Read("no es json"));
    }
}
