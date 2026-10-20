using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Layout;
using DigitalDeck.ViewModels;

namespace DigitalDeck.Avalonia;

public partial class App : Application
{
    /// <summary>The deck, already built by <see cref="Program"/> against the real database.</summary>
    public static MainViewModel? Deck { get; set; }

    /// <summary>Where the window's position is remembered. Set by <see cref="Program"/>.</summary>
    public static IAppSettingsStore? Settings { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow { DataContext = Deck };

            Remember(window);

            // El deck se carga cuando la ventana ya existe: la primera vez pregunta si se va a
            // usar Live, y una pregunta necesita una ventana en la que abrirse.
            window.Opened += async (_, _) =>
            {
                if (Deck is { } deck) await deck.InitializeAsync();
            };

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Puts the window back where it was, and writes down where it ends up.
    /// </summary>
    /// <remarks>
    /// Restored once the window has a handle — before that it is on no screen and Avalonia has
    /// nothing to answer about monitors. Saved on close rather than on every move: a deck that is
    /// dragged around while somebody finds the right spot should write one setting, not fifty.
    /// </remarks>
    private static void Remember(Window window)
    {
        if (Settings is not { } settings) return;

        window.Opened += (_, _) =>
        {
            var saved = WindowPlacement.Read(settings.GetAsync(SettingKeys.WindowPlacement).GetAwaiter().GetResult());

            if (AvaloniaPlacement.Restore(window, saved)) window.WindowState = WindowState.FullScreen;
        };

        window.Closing += (_, _) =>
        {
            if (AvaloniaPlacement.Capture(window) is not { } placement) return;

            try
            {
                settings.SetAsync(SettingKeys.WindowPlacement, placement.ToJson()).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // Losing the remembered position is not a reason to block the app from closing.
            }
        };
    }
}
