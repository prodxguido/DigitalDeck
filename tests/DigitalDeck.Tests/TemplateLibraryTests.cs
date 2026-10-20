using DigitalDeck.Core.Model;
using DigitalDeck.Data.Packs;
using Xunit;

namespace DigitalDeck.Tests;

/// <summary>
/// The templates carried inside the app, and that they are all of them.
/// </summary>
/// <remarks>
/// The point of embedding these is that whoever is handed a build gets the templates too. A file
/// added to <c>plantillas/</c> and not picked up would be a template that exists in the repository
/// and nowhere else — invisible, with nothing failing to say so. That is what the first test here
/// is for; it is the same guard the Remote Script has for its modules.
/// </remarks>
public class TemplateLibraryTests
{
    private readonly TemplateLibrary _library = new();

    [Fact]
    public void EveryTemplateInTheFolderIsCarriedInsideTheApp()
    {
        var folder = Path.Combine(RepositoryRoot(), "plantillas");

        Assert.True(Directory.Exists(folder), $"no encontré las plantillas en {folder}");

        var onDisk = Directory
            .EnumerateFiles(folder, "*" + DeckPackService.FileExtension)
            .Select(file => Path.GetFileName(file)!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var carried = _library.List()
            .Select(template => template.FileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(onDisk, carried);
    }

    [Fact]
    public void EachOneKnowsWhatItIsCalledAndHowBigItIs()
    {
        var templates = _library.List();

        Assert.NotEmpty(templates);

        Assert.All(templates, template =>
        {
            Assert.False(string.IsNullOrWhiteSpace(template.Name));
            Assert.True(template.Buttons > 0, $"«{template.Name}» no trae botones");
            Assert.False(string.IsNullOrWhiteSpace(template.Summary));
        });
    }

    [Fact]
    public void TheNamesComeFromThePacksThemselves()
    {
        var names = _library.List().Select(template => template.Name).ToList();

        Assert.Contains("Tareas básicas", names);
        Assert.Contains("FabFilter", names);
    }

    [Fact]
    public void TheListIsSortedSoItReadsTheSameEveryTime()
    {
        var names = _library.List().Select(template => template.Name).ToList();

        Assert.Equal(names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase), names);
    }

    [Fact]
    public void ASummaryCountsButtonsAtEveryDepth()
    {
        // Waves nests its plugins two levels down. Counting only the top level would advertise a
        // shelf of 85 plugins as "10 botones".
        var waves = _library.List().Single(template => template.FileName == "waves.deckpack");

        Assert.True(waves.Buttons > 50, $"contó {waves.Buttons}");
        Assert.Contains("grupos", waves.Summary);
    }

    [Fact]
    public async Task AnExtractedTemplateImportsLikeAnyOtherPack()
    {
        // The end of the road: what comes out of the app has to be a file the importer accepts,
        // because that is the only thing the install button does with it.
        using var workspace = new DeckWorkspace();

        var template = _library.List().Single(t => t.FileName == "tareas-basicas.deckpack");
        var directory = Path.Combine(Path.GetTempPath(), "digitaldeck-templates", Guid.NewGuid().ToString("n"));

        try
        {
            var file = _library.ExtractTo(template, directory);

            Assert.True(File.Exists(file));

            var profile = await workspace.Packs.ImportAsync(file);
            var page = Assert.Single(await workspace.Repository.GetPagesAsync(profile.Id));
            var root = Assert.Single(await workspace.Repository.GetChildrenAsync(page.Id, null));

            Assert.Equal(ItemType.Folder, root.Type);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { /* A leftover temp folder is not worth failing a run over. */ }
        }
    }

    [Fact]
    public void AskingForSomethingThatIsNotThereSaysSo()
    {
        var missing = new DeckTemplate("no-existe.deckpack", "No existe", 1, 1);

        var problem = Assert.Throws<InvalidOperationException>(
            () => _library.ExtractTo(missing, Path.GetTempPath()));

        Assert.Contains("no-existe.deckpack", problem.Message);
    }

    /// <summary>Walks up from the test binary to the repository root.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "plantillas")))
            directory = directory.Parent;

        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
