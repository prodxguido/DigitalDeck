using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Core.Ordering;
using DigitalDeck.Data.Packs;
using Xunit;

namespace DigitalDeck.Tests;

public class DeckPackTests
{
    [Fact]
    public async Task ExportingAndImportingAProfileKeepsTheWholeTree()
    {
        using var deck = new DeckWorkspace();

        var profile = await deck.Repository.CreateProfileAsync("Trap");
        var page = (await deck.Repository.GetPagesAsync(profile.Id))[0];

        var folder = DeckItem.NewFolder(page.Id, null, FractionalIndex.Last(), "MIXING");
        folder.Col = 1;
        folder.Row = 1;
        folder.ColorHex = "#5FD88F";

        var nested = DeckItem.NewFolder(page.Id, folder.Id, FractionalIndex.Last(), "VOCAL");

        var link = DeckItem.NewButton(page.Id, nested.Id, FractionalIndex.Last(), "Splice");
        link.Action = new OpenUrlAction { Url = "https://splice.com" };
        link.Width = 2;

        await deck.Repository.AddItemsAsync(new[] { folder, nested, link });

        var packFile = Path.Combine(deck.Root, "trap.deckpack");
        await deck.Packs.ExportProfileAsync(profile.Id, packFile);

        Assert.True(File.Exists(packFile));

        var imported = await deck.Packs.ImportAsync(packFile);
        var importedPages = await deck.Repository.GetPagesAsync(imported.Id);
        var importedRoot = await deck.Repository.GetChildrenAsync(importedPages[0].Id, null);
        var importedFolder = importedRoot.Single();

        var importedNested = (await deck.Repository.GetChildrenAsync(importedPages[0].Id, importedFolder.Id)).Single();
        var importedLink = (await deck.Repository.GetChildrenAsync(importedPages[0].Id, importedNested.Id)).Single();

        Assert.Equal("MIXING", importedFolder.Label);
        Assert.Equal((1, 1), (importedFolder.Col, importedFolder.Row));
        Assert.Equal("#5FD88F", importedFolder.ColorHex);
        Assert.Equal("VOCAL", importedNested.Label);
        Assert.Equal(2, importedLink.Width);
        Assert.Equal("https://splice.com", Assert.IsType<OpenUrlAction>(importedLink.Action).Url);

        // The import must not disturb what was already there.
        Assert.NotEqual(profile.Id, imported.Id);
        Assert.NotEqual(folder.Id, importedFolder.Id);
    }

    [Fact]
    public async Task ImportingTwiceProducesDistinctProfileNames()
    {
        using var deck = new DeckWorkspace();

        var profile = await deck.Repository.CreateProfileAsync("Mastering");
        var packFile = Path.Combine(deck.Root, "mastering.deckpack");
        await deck.Packs.ExportProfileAsync(profile.Id, packFile);

        var first = await deck.Packs.ImportAsync(packFile);
        var second = await deck.Packs.ImportAsync(packFile);

        Assert.NotEqual(first.Name, second.Name);
        Assert.NotEqual("Mastering", first.Name);
    }

    [Fact]
    public async Task ExportingASingleGroupProducesAPackRootedAtThatGroup()
    {
        using var deck = new DeckWorkspace();

        var profile = await deck.Repository.CreateProfileAsync("Estudio");
        var page = (await deck.Repository.GetPagesAsync(profile.Id))[0];

        var folder = DeckItem.NewFolder(page.Id, null, FractionalIndex.Last(), "VOCAL CHAIN");
        folder.Col = 4;
        folder.Row = 2;

        var child = DeckItem.NewButton(page.Id, folder.Id, FractionalIndex.Last(), "Pro-Q");
        var loose = DeckItem.NewButton(page.Id, null, FractionalIndex.Last(folder.SortKey), "Fuera del grupo");

        await deck.Repository.AddItemsAsync(new[] { folder, child, loose });

        var packFile = Path.Combine(deck.Root, "vocal.deckpack");
        await deck.Packs.ExportFolderAsync(folder.Id, packFile);

        var imported = await deck.Packs.ImportAsync(packFile);
        var importedPages = await deck.Repository.GetPagesAsync(imported.Id);
        var root = await deck.Repository.GetChildrenAsync(importedPages[0].Id, null);
        var importedFolder = root.Single();

        Assert.Equal("VOCAL CHAIN", importedFolder.Label);

        // Re-rooted to the first cell so it lands cleanly on whatever page it is dropped into.
        Assert.Equal((0, 0), (importedFolder.Col, importedFolder.Row));

        var contents = await deck.Repository.GetChildrenAsync(importedPages[0].Id, importedFolder.Id);
        Assert.Equal("Pro-Q", contents.Single().Label);
    }

