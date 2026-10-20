using DigitalDeck.Core.Model;
using System.Windows;
using System.Windows.Interop;
using DigitalDeck.ViewModels;
using DigitalDeck.App.Views;
using DigitalDeck.Core;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Voice;
using DigitalDeck.Integrations.Voice;

namespace DigitalDeck.App.Services;

/// <summary>
/// The voice assistant as the rest of the app sees it: a switch, a key combination, and a
/// microphone to choose.
/// </summary>
/// <remarks>
/// Everything expensive here is deferred. The speech model is forty megabytes and takes about a
/// second to load, and most launches of this app never say a word to it — so nothing is loaded
/// until the feature is turned on, and turning it off puts it all back down again.
/// </remarks>
public sealed class VoiceService : IDisposable
{
    private readonly IAppSettingsStore _settings;
    private readonly DeckPaths _paths;
    private readonly IMonitors _monitors;
    private readonly IWindowEffects _effects;
    private readonly IVoiceModelInstaller _modelInstaller;

    private MainViewModel? _deck;
    private Window? _host;
    private readonly IGlobalHotkey _hotkey;

    /// <summary>True once the host window's messages are being fed to the hotkey.</summary>
    private bool _hooked;
    private VoiceOverlayWindow? _overlay;

    private IVoiceInput? _input;
    private IVoiceRecogniser? _recogniser;
    private VoiceAssistant? _assistant;

    private bool _disposed;

    public VoiceService(
        IAppSettingsStore settings,
        DeckPaths paths,
        IMonitors monitors,
        IWindowEffects effects,
        IGlobalHotkey hotkey,
        IVoiceModelInstaller modelInstaller)
    {
        _settings = settings;
        _paths = paths;
        _monitors = monitors;
        _effects = effects;
        _hotkey = hotkey;
        _modelInstaller = modelInstaller;
    }

    public bool IsEnabled { get; private set; }

    public HotkeySpec Hotkey { get; private set; } = HotkeySpec.Default;

    public string? DeviceId { get; private set; }

    /// <summary>True when a speech model is unpacked and ready to be loaded.</summary>
    public bool HasModel => VoskRecogniser.FindModel(_paths.VoiceDirectory) is not null;

    /// <summary>True when the model is not installed yet but this build carries one to install.</summary>
    public bool CanInstallModel => !HasModel && _modelInstaller.HasBundle;

    /// <summary>True while <see cref="InstallModelAsync"/> is running, so the UI can lock its button.</summary>
    public bool IsInstallingModel { get; private set; }

    /// <summary>
    /// Unpacks the bundled speech model into the voice directory. Off the UI thread — it writes
    /// about sixty megabytes. Reports the outcome through <see cref="Reported"/>.
    /// </summary>
    /// <returns>True when a model is in place afterwards.</returns>
    public async Task<bool> InstallModelAsync(IProgress<double>? progress = null)
    {
        if (_disposed || HasModel) return HasModel;

        if (!_modelInstaller.HasBundle)
        {
            Report("Esta copia de la app no trae el modelo de voz adentro.", isError: true);
            return false;
        }

        if (IsInstallingModel) return false;

        IsInstallingModel = true;
        Report("Instalando el asistente de voz…", isError: false);

        try
        {
            var result = await Task.Run(() => _modelInstaller.Install(_paths.VoiceDirectory, progress))
                .ConfigureAwait(true);

            Report(result.Message, isError: !result.Success);
            return result.Success;
        }
        finally
        {
            IsInstallingModel = false;
        }
    }

    /// <summary>Raised with something to put in the status bar. Always on the UI thread.</summary>
    public event Action<string, bool>? Reported;

    /// <summary>
    /// Attaches to the main window, which is the earliest point a global hotkey can be claimed —
    /// <c>RegisterHotKey</c> needs a window handle to deliver its message to.
    /// </summary>
    public async Task AttachAsync(Window host, MainViewModel deck)
    {
        _host = host;
        _deck = deck;

        Hotkey = HotkeySpec.TryParse(await _settings.GetAsync(SettingKeys.VoiceHotkey).ConfigureAwait(true), out var saved)
            ? saved
            : HotkeySpec.Default;

        DeviceId = await _settings.GetAsync(SettingKeys.VoiceInputDevice).ConfigureAwait(true);

        var enabled = await _settings.GetAsync(SettingKeys.VoiceEnabled).ConfigureAwait(true) == "1";

        if (enabled) await SetEnabledAsync(true).ConfigureAwait(true);
    }

    public IReadOnlyList<VoiceInputDevice> GetDevices() => (_input ??= new WasapiVoiceInput()).GetDevices();

    // ---------- Settings ----------

    /// <summary>Turns the feature on or off, loading or releasing everything it needs.</summary>
    /// <returns>False when it could not be turned on, having already reported why.</returns>
    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        if (_disposed) return false;

        if (!enabled)
        {
            Teardown();

            IsEnabled = false;
            await _settings.SetAsync(SettingKeys.VoiceEnabled, "0").ConfigureAwait(true);

            return true;
        }

        if (IsEnabled) return true;

        var modelPath = VoskRecogniser.FindModel(_paths.VoiceDirectory);

        if (modelPath is null)
        {
            Report("Falta el modelo de voz. Descargalo y descomprimilo en " + _paths.VoiceDirectory, isError: true);
            return false;
        }

