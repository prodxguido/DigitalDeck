using DigitalDeck.Core.Model;
using DigitalDeck.Core.Services;
using DigitalDeck.Core.Voice;
using Xunit;

namespace DigitalDeck.Tests;

/// <summary>
/// The assistant refuses a tie, so these cover what counts as one: same name in a different case is
/// a collision, a trailing number is not, and a name nobody typed does not count at all.
/// </summary>
public class NameCollisionsTests
{
    private static readonly Page Home = new() { Id = "p1", ProfileId = "prof", Name = "Inicio", SortKey = "a", Columns = 6, Rows = 4 };

    private static DeckItem Button(string id, string label) => new()
    {
        Id = id,
        PageId = "p1",
        SortKey = "a",
        Type = ItemType.Button,
        Label = label
    };

    private static IReadOnlyList<DeckSearchHit> Hits(params DeckItem[] items) =>
        DeckSearch.All(items, new[] { Home });

    [Fact]
    public void GroupsTilesThatDifferOnlyInCase()
    {
        var collision = Assert.Single(NameCollisions.Find(Hits(
            Button("b1", "Estudio"),
            Button("b2", "ESTUDIO"))));

        Assert.Equal("estudio", collision.Name);
        Assert.Equal(new[] { "b1", "b2" }, collision.Tiles.Select(t => t.Item.Id));
    }

    [Fact]
    public void DoesNotGroupNamesThatEndInADifferentNumber()
    {
        Assert.Empty(NameCollisions.Find(Hits(
            Button("b1", "Reverb 1"),
            Button("b2", "Reverb 2"))));
    }

    [Fact]
    public void ASingleTileIsNotACollision()
    {
        Assert.Empty(NameCollisions.Find(Hits(Button("b1", "Estudio"))));
    }

    [Fact]
    public void IgnoresTilesWithNoName()
    {
        Assert.Empty(NameCollisions.Find(Hits(
            Button("b1", ""),
            Button("b2", "   "))));
    }
}
