namespace DigitalDeck.Core.Model;

/// <summary>
/// What a tile in the grid is. Deliberately minimal: a folder is just an item that holds other
/// items, so nesting has no depth limit and no category is imposed on the user.
/// </summary>
public enum ItemType
{
    /// <summary>Runs an action when pressed.</summary>
    Button = 0,

    /// <summary>Holds other items; pressing it navigates into them.</summary>
    Folder = 1
}
