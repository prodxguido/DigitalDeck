using Avalonia.Headless.XUnit;
using DigitalDeck.Core.Abstractions;
using Xunit;

namespace DigitalDeck.Avalonia.Tests;

/// <summary>
/// La ventana que instala el Remote Script.
/// </summary>
/// <remarks>
/// La única que una Mac recién sacada de la caja de verdad no puede saltear: el script es Python
/// que corre adentro de Live, y hasta que no está copiado en la User Library no hay puente por más
/// que el deck funcione perfecto.
/// </remarks>
public class RemoteScriptWindowTests
{
    /// <summary>Un instalador que no toca el disco y contesta lo que el test necesita.</summary>
    private sealed class FakeInstaller : IRemoteScriptInstaller
    {
        public RemoteScriptStatus Status { get; set; } = RemoteScriptStatus.NotInstalled;

        public IReadOnlyList<string> UserLibraries { get; set; } = ["/Users/guido/Music/Ableton/User Library"];

        public bool IsLiveRunning { get; set; }

        public string? InstalledInto { get; private set; }

        public bool Fails { get; set; }

        public string BundledVersion => "2.6.0";

        public RemoteScriptState Inspect(string? userLibrary = null) =>
            new(Status,
                Status == RemoteScriptStatus.Outdated ? "2.4.0" : Status == RemoteScriptStatus.Installed ? "2.6.0" : null,
                BundledVersion,
                userLibrary ?? UserLibraries.FirstOrDefault());

        public RemoteScriptInstallResult Install(string? userLibrary = null, AbletonPorts? ports = null)
        {
            if (Fails) return new RemoteScriptInstallResult(false, "No pude escribir ahí.");

            InstalledInto = userLibrary ?? UserLibraries.FirstOrDefault();
            Status = RemoteScriptStatus.Installed;

            return new RemoteScriptInstallResult(true, "Copié el Remote Script 2.6.0.");
        }

        public RemoteScriptInstallResult Uninstall(string? userLibrary = null) =>
            new(true, "Borrado.");
    }

    [AvaloniaFact]
    public void WithNothingInstalledItSaysSoAndOffersTheButton()
    {
        var window = new RemoteScriptWindow(new FakeInstaller());

        try
        {
            Assert.False(window.Installed);
            Assert.Contains("Falta instalar", window.StateText.Text);
            Assert.True(window.InstallButton.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InstallingItCopiesItIntoTheLibraryThatWasChosen()
    {
        var installer = new FakeInstaller();
        var window = new RemoteScriptWindow(installer);

        try
        {
            Assert.True(window.Install());

            Assert.Equal("/Users/guido/Music/Ableton/User Library", installer.InstalledInto);
            Assert.True(window.Installed);
            Assert.Contains("instalado", window.StateText.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AnOldVersionIsNotTheSameAsNoVersion()
    {
        // Dice cuál está y cuál habla la app: un puente que no levanta por versiones que no se
        // hablan es indistinguible de Live cerrado si nadie lo cuenta.
        var window = new RemoteScriptWindow(new FakeInstaller { Status = RemoteScriptStatus.Outdated });

        try
        {
            Assert.False(window.Installed);
            Assert.Contains("vieja", window.StateText.Text);
            Assert.Contains("2.4.0", window.Detail.Text);
            Assert.Contains("2.6.0", window.Detail.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void WithNoUserLibraryThereIsNothingToPressAndTheReasonIsOnScreen()
    {
        var window = new RemoteScriptWindow(new FakeInstaller
        {
            UserLibraries = [],
            Status = RemoteScriptStatus.Unknown
        });

        try
        {
            Assert.False(window.InstallButton.IsEnabled);
            Assert.True(window.NoLibrary.IsVisible);
            Assert.False(window.LibraryBox.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void WithLiveOpenItWarnsInsteadOfBlocking()
    {
        // Instalar con Live abierto deja el código viejo en memoria y el nuevo en disco. Quien va a
        // reiniciar Live enseguida igual tiene que poder hacerlo.
        var window = new RemoteScriptWindow(new FakeInstaller { IsLiveRunning = true });

        try
        {
            Assert.True(window.Result.IsVisible);
            Assert.Contains("reiniciarlo", window.Result.Text);
            Assert.True(window.InstallButton.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AFailedInstallSaysWhyAndDoesNotClaimSuccess()
    {
        var window = new RemoteScriptWindow(new FakeInstaller { Fails = true });

        try
        {
            Assert.False(window.Install());
            Assert.False(window.Installed);
            Assert.Contains("No pude escribir", window.Result.Text);
        }
        finally
        {
            window.Close();
        }
    }
}
