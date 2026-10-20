using DigitalDeck.Core.Model;
using DigitalDeck.Core.Services;
using Xunit;

namespace DigitalDeck.Tests;

/// <summary>
/// Which of today's displays is the one the deck was left on.
/// </summary>
/// <remarks>
/// This logic used to live inside the Win32 monitor code, where it could only be exercised by
/// plugging and unplugging real screens. It is the part of multi-monitor that actually has
/// decisions in it, and every one of those decisions is about a desk that changed since last time.
/// </remarks>
public class MonitorMatchTests
{
    private static MonitorInfo Screen(
        string name, int left, int top, int width, int height, bool primary = false) =>
        new(name,
            new MonitorRect(left, top, left + width, top + height),
            new MonitorRect(left, top, left + width, top + height - 40),
            primary);

    private static readonly MonitorInfo Laptop = Screen("\\\\.\\DISPLAY1", 0, 0, 1920, 1080, primary: true);
    private static readonly MonitorInfo Second = Screen("\\\\.\\DISPLAY2", 1920, 0, 2560, 1440);

    private static readonly IReadOnlyList<MonitorInfo> Desk = new[] { Laptop, Second };

    [Fact]
    public void TheSameSlotWithTheSameGeometryIsTheSameScreen()
    {
        var found = MonitorMatch.FindBest(Desk, Second.DeviceName, Second.Bounds);

        Assert.Same(Second, found);
    }

    [Fact]
    public void ASlotThatNowHoldsADifferentScreenIsNotTrusted()
    {
        // DISPLAY2 exists, but it is not the 2560x1440 the deck was left on: Windows renumbered the
        // slots and something else landed in it. Nothing else matches either, so it opens on the
        // primary rather than on the wrong screen.
        var found = MonitorMatch.FindBest(Desk, Second.DeviceName, new MonitorRect(1920, 0, 3200, 1024));

        Assert.Same(Laptop, found);
    }

    [Fact]
    public void AScreenSittingExactlyWhereTheOldOneWasIsTakenEvenIfItIsCalledSomethingElse()
    {
        var found = MonitorMatch.FindBest(Desk, "\\\\.\\DISPLAY9", Second.Bounds);

        Assert.Same(Second, found);
    }

    [Fact]
    public void AScreenOfTheSameSizeSomewhereElseIsTheNextBestThing()
    {
        // The desk was rearranged: the same monitor is now to the left instead of to the right.
        var moved = Screen("\\\\.\\DISPLAY7", -2560, 0, 2560, 1440);
        var desk = new[] { Laptop, moved };

        var found = MonitorMatch.FindBest(desk, "\\\\.\\DISPLAY2", Second.Bounds);

        Assert.Same(moved, found);
    }

    [Fact]
    public void AScreenThatIsGoneFallsBackToThePrimaryRatherThanToNothing()
    {
        // The whole point: a window restored onto a monitor that is not there is invisible, and
        // invisible is worse than in the wrong place.
        var found = MonitorMatch.FindBest(
            new[] { Laptop }, Second.DeviceName, Second.Bounds);

        Assert.Same(Laptop, found);
    }

    [Fact]
    public void WithNothingRememberedItIsThePrimary()
    {
        Assert.Same(Laptop, MonitorMatch.FindBest(Desk, null, null));
    }

    [Fact]
    public void WithNoScreensAtAllThereIsNoAnswer()
    {
        Assert.Null(MonitorMatch.FindBest(Array.Empty<MonitorInfo>(), "\\\\.\\DISPLAY1", null));
        Assert.Null(MonitorMatch.Primary(Array.Empty<MonitorInfo>()));
    }

    [Fact]
    public void WithNoPrimaryFlaggedTheFirstScreenStandsIn()
    {
        var odd = new[] { Second };

        Assert.Same(Second, MonitorMatch.Primary(odd));
    }
}
