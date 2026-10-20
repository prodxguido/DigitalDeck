using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Layout;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Ordering;
using DigitalDeck.Core.Serialization;

namespace DigitalDeck.Data.Packs;

/// <summary>
/// Backup, transfer and sharing: writes a profile or a single group to a self-contained
/// <c>.deckpack</c> file and reads one back in.
/// </summary>
public sealed class DeckPackService
{
    public const string FileExtension = ".deckpack";
    public const string FileDialogFilter = "Paquete de DigitalDeck|*.deckpack";

    private const string ManifestEntry = "manifest.json";
    private const string IconsFolder = "icons/";

    private readonly IDeckRepository _repository;
    private readonly IconStore _icons;

    public DeckPackService(IDeckRepository repository, IconStore icons)
    {
        _repository = repository;
        _icons = icons;
    }

    private static string? AppVersion => Assembly.GetEntryAssembly()?.GetName().Version?.ToString();

    // ---------- Export ----------

    public async Task ExportProfileAsync(string profileId, string destinationFile, CancellationToken ct = default)
    {
        var profile = (await _repository.GetProfilesAsync(ct).ConfigureAwait(false))
            .FirstOrDefault(p => p.Id == profileId)
            ?? throw new InvalidOperationException("El perfil ya no existe.");

        var icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pack = new DeckPack { AppVersion = AppVersion, Profile = new PackProfile { Name = profile.Name } };

        foreach (var page in await _repository.GetPagesAsync(profileId, ct).ConfigureAwait(false))
        {
            var items = await _repository.GetPageItemsAsync(page.Id, ct).ConfigureAwait(false);

            pack.Profile.Pages.Add(new PackPage
            {
                Name = page.Name,
                Columns = page.Columns,
                Rows = page.Rows,
                Items = BuildBranch(items, null, icons)
            });
        }

        await WritePackAsync(destinationFile, pack, icons, ct).ConfigureAwait(false);
    }

    /// <summary>Exports one folder and everything inside it, as a one-page pack.</summary>
    public async Task ExportFolderAsync(string itemId, string destinationFile, CancellationToken ct = default)
    {
        var subtree = await _repository.GetSubtreeAsync(itemId, ct).ConfigureAwait(false);

        var root = subtree.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException("El grupo ya no existe.");

        var page = await _repository.GetPageAsync(root.PageId, ct).ConfigureAwait(false);
        var icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Re-rooted to the top-left cell so it lands cleanly whatever page it is imported into.
        var packRoot = ToPackItem(root, subtree, icons);
        packRoot.Col = 0;
        packRoot.Row = 0;

        var pack = new DeckPack
        {
            AppVersion = AppVersion,
            Profile = new PackProfile
            {
                Name = root.Label,
                Pages =
                {
                    new PackPage
                    {
                        Name = root.Label,
                        Columns = page?.Columns ?? 6,
                        Rows = page?.Rows ?? 4,
                        Items = { packRoot }
                    }
                }
            }
        };

        await WritePackAsync(destinationFile, pack, icons, ct).ConfigureAwait(false);
    }

    private List<PackItem> BuildBranch(
        IReadOnlyList<DeckItem> all,
        string? parentId,
        Dictionary<string, string> icons) =>
        all.Where(i => i.ParentId == parentId)
            .OrderBy(i => i.SortKey, StringComparer.Ordinal)
            .Select(i => ToPackItem(i, all, icons))
            .ToList();

    private PackItem ToPackItem(DeckItem item, IReadOnlyList<DeckItem> all, Dictionary<string, string> icons) => new()
    {
        Type = item.Type,
        Label = item.Label,
        Col = item.Col,
        Row = item.Row,
        Width = item.Width,
        Height = item.Height,
        ColorHex = item.ColorHex,
        IconGlyph = item.IconGlyph,
        IconFile = RegisterIcon(item.IconPath, icons),
        Action = item.Action,
        Children = BuildBranch(all, item.Id, icons)
    };

    /// <summary>Records an icon for inclusion in the zip and returns the entry name to reference.</summary>
    private string? RegisterIcon(string? relativePath, Dictionary<string, string> icons)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        if (_icons.GetAbsolutePath(relativePath) is null) return null;

