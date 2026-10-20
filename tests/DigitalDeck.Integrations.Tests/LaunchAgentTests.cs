using DigitalDeck.Integrations.Startup;
using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// The plist that asks macOS to open the deck at login.
/// </summary>
/// <remarks>
/// Worth pinning because launchd does not argue: a plist it does not like is ignored, not
/// rejected, and the app simply never opens at login with nothing anywhere to say why. Round
/// tripping it here is the only feedback available from a machine that is not a Mac.
/// </remarks>
public class LaunchAgentTests
{
    private const string Executable = "/Applications/DigitalDeck.app/Contents/MacOS/DigitalDeck";

    [Fact]
    public void ThePlistNamesTheProgramItOpens()
    {
        Assert.Equal(Executable, LaunchAgent.ProgramIn(LaunchAgent.Plist(Executable)));
    }

    [Fact]
    public void ThePlistCarriesTheLabelLaunchdKnowsTheJobBy()
    {
        Assert.Contains(LaunchAgent.Label, LaunchAgent.Plist(Executable));
    }

    [Fact]
    public void ItAsksToRunAtLoginAndNothingMore()
    {
        var plist = LaunchAgent.Plist(Executable);

        Assert.Contains("RunAtLoad", plist);

        // KeepAlive would have launchd reopen the deck every time somebody closes it, which is not
        // what "open at login" means to a person.
        Assert.DoesNotContain("KeepAlive", plist);
    }

    [Fact]
    public void ItIsARealPropertyListAndSaysSo()
    {
        var plist = LaunchAgent.Plist(Executable);

        Assert.Contains("<!DOCTYPE plist", plist);
        Assert.Contains("apple.com/DTDs/PropertyList-1.0.dtd", plist);
    }

    [Fact]
    public void APathWithSpacesSurvivesTheRoundTrip()
    {
        // "Application Support" and "Mi música" are both normal on a Mac, and a plist is XML: the
        // one thing that must not happen is the path coming back split or escaped wrong.
        const string awkward = "/Users/guido/Mi música & cosas/DigitalDeck.app/Contents/MacOS/DigitalDeck";

        Assert.Equal(awkward, LaunchAgent.ProgramIn(LaunchAgent.Plist(awkward)));
    }

    [Fact]
    public void SomethingThatIsNotOursReadsAsUnknownRatherThanThrowing()
    {
        // A plist somebody edited by hand is their business; the honest answer is "I cannot tell
        // you what this points at", which is what the settings screen then shows.
        Assert.Null(LaunchAgent.ProgramIn("no es xml"));
        Assert.Null(LaunchAgent.ProgramIn("<plist><dict><key>Otra</key><string>cosa</string></dict></plist>"));
        Assert.Null(LaunchAgent.ProgramIn(string.Empty));
    }

    // ---------- Writing and removing the file ----------

    private static (MacStartupRegistration Registration, string Path) InATempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "digitaldeck-launchagent", Guid.NewGuid().ToString("n"));
        var plist = Path.Combine(folder, LaunchAgent.Label + ".plist");

        return (new MacStartupRegistration(plist, () => Executable), plist);
    }

    [Fact]
    public void TurningItOnWritesThePlistAndTurningItOffRemovesIt()
    {
        var (registration, path) = InATempFolder();

        try
        {
            Assert.False(registration.Read().Enabled);

            var on = registration.Set(true);

            Assert.True(on.Enabled);
            Assert.Equal(Executable, on.Command);
            Assert.True(File.Exists(path), "el plist tendría que existir");

            // Read back from disk rather than from what Set answered, which is the question the
            // settings screen actually asks on every launch.
            Assert.True(registration.Read().Enabled);
            Assert.Equal(Executable, registration.Read().Command);

            Assert.False(registration.Set(false).Enabled);
            Assert.False(File.Exists(path), "el plist tendría que haberse borrado");
        }
        finally
        {
            var folder = Path.GetDirectoryName(path);

            if (folder is { Length: > 0 } && Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TurningItOffWhenItWasNeverOnIsNotAnError()
    {
        var (registration, _) = InATempFolder();

        var state = registration.Set(false);

        Assert.False(state.Enabled);
        Assert.Null(state.Problem);
    }

    [Fact]
    public void APlistLeftPointingAtAnOldCopyIsNoticed()
    {
        // The same question the Windows registry version answers: the deck was moved or reinstalled
        // somewhere else, and the entry still opens whatever used to be there.
        var (registration, path) = InATempFolder();

        try
        {
            registration.Set(true);

            Assert.True(registration.Read().PointsElsewhere("/Applications/OtroDeck.app"));
            Assert.False(registration.Read().PointsElsewhere(Executable));
        }
        finally
        {
            var folder = Path.GetDirectoryName(path);

            if (folder is { Length: > 0 } && Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }
}