        try
        {
            // Off the UI thread: loading the model takes about a second, and doing it inline would
            // freeze the deck at the exact moment the user flipped the switch.
            _recogniser = await Task.Run(() => (IVoiceRecogniser)new VoskRecogniser(modelPath)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Report($"No se pudo cargar el modelo de voz: {ex.Message}", isError: true);
            return false;
        }

        _input ??= new WasapiVoiceInput();
        _assistant = new VoiceAssistant(_input, _recogniser);

        _assistant.StatusChanged += OnStatusChanged;
        _assistant.Recognised += OnRecognised;

        if (!TryClaimHotkey(Hotkey))
        {
            Teardown();
            return false;
        }

        IsEnabled = true;
        await _settings.SetAsync(SettingKeys.VoiceEnabled, "1").ConfigureAwait(true);

        Report($"Asistente de voz activo. Apretá {Hotkey} y decí el nombre de un botón.", isError: false);

        return true;
    }

    /// <returns>False when the combination belongs to another program, having reported it.</returns>
    public async Task<bool> SetHotkeyAsync(HotkeySpec spec)
    {
        if (!spec.IsValid) return false;

        // Only claimed while the feature is on; otherwise it is just remembered for next time.
        if (IsEnabled && !TryClaimHotkey(spec))
        {
            // The old one is gone either way, so put it back rather than leaving the user with no
            // working hotkey after a failed change.
            TryClaimHotkey(Hotkey);
            return false;
        }

        Hotkey = spec;
        await _settings.SetAsync(SettingKeys.VoiceHotkey, spec.ToString()).ConfigureAwait(true);

        return true;
    }

    public async Task SetDeviceAsync(string? deviceId)
    {
        DeviceId = deviceId;

        if (string.IsNullOrEmpty(deviceId))
            await _settings.RemoveAsync(SettingKeys.VoiceInputDevice).ConfigureAwait(true);
        else
            await _settings.SetAsync(SettingKeys.VoiceInputDevice, deviceId).ConfigureAwait(true);
    }

    // ---------- Listening ----------

    private bool TryClaimHotkey(HotkeySpec spec)
    {
        if (_host is null) return false;

        var handle = new WindowInteropHelper(_host).Handle;

        if (handle == IntPtr.Zero) return false;

        if (!_hooked)
        {
            _hotkey.Attach(handle);
            _hotkey.Pressed += Listen;

            // The hotkey arrives as a message on this window's queue, and only the shell has that
            // queue. Feeding it in is the whole of what the platform needs from here.
            HwndSource.FromHwnd(handle)?.AddHook(OnWindowMessage);

            _hooked = true;
        }

        if (_hotkey.TryRegister(spec, out var problem)) return true;

        Report(problem, isError: true);

        return false;
    }

    private IntPtr OnWindowMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_hotkey.OnHostMessage(message, wParam)) return IntPtr.Zero;

        handled = true;

        return IntPtr.Zero;
    }

    private void Listen()
    {
        if (_assistant is null || _deck is null || _host is null) return;

        ShowOverlay();

        var assistant = _assistant;
        var deck = _deck;
        var device = DeviceId;

        // Posted rather than run here, and both hops matter.
        //
        // BeginInvoke gets us out of the window procedure: opening a WASAPI capture needs the STA
        // thread to keep pumping messages while it comes up, and inside a window procedure the pump
        // is stopped until we return — doing it inline deadlocks the whole application.
        //
        // Then Task.Run gets the microphone off the UI thread entirely, so building the vocabulary
        // and opening the device do not freeze the deck.
        _host.Dispatcher.BeginInvoke(async () =>
        {
            var targets = await deck.PrepareVoiceTargetsAsync().ConfigureAwait(true);

            _ = Task.Run(() => assistant.Listen(targets, device));
        });
    }

    private void OnStatusChanged(VoiceAssistantStatus status)
    {
        // Arrives from the capture thread, and everything below touches windows.
        _host?.Dispatcher.BeginInvoke(() =>
        {
            _overlay?.Render(status);

            if (status.State == VoiceAssistantState.Missed) Report(status.Detail, isError: true);
        });
    }

    private void OnRecognised(VoiceMatch match)
    {
        _host?.Dispatcher.BeginInvoke(() => _deck?.ActivateByIdAsync(match.Target.Id));
    }

    private void ShowOverlay()
    {
        if (_host is null) return;

        // No Owner on purpose. An owned window drags its owner to the foreground when it activates,
        // and WPF's ShowActivated="False" only holds for the first Show — so from the second press
        // on, showing the overlay was yanking the whole deck in front of Live. Parentless plus
        // Topmost plus WS_EX_NOACTIVATE keeps it visible without ever taking the foreground.
        _overlay ??= new VoiceOverlayWindow(_effects);

        // Placed on every press rather than once: the deck can be moved between monitors, and an
        // overlay left behind on the old one is an overlay nobody sees.
        var monitor = _monitors.GetMonitorForWindow(new WindowInteropHelper(_host).Handle);

        if (monitor is not null) _overlay.PlaceNear(monitor);
    }

    // ---------- Teardown ----------

    private void Teardown()
    {
        if (_assistant is not null)
        {
            _assistant.StatusChanged -= OnStatusChanged;
            _assistant.Recognised -= OnRecognised;
            _assistant.Dispose();
            _assistant = null;
        }

        _hotkey.Unregister();

        _recogniser?.Dispose();
        _recogniser = null;

        _overlay?.Hide();

        IsEnabled = false;
    }

    private void Report(string message, bool isError) => Reported?.Invoke(message, isError);

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        Teardown();

        _input?.Dispose();
        _input = null;

        _overlay?.Close();
        _overlay = null;
    }
}
