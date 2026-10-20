namespace DigitalDeck.Core.Layout;

/// <summary>A tile's footprint in grid cells.</summary>
public readonly record struct GridRect(int Col, int Row, int Width, int Height)
{
    public int Right => Col + Width;
    public int Bottom => Row + Height;

    public bool Overlaps(GridRect other) =>
        Col < other.Right && other.Col < Right &&
        Row < other.Bottom && other.Row < Bottom;

    public bool Contains(int col, int row) =>
        col >= Col && col < Right && row >= Row && row < Bottom;

    public bool FitsWithin(int columns, int rows) =>
        Col >= 0 && Row >= 0 && Width >= 1 && Height >= 1 && Right <= columns && Bottom <= rows;
}
