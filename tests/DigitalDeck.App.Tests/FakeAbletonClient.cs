using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.App.Tests;

/// <summary>
/// An Ableton bridge the test drives by hand, with no socket and no Live.
/// </summary>
/// <remarks>
/// Both faces of the bridge on one object, the way <c>AbletonAdapter</c> is both an
/// <see cref="IAbletonClient"/> and an <see cref="IAbletonSong"/> — a test that reports the set as
/// playing and then presses a button is exercising the same shape production has.
/// </remarks>
public sealed class FakeAbletonClient : IAbletonClient, IAbletonSong
{
    public AbletonStatus Status { get; private set; } =
        new(AbletonConnectionState.Disconnected, "Ableton Live no está conectado.");

    public List<AbletonAction> Executed { get; } = new();

    public bool Started { get; private set; }

    public event Action<AbletonStatus>? StatusChanged;

    public event Action<string>? ScriptFailed;

    public void Start() => Started = true;

    // ---------- What the set is doing ----------

    public LiveSongState State { get; private set; } = LiveSongState.Unknown;

    public event Action<LiveSongState>? StateChanged;

    public LiveTracksState Tracks { get; private set; } = LiveTracksState.Empty;

    public event Action<LiveTracksState>? TracksChanged;

    /// <summary>Announces a change the way the Remote Script's observers do.</summary>
    public void ReportSong(LiveSongState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    /// <inheritdoc cref="ReportSong"/>
    public void ReportTracks(int selected, params LiveTrackState[] tracks)
    {
        Tracks = new LiveTracksState(tracks, selected);
        TracksChanged?.Invoke(Tracks);
    }

    public Task<ActionResult> ExecuteAsync(AbletonAction action, CancellationToken ct = default)
    {
        Executed.Add(action);
        return Task.FromResult(Status.IsUsable
            ? ActionResult.Ok()
            : ActionResult.Failed("Ableton Live no está conectado."));
    }

    public List<AbletonLoadDeviceAction> Loaded { get; } = new();

    public Task<ActionResult> LoadDeviceAsync(AbletonLoadDeviceAction action, CancellationToken ct = default)
    {
        Loaded.Add(action);
        return Task.FromResult(Status.IsUsable
            ? ActionResult.Ok()
            : ActionResult.Failed("Ableton Live no está conectado."));
    }

    // ---------- Questions ----------

    /// <summary>Category and path (or query) of every question asked, in order.</summary>
    public List<string> Asked { get; } = new();

    /// <summary>What ListAsync should answer, keyed by "category/path".</summary>
    public Dictionary<string, IReadOnlyList<AbletonBrowserEntry>> Tree { get; } = new();

    /// <summary>What SearchAsync should answer, whatever it is asked.</summary>
    public List<AbletonBrowserEntry> SearchResults { get; } = new();

    /// <summary>Set to make every question fail the way a closed Live does.</summary>
    public string? QueryError { get; set; }

    public List<string> Categories { get; } = new();

    public Task<AbletonBrowserListing> ListAsync(
        string category,
        string path,
        CancellationToken ct = default)
    {
        Asked.Add($"list {category}/{path}");

        if (QueryError is not null)
            return Task.FromResult(AbletonBrowserListing.Failed(category, path, QueryError));

        var entries = Tree.TryGetValue($"{category}/{path}", out var found)
            ? found
            : Array.Empty<AbletonBrowserEntry>();

        return Task.FromResult(AbletonBrowserListing.Ok(category, path, entries, true));
    }

    public Task<AbletonBrowserListing> SearchAsync(
        string category,
        string query,
        int limit = 100,
        CancellationToken ct = default)
    {
        Asked.Add($"search {category}/{query}");

        return Task.FromResult(QueryError is not null
            ? AbletonBrowserListing.Failed(category, query, QueryError)
            : AbletonBrowserListing.Ok(category, query, SearchResults, true));
    }

    public Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(Categories);

    /// <summary>Pretends the bridge changed state.</summary>
    public void Report(AbletonConnectionState state, string detail, string? liveVersion = null)
    {
        Status = new AbletonStatus(state, detail, LiveVersion: liveVersion);
        StatusChanged?.Invoke(Status);
    }

    /// <summary>Pretends Live refused something after the command had already been sent.</summary>
    public void ReportFailure(string message) => ScriptFailed?.Invoke(message);
}
