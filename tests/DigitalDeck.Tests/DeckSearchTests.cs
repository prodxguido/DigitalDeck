using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Core.Services;
using Xunit;

namespace DigitalDeck.Tests;

/// <summary>
/// The search is the way in for a deck the user no longer remembers the shape of, so these cover
/// what it has to survive: accents nobody types, results that share a name, and the ranking that
/// makes pressing Enter safe.
/// </summary>
public class DeckSearchTests
{
    private static readonly Page Home = new() { Id = "p1", ProfileId = "prof", Name = "Inicio", SortKey = "a", Columns = 6, Rows = 4 };
    private static readonly Page Live = new() { Id = "p2", ProfileId = "prof", Name = "Live", SortKey = "b", Columns = 6, Rows = 4 };

    private static DeckItem Button(string id, string page, string label, string? parent = null) => new()
    {
        Id = id,
        PageId = page,
        ParentId = parent,
        SortKey = "a",
        Type = ItemType.Button,
        Label = label
    };

    private static DeckItem Folder(string id, string page, string label, string? parent = null) => new()
    {
        Id = id,
        PageId = page,
        ParentId = parent,
        SortKey = "a",
        Type = ItemType.Folder,
        Label = label
    };

    [Fact]
    public void FindsAButtonBuriedInsideNestedFolders()
    {
        var items = new[]
        {
            Folder("f1", "p1", "PRODUCCIÓN"),
            Folder("f2", "p1", "Mezcla", "f1"),
            Button("b1", "p1", "Compresor", "f2")
        };

        var hit = Assert.Single(DeckSearch.Find(items, new[] { Home }, "compresor"));

        Assert.Equal("b1", hit.Item.Id);
        Assert.Equal("Inicio / PRODUCCIÓN / Mezcla", hit.Location);
    }

    [Fact]
    public void IgnoresAccentsAndCaseSoTheLabelCanBeTypedInAHurry()
    {
        var items = new[] { Button("b1", "p1", "Compresión") };

        Assert.Single(DeckSearch.Find(items, new[] { Home }, "COMPRESION"));
    }

    [Fact]
    public void PutsTheClosestNameFirst()
    {
        var items = new[]
        {
            Button("b1", "p1", "Recompresor viejo"),
            Button("b2", "p1", "Compresor"),
            Button("b3", "p1", "Compresor de bus")
        };

        var order = DeckSearch.Find(items, new[] { Home }, "compresor").Select(h => h.Item.Id).ToList();

        Assert.Equal(new[] { "b2", "b3", "b1" }, order);
    }

    [Fact]
    public void MatchesEveryWordSeparatelySoAPartialMemoryStillLands()
    {
        var items = new[] { Button("b1", "p1", "EQ de voz principal") };

        Assert.Single(DeckSearch.Find(items, new[] { Home }, "voz eq"));
        Assert.Empty(DeckSearch.Find(items, new[] { Home }, "voz bateria"));
    }

    [Fact]
    public void FallsBackToWhatTheButtonDoesWhenTheLabelSaysNothing()
    {
        var item = Button("b1", "p1", "Carpeta de bounces");
        item.Action = new OpenPathAction { Path = @"D:\Bounces" };

        var hit = Assert.Single(DeckSearch.Find(new[] { item }, new[] { Home }, "bounces"));
        Assert.Equal("b1", hit.Item.Id);
    }

    [Fact]
    public void LooksAcrossEveryPageOfTheProfile()
    {
        var items = new[]
        {
            Button("b1", "p1", "Arrancar set"),
            Button("b2", "p2", "Arrancar grabación")
        };

        var hits = DeckSearch.Find(items, new[] { Home, Live }, "arrancar");

        Assert.Equal(2, hits.Count);
        Assert.Contains(hits, h => h.PageName == "Live");
    }

    [Fact]
    public void AnEmptyQueryReturnsNothingRatherThanTheWholeDeck()
    {
        var items = new[] { Button("b1", "p1", "Compresor") };

        Assert.Empty(DeckSearch.Find(items, new[] { Home }, "   "));
        Assert.Empty(DeckSearch.Find(items, new[] { Home }, null));
    }
}
