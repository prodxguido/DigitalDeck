using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Core.Ordering;
using Xunit;

namespace DigitalDeck.Tests;

/// <summary>
/// Importing a pack as pages of the profile already open, rather than as a group or as a deck of
/// its own.
/// </summary>
/// <remarks>
/// The answer that matters for anything big. A shelf of fifteen plugins filed as a group costs a
/// press to reach and a cell to hold, and the shape a pack travels in — one folder wrapping the
/// lot — is exactly the shape that has to be opened out for a page to be worth having. That
/// unwrapping, and the fact that nothing else about the deck changes, is what these cover.
/// </remarks>
public class DeckPackAsPagesTests
{
    /// <summary>Exports a one-folder pack, which is the shape every template has.</summary>
    private static async Task<string> WriteGroupPackAsync(
        DeckWorkspace deck,
        string label,
        int buttons = 3,
        string fileName = "grupo.deckpack")
    {
        var profile = await deck.Repository.CreateProfileAsync($"origen-{label}");
        var page = (await deck.Repository.GetPagesAsync(profile.Id))[0];

        var folder = DeckItem.NewFolder(page.Id, null, FractionalIndex.Last(), label);
        var items = new List<DeckItem> { folder };

        for (var i = 0; i < buttons; i++)
        {
            var button = DeckItem.NewButton(page.Id, folder.Id, FractionalIndex.Last(), $"{label} {i + 1}");
            button.Col = i % page.Columns;
            button.Row = i / page.Columns;
            button.Action = new OpenUrlAction { Url = $"https://example.com/{i}" };
            items.Add(button);
        }

        await deck.Repository.AddItemsAsync(items);

        var packFile = Path.Combine(deck.Root, fileName);
        await deck.Packs.ExportFolderAsync(folder.Id, packFile);

        return packFile;
    }

    [Fact]
    public async Task AGroupPackBecomesAPageWithItsContentsOnTheGrid()
    {
        using var deck = new DeckWorkspace();

        var packFile = await WriteGroupPackAsync(deck, "VALHALLA");

        var target = await deck.Repository.CreateProfileAsync("Estudio");
        var profilesBefore = (await deck.Repository.GetProfilesAsync()).Count;

        var created = await deck.Packs.ImportAsPagesAsync(packFile, target.Id);

        var page = Assert.Single(created);
        Assert.Equal("VALHALLA", page.Name);
        Assert.Equal(target.Id, page.ProfileId);

        // The wrapper is gone and what was inside it is the page: a tab holding one tile to press
        // would be the same click the group answer costs, for the price of a whole page.
        var placed = await deck.Repository.GetChildrenAsync(page.Id, null);

        Assert.Equal(3, placed.Count);
        Assert.All(placed, item => Assert.Equal(ItemType.Button, item.Type));
        Assert.All(placed, item => Assert.IsType<OpenUrlAction>(item.Action));

        // Pages of the deck that was open, not a deck of its own to switch to.
        Assert.Equal(profilesBefore, (await deck.Repository.GetProfilesAsync()).Count);
    }

    [Fact]
    public async Task ThePageThatWasAlreadyThereIsLeftAlone()
    {
        using var deck = new DeckWorkspace();

        var packFile = await WriteGroupPackAsync(deck, "WAVES");

        var target = await deck.Repository.CreateProfileAsync("Estudio");
        var existing = (await deck.Repository.GetPagesAsync(target.Id))[0];

        var mine = DeckItem.NewButton(existing.Id, null, FractionalIndex.Last(), "Lo mío");
        mine.Col = 2;
        mine.Row = 1;
        await deck.Repository.AddItemAsync(mine);

        await deck.Packs.ImportAsPagesAsync(packFile, target.Id);

        var untouched = Assert.Single(await deck.Repository.GetChildrenAsync(existing.Id, null));

        Assert.Equal("Lo mío", untouched.Label);
        Assert.Equal((2, 1), (untouched.Col, untouched.Row));
        Assert.Equal(2, (await deck.Repository.GetPagesAsync(target.Id)).Count);
    }

