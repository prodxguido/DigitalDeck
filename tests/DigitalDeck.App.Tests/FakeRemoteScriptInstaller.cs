using DigitalDeck.Core.Abstractions;

namespace DigitalDeck.App.Tests;

/// <summary>A Remote Script installer with no Ableton and no filesystem behind it.</summary>
public sealed class FakeRemoteScriptInstaller : IRemoteScriptInstaller
{
    public string BundledVersion { get; set; } = "2.2.0";

    public IReadOnlyList<string> UserLibraries { get; set; } = new[] { @"C:\Ableton\User Library" };

    public bool IsLiveRunning { get; set; }

    /// <summary>What <see cref="Inspect"/> should report. Installed unless a test says otherwise.</summary>
    public RemoteScriptStatus Status { get; set; } = RemoteScriptStatus.Installed;

    public int Installs { get; private set; }
    public int Uninstalls { get; private set; }

    public RemoteScriptState Inspect(string? userLibrary = null) =>
        new(Status,
            Status == RemoteScriptStatus.Installed ? BundledVersion : "1.0.0",
            BundledVersion,
            UserLibraries.FirstOrDefault());

    /// <summary>The pair the last install was told to write.</summary>
    public AbletonPorts? InstalledWith { get; private set; }

    public RemoteScriptInstallResult Install(string? userLibrary = null, AbletonPorts? ports = null)
    {
        Installs++;
        Status = RemoteScriptStatus.Installed;
        InstalledWith = ports;

        return new RemoteScriptInstallResult(true, "Instalado.", UserLibraries.FirstOrDefault());
    }

    public RemoteScriptInstallResult Uninstall(string? userLibrary = null)
    {
        Uninstalls++;
        Status = RemoteScriptStatus.NotInstalled;

        return new RemoteScriptInstallResult(true, "Desinstalado.", UserLibraries.FirstOrDefault());
    }
}
