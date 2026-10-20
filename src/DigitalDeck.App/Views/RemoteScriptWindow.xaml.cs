using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DigitalDeck.Integrations.Ableton;
using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using DigitalDeck.Core.Abstractions;
using Microsoft.Win32;

namespace DigitalDeck.App.Views;

/// <summary>
/// The one screen that turns a downloaded .exe into a working bridge.
/// </summary>
/// <remarks>
/// Everything here exists because of one failure that has no visible cause: with no Remote Script
/// installed, Live simply does not list DigitalDeck under Control Surface, and the app can only say
/// that it cannot find Live. Somebody who was handed a build has no way to get from there to a
/// working setup. This window states which of the three steps is missing and does the first one.
/// </remarks>
public partial class RemoteScriptWindow : Window
{
    /// <summary>Tick, from a code point, the way every mark in this app is built.</summary>
    private static readonly string DoneMark = char.ConvertFromUtf32(0x2713);

    private readonly IRemoteScriptInstaller _installer;
    private readonly AbletonPortSettings _ports;

    private string? _library;
    private bool _loading = true;

    public RemoteScriptWindow(IRemoteScriptInstaller installer, AbletonPortSettings ports)
    {
        InitializeComponent();

        _installer = installer;
        _ports = ports;
        _library = installer.UserLibraries.FirstOrDefault();

        ScriptPortBox.Text = ports.Pending.ScriptListensOn.ToString(CultureInfo.InvariantCulture);
        AppPortBox.Text = ports.Pending.ScriptRepliesTo.ToString(CultureInfo.InvariantCulture);

        LibraryBox.ItemsSource = installer.UserLibraries;
        LibraryBox.SelectedItem = _library;
        LibraryPanel.Visibility = installer.UserLibraries.Count > 1
            ? Visibility.Visible
            : Visibility.Collapsed;

        _loading = false;

        Refresh();
    }

    /// <summary>True when the script ended up installed by the time the window closed.</summary>
    public bool Installed { get; private set; }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void Library_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;

