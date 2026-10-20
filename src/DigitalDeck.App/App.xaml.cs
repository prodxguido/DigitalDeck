using System.Windows;
using System.Windows.Threading;
using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using DigitalDeck.Core;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Services;
using DigitalDeck.Data;
using DigitalDeck.Data.Packs;
using DigitalDeck.Integrations.Ableton;
using DigitalDeck.Integrations.Ableton.Osc;
using DigitalDeck.Integrations.Shell;
using DigitalDeck.Integrations.Voice;
using DigitalDeck.Integrations.Web;
using DigitalDeck.Integrations.Keyboard;
using DigitalDeck.Integrations.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalDeck.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnUnhandledException;

        var services = new ServiceCollection();
        ConfigureServices(services);
        // An isolated data directory is useful for previews and integration checks.
        var dataArgument = Array.IndexOf(e.Args, "--data-dir");
        if (dataArgument >= 0 && dataArgument + 1 < e.Args.Length)
            services.AddSingleton(new DeckPaths(System.IO.Path.GetFullPath(e.Args[dataArgument + 1])));
        _services = services.BuildServiceProvider();

        try
        {
            _services.GetRequiredService<DeckDatabase>().Migrate();
            var settings = _services.GetRequiredService<IAppSettingsStore>();
            if (await settings.GetAsync(SettingKeys.StarterDeckSuppressed) != "1")
                await _services.GetRequiredService<DeckSeeder>().SeedIfEmptyAsync();
            if (e.Args.Contains("--skip-setup"))
                await settings.SetAsync(SettingKeys.OnboardingSeen, "1");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"No se pudo preparar la base de datos del deck.\n\n{ex.Message}",
                "DigitalDeck", MessageBoxButton.OK, MessageBoxImage.Error);

            Shutdown(1);
            return;
        }

        // Before the transport is resolved, or it would bind the default pair and ignore whatever
        // the user configured.
        await _services.GetRequiredService<AbletonPortSettings>().LoadAsync();

        // Starts looking for Live in the background. A failure here is not fatal — the deck works
        // perfectly well with Ableton closed, and the indicator explains what is missing.
        _services.GetRequiredService<IAbletonClient>().Start();

        var window = _services.GetRequiredService<Views.MainWindow>();
        MainWindow = window;
        window.Show();

        if (!e.Args.Contains("--skip-setup"))
        {
            // La configuración del Remote Script queda disponible desde el indicador de Ableton
            // y el menú de ajustes, pero no interrumpe el arranque cada vez que Live está cerrado
            // o tiene una versión distinta instalada.
            await OfferToInstallTheVoiceAssistantAsync();
        }
    }

    /// <summary>
    /// On the first run, asks whether to unpack the speech model that ships inside the app.
    /// </summary>
    /// <remarks>
    /// Asked once. A "no" is remembered so it does not come back every launch — the install button
    /// in the voice settings stays there for whenever the user changes their mind. Skipped entirely
    /// when the model is already unpacked, which is the case on the machine the app was built on.
    /// </remarks>
    private async Task OfferToInstallTheVoiceAssistantAsync()
    {
        if (_services is null) return;

        try
        {
            var settings = _services.GetRequiredService<IAppSettingsStore>();

            if (await settings.GetAsync(SettingKeys.VoiceOnboardingAsked) == "1") return;

            var voice = _services.GetRequiredService<VoiceService>();

            if (!voice.CanInstallModel) return;

            await settings.SetAsync(SettingKeys.VoiceOnboardingAsked, "1");

            var wants = _services.GetRequiredService<IDeckDialogs>().Confirm(
                "Asistente de voz",
                "DigitalDeck puede apretar un botón cuando decís su nombre en voz alta, sin que nada " +
                "del audio salga de esta máquina.\n\n¿Instalás el asistente de voz ahora? Podés hacerlo " +
                "más tarde desde Ajustes de voz.");

            if (wants) await voice.InstallModelAsync();
        }
        catch (Exception)
        {
            // The deck works perfectly well with no voice assistant; nothing here fails a startup.
        }
    }

    /// <summary>
    /// Puts the setup screen in front of anyone whose bridge cannot possibly work yet.
    /// </summary>
    /// <remarks>
    /// Only when the script is missing or out of date, and only after the main window exists so the
    /// dialog has something to centre on. Without this the first run of a copied .exe is a red
    /// indicator saying to pick DigitalDeck in Control Surface, where it will never appear — the
    /// one failure in this app with no visible cause and no way out from inside it.
    /// </remarks>
    private void OfferToInstallTheRemoteScript()
    {
        if (_services is null) return;

        try
        {
            var installer = _services.GetRequiredService<IRemoteScriptInstaller>();

            if (!installer.Inspect().NeedsAttention) return;

            _services.GetRequiredService<IDeckDialogs>().ShowRemoteScriptSetup();
        }
        catch (Exception)
        {
            // The deck works perfectly well with Ableton out of the picture; nothing here is worth
            // failing a startup over.
        }
    }

    /// <summary>
    /// The container the app runs on.
    /// </summary>
    /// <remarks>
    /// Internal rather than private so a test can check the wiring. A service nobody registered is
    /// a crash on launch and nowhere else — the compiler is happy with it right up until the window
    /// that needs it is asked for.
    /// </remarks>
    internal static void ConfigureServices(IServiceCollection services)
    {
        // Storage
        services.AddSingleton(new DeckPaths());
        services.AddSingleton<DeckDatabase>();
        services.AddSingleton<IDeckRepository, SqliteDeckRepository>();
        services.AddSingleton<IAppSettingsStore, SqliteSettingsStore>();
        services.AddSingleton<IconStore>();
        services.AddSingleton<DeckPackService>();
        services.AddSingleton<TemplateLibrary>();
        services.AddSingleton<DeckSeeder>();
        services.AddSingleton<IUsageTracker, SqliteUsageTracker>();
        services.AddSingleton<FrequentFolderService>();

        // Ableton bridge. The transport is loopback-only UDP; the adapter turns it into the
        // connection state the indicator shows and the actions a button can fire.
        services.AddSingleton<AbletonPortSettings>();

        // Resolved lazily, which matters: by the time anything asks for the transport, startup has
        // already loaded the stored pair into AbletonPortSettings.
        services.AddSingleton<IOscTransport>(provider =>
        {
            var ports = provider.GetRequiredService<AbletonPortSettings>().Current;

            return new UdpOscTransport(ports.ScriptRepliesTo, ports.ScriptListensOn);
        });
        services.AddSingleton<IRemoteScriptInstaller>(_ => new RemoteScriptInstaller());
        // Registered by its concrete type and then handed out twice: the mixer's push subscription
        // and the deck's commands travel over one socket, and two adapters would each be deaf to
        // half of what came back on it.
        services.AddSingleton(provider => new AbletonAdapter(provider.GetRequiredService<IOscTransport>()));
        services.AddSingleton<IAbletonClient>(provider => provider.GetRequiredService<AbletonAdapter>());
        services.AddSingleton<IAbletonMixer>(provider => provider.GetRequiredService<AbletonAdapter>());
        services.AddSingleton<IAbletonSong>(provider => provider.GetRequiredService<AbletonAdapter>());

        // Export / Save / Save As are not on Live's API, so they are pressed as keyboard shortcuts
        // straight into Live's window — no bridge, works for any user with Live open.
        services.AddSingleton<IHotkeySender, WindowsHotkeySender>();
        services.AddSingleton<ILiveKeyboard, WindowsLiveKeyboard>();

        // Action handlers. Adding a new kind of button means registering one more of these —
        // nothing else in the app needs to change.
        services.AddSingleton<IActionHandler, OpenPathHandler>();
        services.AddSingleton<IActionHandler, OpenUrlHandler>();
        services.AddSingleton<IActionHandler, AbletonActionHandler>();
        services.AddSingleton<IActionHandler, AbletonLoadDeviceHandler>();
        services.AddSingleton<IActionHandler, MixerPanelHandler>();
        services.AddSingleton<IActionHandler, HotkeyHandler>();
        services.AddSingleton<IActionHandler, MacroHandler>();
        services.AddSingleton<IActionDispatcher, ActionDispatcher>();

        // Windows integration
        services.AddSingleton<IStartupRegistration>(_ => new WindowsStartupRegistration());
        services.AddSingleton<IMonitors, WindowsMonitors>();
        services.AddSingleton<IWindowEffects, WindowsWindowEffects>();
        services.AddSingleton<IGlobalHotkey, WindowsGlobalHotkey>();

        // Built here on purpose: this runs on the thread WPF owns the windows on, and
        // WpfUiThread captures whichever dispatcher builds it.
        services.AddSingleton<IUiThread>(_ => new WpfUiThread());
        services.AddSingleton<WindowPlacementService>();

        // Nothing of the voice assistant is loaded until it is switched on: the speech model
        // is forty megabytes, and most launches never say a word to it. The model itself travels
        // inside the app and is unpacked to the voice directory on first run.
        services.AddSingleton<IVoiceModelInstaller>(_ => new EmbeddedVoiceModelInstaller());
        services.AddSingleton<VoiceService>();

        // UI
        services.AddSingleton<IDeckDialogs, DeckDialogs>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<Views.MainWindow>();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Better a visible message than a window that disappears without explanation.
        MessageBox.Show(
            $"{e.Exception.Message}",
            "DigitalDeck", MessageBoxButton.OK, MessageBoxImage.Error);

        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
