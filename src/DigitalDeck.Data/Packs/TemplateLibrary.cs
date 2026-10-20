using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using DigitalDeck.Core.Serialization;

namespace DigitalDeck.Data.Packs;

/// <summary>
/// One ready-made template carried inside the app.
/// </summary>
/// <param name="FileName">Its name inside the app, which is also what it was called on disk.</param>
/// <param name="Name">The profile name written in the pack's own manifest.</param>
/// <param name="Groups">
/// How many groups it is divided into inside. Zero for a flat template: every pack arrives wrapped
/// in one group of its own, and counting that wrapper would say "en 1 grupo" about all of them.
/// </param>
/// <param name="Buttons">How many buttons it holds, at any depth.</param>
public sealed record DeckTemplate(string FileName, string Name, int Groups, int Buttons)
{
    /// <summary>
    /// A line describing the size of it, worked out from the pack rather than written down.
    /// </summary>
    /// <remarks>
    /// Derived on purpose: a hand-maintained description is one more thing to forget when a
    /// template is added, and the whole point of this list is that adding a file to the templates
    /// folder is all it takes.
    /// </remarks>
    public string Summary
    {
        get
        {
            var buttons = Buttons == 1 ? "1 botón" : $"{Buttons} botones";

            return Groups <= 1
                ? buttons
                : $"{buttons} en {Groups} grupos";
        }
    }
}

/// <summary>
/// The templates that travel inside the app, ready to be installed without hunting for a file.
/// </summary>
/// <remarks>
/// Embedded for the same reason the Remote Script is: a build handed to somebody is a single thing,
/// and templates sitting in a folder of the repository are templates that person does not have. The
/// project file picks them up with a wildcard, so adding one to <c>plantillas/</c> is the whole job
/// — there is no list here to keep in step, and a test asserts the two cannot drift apart.
/// <para>
/// Nothing is read until somebody asks. A launch that never opens the template list pays for none
/// of this.
/// </para>
/// </remarks>
public sealed class TemplateLibrary
{
    /// <summary>Prefix the project file gives every embedded template.</summary>
    private const string Prefix = "Templates/";

    private const string ManifestEntry = "manifest.json";

    private readonly Assembly _assembly;
    private IReadOnlyList<DeckTemplate>? _templates;

    public TemplateLibrary(Assembly? assembly = null) =>
        _assembly = assembly ?? typeof(TemplateLibrary).Assembly;

    /// <summary>
    /// Every template inside the app, in alphabetical order by what it is called.
    /// </summary>
    /// <remarks>
    /// A template whose manifest cannot be read is left out rather than thrown over: one bad file
    /// must not cost the user the list of the good ones.
    /// </remarks>
    public IReadOnlyList<DeckTemplate> List() => _templates ??= Read();

    private IReadOnlyList<DeckTemplate> Read()
    {
        var templates = new List<DeckTemplate>();

        foreach (var resource in _assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(Prefix, StringComparison.Ordinal)) continue;

            if (Describe(resource) is { } template) templates.Add(template);
        }

        return templates
            .OrderBy(template => template.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private DeckTemplate? Describe(string resource)
    {
        try
        {
            using var stream = _assembly.GetManifestResourceStream(resource);
            if (stream is null) return null;

            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            var entry = archive.GetEntry(ManifestEntry);
            if (entry is null) return null;

            using var manifest = entry.Open();
            var pack = JsonSerializer.Deserialize<DeckPack>(manifest, DeckJson.Readable);

            if (pack?.Profile is null) return null;

            var items = pack.Profile.Pages.SelectMany(page => page.Items).ToList();

            // Every pack is one group at the top; the number worth showing is how many it splits
            // into below that, which is the difference between "85 botones" and something usable.
            var wrappers = items.Count(item => item.Type == Core.Model.ItemType.Folder);

            return new DeckTemplate(
                resource[Prefix.Length..],
                pack.Profile.Name,
                Math.Max(0, CountFolders(items) - wrappers),
                CountButtons(items));
        }
        catch (Exception)
        {
            // A template that will not parse is one the app cannot offer. Saying nothing about it
            // is better than a list that refuses to open because of it.
            return null;
        }
    }

    private static int CountButtons(IEnumerable<PackItem> items) =>
        items.Sum(item =>
            (item.Type == Core.Model.ItemType.Button ? 1 : 0) + CountButtons(item.Children));

    private static int CountFolders(IEnumerable<PackItem> items) =>
        items.Sum(item =>
            (item.Type == Core.Model.ItemType.Folder ? 1 : 0) + CountFolders(item.Children));

    /// <summary>
    /// Writes one template out to a real file, because importing reads a path.
    /// </summary>
    /// <returns>The file written, which the caller owns and should delete when it is done.</returns>
    public string ExtractTo(DeckTemplate template, string directory)
    {
        ArgumentNullException.ThrowIfNull(template);

        Directory.CreateDirectory(directory);

        var destination = Path.Combine(directory, template.FileName);

        using var source = _assembly.GetManifestResourceStream(Prefix + template.FileName)
                           ?? throw new InvalidOperationException(
                               $"«{template.FileName}» no está adentro de esta versión de la app.");

        using var file = File.Create(destination);
        source.CopyTo(file);

        return destination;
    }
}
