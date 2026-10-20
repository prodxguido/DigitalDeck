namespace DigitalDeck.Core.Model;

/// <summary>
/// One grid within a profile. Each page has its own column/row count so a profile can mix a
/// large-button production page with a dense page of shortcuts.
/// </summary>
public sealed class Page
{
    public const int MinColumns = 2;
    public const int MaxColumns = 16;
    public const int MinRows = 2;
    public const int MaxRows = 12;

    public required string Id { get; set; }
    public required string ProfileId { get; set; }
    public required string Name { get; set; }
    public required string SortKey { get; set; }
    public int Columns { get; set; } = 6;
    public int Rows { get; set; } = 4;

    public static Page New(string profileId, string name, string sortKey, int columns = 6, int rows = 4) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        ProfileId = profileId,
        Name = name,
        SortKey = sortKey,
        Columns = Math.Clamp(columns, MinColumns, MaxColumns),
        Rows = Math.Clamp(rows, MinRows, MaxRows)
    };
}
