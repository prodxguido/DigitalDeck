using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.Avalonia.Tests;

/// <summary>A bridge to Live that is not there, so a test never touches a socket.</summary>
/// <remarks>
/// The shell's own <c>NoAbleton</c> went away when Integrations became portable and the real
/// adapter took its place. A test still wants one: opening a UDP socket to look at a window is
/// noise, and two tests doing it at once fight over the port.
/// </remarks>
public sealed class NoAbletonForTests : IAbletonClient, IAbletonSong
{
    private const string Closed = "Live no está conectado.";

    public AbletonStatus Status { get; } = new(AbletonConnectionState.Disconnected, Closed);

    public event Action<AbletonStatus>? StatusChanged { add { } remove { } }

    public event Action<string>? ScriptFailed { add { } remove { } }

    public void Start() { }

    public Task<ActionResult> ExecuteAsync(AbletonAction action, CancellationToken ct = default) =>
        Task.FromResult(ActionResult.Failed(Closed));

    public Task<ActionResult> LoadDeviceAsync(AbletonLoadDeviceAction action, CancellationToken ct = default) =>
        Task.FromResult(ActionResult.Failed(Closed));

    public Task<AbletonBrowserListing> ListAsync(string category, string path, CancellationToken ct = default) =>
        Task.FromResult(AbletonBrowserListing.Failed(category, path, Closed));

    public Task<AbletonBrowserListing> SearchAsync(
        string category, string query, int limit = 100, CancellationToken ct = default) =>
        Task.FromResult(AbletonBrowserListing.Failed(category, query, Closed));

    public Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public LiveSongState State => LiveSongState.Unknown;

    public event Action<LiveSongState>? StateChanged { add { } remove { } }

    public LiveTracksState Tracks => LiveTracksState.Empty;

    public event Action<LiveTracksState>? TracksChanged { add { } remove { } }
}

/// <summary>An installer that never looks at the disk.</summary>
public sealed class NoRemoteScriptForTests : IRemoteScriptInstaller
{
    public string BundledVersion => "0.0.0";

    public IReadOnlyList<string> UserLibraries => [];

    public bool IsLiveRunning => false;

    public RemoteScriptState Inspect(string? userLibrary = null) =>
        new(RemoteScriptStatus.Unknown, null, BundledVersion, null);

    public RemoteScriptInstallResult Install(string? userLibrary = null, AbletonPorts? ports = null) =>
        new(false, "No en un test.");

    public RemoteScriptInstallResult Uninstall(string? userLibrary = null) =>
        new(false, "No en un test.");
}
