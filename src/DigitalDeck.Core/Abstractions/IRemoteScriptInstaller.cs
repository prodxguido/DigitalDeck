namespace DigitalDeck.Core.Abstractions;

/// <summary>Where the Remote Script stands on this machine.</summary>
public enum RemoteScriptStatus
{
    /// <summary>Ableton's User Library was found, and DigitalDeck is not in it.</summary>
    NotInstalled,

    /// <summary>An older build of the script is installed than the one this app speaks.</summary>
    Outdated,

    /// <summary>Installed, and the version this app was built against.</summary>
    Installed,

    /// <summary>Ableton's User Library could not be found, so there is nothing to report.</summary>
    Unknown
}

/// <param name="InstalledVersion">What is on disk, or null when nothing is.</param>
/// <param name="BundledVersion">What this build of the app carries.</param>
/// <param name="Location">Where the script is, or where it would go.</param>
/// <param name="InstalledPorts">The pair the installed script is set to, or null when unknown.</param>
/// <param name="Problem">Why the question could not be answered, or null.</param>
public sealed record RemoteScriptState(
    RemoteScriptStatus Status,
    string? InstalledVersion,
    string BundledVersion,
    string? Location,
    AbletonPorts? InstalledPorts = null,
    string? Problem = null)
{
    /// <summary>Whether the user has something to do before the bridge can work.</summary>
    public bool NeedsAttention => Status is RemoteScriptStatus.NotInstalled or RemoteScriptStatus.Outdated;

    /// <summary>
    /// True when the script is listening somewhere the app is not talking.
    /// </summary>
    /// <remarks>
    /// Worth its own question because of how it presents: a script on the wrong ports is indistinguishable
    /// from Live not being open, and the app would send the user to check Control Surface, where
    /// there is nothing wrong.
    /// </remarks>
    public bool PortsDisagreeWith(AbletonPorts expected) =>
        InstalledPorts is { } installed && installed != expected;
}

/// <param name="Location">Where the files ended up, when it worked.</param>
public sealed record RemoteScriptInstallResult(bool Success, string Message, string? Location = null);

/// <summary>
/// Puts the Remote Script where Ableton will find it.
/// </summary>
/// <remarks>
/// The script is the half of the bridge that runs inside Live, and until this existed the only way
/// to install it was a PowerShell script from a clone of the repo. That is fine for the machine the
/// app is built on and hopeless for anyone handed an .exe: the bridge simply never comes up, and
/// the indicator tells them to pick DigitalDeck in Control Surface — where it is not, because
/// nothing ever copied it there.
/// <para>
/// So the script travels inside the app, and this puts it in place. It is versioned together with
/// the code that talks to it, which is what makes "the app and the script disagree" a state that
/// can be detected and fixed with a button rather than diagnosed from Live's log.
/// </para>
/// </remarks>
public interface IRemoteScriptInstaller
{
    /// <summary>The script version carried inside this build.</summary>
    string BundledVersion { get; }

    /// <summary>
    /// The Ableton User Library folders found on this machine, best guess first.
    /// </summary>
    /// <remarks>Empty when none were found, which is ordinary on a machine with no Ableton.</remarks>
    IReadOnlyList<string> UserLibraries { get; }

    /// <summary>True while Ableton is running, which matters because it reads scripts only at startup.</summary>
    bool IsLiveRunning { get; }

    /// <summary>Looks at what is installed. Never throws — a problem comes back in the state.</summary>
    RemoteScriptState Inspect(string? userLibrary = null);

    /// <summary>
    /// Copies the script into place, replacing any earlier install. Never throws.
    /// </summary>
    /// <param name="ports">
    /// Written into the script as it is copied. Passing them here rather than leaving the file as
    /// bundled is what keeps the two halves of the bridge on the same pair: the app cannot end up
    /// talking to a port the script was never told to listen on.
    /// </param>
    RemoteScriptInstallResult Install(string? userLibrary = null, AbletonPorts? ports = null);

    /// <summary>
    /// Removes the script from the User Library. Never throws. Meant for taking the bridge down
    /// for a while without losing the port choice — installing again puts it right back.
    /// </summary>
    RemoteScriptInstallResult Uninstall(string? userLibrary = null);
}
