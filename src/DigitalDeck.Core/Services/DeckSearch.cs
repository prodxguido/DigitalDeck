using System.Globalization;
using System.Text;
using DigitalDeck.Core.Model;

namespace DigitalDeck.Core.Services;

/// <summary>One folder on the way to a hit, carrying the id the deck needs to navigate there.</summary>
public sealed record DeckSearchStep(string Id, string Label);

/// <summary>One item found by <see cref="DeckSearch"/>, with enough context to say where it lives.</summary>
/// <param name="FolderPath">Folders between the page root and the item, outermost first.</param>
/// <param name="Score">Lower is a better match; only meaningful for ordering.</param>
public sealed record DeckSearchHit(
    DeckItem Item,
    string PageId,
    string PageName,
    IReadOnlyList<DeckSearchStep> FolderPath,
    int Score)
{
    public bool IsFolder => Item.Type == ItemType.Folder;

    /// <summary>Breadcrumb shown under the label, so two buttons with the same name are told apart.</summary>
    public string Location => string.Join(" / ", new[] { PageName }.Concat(FolderPath.Select(f => f.Label)));
}

/// <summary>
/// Finds items by name across every page of a profile.
/// </summary>
/// <remarks>
/// A deck that grew past a couple of pages stops being navigable by memory: the button exists, but
/// nobody remembers which folder it was filed under. Searching sidesteps the tree entirely — the
/// name is the one thing the user does remember.
/// </remarks>
public static class DeckSearch
{
    /// <summary>The list is meant to be skimmed, not paged through; a longer one means refining the query.</summary>
    public const int MaxResults = 60;

    public static IReadOnlyList<DeckSearchHit> Find(
        IEnumerable<DeckItem> items,
        IEnumerable<Page> pages,
        string? query)
    {
        var needle = Normalise(query);
        if (needle.Length == 0) return Array.Empty<DeckSearchHit>();

        var tokens = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var pageNames = pages.ToDictionary(p => p.Id, p => p.Name);
        var byId = items.ToDictionary(i => i.Id);

        var hits = new List<DeckSearchHit>();

        foreach (var item in byId.Values)
        {
            var label = Normalise(item.Label);
            var summary = Normalise(item.Action?.Summary);

            var score = ScoreOf(label, summary, needle, tokens);
            if (score is null) continue;

            hits.Add(new DeckSearchHit(
                item,
                item.PageId,
                pageNames.TryGetValue(item.PageId, out var name) ? name : string.Empty,
                PathTo(item, byId),
                score.Value));
        }

        return hits
            .OrderBy(h => h.Score)
            .ThenBy(h => h.Item.Label, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxResults)
            .ToList();
    }

    /// <summary>
    /// Every item in the profile, each carrying where it lives.
    /// </summary>
    /// <remarks>
    /// The same walk as <see cref="Find"/> without the filtering, for the voice assistant: it does
    /// not have a query to narrow things down with, it has to hand the recogniser every name it is
    /// allowed to hear up front. Sharing <see cref="PathTo"/> is the point — a second walk that
    /// built folder paths its own way would drift, and the paths are what navigation depends on.
    /// </remarks>
    public static IReadOnlyList<DeckSearchHit> All(IEnumerable<DeckItem> items, IEnumerable<Page> pages)
    {
        var pageNames = pages.ToDictionary(p => p.Id, p => p.Name);
        var byId = items.ToDictionary(i => i.Id);

        return byId.Values
            .Select(item => new DeckSearchHit(
                item,
                item.PageId,
                pageNames.TryGetValue(item.PageId, out var name) ? name : string.Empty,
                PathTo(item, byId),
                Score: 0))
            .ToList();
    }

    /// <summary>
    /// How good a match this is, or null for none.
    /// </summary>
    /// <remarks>
    /// The order matters more than the numbers: somebody typing "comp" wants the button called
    /// «Compressor» before the one whose label says something else and only mentions a compressor
    /// in its action. Matching every word separately is what makes "eq ableton" work.
    /// </remarks>
    private static int? ScoreOf(string label, string summary, string needle, string[] tokens)
    {
        if (label == needle) return 0;
        if (label.StartsWith(needle, StringComparison.Ordinal)) return 1;
        if (label.Contains(needle, StringComparison.Ordinal)) return 2;
        if (tokens.All(t => label.Contains(t, StringComparison.Ordinal))) return 3;

        var haystack = label + " " + summary;
        return tokens.All(t => haystack.Contains(t, StringComparison.Ordinal)) ? 4 : null;
    }

    /// <summary>Folder labels from the page root down to the item's parent.</summary>
    private static IReadOnlyList<DeckSearchStep> PathTo(DeckItem item, IReadOnlyDictionary<string, DeckItem> byId)
    {
        var path = new List<DeckSearchStep>();
        var parentId = item.ParentId;

        // Guarded against a corrupt tree looping back on itself rather than hanging the search.
        var seen = new HashSet<string>();

        while (parentId is not null && seen.Add(parentId) && byId.TryGetValue(parentId, out var parent))
        {
            path.Add(new DeckSearchStep(parent.Id, parent.Label));
            parentId = parent.ParentId;
        }

        path.Reverse();
        return path;
    }

    /// <summary>
    /// Lowercases and strips accents, so "compresion" finds «Compresión».
    /// </summary>
    /// <remarks>
    /// Labels here are written in Spanish and typed in a hurry, usually without the accent. A
    /// search that respected them would miss the very buttons it exists to find.
    /// </remarks>
    private static string Normalise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsWhiteSpace(c) ? ' ' : c);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
