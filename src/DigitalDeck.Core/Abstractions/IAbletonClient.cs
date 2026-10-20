using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.Core.Abstractions;

/// <summary>Where the bridge to Live currently stands.</summary>
public enum AbletonConnectionState
{
    /// <summary>Nothing has been heard from Live. Usually Live is closed, or the script is not installed.</summary>
    Disconnected,

    /// <summary>A handshake has gone out and we are waiting for the answer.</summary>
    Connecting,

    /// <summary>The script answered and is speaking a version we understand.</summary>
    Connected,

    /// <summary>The script answered, but it was built against a different version of the bridge.</summary>
    VersionMismatch,

    /// <summary>The socket could not be opened at all, so no amount of waiting will help.</summary>
    Unavailable
}

/// <param name="Detail">Human-readable explanation, shown in the indicator's tooltip.</param>
/// <param name="ScriptVersion">Bridge version the Remote Script reported, or null.</param>
/// <param name="LiveVersion">Version of Live the script is running inside, or null.</param>
/// <param name="Ports">The pair the script reported, or null when it is too old to say.</param>
public readonly record struct AbletonStatus(
    AbletonConnectionState State,
    string Detail,
    string? ScriptVersion = null,
    string? LiveVersion = null,
    AbletonPorts? Ports = null)
{
    public bool IsUsable => State == AbletonConnectionState.Connected;
}

/// <summary>
/// The app's view of Ableton Live.
/// </summary>
/// <remarks>
/// Core stops here: it knows there is a Live to talk to and what can be asked of it, but nothing
/// about OSC, UDP or Remote Scripts. That boundary is what would let a second DAW adapter drop in
/// later without Core changing, and what lets every test above this line run without a socket.
/// </remarks>
public interface IAbletonClient
{
    AbletonStatus Status { get; }

    /// <summary>Raised whenever <see cref="Status"/> changes. May arrive on a background thread.</summary>
    event Action<AbletonStatus>? StatusChanged;

    /// <summary>
    /// Raised when Live refuses something after the fact.
    /// </summary>
    /// <remarks>
    /// Commands are sent and not waited on, so a failure inside Live cannot come back as the
    /// result of <see cref="ExecuteAsync"/> — it arrives moments later, on its own. Without this
    /// event those failures would be invisible and the button would look like it worked.
    /// </remarks>
    event Action<string>? ScriptFailed;

    /// <summary>Opens the bridge and begins looking for Live. Safe to call more than once.</summary>
    void Start();

    /// <summary>
    /// Carries out one action, or explains why it could not. Never throws for an ordinary problem
    /// such as Live being closed — that is a message for the status bar, not an exception.
    /// </summary>
    Task<ActionResult> ExecuteAsync(AbletonAction action, CancellationToken ct = default);

    /// <summary>
    /// Loads a browser item into the track selected in Live.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ExecuteAsync"/> because it is the one operation whose target is
    /// named by text rather than chosen from a fixed set — and the one whose failure is most
    /// likely, since it depends on what this particular Live has indexed.
    /// </remarks>
    Task<ActionResult> LoadDeviceAsync(AbletonLoadDeviceAction action, CancellationToken ct = default);

    // ---------- Asking Live things ----------
    //
    // Everything above is fire-and-forget, because a command has no answer worth waiting for. The
    // three below do: they exist so the editor can offer what this particular Live has indexed
    // instead of asking the user to type a file name and hope. That makes them the only place the
    // bridge waits on a reply, and the only place a lost datagram is visible as a stall rather
    // than as a button that quietly did nothing.

    /// <summary>
    /// Lists one level of a browser category.
    /// </summary>
    /// <param name="path">
    /// Folder names joined by <c>/</c>, as in <c>Space/Chorus</c>. Empty means the top of the
    /// category. Walking one level at a time is deliberate: a whole-tree dump would be both a
    /// huge datagram and a long walk on Live's thread.
    /// </param>
    Task<AbletonBrowserListing> ListAsync(string category, string path, CancellationToken ct = default);

    /// <summary>
    /// Finds loadable items anywhere under a category whose name contains <paramref name="query"/>.
    /// </summary>
    /// <remarks>
    /// The result may come back with <see cref="AbletonBrowserListing.Complete"/> false, which
    /// means the search hit its ceiling rather than ran out of matches.
    /// </remarks>
    Task<AbletonBrowserListing> SearchAsync(
        string category,
        string query,
        int limit = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Which categories this Live actually exposes.
    /// </summary>
    /// <remarks>
    /// They differ between versions and editions, and an account running Live in demo mode has an
    /// empty User Library. An empty result means the question could not be asked at all — the
    /// caller should fall back to <see cref="AbletonBrowserCategories.All"/> rather than show the
    /// user nothing.
    /// </remarks>
    Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken ct = default);
}
