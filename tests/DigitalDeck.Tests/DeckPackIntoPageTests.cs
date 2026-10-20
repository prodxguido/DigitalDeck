using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Core.Ordering;
using Xunit;

namespace DigitalDeck.Tests;

/// <summary>
/// Importing a pack into a page that already exists, rather than as a profile of its own.
/// </summary>
/// <remarks>
/// This is the path templates take, so what matters is that several packs can pile into one page
/// without treading on each other or on what the user already had there.
/// </remarks>
public class DeckPackIntoPageTests
{
    /// <summary>Exports a one-folder pack, which is the shape every template has.</summary>
    private static async Task<string> WritePackAsync(
        DeckWorkspace deck,
        string label,
        int buttons = 3,
        string fileName = "pack.deckpack")
    {
        var profile = await deck.Repository.CreateProfileAsync($"origen-{label}");
        var page = (await deck.Repository.GetPagesAsync(profile.Id))[0];

        var folder = DeckItem.NewFolder(page.Id, null, FractionalIndex.Last(), label);
        folder.Col = 3;
        folder.Row = 2;

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
    public async Task APackArrivesAsAGroupOnThePageWithoutCreatingAProfile()
    {
        using var deck = new DeckWorkspace();

        var packFile = await WritePackAsync(deck, "VALHALLA");

        var target = await deck.Repository.CreateProfileAsync("Estudio");
        var page = (await deck.Repository.GetPagesAsync(target.Id))[0];

        var before = (await deck.Repository.GetProfilesAsync()).Count;

        var added = await deck.Packs.ImportIntoPageAsync(packFile, page.Id);

        var root = Assert.Single(added);
        Assert.Equal("VALHALLA", root.Label);
        Assert.Equal(ItemType.Folder, root.Type);
        Assert.Equal(page.Id, root.PageId);
        Assert.Null(root.ParentId);

        // The whole point: no new profile, and the deck the user was in is the deck they stay in.
        Assert.Equal(before, (await deck.Repository.GetProfilesAsync()).Count);

        var contents = await deck.Repository.GetChildrenAsync(page.Id, root.Id);
        Assert.Equal(3, contents.Count);
        Assert.All(contents, item => Assert.IsType<OpenUrlAction>(item.Action));
    }

    [Fact]
    public async Task SeveralPacksSitSideBySideInsteadOfOnTopOfEachOther()
    {
        using var deck = new DeckWorkspace();

        var first = await WritePackAsync(deck, "VALHALLA", fileName: "a.deckpack");
        var second = await WritePackAsync(deck, "SOUNDTOYS", fileName: "b.deckpack");
        var third = await WritePackAsync(deck, "D16", fileName: "c.deckpack");

        var target = await deck.Repository.CreateProfileAsync("Estudio");
        var page = (await deck.Repository.GetPagesAsync(target.Id))[0];

        foreach (var pack in new[] { first, second, third })
            await deck.Packs.ImportIntoPageAsync(pack, page.Id);

        var roots = await deck.Repository.GetChildrenAsync(page.Id, null);

        Assert.Equal(3, roots.Count);
        Assert.Equal(3, roots.Select(r => (r.Col, r.Row)).Distinct().Count());
        Assert.Equal(new[] { "VALHALLA", "SOUNDTOYS", "D16" }, roots.Select(r => r.Label).ToArray());
    }

    [Fact]
    public async Task WhatWasAlreadyOnThePageIsLeftWhereItWas()
    {
        using var deck = new DeckWorkspace();

        var packFile = await WritePackAsync(deck, "IZOTOPE");

        var target = await deck.Repository.CreateProfileAsync("Estudio");
        var page = (await deck.Repository.GetPagesAsync(target.Id))[0];

        var mine = DeckItem.NewButton(page.Id, null, FractionalIndex.Last(), "Mío");
        mine.Col = 0;
        mine.Row = 0;
        await deck.Repository.AddItemAsync(mine);

        var root = (await deck.Packs.ImportIntoPageAsync(packFile, page.Id))[0];

        var stored = await deck.Repository.GetItemAsync(mine.Id);

        Assert.Equal((0, 0), (stored!.Col, stored.Row));
        Assert.NotEqual((0, 0), (root.Col, root.Row));
    }

    [Fact]
    public async Task APackCanBeImportedInsideAFolderThatIsAlreadyThere()
    {
        using var deck = new DeckWorkspace();

        var packFile = await WritePackAsync(deck, "U-HE");

        var target = await deck.Repository.CreateProfileAsync("Estudio");
        var page = (await deck.Repository.GetPagesAsync(target.Id))[0];

        var shelf = DeckItem.NewFolder(page.Id, null, FractionalIndex.Last(), "PLUGINS");
        await deck.Repository.AddItemAsync(shelf);

        var root = (await deck.Packs.ImportIntoPageAsync(packFile, page.Id, shelf.Id))[0];

        Assert.Equal(shelf.Id, root.ParentId);
        Assert.Empty((await deck.Repository.GetChildrenAsync(page.Id, null)).Where(i => i.Label == "U-HE"));
    }

    [Fact]
    public async Task AFullPageIsRefusedByNameRatherThanLosingTheTiles()
    {
        using var deck = new DeckWorkspace();

        var packFile = await WritePackAsync(deck, "ROLAND");

        var target = await deck.Repository.CreateProfileAsync("Estudio");
        var page = (await deck.Repository.GetPagesAsync(target.Id))[0];

        var filler = new List<DeckItem>();
        for (var row = 0; row < page.Rows; row++)
        for (var col = 0; col < page.Columns; col++)
        {
            var button = DeckItem.NewButton(page.Id, null, FractionalIndex.Last(), $"{col},{row}");
            button.Col = col;
            button.Row = row;
            filler.Add(button);
        }

        await deck.Repository.AddItemsAsync(filler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => deck.Packs.ImportIntoPageAsync(packFile, page.Id));

        Assert.Contains("ROLAND", error.Message, StringComparison.Ordinal);

        // Nothing half-written: the page holds exactly what it held before.
        Assert.Equal(filler.Count, (await deck.Repository.GetPageItemsAsync(page.Id)).Count);
    }

    [Fact]
    public async Task ContentsThatWouldFallOffASmallerPageArePlacedWhereTheyCanBeReached()
    {
        using var deck = new DeckWorkspace();

        // Built on a wide page, imported into a narrow one: the cells the pack remembers do not
        // all exist any more, and a tile outside the grid is a tile nobody can press.
        var packFile = await WritePackAsync(deck, "WAVES", buttons: 12);

        var target = await deck.Repository.CreateProfileAsync("Estudio");
        var page = await deck.Repository.CreatePageAsync(target.Id, "Chica", 3, 4);

        var root = (await deck.Packs.ImportIntoPageAsync(packFile, page.Id))[0];
        var contents = await deck.Repository.GetChildrenAsync(page.Id, root.Id);

        Assert.Equal(12, contents.Count);

        Assert.All(contents, item =>
        {
            Assert.InRange(item.Col, 0, page.Columns - 1);
            Assert.InRange(item.Row, 0, page.Rows - 1);
        });

        Assert.Equal(contents.Count, contents.Select(i => (i.Col, i.Row)).Distinct().Count());
    }

    [Fact]
    public async Task APackWithMoreInAGroupThanThePageHoldsSaysSo()
    {
        using var deck = new DeckWorkspace();

        var packFile = await WritePackAsync(deck, "WAVES", buttons: 20);

        var target = await deck.Repository.CreateProfileAsync("Estudio");
        var page = await deck.Repository.CreatePageAsync(target.Id, "Chica", 3, 3);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => deck.Packs.ImportIntoPageAsync(packFile, page.Id));

        Assert.Contains("20", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryShippedTemplateGoesIntoOneSinglePage()
    {
        // The reason this path exists. Six shelves of plugins, one profile, one page.
        using var deck = new DeckWorkspace();

        var target = await deck.Repository.CreateProfileAsync("Estudio");
        var page = (await deck.Repository.GetPagesAsync(target.Id))[0];

        // Every template in the folder, so one added later is covered without touching this test.
        var templates = Directory
            .GetFiles(TemplatesFolder(), "*.deckpack")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.NotEmpty(templates);

        foreach (var template in templates)
            await deck.Packs.ImportIntoPageAsync(template, page.Id);

        var roots = await deck.Repository.GetChildrenAsync(page.Id, null);

        Assert.Equal(templates.Count, roots.Count);
        Assert.All(roots, root => Assert.Equal(ItemType.Folder, root.Type));
        Assert.Equal(roots.Count, roots.Select(r => (r.Col, r.Row)).Distinct().Count());
        Assert.Single(await deck.Repository.GetPagesAsync(target.Id));
    }

    /// <summary>Walks up from the test binary to the templates folder in the repository.</summary>
    private static string TemplatesFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "plantillas");
            if (Directory.Exists(candidate)) return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró la carpeta «plantillas».");
    }
}
