using DigitalDeck.Core.Model;
using DigitalDeck.Integrations.Windows;
using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// Pressing a key combination inside another program.
/// </summary>
/// <remarks>
/// The Win32 halves (find the window, send the keys) are swapped for fakes: what matters here is
/// the order — the program has to be found and raised before a keystroke goes anywhere — and that
/// a program that is not open becomes a message rather than a dropped press.
/// </remarks>
public class WindowsHotkeySenderTests
{
    private static readonly HotkeySpec Chord =
        new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 'E');

    private static WindowsHotkeySender Sender(
        Func<string, IntPtr?> findWindow,
        Func<HotkeySpec, CancellationToken, Task> pressKeys) =>
        new(findWindow, pressKeys, foregroundTimeout: TimeSpan.FromMilliseconds(50));

    [Fact]
    public async Task WithTheProgramClosedItSaysSoInsteadOfPressingKeys()
    {
        var pressed = false;

        var sender = Sender(
            findWindow: _ => null,
            pressKeys: (_, _) => { pressed = true; return Task.CompletedTask; });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendAsync(Chord, "Photoshop"));

        Assert.Contains("Photoshop", error.Message);
        Assert.False(pressed);
    }

    [Fact]
    public async Task TheProgramItLooksForIsTheOneTheButtonNames()
    {
        string? asked = null;

        var sender = Sender(
            findWindow: name => { asked = name; return null; },
            pressKeys: (_, _) => Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendAsync(Chord, "  Ableton Live  "));

        // Trimmed, because a name typed into a text box usually is not.
        Assert.Equal("Ableton Live", asked);
    }

    [Fact]
    public async Task AButtonWithNoKeysIsRefusedBeforeAnyWindowIsTouched()
    {
        var looked = false;

        var sender = Sender(
            findWindow: _ => { looked = true; return null; },
            pressKeys: (_, _) => Task.CompletedTask);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendAsync(default, "Photoshop"));

        Assert.False(looked);
    }

    [Fact]
    public async Task AButtonWithNoProgramIsRefusedToo()
    {
        // Without a target the chord would land on the deck, which is what took the focus when the
        // tile was touched.
        var sender = Sender(
            findWindow: _ => new IntPtr(1234),
            pressKeys: (_, _) => Task.CompletedTask);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendAsync(Chord, "   "));

        Assert.Contains("programa", error.Message);
    }

    [Fact]
    public async Task AWindowThatWasFoundIsNeverReportedAsMissing()
    {
        HotkeySpec? sent = null;

        var sender = Sender(
            findWindow: _ => new IntPtr(1234),
            pressKeys: (keys, _) => { sent = keys; return Task.CompletedTask; });

        // The foreground check inside SendAsync cannot pass against a fake handle, so a failure to
        // *raise* the window is fine — what this asserts is that a found window is never reported
        // as "no encontré".
        try
        {
            await sender.SendAsync(Chord, "Photoshop");
        }
        catch (InvalidOperationException ex)
        {
            Assert.DoesNotContain("No encontré", ex.Message);
            return;
        }

        Assert.Equal(Chord, sent);
    }
}