        var entryName = Path.GetFileName(relativePath);
        icons[relativePath] = entryName;
        return entryName;
    }

    private async Task WritePackAsync(
        string destinationFile,
        DeckPack pack,
        Dictionary<string, string> icons,
        CancellationToken ct)
    {
        // Written to a sidecar first so an interrupted export cannot leave a half-written pack in
        // place of a good one.
        var temporaryFile = destinationFile + ".tmp";

        await using (var file = File.Create(temporaryFile))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            await using (var manifest = archive.CreateEntry(ManifestEntry, CompressionLevel.Optimal).Open())
                await JsonSerializer.SerializeAsync(manifest, pack, DeckJson.Readable, ct).ConfigureAwait(false);

            foreach (var (relativePath, entryName) in icons)
            {
                var absolute = _icons.GetAbsolutePath(relativePath);
                if (absolute is null) continue;

                await using var entryStream = archive.CreateEntry(IconsFolder + entryName, CompressionLevel.Optimal).Open();
                await using var source = File.OpenRead(absolute);
                await source.CopyToAsync(entryStream, ct).ConfigureAwait(false);
            }
        }

        File.Move(temporaryFile, destinationFile, overwrite: true);
    }

    // ---------- Import ----------

    /// <summary>Reads a pack in as a new profile, leaving existing profiles untouched.</summary>
    public async Task<Profile> ImportAsync(string packFile, CancellationToken ct = default)
    {
        using var archive = ZipFile.OpenRead(packFile);
        var pack = await ReadManifestAsync(archive, ct).ConfigureAwait(false);

        var name = await UniqueProfileNameAsync(pack.Profile!.Name, ct).ConfigureAwait(false);
        var profile = await _repository.CreateProfileAsync(name, createDefaultPage: false, ct).ConfigureAwait(false);

        foreach (var packPage in pack.Profile.Pages)
        {
            var page = await _repository
                .CreatePageAsync(profile.Id, packPage.Name, packPage.Columns, packPage.Rows, ct)
                .ConfigureAwait(false);

            var items = new List<DeckItem>();
            await FlattenAsync(packPage.Items, page.Id, null, archive, items, ct).ConfigureAwait(false);

            if (items.Count > 0)
                await _repository.AddItemsAsync(items, ct).ConfigureAwait(false);
        }

        return profile;
    }

    /// <summary>
    /// Reads a pack in as pages of its own, added to a profile that already exists.
    /// </summary>
    /// <remarks>
    /// The third answer to "where does this go", and the right one for anything big. A shelf of
    /// fifteen plugins is not really a group — it is a screen — and filed as one it costs a press
    /// to open before anything on it can be seen, plus the cell it sits in. As a page it is one
    /// tab away, next to everything the user already had, and nothing on the grid moves.
    /// <para>
    /// Each page keeps the grid the pack was built on, so nothing has to be re-placed to fit.
    /// </para>
    /// </remarks>
    /// <returns>The pages created, in the order the pack lists them.</returns>
    public async Task<IReadOnlyList<Page>> ImportAsPagesAsync(
        string packFile,
        string profileId,
        CancellationToken ct = default)
    {
        using var archive = ZipFile.OpenRead(packFile);
        var pack = await ReadManifestAsync(archive, ct).ConfigureAwait(false);

        var taken = (await _repository.GetPagesAsync(profileId, ct).ConfigureAwait(false))
            .Select(page => page.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var created = new List<Page>();

        foreach (var packPage in pack.Profile!.Pages)
        {
            var (name, items) = OpenOut(packPage, pack.Profile.Name);
            if (items.Count == 0) continue;

            var unique = Unique(name, "Página importada", taken);
            taken.Add(unique);

            var page = await _repository
                .CreatePageAsync(profileId, unique, packPage.Columns, packPage.Rows, ct)
                .ConfigureAwait(false);

            var contents = new List<DeckItem>();
            await FlattenIntoAsync(items, page.Id, null, page, archive, contents, ct).ConfigureAwait(false);

            await _repository.AddItemsAsync(contents, ct).ConfigureAwait(false);
            created.Add(page);
        }

        if (created.Count == 0)
            throw new InvalidDataException("El paquete no trae nada para importar.");

        return created;
    }

    /// <summary>
    /// What a pack page becomes when it arrives as a page, and what its tab should say.
    /// </summary>
    /// <remarks>
    /// Every template, and every group ever exported, is a pack holding one folder and nothing
    /// else. Brought in whole that would make a page whose entire contents is a single tile to
    /// press: the wrapper is how a group travels, not something anybody asked for. So a lone
    /// folder is opened out — its contents go on the grid and its name goes on the tab. A pack
    /// page holding several things is already a page, and arrives exactly as it was.
    /// </remarks>
    private static (string Name, List<PackItem> Items) OpenOut(PackPage page, string profileName)
    {
        if (page.Items is [{ Type: ItemType.Folder, Children.Count: > 0 } only])
            return (FirstNamed(page.Name, only.Label, profileName), only.Children);

        return (FirstNamed(page.Name, profileName), page.Items);
    }

    private static string FirstNamed(params string?[] candidates) =>
        candidates.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))?.Trim() ?? string.Empty;

    /// <summary>
    /// Reads a pack into a page that already exists, as one more group alongside what is there.
    /// </summary>
    /// <remarks>
    /// The other import answers "give me this deck"; this one answers "add this to the deck I am
    /// using". Arriving as a profile is right for a backup and wrong for a template: a template is
    /// a shelf of plugins, and a shelf is only useful next to the other shelves — reaching it by
    /// switching profiles means leaving behind everything else you were working with.
    /// <para>
    /// Nothing already on the page moves. Each group coming in is placed in the first cell free at
    /// the time, and if there is no room the import is refused by name rather than dropping tiles
    /// somewhere they cannot be seen.
    /// </para>
    /// </remarks>
    /// <param name="parentId">Folder to import into, or null for the root of the page.</param>
    /// <returns>The groups added at the top level, in the order they were placed.</returns>
    public async Task<IReadOnlyList<DeckItem>> ImportIntoPageAsync(
        string packFile,
        string pageId,
        string? parentId = null,
        CancellationToken ct = default)
    {
        using var archive = ZipFile.OpenRead(packFile);
        var pack = await ReadManifestAsync(archive, ct).ConfigureAwait(false);

        var page = await _repository.GetPageAsync(pageId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("La página ya no existe.");

        // Every page of the pack contributes its top level. A template has one; a whole profile
        // exported as a pack arrives as one group per page, which is the honest reading of "put
        // this inside that".
        var incoming = pack.Profile!.Pages.SelectMany(p => p.Items).ToList();

        if (incoming.Count == 0)
            throw new InvalidDataException("El paquete no trae nada para importar.");

        var siblings = (await _repository.GetChildrenAsync(pageId, parentId, ct).ConfigureAwait(false)).ToList();
        var sortKey = siblings.OrderBy(s => s.SortKey, StringComparer.Ordinal).LastOrDefault()?.SortKey;

        var everything = new List<DeckItem>();
        var roots = new List<DeckItem>();

        foreach (var source in incoming)
        {
            var width = Math.Clamp(Math.Max(1, source.Width), 1, page.Columns);
            var height = Math.Clamp(Math.Max(1, source.Height), 1, page.Rows);

            var slot = GridLayoutEngine.FindFirstFreeSlot(siblings, width, height, page.Columns, page.Rows)
                ?? throw new InvalidOperationException(
                    $"No queda lugar en «{page.Name}» para «{source.Label}».");

            sortKey = FractionalIndex.Last(sortKey);

            var item = await ToDeckItemAsync(source, pageId, parentId, sortKey, archive, ct).ConfigureAwait(false);
            item.Col = slot.Col;
            item.Row = slot.Row;
            item.Width = width;
            item.Height = height;

            everything.Add(item);
            roots.Add(item);

            // Placed as it goes so the next group sees this one, exactly as a user adding tiles
            // one after another would.
            siblings.Add(item);

            await FlattenIntoAsync(source.Children, pageId, item.Id, page, archive, everything, ct)
                .ConfigureAwait(false);
        }

        await _repository.AddItemsAsync(everything, ct).ConfigureAwait(false);
        return roots;
    }

    /// <summary>
    /// Fills a folder, keeping the layout the pack came with wherever it still makes sense.
    /// </summary>
    /// <remarks>
    /// A pack carries the grid it was built on, and the page receiving it may be smaller. A tile
    /// whose cell no longer exists is not a tile in a slightly wrong place — it is one that cannot
    /// be reached at all, so those, and only those, are re-placed in the first free cell. Packs
    /// coming into a page of the same shape keep their arrangement untouched.
    /// </remarks>
    private async Task FlattenIntoAsync(
        List<PackItem> packItems,
        string pageId,
        string? parentId,
        Page page,
        ZipArchive archive,
        List<DeckItem> output,
        CancellationToken ct)
    {
        if (packItems.Count == 0) return;

        if (packItems.Count > page.Columns * page.Rows)
            throw new InvalidOperationException(
                $"Un grupo del paquete trae {packItems.Count} elementos y en esta página entran " +
                $"{page.Columns * page.Rows}.");

        var sortKeys = FractionalIndex.Sequence(packItems.Count);
        var placed = new List<DeckItem>();

        for (var i = 0; i < packItems.Count; i++)
        {
            var source = packItems[i];

            var item = await ToDeckItemAsync(source, pageId, parentId, sortKeys[i], archive, ct)
                .ConfigureAwait(false);

            item.Width = Math.Clamp(item.Width, 1, page.Columns);
            item.Height = Math.Clamp(item.Height, 1, page.Rows);

            var wanted = new GridRect(item.Col, item.Row, item.Width, item.Height);

            if (!GridLayoutEngine.CanPlace(placed, wanted, page.Columns, page.Rows))
            {
                var slot = GridLayoutEngine.FindFirstFreeSlot(placed, item.Width, item.Height, page.Columns, page.Rows)
                    ?? throw new InvalidOperationException(
                        $"No entra «{item.Label}» en la grilla de {page.Columns}×{page.Rows} de esta página.");

                item.Col = slot.Col;
                item.Row = slot.Row;
            }

            placed.Add(item);
            output.Add(item);

            if (source.Children.Count > 0)
                await FlattenIntoAsync(source.Children, pageId, item.Id, page, archive, output, ct)
                    .ConfigureAwait(false);
        }
    }

    /// <summary>One pack entry as a stored item, without deciding where on the grid it goes.</summary>
    private async Task<DeckItem> ToDeckItemAsync(
        PackItem source,
        string pageId,
        string? parentId,
        string sortKey,
        ZipArchive archive,
        CancellationToken ct) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        PageId = pageId,
        ParentId = parentId,
        Type = source.Type,
        SortKey = sortKey,
        Col = Math.Max(0, source.Col),
        Row = Math.Max(0, source.Row),
        Width = Math.Max(1, source.Width),
        Height = Math.Max(1, source.Height),
        Label = source.Label,
        ColorHex = string.IsNullOrWhiteSpace(source.ColorHex) ? DeckItem.DefaultColor : source.ColorHex,
        IconGlyph = source.IconGlyph,
        IconPath = await ExtractIconAsync(archive, source.IconFile, ct).ConfigureAwait(false),
        Action = source.Type == ItemType.Folder ? null : source.Action
    };

    /// <summary>Reads and validates the manifest. Throws with a readable reason if it is not one.</summary>
    private static async Task<DeckPack> ReadManifestAsync(ZipArchive archive, CancellationToken ct)
    {
        var manifestEntry = archive.GetEntry(ManifestEntry)
            ?? throw new InvalidDataException("El archivo no es un paquete de DigitalDeck válido.");

        DeckPack? pack;
        await using (var manifest = manifestEntry.Open())
            pack = await JsonSerializer.DeserializeAsync<DeckPack>(manifest, DeckJson.Readable, ct).ConfigureAwait(false);

        if (pack?.Profile is null)
            throw new InvalidDataException("El paquete no contiene ningún perfil.");

        if (pack.FormatVersion > DeckPack.CurrentFormatVersion)
            throw new InvalidDataException(
                $"El paquete usa el formato {pack.FormatVersion} y esta versión sólo entiende hasta el {DeckPack.CurrentFormatVersion}.");

        return pack;
    }

    private async Task FlattenAsync(
        List<PackItem> packItems,
        string pageId,
        string? parentId,
        ZipArchive archive,
        List<DeckItem> output,
        CancellationToken ct)
    {
        var sortKeys = FractionalIndex.Sequence(packItems.Count);

        for (var i = 0; i < packItems.Count; i++)
        {
            var source = packItems[i];

            var item = await ToDeckItemAsync(source, pageId, parentId, sortKeys[i], archive, ct)
                .ConfigureAwait(false);

            output.Add(item);

            if (source.Children.Count > 0)
                await FlattenAsync(source.Children, pageId, item.Id, archive, output, ct).ConfigureAwait(false);
        }
    }

    private async Task<string?> ExtractIconAsync(ZipArchive archive, string? iconFile, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(iconFile)) return null;

        // Only the bare filename is honoured, so an entry named "../../evil.png" cannot write
        // outside the icon store.
        var safeName = Path.GetFileName(iconFile);
        var entry = archive.GetEntry(IconsFolder + safeName);
        if (entry is null) return null;

        try
        {
            await using var stream = entry.Open();
            return await _icons.ImportAsync(stream, Path.GetExtension(safeName), ct).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            return null; // Unrecognised image format in a foreign pack — the button keeps its glyph.
        }
        catch (InvalidOperationException)
        {
            return null; // Oversized image.
        }
    }

    private async Task<string> UniqueProfileNameAsync(string desired, CancellationToken ct)
    {
        var existing = (await _repository.GetProfilesAsync(ct).ConfigureAwait(false))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Unique(desired, "Perfil importado", existing);
    }

    /// <summary>
    /// The name asked for, or the first «name (n)» nobody has taken yet.
    /// </summary>
    /// <remarks>
    /// Shared by profiles and by pages because the reason is the same: both are picked off a list
    /// that shows nothing but the name, so two called «Waves» is two things the user has to open
    /// to tell apart.
    /// </remarks>
    private static string Unique(string? desired, string fallback, ISet<string> taken)
    {
        var baseName = string.IsNullOrWhiteSpace(desired) ? fallback : desired.Trim();

        if (!taken.Contains(baseName)) return baseName;

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = $"{baseName} ({suffix})";
            if (!taken.Contains(candidate)) return candidate;
        }

        return $"{baseName} ({Guid.NewGuid():n})";
    }
}
