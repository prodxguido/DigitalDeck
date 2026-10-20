using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using Xunit;

namespace DigitalDeck.Tests;

/// <summary>
/// The ready-made templates that ship with the project, read by the same importer a user runs.
/// </summary>
/// <remarks>
/// A template is a hand-written <c>.deckpack</c>, so nothing catches a misspelled field or a wrong
/// enum until somebody tries to import it and gets an error instead of a deck. These tests are
/// that somebody.
/// </remarks>
public class TemplatePackTests
{
    [Theory]
    [InlineData("fabfilter.deckpack")]
    [InlineData("waves.deckpack")]
    [InlineData("valhalla.deckpack")]
    [InlineData("u-he.deckpack")]
    [InlineData("soundtoys.deckpack")]
    [InlineData("roland-cloud.deckpack")]
    [InlineData("izotope.deckpack")]
    [InlineData("d16.deckpack")]
    [InlineData("ableton-dispositivos.deckpack")]
    public async Task EveryTemplateImportsAsAGroupOfWorkingButtons(string fileName)
    {
        using var workspace = new DeckWorkspace();

        var profile = await workspace.Packs.ImportAsync(TemplateFile(fileName));
        var page = Assert.Single(await workspace.Repository.GetPagesAsync(profile.Id));

        var root = Assert.Single(await workspace.Repository.GetChildrenAsync(page.Id, null));
        Assert.Equal(ItemType.Folder, root.Type);

        // A template is one group; how deep it nests inside is its own business — FabFilter is flat,
        // Waves needs a folder per category because one folder shows a single page of tiles.
        var everything = await workspace.Repository.GetPageItemsAsync(page.Id);
        var buttons = everything.Where(i => i.Type == ItemType.Button).ToList();

        Assert.NotEmpty(buttons);

        Assert.All(buttons, button =>
        {
            var action = Assert.IsType<AbletonLoadDeviceAction>(button.Action);

            // Names, not paths and not URIs: a plugin is reached through Live's browser, and the
            // URI is something Live hands out on the machine it is installed on.
            Assert.False(string.IsNullOrWhiteSpace(action.Name));
            Assert.Null(action.Uri);
            // The section of the browser, not always «plugins»: the factory-device pack points at
            // audio_effects, midi_effects and max_for_live, and a section Live does not have is a
            // button that fails at the press with nothing to fix it by.
            Assert.True(AbletonBrowserCategories.IsKnown(action.Category), action.Category);
            Assert.Null(action.Validate());

            Assert.InRange(button.Col, 0, page.Columns - 1);
            Assert.InRange(button.Row, 0, page.Rows - 1);
        });

        // Every folder has to hold one page worth of tiles and no more, or the ones past the end
        // are simply unreachable.
        foreach (var folder in everything.Where(i => i.Type == ItemType.Folder))
        {
            var children = everything.Where(i => i.ParentId == folder.Id).ToList();

            Assert.InRange(children.Count, 1, page.Columns * page.Rows);

            // No two tiles in the same cell of the same folder, which a hand-written pack makes
            // easy to get wrong and which only shows up as tiles drawn on top of each other.
            Assert.Equal(children.Count, children.Select(c => (c.Col, c.Row)).Distinct().Count());
        }

        Assert.Equal(buttons.Count, buttons.Select(b => b.Action).OfType<AbletonLoadDeviceAction>()
            .Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// The everyday pack, which is the only one that is not about Ableton.
    /// </summary>
    /// <remarks>
    /// Deliberately outside the theory above: every other template loads plugins through Live's
    /// browser, and this one opens web pages and folders. Asserting it against the same rules would
    /// have meant loosening them for all eight.
    /// </remarks>
    [Fact]
    public async Task TheEverydayTemplateOpensPagesAndFolders()
    {
        using var workspace = new DeckWorkspace();

        var profile = await workspace.Packs.ImportAsync(TemplateFile("tareas-basicas.deckpack"));
        var page = Assert.Single(await workspace.Repository.GetPagesAsync(profile.Id));

        var root = Assert.Single(await workspace.Repository.GetChildrenAsync(page.Id, null));
        Assert.Equal(ItemType.Folder, root.Type);

        var buttons = await workspace.Repository.GetChildrenAsync(page.Id, root.Id);

        Assert.Equal(
            new[] { "YouTube", "Gmail", "Drive", "Documentos", "Descargas" },
            buttons.Select(b => b.Label));

        Assert.All(buttons, button => Assert.Null(button.Action!.Validate()));

        // Nothing here starts a program, so nothing needs the acknowledgement an .exe would — and a
        // pack that arrived needing one would import as a row of buttons that refuse to run.
        Assert.All(buttons, button => Assert.False(button.Action!.IsPotentiallyDangerous));
    }

    [Fact]
    public async Task TheEverydayTemplateWorksForWhoeverImportsIt()
    {
        using var workspace = new DeckWorkspace();

        var profile = await workspace.Packs.ImportAsync(TemplateFile("tareas-basicas.deckpack"));
        var page = (await workspace.Repository.GetPagesAsync(profile.Id))[0];
        var root = (await workspace.Repository.GetChildrenAsync(page.Id, null))[0];

        var actions = (await workspace.Repository.GetChildrenAsync(page.Id, root.Id))
            .Select(b => b.Action!)
            .ToList();

        Assert.Equal(3, actions.OfType<OpenUrlAction>().Count());

        // The folders are written with an environment variable, which the handler expands. A real
        // path would be this machine's user name baked into a file meant for anyone.
        var folders = actions.OfType<OpenPathAction>().ToList();

        Assert.Equal(2, folders.Count);
        Assert.All(folders, folder => Assert.StartsWith("%USERPROFILE%", folder.Path));
        Assert.All(folders, folder =>
            Assert.True(
                Directory.Exists(Environment.ExpandEnvironmentVariables(folder.Path)),
                $"«{folder.Path}» no existe en esta máquina."));
    }

    [Fact]
    public async Task TheWavesTemplateSplitsIntoCategoriesThatEachFitAPage()
    {
        using var workspace = new DeckWorkspace();

        var profile = await workspace.Packs.ImportAsync(TemplateFile("waves.deckpack"));
        var page = (await workspace.Repository.GetPagesAsync(profile.Id))[0];
        var root = (await workspace.Repository.GetChildrenAsync(page.Id, null))[0];

        var categories = await workspace.Repository.GetChildrenAsync(page.Id, root.Id);

        Assert.All(categories, c => Assert.Equal(ItemType.Folder, c.Type));
        Assert.InRange(categories.Count, 1, page.Columns * page.Rows);

        Assert.Contains(categories, c => c.Label == "LIMPIEZA");
        Assert.Contains(categories, c => c.Label == "MASTERING");
    }

    [Fact]
    public async Task TheFabFilterTemplateCarriesTheMixingChain()
    {
        using var workspace = new DeckWorkspace();

        var profile = await workspace.Packs.ImportAsync(TemplateFile("fabfilter.deckpack"));
        var page = (await workspace.Repository.GetPagesAsync(profile.Id))[0];
        var root = (await workspace.Repository.GetChildrenAsync(page.Id, null))[0];

        var buttons = await workspace.Repository.GetChildrenAsync(page.Id, root.Id);
        var names = buttons.Select(b => ((AbletonLoadDeviceAction)b.Action!).Name).ToList();

        Assert.Contains("FabFilter Pro-Q 3", names);
        Assert.Contains("FabFilter Pro-C 2", names);
        Assert.Contains("FabFilter Pro-L 2", names);

        // The generated per-plugin icons travel in the pack and land in the icon store on import.
        Assert.All(buttons, b => Assert.False(string.IsNullOrEmpty(b.IconPath)));
        Assert.All(buttons, b => Assert.True(File.Exists(workspace.Paths.ResolveIcon(b.IconPath!))));
    }

    /// <summary>
    /// Walks up from the test binary to the templates folder in the repository.
    /// </summary>
    private static string TemplateFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "plantillas", fileName);
            if (File.Exists(candidate)) return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"No se encontró la plantilla «{fileName}».");
    }
}
