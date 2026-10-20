using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Core.Ordering;
using Xunit;

namespace DigitalDeck.Tests;

public class DeckRepositoryTests
{
    [Fact]
    public async Task ANewProfileComesWithAPageToStartFrom()
    {
        using var deck = new DeckWorkspace();

        var profile = await deck.Repository.CreateProfileAsync("Trap");
        var pages = await deck.Repository.GetPagesAsync(profile.Id);

        Assert.Single(pages);
    }

    [Fact]
    public async Task AProfileCanBeCreatedWithoutAPageForImports()
    {
        using var deck = new DeckWorkspace();

        var profile = await deck.Repository.CreateProfileAsync("Importado", createDefaultPage: false);

        Assert.Empty(await deck.Repository.GetPagesAsync(profile.Id));
    }

    [Fact]
    public async Task ProfilesComeBackInTheOrderTheyWereCreated()
    {
        using var deck = new DeckWorkspace();

        await deck.Repository.CreateProfileAsync("Uno");
        await deck.Repository.CreateProfileAsync("Dos");
        await deck.Repository.CreateProfileAsync("Tres");

        var names = (await deck.Repository.GetProfilesAsync()).Select(p => p.Name).ToArray();

        Assert.Equal(new[] { "Uno", "Dos", "Tres" }, names);
    }

    [Fact]
    public async Task ChildrenAreScopedToTheirFolder()
    {
        using var deck = new DeckWorkspace();
        var (_, page) = await NewPageAsync(deck);

        var folder = DeckItem.NewFolder(page.Id, null, FractionalIndex.Last(), "MIXING");
        var atRoot = DeckItem.NewButton(page.Id, null, FractionalIndex.Last(folder.SortKey), "Raíz");
        var inside = DeckItem.NewButton(page.Id, folder.Id, FractionalIndex.Last(), "Adentro");

        await deck.Repository.AddItemsAsync(new[] { folder, atRoot, inside });

        var root = await deck.Repository.GetChildrenAsync(page.Id, null);
        var nested = await deck.Repository.GetChildrenAsync(page.Id, folder.Id);

        Assert.Equal(new[] { "MIXING", "Raíz" }, root.Select(i => i.Label).ToArray());
        Assert.Equal(new[] { "Adentro" }, nested.Select(i => i.Label).ToArray());
    }

    [Fact]
    public async Task NestingHasNoDepthLimit()
    {
        using var deck = new DeckWorkspace();
        var (_, page) = await NewPageAsync(deck);

        var chain = new List<DeckItem>();
        string? parentId = null;

        for (var depth = 0; depth < 12; depth++)
        {
            var folder = DeckItem.NewFolder(page.Id, parentId, FractionalIndex.Last(), $"Nivel {depth}");
            chain.Add(folder);
            parentId = folder.Id;
        }

        await deck.Repository.AddItemsAsync(chain);

        var subtree = await deck.Repository.GetSubtreeAsync(chain[0].Id);

        Assert.Equal(12, subtree.Count);
    }

    [Fact]
    public async Task DeletingAFolderTakesItsContentsWithIt()
    {
        using var deck = new DeckWorkspace();
        var (_, page) = await NewPageAsync(deck);

        var folder = DeckItem.NewFolder(page.Id, null, FractionalIndex.Last(), "Grupo");
        var child = DeckItem.NewButton(page.Id, folder.Id, FractionalIndex.Last(), "Hijo");
        var grandchild = DeckItem.NewButton(page.Id, child.Id, FractionalIndex.Last(), "Nieto");
        var untouched = DeckItem.NewButton(page.Id, null, FractionalIndex.Last(folder.SortKey), "Otro");

        await deck.Repository.AddItemsAsync(new[] { folder, child, grandchild, untouched });
        await deck.Repository.DeleteItemAsync(folder.Id);

        Assert.Null(await deck.Repository.GetItemAsync(child.Id));
        Assert.Null(await deck.Repository.GetItemAsync(grandchild.Id));
        Assert.NotNull(await deck.Repository.GetItemAsync(untouched.Id));
    }

    [Fact]
    public async Task MovingAnItemRecordsItsNewFolderAndCell()
    {
        using var deck = new DeckWorkspace();
        var (_, page) = await NewPageAsync(deck);

        var folder = DeckItem.NewFolder(page.Id, null, FractionalIndex.Last(), "Grupo");
        var item = DeckItem.NewButton(page.Id, null, FractionalIndex.Last(folder.SortKey), "Botón");

        await deck.Repository.AddItemsAsync(new[] { folder, item });
        await deck.Repository.MoveItemAsync(item.Id, folder.Id, 2, 3, FractionalIndex.Last());

        var moved = await deck.Repository.GetItemAsync(item.Id);

        Assert.NotNull(moved);
        Assert.Equal(folder.Id, moved!.ParentId);
        Assert.Equal(2, moved.Col);
        Assert.Equal(3, moved.Row);
    }

    [Fact]
    public async Task SwappingExchangesBothPositions()
    {
        using var deck = new DeckWorkspace();
        var (_, page) = await NewPageAsync(deck);

        var first = DeckItem.NewButton(page.Id, null, FractionalIndex.Last(), "A");
        first.Col = 0;
        first.Row = 0;

        var second = DeckItem.NewButton(page.Id, null, FractionalIndex.Last(first.SortKey), "B");
        second.Col = 3;
        second.Row = 2;

        await deck.Repository.AddItemsAsync(new[] { first, second });
        await deck.Repository.SwapPositionsAsync(first.Id, 3, 2, second.Id, 0, 0);

        var movedFirst = await deck.Repository.GetItemAsync(first.Id);
        var movedSecond = await deck.Repository.GetItemAsync(second.Id);

        Assert.Equal((3, 2), (movedFirst!.Col, movedFirst.Row));
        Assert.Equal((0, 0), (movedSecond!.Col, movedSecond.Row));
    }

