using Avalonia;
using DigitalDeck.Core;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Services;
using DigitalDeck.Data;
using DigitalDeck.Data.Packs;
using DigitalDeck.Integrations.Ableton;
using DigitalDeck.Integrations.Ableton.Osc;
using DigitalDeck.Integrations.Shell;
using DigitalDeck.Integrations.Startup;
using DigitalDeck.Integrations.Web;
using DigitalDeck.ViewModels;

namespace DigitalDeck.Avalonia;

/// <summary>
/// Builds the deck against the real database and hands it to Avalonia.
/// </summary>
/// <remarks>
/// Deliberately by hand rather than through a container: what matters about this shell is which
/// pieces it needs, and a list of fourteen arguments says that better than a registration list. Ten
/// of them are the same objects the WPF app builds — the repository, the settings, the icons, the
/// deck's own view model — and the four that are not are the ones named "No…" in
/// <see cref="Shell"/>: exactly the surface still to port.
/// </remarks>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var paths = new DeckPaths(RootFrom(args));
        paths.EnsureCreated();

        var database = new DeckDatabase(paths);
        database.Migrate();

        var repository = new SqliteDeckRepository(database);
        var settings = new SqliteSettingsStore(database);
        var usage = new SqliteUsageTracker(database);
        var icons = new IconStore(paths);

        // El puente con Live, de verdad. Es el mismo AbletonAdapter sobre el mismo socket UDP que
        // usa la app de WPF: desde que DigitalDeck.Integrations dejo de ser net8.0-windows, no hay
        // nada que reimplementar de este lado.
        var ports = new AbletonPortSettings(settings);
        ports.LoadAsync().GetAwaiter().GetResult();

        var transport = new UdpOscTransport(
            ports.Current.ScriptRepliesTo, ports.Current.ScriptListensOn);

        var ableton = new AbletonAdapter(transport);

        var installer = new RemoteScriptInstaller();
        var dialogs = new AvaloniaDialogs(() => ableton, () => ableton, () => installer);

        App.Deck = Build(repository, settings, usage, icons, ableton, installer, dialogs);

        App.Settings = settings;

        // Seeded the same way the WPF app seeds a first run, so an empty deck opens with something
        // on it rather than with a blank grid that says nothing about whether this works.
        new DeckSeeder(repository, settings).SeedIfEmptyAsync().GetAwaiter().GetResult();

        // Empieza a buscar Live en segundo plano, igual que la app de WPF. Que Live este cerrado
        // no es un error: el indicador lo dice y el deck anda igual.
        ableton.Start();

        if (args.Contains("--smoke"))
        {
            // Started by a test or a script that only wants to know the stack came up: report what
            // was loaded and leave, instead of opening a window nobody is there to close. The
            // dialogs are swapped for silent ones because there is no UI for one to open into.
            App.Deck = Build(repository, settings, usage, icons, ableton, installer, new SilentDialogs());

            App.Deck.InitializeAsync().GetAwaiter().GetResult();

            Console.WriteLine($"páginas: {App.Deck.Pages.Count}, botones: {App.Deck.Items.Count}");

            return App.Deck.Items.Count > 0 ? 0 : 1;
        }

        // InitializeAsync no se llama acá: la primera vez pregunta si se va a usar Live, y una
        // pregunta necesita una ventana. App la hace cuando la ventana ya existe.
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>The deck, wired to everything it needs.</summary>
    private static MainViewModel Build(
        IDeckRepository repository,
        IAppSettingsStore settings,
        IUsageTracker usage,
        IconStore icons,
        AbletonAdapter ableton,
        IRemoteScriptInstaller installer,
        IDeckDialogs dialogs) =>
        new(repository,
            new ActionDispatcher(
            [
                new OpenPathHandler(),
                new OpenUrlHandler(),
                new AbletonActionHandler(ableton, new NoLiveKeyboard()),
                new AbletonLoadDeviceHandler(ableton),
                new MixerPanelHandler(dialogs),
            ]),
            settings,
            dialogs,
            new DeckPackService(repository, icons),
            icons,
            ableton,
            ableton,
            new AvaloniaUiThread(),
            installer,
            usage,
            new FrequentFolderService(repository, settings, usage),
            Startup(),
            new TemplateLibrary());

    /// <summary>
    /// How this desktop is asked to open the deck at login.
    /// </summary>
    /// <remarks>
    /// A real one on macOS, which is where this shell is going. On anything else it says no rather
    /// than pretending: the Windows implementation is a registry key and lives in the Windows-only
    /// project, which this one deliberately does not reference — running the Avalonia shell on
    /// Windows is for developing it, not for using it.
    /// </remarks>
    private static IStartupRegistration Startup() =>
        OperatingSystem.IsMacOS() ? new MacStartupRegistration() : new NoStartup();

    /// <summary>
    /// Which deck to open: the user's own, or one passed in with <c>--root</c>.
    /// </summary>
    /// <remarks>
    /// The override is what lets this be run against a throwaway folder. Pointing a half-ported
    /// shell at somebody's real deck to see whether it draws is not a risk worth taking.
    /// </remarks>
    private static string? RootFrom(string[] args)
    {
        var at = Array.IndexOf(args, "--root");

        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
