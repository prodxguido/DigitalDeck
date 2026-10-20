using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Core.Ordering;

namespace DigitalDeck.ViewModels;

/// <summary>
/// Keeps the "most used" folder holding what the deck is actually used for.
/// </summary>
/// <remarks>
/// The folder is a real folder of real tiles, rewritten when it is opened, rather than a virtual
/// listing. That choice is what keeps everything else — dragging a tile out of it onto the page,
/// editing one, exporting the profile — working exactly as it does anywhere else in the deck.
/// <para>
/// The cost is stated plainly because it will surprise someone: <b>anything put inside this folder
/// by hand is replaced the next time it is opened.</b> It is a view of the ranking, not a place to
/// keep things.
/// </para>
/// </remarks>
public sealed class FrequentFolderService
{
    /// <summary>What the folder is called, wherever it is created.</summary>
    public const string DefaultLabel = "Favoritos";

    public const string DefaultIcon = "star";

    public const string DefaultColorHex = "#5FD88F";

    private readonly IDeckRepository _repository;
    private readonly IAppSettingsStore _settings;
    private readonly IUsageTracker _usage;

    public FrequentFolderService(IDeckRepository repository, IAppSettingsStore settings, IUsageTracker usage)
    {
        _repository = repository;
        _settings = settings;
        _usage = usage;
    }

    /// <summary>The folder this service owns, or null when the profile has none.</summary>
    public async Task<string?> GetFolderIdAsync(CancellationToken ct = default) =>
        await _settings.GetAsync(SettingKeys.FrequentFolderId, ct).ConfigureAwait(false);

    /// <summary>True when <paramref name="itemId"/> is the folder the ranking writes into.</summary>
    public async Task<bool> IsFrequentFolderAsync(string? itemId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(itemId)) return false;

        return string.Equals(await GetFolderIdAsync(ct).ConfigureAwait(false), itemId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rewrites the folder from the ranking. Does nothing when there is no such folder any more —
    /// the user is free to delete it, and deleting it has to mean deleting it.
    /// </summary>
    public async Task RebuildAsync(int columns, int rows, CancellationToken ct = default)
    {
        var folderId = await GetFolderIdAsync(ct).ConfigureAwait(false);
        if (folderId is null) return;

        var folder = await _repository.GetItemAsync(folderId, ct).ConfigureAwait(false);

        if (folder is null || folder.Type != ItemType.Folder)
        {
            await _settings.RemoveAsync(SettingKeys.FrequentFolderId, ct).ConfigureAwait(false);
            return;
        }

        var capacity = Math.Max(columns, 1) * Math.Max(rows, 1);
        var top = await _usage.GetTopAsync(capacity, ct).ConfigureAwait(false);

        var existing = await _repository.GetChildrenAsync(folder.PageId, folder.Id, ct).ConfigureAwait(false);

        // Nothing pressed yet, and nothing stale inside: leaving the folder untouched keeps the
        // first launch from writing to the database on every navigation.
        if (top.Count == 0 && existing.Count == 0) return;

        foreach (var child in existing)
            await _repository.DeleteItemAsync(child.Id, ct).ConfigureAwait(false);

        if (top.Count == 0) return;

        var keys = FractionalIndex.Sequence(top.Count);
        var tiles = new List<DeckItem>(top.Count);

        for (var i = 0; i < top.Count; i++)
        {
            var entry = top[i];

            var tile = DeckItem.NewButton(folder.PageId, folder.Id, keys[i], entry.Label);
            tile.Col = i % Math.Max(columns, 1);
            tile.Row = i / Math.Max(columns, 1);
            tile.ColorHex = entry.ColorHex;
            tile.IconGlyph = entry.Icon;
            tile.Action = entry.Action;

            tiles.Add(tile);
        }

        await _repository.AddItemsAsync(tiles, ct).ConfigureAwait(false);
    }
}
