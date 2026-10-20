using DigitalDeck.Core.Abstractions;
using DigitalDeck.Integrations.Ableton;
using DigitalDeck.Integrations.Ableton.Osc;
using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// Living alongside AbletonOSC.
/// </summary>
/// <remarks>
/// Live loads both Remote Scripts without complaint — Control Surface has several slots and they do
/// not compete. The operating system is the one that objects: only one process can bind a UDP port,
/// so sharing a pair leaves whichever script started second silently deaf. Nothing reports it,
/// because a script that never hears anything and a Live that is not running look identical.
/// </remarks>
public class AbletonPortTests : IDisposable
{
    private readonly string _library;

    public AbletonPortTests()
    {
        _library = Path.Combine(Path.GetTempPath(), "digitaldeck-ports", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_library);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_library)) Directory.Delete(_library, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private RemoteScriptInstaller Installer =>
        new(liveIsRunning: () => false, userLibraries: new[] { _library });

    private string Target => Path.Combine(_library, "Remote Scripts", "DigitalDeck");

    // ---------- Staying out of the way ----------

    [Fact]
    public void TheDefaultPairIsClearOfAbletonOsc()
    {
        Assert.False(AbletonPorts.Default.CollidesWithAbletonOsc);
        Assert.True(AbletonPorts.AbletonOsc.CollidesWithAbletonOsc);
    }

    [Fact]
    public void TheScriptCarriedInsideTheAppAgreesWithTheSocketTheAppOpens()
    {
        // Two constants in two languages that have to match. This is what catches one being
        // changed alone, which produces a bridge that never connects and says nothing about why.
        Assert.Equal(AbletonPorts.Default.ScriptListensOn, UdpOscTransport.DefaultSendPort);
        Assert.Equal(AbletonPorts.Default.ScriptRepliesTo, UdpOscTransport.DefaultReceivePort);
        Assert.Equal(AbletonPorts.Default, Installer.BundledPorts);
    }

    [Theory]
    [InlineData(11000, 11001)]
    [InlineData(11001, 11000)]
    [InlineData(11010, 11000)]
    [InlineData(11000, 11011)]
    public void AnyPairTouchingAbletonOscsIsFlagged(int listens, int replies)
    {
        Assert.True(new AbletonPorts(listens, replies).CollidesWithAbletonOsc);
    }

    // ---------- What a pair has to be ----------

    [Theory]
    [InlineData(80, 11011)]
    [InlineData(11010, 70000)]
    [InlineData(11010, 11010)]
    public void APairThatCouldNotWorkIsRefusedWithAReason(int listens, int replies)
    {
        Assert.NotNull(new AbletonPorts(listens, replies).Validate());
    }

    [Fact]
    public void AnOrdinaryPairIsAccepted()
    {
        Assert.Null(new AbletonPorts(11010, 11011).Validate());
        Assert.Null(AbletonPorts.AbletonOsc.Validate());
    }

    // ---------- Keeping the two halves together ----------

    [Fact]
    public void InstallingWritesTheChosenPairIntoTheScript()
    {
        var chosen = new AbletonPorts(12500, 12501);
        var installer = Installer;

        Assert.True(installer.Install(ports: chosen).Success);

        var config = File.ReadAllText(Path.Combine(Target, "config.py"));

        Assert.Contains("RECEIVE_PORT = 12500", config);
        Assert.Contains("SEND_PORT = 12501", config);
    }

    [Fact]
    public void AnInstallReportsThePairItIsOn()
    {
        var chosen = new AbletonPorts(12500, 12501);
        var installer = Installer;

        installer.Install(ports: chosen);

        Assert.Equal(chosen, installer.Inspect().InstalledPorts);
    }

    [Fact]
    public void AScriptOnOnePairAndAnAppOnAnotherIsCaughtBeforeItLooksLikeSilence()
    {
        // The whole reason the installed pair is read back off the disk. Over the wire this state
        // is invisible: the ping goes to a port nobody is bound to, so nothing ever comes back.
        var installer = Installer;
        installer.Install(ports: new AbletonPorts(12500, 12501));

        var state = installer.Inspect();

        Assert.True(state.PortsDisagreeWith(AbletonPorts.Default));
        Assert.False(state.PortsDisagreeWith(new AbletonPorts(12500, 12501)));
    }

    [Fact]
    public void InstallingWithoutSayingLeavesTheBundledPairAlone()
    {
        var installer = Installer;
        installer.Install();

        Assert.Equal(installer.BundledPorts, installer.Inspect().InstalledPorts);
    }

    // ---------- The handshake ----------

    [Fact]
    public void TheScriptSaysWhichPortsItIsOnceItIsTalking()
    {
        var transport = new FakeOscTransport();
        using var adapter = new AbletonAdapter(transport, AbletonAdapter.ManualHeartbeat);

        adapter.Start();
        transport.Receive(new OscMessage(
            "/digitaldeck/pong", AbletonAdapter.ExpectedScriptVersion, "12.3.0", "12", 11010, 11011));

        Assert.Equal(new AbletonPorts(11010, 11011), adapter.Status.Ports);
        Assert.Contains("11010", adapter.Status.Detail);
    }

    [Fact]
    public void AScriptTooOldToReportItsPortsStillConnects()
    {
        var transport = new FakeOscTransport();
        using var adapter = new AbletonAdapter(transport, AbletonAdapter.ManualHeartbeat);

        adapter.Start();
        transport.ReceivePong(AbletonAdapter.ExpectedScriptVersion);

        Assert.Equal(AbletonConnectionState.Connected, adapter.Status.State);
        Assert.Null(adapter.Status.Ports);
    }
}