    [Fact]
    public async Task TheGridAndTheArrangementComeAcrossUntouched()
    {
        using var deck = new DeckWorkspace();

        var profile = await deck.Repository.CreateProfileAsync("origen");
        var wide = await deck.Repository.CreatePageAsync(profile.Id, "Ancha", 8, 3);

        var folder = DeckItem.NewFolder(wide.Id, null, FractionalIndex.Last(), "MARCA");
        var corner = DeckItem.NewButton(wide.Id, folder.Id, FractionalIndex.Last(), "Esquina");
        corner.Col = 7;
        corner.Row = 2;

        await deck.Repository.AddItemsAsync([folder, corner]);

        var packFile = Path.Combine(deck.Root, "ancha.deckpack");
        await deck.Packs.ExportFolderAsync(folder.Id, packFile);

        var target = await deck.Repository.CreateProfileAsync("Estudio");
        var page = Assert.Single(await deck.Packs.ImportAsPagesAsync(packFile, target.Id));

        // A pack carries the grid it was built on, and a page of its own can simply take it — so
        // nothing has to be re-placed and the arrangement arrives as it was drawn.
        Assert.Equal((8, 3), (page.Columns, page.Rows));

        var arrived = Assert.Single(await deck.Repository.GetChildrenAsync(page.Id, null));
        Assert.Equal((7, 2), (arrived.Col, arrived.Row));
    }

    [Fact]
    public async Task AWholeDeckArrivesAsThePagesItWas()
    {
        using var deck = new DeckWorkspace();

        var origin = await deck.Repository.CreateProfileAsync("Origen");
        var first = (await deck.Repository.GetPagesAsync(origin.Id))[0];
        var second = await deck.Repository.CreatePageAsync(origin.Id, "Efectos", 6, 4);

        // Renamed off the default so this test is about the pages arriving and not about the
        // numbering that a name already in use gets — which has a test of its own.
        first.Name = "Instrumentos";
        await deck.Repository.UpdatePageAsync(first);

        var loose = DeckItem.NewButton(first.Id, null, FractionalIndex.Last(), "Suelto");
        var group = DeckItem.NewFolder(first.Id, null, FractionalIndex.Last(), "GRUPO");
        group.Col = 1;
        var inside = DeckItem.NewButton(first.Id, group.Id, FractionalIndex.Last(), "Adentro");
        var other = DeckItem.NewButton(second.Id, null, FractionalIndex.Last(), "Reverb");

        await deck.Repository.AddItemsAsync([loose, group, inside, other]);

        var packFile = Path.Combine(deck.Root, "deck.deckpack");
        await deck.Packs.ExportProfileAsync(origin.Id, packFile);

        var target = await deck.Repository.CreateProfileAsync("Estudio");
        var created = await deck.Packs.ImportAsPagesAsync(packFile, target.Id);

        // Two pages in, two pages out: a page holding more than one thing is already a page, so
        // there is nothing to open out and nothing to merge.
        Assert.Equal(2, created.Count);
        Assert.Equal([first.Name, "Efectos"], created.Select(page => page.Name));

        var arrived = await deck.Repository.GetChildrenAsync(created[0].Id, null);

        Assert.Equal(["Suelto", "GRUPO"], arrived.Select(item => item.Label));
        Assert.Equal("Adentro", Assert.Single(
            await deck.Repository.GetChildrenAsync(created[0].Id, arrived[1].Id)).Label);
    }

    [Fact]
    public async Task TheSamePackTwiceGetsTwoTabsTellableApart()
    {
        using var deck = new DeckWorkspace();

        var packFile = await WriteGroupPackAsync(deck, "SOUNDTOYS");
        var target = await deck.Repository.CreateProfileAsync("Estudio");

        await deck.Packs.ImportAsPagesAsync(packFile, target.Id);
        var again = Assert.Single(await deck.Packs.ImportAsPagesAsync(packFile, target.Id));

        // Tabs are picked off a strip that shows nothing but the name, so two called the same
        // thing is two tabs to open to find out which is which.
        Assert.Equal("SOUNDTOYS (2)", again.Name);
    }

    [Fact]
    public async Task APackWithNothingInItSaysSoRatherThanAddingAnEmptyTab()
    {
        using var deck = new DeckWorkspace();

        var origin = await deck.Repository.CreateProfileAsync("Vacío");
        var packFile = Path.Combine(deck.Root, "vacio.deckpack");
        await deck.Packs.ExportProfileAsync(origin.Id, packFile);

        var target = await deck.Repository.CreateProfileAsync("Estudio");
        var pagesBefore = (await deck.Repository.GetPagesAsync(target.Id)).Count;

        await Assert.ThrowsAsync<InvalidDataException>(
            () => deck.Packs.ImportAsPagesAsync(packFile, target.Id));

        Assert.Equal(pagesBefore, (await deck.Repository.GetPagesAsync(target.Id)).Count);
    }
}
