using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using DigitalDeck.Core.Abstractions;

namespace DigitalDeck.Avalonia;

/// <summary>
/// Installing the Remote Script, which is what makes the bridge with Live exist at all.
/// </summary>
/// <remarks>
/// The one window a fresh Mac genuinely cannot do without: the script is Python that runs inside
/// Live, so it has to be copied into Ableton's User Library before Live will load it, and no
/// amount of the deck working makes up for that not having happened.
/// <para>
/// The installer itself is shared with the WPF app and already knows where the User Library lives
/// on each desktop. What is here is saying which state it is in and offering the one button.
/// </para>
/// </remarks>
public partial class RemoteScriptWindow : Window
{
    private readonly IRemoteScriptInstaller _installer;

    /// <summary>True once the script is on disk in the version this app speaks.</summary>
    public bool Installed { get; private set; }

    public RemoteScriptWindow()
        : this(new NoInstaller())
    {
    }

    public RemoteScriptWindow(IRemoteScriptInstaller installer)
    {
        _installer = installer;

        InitializeComponent();

        LibraryBox.ItemsSource = installer.UserLibraries;
        LibraryBox.SelectedIndex = installer.UserLibraries.Count > 0 ? 0 : -1;

        Describe();
    }

    /// <summary>Reads the state off the disk and says it. Public so a test can ask again.</summary>
    public void Describe()
    {
        var state = _installer.Inspect(LibraryBox.SelectedItem as string);

        Installed = state.Status == RemoteScriptStatus.Installed;

        StateText.Text = state.Status switch
        {
            RemoteScriptStatus.Installed => "El puente está instalado.",
            RemoteScriptStatus.Outdated => "Hay una versión vieja instalada.",
            RemoteScriptStatus.NotInstalled => "Falta instalar el puente.",
            _ => "No pude fijarme."
        };

        Detail.Text = state.Status switch
        {
            RemoteScriptStatus.Installed =>
                $"Versión {state.InstalledVersion} en {state.Location}.",

            RemoteScriptStatus.Outdated =>
                $"Está la {state.InstalledVersion} y esta app habla la {state.BundledVersion}. " +
                "Instalá de nuevo y reiniciá Live.",

            RemoteScriptStatus.NotInstalled =>
                $"Se va a copiar la versión {state.BundledVersion} a {state.Location}.",

            _ => state.Problem ?? "No encontré la User Library de Ableton."
        };

        var known = _installer.UserLibraries.Count > 0;

        LibraryBox.IsVisible = known;
        NoLibrary.IsVisible = !known;
        InstallButton.IsEnabled = known;

        // Live loads the script at startup and then runs from memory: installing with Live open
        // leaves the old code running and the new code on disk, which is the worst combination to
        // diagnose. Said rather than blocked — somebody about to restart Live anyway is fine.
        if (_installer.IsLiveRunning)
            Say("Ableton Live está abierto. Podés instalar igual, pero hay que reiniciarlo después.",
                warning: true);
    }

    private void Install_Click(object? sender, RoutedEventArgs e) => Install();

    /// <summary>Copies the script and reports what happened. Public so a test can press it.</summary>
    public bool Install()
    {
        var result = _installer.Install(LibraryBox.SelectedItem as string);

        if (result.Success) Describe();

        Say(result.Message, warning: !result.Success);

        return result.Success;
    }

    private void Say(string message, bool warning)
    {
        Result.Text = message;
        Result.Foreground = warning ? Brushes.Orange : Brushes.MediumSpringGreen;
        Result.IsVisible = true;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();

    /// <summary>What the parameterless constructor gets. Never used at runtime.</summary>
    private sealed class NoInstaller : IRemoteScriptInstaller
    {
        public string BundledVersion => "0.0.0";

        public IReadOnlyList<string> UserLibraries => [];

        public bool IsLiveRunning => false;

        public RemoteScriptState Inspect(string? userLibrary = null) =>
            new(RemoteScriptStatus.Unknown, null, BundledVersion, null);

        public RemoteScriptInstallResult Install(string? userLibrary = null, AbletonPorts? ports = null) =>
            new(false, "Sin instalador.");

        public RemoteScriptInstallResult Uninstall(string? userLibrary = null) =>
            new(false, "Sin instalador.");
    }
}
