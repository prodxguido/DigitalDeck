using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Integrations.Windows;
using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// Live's factory shortcuts for the things its API cannot do — Export, Save, Save As.
/// </summary>
/// <remarks>
/// Since the raising-and-typing half moved to <see cref="WindowsHotkeySender"/>, all that is left
/// here is the mapping, and that is worth pinning: these three combinations are the whole contract
/// with Live, and a wrong one is a button that saves when it was asked to export.
/// </remarks>
public class WindowsLiveKeyboardTests
{
    /// <summary>A sender that records rather than touching any window.</summary>
    private sealed class RecordingSender : IHotkeySender
    {
        public HotkeySpec? Keys { get; private set; }

        public string? Program { get; private set; }

        public Task SendAsync(HotkeySpec keys, string program, CancellationToken ct = default)
        {
            Keys = keys;
            Program = program;
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData(LiveShortcut.ExportAudioVideo, "Ctrl+Shift+R")]
    [InlineData(LiveShortcut.SaveSet, "Ctrl+S")]
    [InlineData(LiveShortcut.SaveSetAs, "Ctrl+Shift+S")]
    public async Task EachShortcutIsLivesFactoryCombination(LiveShortcut shortcut, string expected)
    {
        var sender = new RecordingSender();

        await new WindowsLiveKeyboard(sender).SendAsync(shortcut);

        Assert.Equal(expected, sender.Keys?.ToString());
    }

    [Fact]
    public async Task TheKeysGoToLiveAndNotToWhateverIsInFront()
    {
        var sender = new RecordingSender();

        await new WindowsLiveKeyboard(sender).SendAsync(LiveShortcut.SaveSet);

        Assert.Equal(WindowsLiveKeyboard.ProcessPrefix, sender.Program);
    }
}
