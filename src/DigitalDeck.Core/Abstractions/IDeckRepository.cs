using DigitalDeck.Core.Model;

namespace DigitalDeck.Core.Abstractions;

/// <summary>
/// Storage for the deck tree. Tree shape is an implementation detail — callers ask for the
/// children of a folder or a whole subtree and never see sort keys or recursive queries.
/// </summary>
public interface IDeckRepository
{
    Task<IReadOnlyList<Profile>> GetProfilesAsync(CancellationToken ct = default);
    /// <param name="createDefaultPage">
    /// False when the caller supplies its own pages, as an import does.
    /// </param>
    Task<Profile> CreateProfileAsync(string name, bool createDefaultPage = true, CancellationToken ct = default);
    Task RenameProfileAsync(string profileId, string name, CancellationToken ct = default);
    Task DeleteProfileAsync(string profileId, CancellationToken ct = default);

    /// <summary>Deep copy of a profile, including every page, item and icon reference.</summary>
    Task<Profile> DuplicateProfileAsync(string profileId, string newName, CancellationToken ct = default);

    Task<IReadOnlyList<Page>> GetPagesAsync(string profileId, CancellationToken ct = default);
    Task<Page?> GetPageAsync(string pageId, CancellationToken ct = default);
    Task<Page> CreatePageAsync(string profileId, string name, int columns, int rows, CancellationToken ct = default);
    Task UpdatePageAsync(Page page, CancellationToken ct = default);
    Task DeletePageAsync(string pageId, CancellationToken ct = default);

    /// <summary>Items directly inside a folder, or at the page root when <paramref name="parentId"/> is null.</summary>
    Task<IReadOnlyList<DeckItem>> GetChildrenAsync(string pageId, string? parentId, CancellationToken ct = default);

    /// <summary>An item plus every descendant, used for export and for duplicating a folder.</summary>
    Task<IReadOnlyList<DeckItem>> GetSubtreeAsync(string itemId, CancellationToken ct = default);

    /// <summary>Every item on a page, at any depth — used for export and for rendering a whole page.</summary>
    Task<IReadOnlyList<DeckItem>> GetPageItemsAsync(string pageId, CancellationToken ct = default);

    /// <summary>
    /// Every item in a profile, across all of its pages and at any depth. Used by the search, which
    /// has to look where the user is not.
    /// </summary>
    Task<IReadOnlyList<DeckItem>> GetProfileItemsAsync(string profileId, CancellationToken ct = default);

    Task<DeckItem?> GetItemAsync(string itemId, CancellationToken ct = default);
    Task AddItemAsync(DeckItem item, CancellationToken ct = default);
    Task AddItemsAsync(IEnumerable<DeckItem> items, CancellationToken ct = default);
    Task UpdateItemAsync(DeckItem item, CancellationToken ct = default);

    /// <summary>Deletes an item and, for a folder, everything inside it.</summary>
    Task DeleteItemAsync(string itemId, CancellationToken ct = default);

    /// <summary>Persists a completed drag: new parent, new cell, new position among siblings.</summary>
    Task MoveItemAsync(string itemId, string? newParentId, int col, int row, string sortKey, CancellationToken ct = default);

    /// <summary>Persists both halves of a swap in one transaction.</summary>
    Task SwapPositionsAsync(string firstId, int firstCol, int firstRow, string secondId, int secondCol, int secondRow, CancellationToken ct = default);
}