    [Fact]
    public async Task IconsTravelInsideThePack()
    {
        using var deck = new DeckWorkspace();

        var profile = await deck.Repository.CreateProfileAsync("Con iconos");
        var page = (await deck.Repository.GetPagesAsync(profile.Id))[0];

        var item = DeckItem.NewButton(page.Id, null, FractionalIndex.Last(), "Con imagen");
        item.IconPath = await deck.Icons.ImportAsync(deck.WriteSampleImage());
        await deck.Repository.AddItemAsync(item);

        var packFile = Path.Combine(deck.Root, "iconos.deckpack");
        await deck.Packs.ExportProfileAsync(profile.Id, packFile);

        var imported = await deck.Packs.ImportAsync(packFile);
        var importedPages = await deck.Repository.GetPagesAsync(imported.Id);
        var importedItem = (await deck.Repository.GetChildrenAsync(importedPages[0].Id, null)).Single();

        Assert.NotNull(importedItem.IconPath);
        Assert.NotNull(deck.Icons.GetAbsolutePath(importedItem.IconPath));

        // Same bytes means the same content hash, so the icon is stored once, not duplicated.
        Assert.Equal(item.IconPath, importedItem.IconPath);
    }

    [Fact]
    public async Task APackFromANewerFormatIsRejectedRatherThanPartiallyRead()
    {
        using var deck = new DeckWorkspace();

        var profile = await deck.Repository.CreateProfileAsync("Futuro");
        var packFile = Path.Combine(deck.Root, "futuro.deckpack");
        await deck.Packs.ExportProfileAsync(profile.Id, packFile);

        RewriteFormatVersion(packFile, DeckPack.CurrentFormatVersion + 1);

        await Assert.ThrowsAsync<InvalidDataException>(() => deck.Packs.ImportAsync(packFile));
    }

    [Fact]
    public async Task AFileThatIsNotAPackIsRejected()
    {
        using var deck = new DeckWorkspace();

        var bogus = Path.Combine(deck.Root, "no-es-un-pack.deckpack");
        await File.WriteAllTextAsync(bogus, "esto no es un zip");

        await Assert.ThrowsAnyAsync<Exception>(() => deck.Packs.ImportAsync(bogus));
    }

    private static void RewriteFormatVersion(string packFile, int version)
    {
        var rewritten = packFile + ".tmp";

        using (var source = System.IO.Compression.ZipFile.OpenRead(packFile))
        using (var target = System.IO.Compression.ZipFile.Open(rewritten, System.IO.Compression.ZipArchiveMode.Create))
        {
            foreach (var entry in source.Entries)
            {
                var copy = target.CreateEntry(entry.FullName);

                using var input = entry.Open();
                using var output = copy.Open();

                if (entry.FullName != "manifest.json")
                {
                    input.CopyTo(output);
                    continue;
                }

                using var reader = new StreamReader(input);
                using var writer = new StreamWriter(output);

                var json = reader.ReadToEnd()
                    .Replace($"\"format_version\": {DeckPack.CurrentFormatVersion}", $"\"format_version\": {version}");

                writer.Write(json);
            }
        }

        File.Move(rewritten, packFile, overwrite: true);
    }
}
