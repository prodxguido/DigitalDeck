using System.Windows;
using System.Windows.Interop;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Integrations.Windows;
using Xunit;

namespace DigitalDeck.App.Tests;

/// <summary>
/// Claiming a key combination from the whole system.
/// </summary>
/// <remarks>
/// Against the real Windows call, with a real window, because the parts worth checking are exactly
/// the ones a fake would invent: that a combination nobody else wants is granted, that a second
/// claim on the same one is refused with something a person can read, and that giving it back
/// frees it. Everything here releases what it claims — a test that leaves a hotkey registered
/// takes it away from the machine it ran on.
/// <para>
/// The combination is deliberately absurd. Ctrl+Alt+Shift+F24 is not a key most keyboards even
/// have, which is what keeps this from colliding with whatever the person running the tests has
/// bound.
/// </para>
/// </remarks>
public sealed class GlobalHotkeyTests : IClassFixture<WpfThread>
{
    private static readonly HotkeySpec Absurd = Parse("Ctrl+Alt+Shift+F24");

    private readonly WpfThread _ui;

    public GlobalHotkeyTests(WpfThread ui) => _ui = ui;

    private static HotkeySpec Parse(string text) =>
        HotkeySpec.TryParse(text, out var spec) ? spec : throw new ArgumentException(text);

    /// <summary>Runs the body with a hotkey already tied to a real window, and cleans both up.</summary>
    private T WithHotkey<T>(Func<IGlobalHotkey, Window, T> body)
    {
        return _ui.Run(() =>
        {
            var window = new Window { Width = 100, Height = 100, ShowInTaskbar = false };
            var hotkey = new WindowsGlobalHotkey();

            try
            {
                window.Show();
                hotkey.Attach(new WindowInteropHelper(window).EnsureHandle());

                return body(hotkey, window);
            }
            finally
            {
                hotkey.Dispose();
                window.Close();
            }
        });
    }

    [Fact]
    public void ACombinationNobodyElseWantsIsGranted()
    {
        var (claimed, problem, current) = WithHotkey((hotkey, _) =>
        {
            var ok = hotkey.TryRegister(Absurd, out var why);
            return (ok, why, hotkey.Current);
        });

        Assert.True(claimed, $"no se pudo reservar {Absurd}: {problem}");
        Assert.Equal(string.Empty, problem);
        Assert.Equal(Absurd, current);
    }

    [Fact]
    public void ACombinationSomebodyElseHasIsRefusedWithSomethingLegible()
    {
        var problem = WithHotkey((mine, window) =>
        {
            Assert.True(mine.TryRegister(Absurd, out _));

            // A second claim on the same window and the same combination is what Windows refuses.
            var other = new WindowsGlobalHotkey();
            other.Attach(new WindowInteropHelper(window).Handle);

            try
            {
                Assert.False(other.TryRegister(Absurd, out var why));
                Assert.Null(other.Current);
                return why;
            }
            finally
            {
                other.Dispose();
            }
        });

        // The message goes to the status bar as it is, so it has to name the combination and say
        // what to do about it.
        Assert.Contains(Absurd.ToString(), problem);
        Assert.Contains("otra combinación", problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GivingItBackFreesItForSomebodyElse()
    {
        var reclaimed = WithHotkey((mine, window) =>
        {
            Assert.True(mine.TryRegister(Absurd, out _));
            mine.Unregister();

            Assert.Null(mine.Current);

            var other = new WindowsGlobalHotkey();
            other.Attach(new WindowInteropHelper(window).Handle);

            try
            {
                return other.TryRegister(Absurd, out _);
            }
            finally
            {
                other.Dispose();
            }
        });

        Assert.True(reclaimed, "el atajo quedó reservado después de devolverlo");
    }

    [Fact]
    public void WithNoWindowThereIsNothingToRegisterAgainst()
    {
        var hotkey = new WindowsGlobalHotkey();

        try
        {
            Assert.False(hotkey.TryRegister(Absurd, out var problem));
            Assert.NotEmpty(problem);
        }
        finally
        {
            hotkey.Dispose();
        }
    }

    [Fact]
    public void AMessageThatIsNotTheHotkeyIsLeftAlone()
    {
        var hotkey = new WindowsGlobalHotkey();

        try
        {
            var fired = 0;
            hotkey.Pressed += () => fired++;

            // 0x0100 is WM_KEYDOWN: an ordinary keystroke on the way past.
            Assert.False(hotkey.OnHostMessage(0x0100, 1));
            Assert.Equal(0, fired);
        }
        finally
        {
            hotkey.Dispose();
        }
    }

    [Fact]
    public void TheHotkeyMessageRaisesPressedAndIsClaimed()
    {
        var hotkey = new WindowsGlobalHotkey();

        try
        {
            var fired = 0;
            hotkey.Pressed += () => fired++;

            // 0x0312 is WM_HOTKEY, and 1 is the id this claims. Fed by hand rather than by pressing
            // the keys, which no test can do — what is checked is the routing, not Windows.
            Assert.True(hotkey.OnHostMessage(0x0312, 1));
            Assert.Equal(1, fired);
        }
        finally
        {
            hotkey.Dispose();
        }
    }
}
