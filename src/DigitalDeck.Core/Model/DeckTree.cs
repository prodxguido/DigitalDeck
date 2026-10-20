namespace DigitalDeck.Core.Model;

/// <summary>Helpers for working with a flat list of items that forms a tree via <see cref="DeckItem.ParentId"/>.</summary>
public static class DeckTree
{
    /// <summary>
    /// Orders items so a parent always comes before its children. Insertion has to follow this
    /// order because <c>parent_id</c> is a foreign key onto the same table.
    /// </summary>
    public static IReadOnlyList<DeckItem> ParentsFirst(IEnumerable<DeckItem> items)
    {
        var remaining = items.ToList();
        var byParent = remaining
            .GroupBy(i => i.ParentId ?? string.Empty)
            .ToDictionary(g => g.Key, g => g.ToList());

        var known = remaining.Select(i => i.Id).ToHashSet();
        var ordered = new List<DeckItem>(remaining.Count);

        // Roots are items whose parent is null or lives outside this set (e.g. exporting a subtree).
        var queue = new Queue<DeckItem>(remaining.Where(i => i.ParentId is null || !known.Contains(i.ParentId)));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            ordered.Add(current);

            if (!byParent.TryGetValue(current.Id, out var children)) continue;
            foreach (var child in children) queue.Enqueue(child);
        }

        // A cycle would be a corrupt tree; append the stragglers rather than losing them silently.
        if (ordered.Count != remaining.Count)
        {
            var placed = ordered.Select(i => i.Id).ToHashSet();
            ordered.AddRange(remaining.Where(i => !placed.Contains(i.Id)));
        }

        return ordered;
    }
}