    [Fact]
    public async Task ActionsSurviveARoundTripThroughTheDatabase()
    {
        using var deck = new DeckWorkspace();
        var (_, page) = await NewPageAsync(deck);

        var web = DeckItem.NewButton(page.Id, null, FractionalIndex.Last(), "Ableton");
        web.Action = new OpenUrlAction { Url = "https://www.ableton.com" };

        var folderButton = DeckItem.NewButton(page.Id, null, FractionalIndex.Last(web.SortKey), "Samples");
        folderButton.Action = new OpenPathAction { Path = @"D:\Samples", Acknowledged = true };

        await deck.Repository.AddItemsAsync(new[] { web, folderButton });

        var stored = await deck.Repository.GetChildrenAsync(page.Id, null);

        var url = Assert.IsType<OpenUrlAction>(stored.Single(i => i.Label == "Ableton").Action);
        var path = Assert.IsType<OpenPathAction>(stored.Single(i => i.Label == "Samples").Action);

        Assert.Equal("https://www.ableton.com", url.Url);
        Assert.Equal(@"D:\Samples", path.Path);
        Assert.True(path.Acknowledged);
    }

    [Fact]
    public async Task DuplicatingAProfileCopiesEveryPageAndKeepsTheTreeShape()
    {
        using var deck = new DeckWorkspace();
        var (profile, page) = await NewPageAsync(deck);

        var folder = DeckItem.NewFolder(page.Id, null, FractionalIndex.Last(), "Grupo");
        var child = DeckItem.NewButton(page.Id, folder.Id, FractionalIndex.Last(), "Hijo");
        await deck.Repository.AddItemsAsync(new[] { folder, child });

        var copy = await deck.Repository.DuplicateProfileAsync(profile.Id, "Copia");
        var copiedPages = await deck.Repository.GetPagesAsync(copy.Id);
        var copiedRoot = await deck.Repository.GetChildrenAsync(copiedPages[0].Id, null);
        var copiedFolder = copiedRoot.Single();
        var copiedChild = await deck.Repository.GetChildrenAsync(copiedPages[0].Id, copiedFolder.Id);

        Assert.Equal("Copia", copy.Name);
        Assert.Single(copiedPages);
        Assert.Equal("Grupo", copiedFolder.Label);
        Assert.Equal("Hijo", copiedChild.Single().Label);

        // The copy has to be independent: fresh ids everywhere, or editing one would change both.
        Assert.NotEqual(folder.Id, copiedFolder.Id);
        Assert.NotEqual(child.Id, copiedChild.Single().Id);
    }

    [Fact]
    public async Task DeletingAProfileRemovesItsPagesAndItems()
    {
        using var deck = new DeckWorkspace();
        var (profile, page) = await NewPageAsync(deck);

        var item = DeckItem.NewButton(page.Id, null, FractionalIndex.Last(), "Botón");
        await deck.Repository.AddItemAsync(item);

        await deck.Repository.DeleteProfileAsync(profile.Id);

        Assert.Empty(await deck.Repository.GetPagesAsync(profile.Id));
        Assert.Null(await deck.Repository.GetItemAsync(item.Id));
    }

    [Fact]
    public async Task SettingsRoundTripAndCanBeOverwritten()
    {
        using var deck = new DeckWorkspace();

        Assert.Null(await deck.Settings.GetAsync(SettingKeys.LastProfileId));

        await deck.Settings.SetAsync(SettingKeys.LastProfileId, "uno");
        await deck.Settings.SetAsync(SettingKeys.LastProfileId, "dos");

        Assert.Equal("dos", await deck.Settings.GetAsync(SettingKeys.LastProfileId));

        await deck.Settings.RemoveAsync(SettingKeys.LastProfileId);

        Assert.Null(await deck.Settings.GetAsync(SettingKeys.LastProfileId));
    }

    [Fact]
    public async Task IdenticalIconsAreStoredOnce()
    {
        using var deck = new DeckWorkspace();

        var first = await deck.Icons.ImportAsync(deck.WriteSampleImage("a.png"));
        var second = await deck.Icons.ImportAsync(deck.WriteSampleImage("b.png"));

        Assert.Equal(first, second);
        Assert.NotNull(deck.Icons.GetAbsolutePath(first));
        Assert.Single(Directory.GetFiles(deck.Paths.IconsDirectory));
    }

    [Fact]
    public async Task UnsupportedImageFormatsAreRejected()
    {
        using var deck = new DeckWorkspace();

        var path = Path.Combine(deck.Root, "icon.svg");
        await File.WriteAllTextAsync(path, "<svg/>");

        await Assert.ThrowsAsync<NotSupportedException>(() => deck.Icons.ImportAsync(path));
    }

    private static async Task<(Profile Profile, Page Page)> NewPageAsync(DeckWorkspace deck)
    {
        var profile = await deck.Repository.CreateProfileAsync("Estudio");
        var page = (await deck.Repository.GetPagesAsync(profile.Id))[0];
        return (profile, page);
    }
}