        _library = LibraryBox.SelectedItem as string;
        Refresh();
    }

    private void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        // The escape hatch for a User Library that was moved somewhere we would never guess.
        var dialog = new OpenFolderDialog
        {
            Title = "Elegí la carpeta «User Library» de Ableton"
        };

        if (dialog.ShowDialog(this) != true) return;

        _library = dialog.FolderName;

        _loading = true;
        LibraryBox.ItemsSource = new[] { _library }.Concat(_installer.UserLibraries).Distinct().ToList();
        LibraryBox.SelectedItem = _library;
        LibraryPanel.Visibility = Visibility.Visible;
        _loading = false;

        Refresh();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var (ports, problem) = ReadPorts();

        if (problem is not null)
        {
            Show(problem, ok: false);
            PortsExpander.IsExpanded = true;
            return;
        }

        // Saved before the copy, so the two cannot end up disagreeing if the write fails halfway.
        await _ports.SaveAsync(ports);

        var result = _installer.Install(_library, ports);

        Installed = result.Success;

        var next = result.Success
            ? " " + (_installer.IsLiveRunning
                ? "Cerrá Ableton Live y volvé a abrirlo: sólo lee los scripts al arrancar."
                : "Ya podés abrir Ableton Live.")
            : string.Empty;

        var restart = result.Success && _ports.RestartNeeded
            ? " Y reiniciá DigitalDeck, porque el puerto se toma al arrancar."
            : string.Empty;

        Show(result.Message + next + restart, result.Success);

        Refresh();
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var result = _installer.Uninstall(_library);

        Installed = false;

        Show(result.Message, result.Success);
        Refresh();
    }

    private void Show(string message, bool ok)
    {
        ResultText.Text = message;
        ResultText.Foreground = Brush(ok ? "AccentBrush" : "DangerBrush");
        ResultText.Visibility = Visibility.Visible;
    }

    // ---------- Ports ----------

    private void Ports_Expanded(object sender, RoutedEventArgs e) => UpdatePorts();

    private void Ports_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loading) UpdatePorts();
    }

    private (AbletonPorts Ports, string? Problem) ReadPorts()
    {
        if (!int.TryParse(ScriptPortBox.Text.Trim(), out var script)
            || !int.TryParse(AppPortBox.Text.Trim(), out var app))
        {
            return (_ports.Pending, "Los puertos tienen que ser números.");
        }

        var ports = new AbletonPorts(script, app);

        return (ports, ports.Validate());
    }

    private void UpdatePorts()
    {
        if (PortsNote is null) return;

        var (ports, problem) = ReadPorts();

        var message = problem;
        var ok = false;

        if (message is null && ports.CollidesWithAbletonOsc)
        {
            message = "Esos son los puertos de AbletonOSC. Si lo tenés instalado, el que arranque "
                      + "segundo se queda sin escuchar.";
        }
        else if (message is null && ports != _ports.Current)
        {
            message = $"Ahora mismo la app está en {_ports.Current}. El cambio toma efecto cuando "
                      + "reinstales el script y reinicies DigitalDeck.";
            ok = true;
        }

        PortsNote.Text = message ?? string.Empty;
        PortsNote.Foreground = Brush(ok ? "TextDimBrush" : "DangerBrush");
        PortsNote.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---------- What the window says ----------

    private void Refresh()
    {
        var state = _installer.Inspect(_library);

        StateDot.Fill = Brush(state.Status switch
        {
            RemoteScriptStatus.Installed => "AccentBrush",
            RemoteScriptStatus.Unknown => "TextFaintBrush",
            _ => "DangerBrush"
        });

        StateText.Text = state.Status switch
        {
            RemoteScriptStatus.Installed => $"Script instalado (versión {state.InstalledVersion})",
            RemoteScriptStatus.Outdated => $"Script desactualizado (tenés la {state.InstalledVersion})",
            RemoteScriptStatus.NotInstalled => "El script todavía no está instalado",
            _ => "No encuentro Ableton en esta máquina"
        };

        StateDetail.Text = state.Status switch
        {
            RemoteScriptStatus.Installed =>
                "Si el indicador sigue en rojo, falta elegir DigitalDeck en Control Surface, o Live "
                + "se abrió antes de que el script estuviera puesto.",

            RemoteScriptStatus.Outdated =>
                $"Esta versión de la app habla la {state.BundledVersion}. Con la vieja el deck anda, "
                + "pero el picker del browser no puede abrir carpetas.",

            RemoteScriptStatus.NotInstalled =>
                "Por eso DigitalDeck no aparece en la lista de Control Surface de Live: no hay nada "
                + "todavía que aparezca ahí.",

            _ => state.Problem ?? "Elegí la carpeta a mano si Live está instalado."
        };

        LocationText.Text = state.Location is null ? string.Empty : $"Va en: {state.Location}";

        // Reading it off the disk is the only way to catch this: with the two halves on different
        // pairs nothing arrives in either direction, so it looks exactly like Live being closed.
        if (state.PortsDisagreeWith(_ports.Current))
        {
            StateDetail.Text =
                $"El script instalado está en los puertos {state.InstalledPorts}, y esta app usa "
                + $"{_ports.Current}. Así no se oyen. Reinstalalo para dejarlos iguales.";

            StateDot.Fill = Brush("DangerBrush");
        }

        UpdatePorts();

        // The step that is actually pending is the one worth pointing at.
        var done = state.Status == RemoteScriptStatus.Installed;

        Step1Mark.Text = done ? DoneMark : "1";
        Step1Mark.Foreground = Brush(done ? "AccentBrush" : "TextFaintBrush");

        Step1Text.Text = done
            ? $"Script instalado, versión {state.InstalledVersion}."
            : "Instalar el script en tu User Library de Ableton.";

        Step2Text.Text = _installer.IsLiveRunning
            ? "Cerrar Ableton Live y volver a abrirlo — sólo lee los scripts al arrancar."
            : "Abrir Ableton Live.";

        InstallButton.IsEnabled = _library is not null;
        InstallButton.Content = state.Status switch
        {
            RemoteScriptStatus.Installed => "Reinstalar",
            RemoteScriptStatus.Outdated => "Actualizar el script",
            _ => "Instalar el script"
        };

        UninstallButton.IsEnabled = _library is not null
            && state.Status is RemoteScriptStatus.Installed or RemoteScriptStatus.Outdated;
    }

    private static Brush Brush(string key) =>
        (Brush)Application.Current.Resources[key];
}
