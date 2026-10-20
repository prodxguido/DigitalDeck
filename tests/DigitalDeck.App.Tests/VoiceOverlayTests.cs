using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using DigitalDeck.App.Views;
using DigitalDeck.Core;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Voice;
using DigitalDeck.Integrations.Voice;
using DigitalDeck.Integrations.Windows;
using Xunit;

namespace DigitalDeck.App.Tests;

/// <summary>
/// The one property of the overlay that matters more than how it looks: it must never take focus.
/// </summary>
/// <remarks>
/// A status popup that activates itself pulls the user out of Ableton every single time they speak,
/// costing them their selection and their place. That would make the assistant worse than useless —
/// so the extended style is asserted on the real window handle rather than trusted to the XAML,
/// because <c>ShowActivated</c> alone governs only the first show and nothing after it.
/// </remarks>
public sealed class VoiceOverlayTests : IClassFixture<WpfThread>
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;

    private readonly WpfThread _ui;

    public VoiceOverlayTests(WpfThread ui) => _ui = ui;

    [Fact]
    public void TheOverlayNeverTakesFocusFromWhateverIsInFront()
    {
        var style = _ui.Run(() =>
        {
            var overlay = new VoiceOverlayWindow(new WindowsWindowEffects());

            try
            {
                overlay.Render(new VoiceAssistantStatus(VoiceAssistantState.Listening, "Escuchando…"));

                return GetWindowLong(new WindowInteropHelper(overlay).Handle, GwlExStyle);
            }
            finally
            {
                overlay.Close();
            }
        });

        Assert.True((style & WsExNoActivate) != 0, "Le falta WS_EX_NOACTIVATE: le robaría el foco a Live.");
        Assert.True((style & WsExTransparent) != 0, "Le falta WS_EX_TRANSPARENT: los clics no pasarían.");
        Assert.True((style & WsExToolWindow) != 0, "Le falta WS_EX_TOOLWINDOW: aparecería en Alt+Tab.");
    }

    /// <remarks>
    /// A miss shows the transcript, because seeing what it thought you said is the whole
    /// explanation. A success does not, because the button's name has already said everything.
    /// </remarks>
    [Fact]
    public void AMissShowsWhatItThoughtItHeardAndASuccessDoesNot()
    {
        var (onMiss, onSuccess) = _ui.Run(() =>
        {
            var overlay = new VoiceOverlayWindow(new WindowsWindowEffects());

            try
            {
                overlay.Render(new VoiceAssistantStatus(
                    VoiceAssistantState.Missed, "No encontré nada.", "pro que cuatro"));

                var miss = overlay.Heard.Visibility;

                overlay.Render(new VoiceAssistantStatus(
                    VoiceAssistantState.Understood, "Pro-Q 4", "pro q cuatro"));

                return (miss, overlay.Heard.Visibility);
            }
            finally
            {
                overlay.Close();
            }
        });

        Assert.Equal(Visibility.Visible, onMiss);
        Assert.Equal(Visibility.Collapsed, onSuccess);
    }

    /// <remarks>
    /// Cheap, and it catches the mistake these windows are most likely to contain: a resource key
    /// that does not exist throws at parse time, in front of the user, the first time they open it.
    /// Constructing is enough — <c>InitializeComponent</c> is what resolves every
    /// <c>StaticResource</c> — and it stops short of the microphone that showing it would open.
    /// </remarks>
    [Fact]
    public void TheSettingsWindowParsesAgainstTheRealTheme()
    {
        var parsed = _ui.Run(() =>
        {
            using var voice = new VoiceService(
                new NoSettings(), new DeckPaths(Path.GetTempPath()), new WindowsMonitors(), new WindowsWindowEffects(), new WindowsGlobalHotkey(),
                new EmbeddedVoiceModelInstaller());

            var window = new VoiceSettingsWindow(voice);
            window.Close();

            return true;
        });

        Assert.True(parsed);
    }

    /// <summary>A settings store that remembers nothing, for a window that is never shown.</summary>
    private sealed class NoSettings : IAppSettingsStore
    {
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>(null);

        public Task SetAsync(string key, string value, CancellationToken ct = default) => Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);
}
