using System.Windows;
using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using Xunit;

namespace DigitalDeck.App.Tests;

/// <summary>
/// Keeping a dialog inside the screen it is on.
/// </summary>
/// <remarks>
/// The bug this replaces was a constant: the button editor shipped with MaxHeight 880, and the
/// machine it was written on has 816 usable pixels once the taskbar is taken out. Opening the
/// browser picker grew the window past the bottom of the screen and took the Save button with it.
/// <para>
/// Every case below is a screen somebody actually has. None of them can be checked by looking at
/// the one monitor in this room, which is exactly why the arithmetic lives apart from the window.
/// </para>
/// </remarks>
public class WindowFitTests
{
    /// <summary>1920×1080 at 125%, which is 1536×864 in the units WPF places windows with.</summary>
    private static Rect ScaledFullHd => new(0, 0, 1536, 816);

    private static Rect Laptop => new(0, 0, 1366, 728);

    /// <summary>A second monitor to the right, which is where this app is meant to live.</summary>
    private static Rect SecondScreen => new(1536, 0, 1920, 1040);

    [Fact]
    public void TheCeilingComesFromTheScreenRatherThanFromAConstant()
    {
        var onDesktop = WindowFit.Solve(ScaledFullHd, new Size(470, 600), new Point(500, 100));
        var onLaptop = WindowFit.Solve(Laptop, new Size(470, 600), new Point(400, 60));

        Assert.True(onDesktop.MaxHeight < ScaledFullHd.Height);
        Assert.True(onLaptop.MaxHeight < Laptop.Height);

        // The whole point: a smaller screen gets a smaller ceiling, on its own.
        Assert.True(onLaptop.MaxHeight < onDesktop.MaxHeight);

        // And both are below the 880 that used to be hard-coded.
        Assert.True(onDesktop.MaxHeight < 880);
        Assert.True(onLaptop.MaxHeight < 880);
    }

    [Fact]
    public void AWindowThatGrewPastTheTaskbarIsPulledBackUp()
    {
        // Exactly the reported bug: the editor sits at y=300 and the picker makes it 700 tall.
        var placement = WindowFit.Solve(ScaledFullHd, new Size(470, 700), new Point(500, 300));

        Assert.True(placement.Top + 700 <= ScaledFullHd.Bottom,
                    $"la ventana termina en {placement.Top + 700} y el área usable en {ScaledFullHd.Bottom}");

        Assert.True(placement.Top < 300, "tenía que subir");
    }

    [Fact]
    public void AWindowThatAlreadyFitsIsLeftWhereItIs()
    {
        // Moving a dialog that was fine would look like a glitch.
        var placement = WindowFit.Solve(ScaledFullHd, new Size(470, 400), new Point(500, 200));

        Assert.Equal(200, placement.Top);
        Assert.Equal(500, placement.Left);
    }

    [Fact]
    public void AWindowAboveTheTopOfTheScreenComesBackDown()
    {
        var placement = WindowFit.Solve(ScaledFullHd, new Size(470, 400), new Point(500, -120));

        Assert.True(placement.Top >= ScaledFullHd.Top);
    }

    [Fact]
    public void OnASecondMonitorTheWindowIsKeptInsideThatOneAndNotTheFirst()
    {
        // A usable area that does not start at zero is the normal case for this app, and the
        // reason SystemParameters.WorkArea was not good enough: it only ever describes the primary.
        var placement = WindowFit.Solve(SecondScreen, new Size(470, 900), new Point(1800, 400));

        Assert.True(placement.Top >= SecondScreen.Top);
        Assert.True(placement.Top + 900 <= SecondScreen.Bottom);
        Assert.True(placement.Left >= SecondScreen.Left);
        Assert.True(placement.Left + 470 <= SecondScreen.Right);
    }

    [Fact]
    public void ATaskbarDownTheSideIsRespectedToo()
    {
        // Docking the taskbar left moves the usable area's left edge, not its top.
        var sideBar = new Rect(72, 0, 1464, 864);

        var placement = WindowFit.Solve(sideBar, new Size(470, 400), new Point(0, 100));

        Assert.True(placement.Left >= sideBar.Left);
    }

    [Fact]
    public void AScreenTooSmallForTheDialogStillLeavesTheTitleVisible()
    {
        // Something has to give on a 600px-tall screen. Losing the bottom row beats losing the
        // title and the way to close the window.
        var tiny = new Rect(0, 0, 1024, 600);

        var placement = WindowFit.Solve(tiny, new Size(470, 900), new Point(100, 400));

        Assert.True(placement.Top >= tiny.Top);
        Assert.True(placement.MaxHeight <= tiny.Height);
    }

    [Fact]
    public void AWindowThatHasNotBeenMeasuredYetIsNotMoved()
    {
        // Before the first layout pass there is no size to place, and guessing would throw the
        // window somewhere arbitrary on the way up.
        var placement = WindowFit.Solve(ScaledFullHd, new Size(double.NaN, double.NaN), new Point(500, 300));

        Assert.True(double.IsNaN(placement.Top));
        Assert.True(double.IsNaN(placement.Left));

        // The ceiling is still worth setting, since it does not depend on the current size.
        Assert.True(placement.MaxHeight > 0);
    }
}
