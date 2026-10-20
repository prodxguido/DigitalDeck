namespace DigitalDeck.Core.Model.Actions;

/// <summary>
/// One row of Live's browser: a folder to walk into, or an item that can be loaded.
/// </summary>
/// <param name="Name">What Live calls it, extension included.</param>
/// <param name="Uri">Live's own identifier for this exact item. Unique; the name is not.</param>
/// <param name="IsLoadable">Whether <c>load_item</c> would accept it.</param>
/// <param name="IsFolder">What Live calls it. Not the same as having children — see below.</param>
/// <param name="HasChildren">Whether walking into it leads anywhere.</param>
public sealed record AbletonBrowserEntry(
    string Name,
    string Uri,
    bool IsLoadable,
    bool IsFolder,
    bool HasChildren = false)
{
    /// <summary>
    /// A row that can be neither loaded nor opened is a dead end, and the picker leaves it out.
    /// </summary>
    /// <remarks>
    /// Note what this does <em>not</em> consult. Measured against Live 12.3, <see cref="IsFolder"/>
    /// is false for all 47 items at the top of Audio Effects, "Audio Effect Rack" included — and
    /// that one holds six folders of the user's own racks. Navigating by it would have made every
    /// one of them unreachable, which is why the script reports the two things separately.
    /// </remarks>
    public bool IsUseful => IsLoadable || HasChildren;
}

/// <summary>
/// The answer to one browser question, whether or not it went well.
/// </summary>
/// <remarks>
/// A failure is a value here rather than an exception, for the same reason
/// <see cref="ActionResult"/> is: Live being closed, a lost datagram or an unindexed category are
/// all ordinary and all end the same way — text for the user to read in the picker.
/// <para>
/// <see cref="Complete"/> exists because the Remote Script's traversals are bounded on purpose:
/// walking the whole tree happens on Live's thread and would freeze it. When the ceiling was hit
/// the picker has to say so, because "nothing else matched" and "I stopped looking" are very
/// different answers to give someone hunting for a preset.
/// </para>
/// </remarks>
public sealed record AbletonBrowserListing
{
    private static readonly IReadOnlyList<AbletonBrowserEntry> NoEntries = Array.Empty<AbletonBrowserEntry>();

    private AbletonBrowserListing(
        string category,
        string path,
        IReadOnlyList<AbletonBrowserEntry> entries,
        bool complete,
        string? error)
    {
        Category = category;
        Path = path;
        Entries = entries;
        Complete = complete;
        Error = error;
    }

    /// <summary>Which browser section this came from.</summary>
    public string Category { get; }

    /// <summary>The folder walked into, or — for a search — what was searched for.</summary>
    public string Path { get; }

    public IReadOnlyList<AbletonBrowserEntry> Entries { get; }

    /// <summary>False when the script stopped early at one of its ceilings.</summary>
    public bool Complete { get; }

    /// <summary>Why there is nothing to show, or null when the answer arrived.</summary>
    public string? Error { get; }

    public bool Success => Error is null;

    public static AbletonBrowserListing Ok(
        string category,
        string path,
        IReadOnlyList<AbletonBrowserEntry> entries,
        bool complete) =>
        new(category, path, entries, complete, null);

    public static AbletonBrowserListing Failed(string category, string path, string error) =>
        new(category, path, NoEntries, true, error);
}
