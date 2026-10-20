namespace DigitalDeck.Core.Model;

/// <summary>
/// Top of the hierarchy: a complete, independent deck configuration ("Trap", "Mixing", "Live").
/// Switching profiles is a full context switch — organising within one context is what folders are for.
/// </summary>
public sealed class Profile
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string SortKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public static Profile New(string name, string sortKey) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Name = name,
        SortKey = sortKey
    };
}
