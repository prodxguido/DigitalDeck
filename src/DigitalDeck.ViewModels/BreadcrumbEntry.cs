namespace DigitalDeck.ViewModels;

/// <summary>One step in the folder path shown above the grid.</summary>
/// <param name="ItemId">Folder being shown, or null for the page root.</param>
public sealed record BreadcrumbEntry(string? ItemId, string Label)
{
    public bool IsRoot => ItemId is null;
}
