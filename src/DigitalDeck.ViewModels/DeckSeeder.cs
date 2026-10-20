using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Core.Ordering;

namespace DigitalDeck.ViewModels;

/// <summary>
/// Builds a starter profile the first time the app runs.
/// </summary>
/// <remarks>
/// An empty grid on first launch teaches nothing. This lays out one working example of every
/// button type the app currently supports, so the shape of the thing is obvious before the user
/// has configured anything — and every tile here is real and editable, not a placeholder.
/// </remarks>
public sealed class DeckSeeder
{
    private readonly IDeckRepository _repository;
    private readonly IAppSettingsStore _settings;

    public DeckSeeder(IDeckRepository repository, IAppSettingsStore settings)
    {
        _repository = repository;
        _settings = settings;
    }

    public async Task SeedIfEmptyAsync(CancellationToken ct = default)
    {
        var profiles = await _repository.GetProfilesAsync(ct).ConfigureAwait(false);
        if (profiles.Count > 0) return;

        var profile = await _repository.CreateProfileAsync("Estudio", createDefaultPage: false, ct).ConfigureAwait(false);
        var page = await _repository.CreatePageAsync(profile.Id, "Inicio", 6, 4, ct).ConfigureAwait(false);

        var keys = FractionalIndex.Sequence(7);
        var rootItems = new List<DeckItem>();
        var slot = 0;

        DeckItem Tile(ItemType type, string label, int col, int row, string colour, string glyph)
        {
            var item = type == ItemType.Folder
                ? DeckItem.NewFolder(page.Id, null, keys[slot], label)
                : DeckItem.NewButton(page.Id, null, keys[slot], label);

            item.Col = col;
            item.Row = row;
            item.ColorHex = colour;
            item.IconGlyph = glyph;
            slot++;

            rootItems.Add(item);
            return item;
        }

        var ableton = Tile(ItemType.Folder, AbletonEssentials.GroupLabel, 0, 0, "#F7768E", "device");
        var production = Tile(ItemType.Folder, "PRODUCCIÓN", 1, 0, "#33C9DC", "folder");
        Tile(ItemType.Folder, "WEB", 2, 0, "#C77DFF", "globe");

        Tile(ItemType.Button, "Mi música", 4, 0, "#5FD88F", "folder-open").Action = new OpenPathAction
        {
            Path = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
        };

        Tile(ItemType.Button, "Documentos", 5, 0, "#EAB54C", "document").Action = new OpenPathAction
        {
            Path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        Tile(ItemType.Button, "Ableton", 0, 2, "#F7768E", "window").Action =
            new OpenUrlAction { Url = "https://www.ableton.com" };

        Tile(ItemType.Button, "YouTube", 1, 2, "#E8615F", "globe").Action =
            new OpenUrlAction { Url = "https://www.youtube.com" };

        await _repository.AddItemsAsync(rootItems, ct).ConfigureAwait(false);

        // One level of nesting inside the folder, so the navigation model is visible from the start.
        var nestedKeys = FractionalIndex.Sequence(2);

        var mixing = DeckItem.NewFolder(page.Id, production.Id, nestedKeys[0], "MIXING");
        mixing.Col = 0;
        mixing.Row = 0;
        mixing.ColorHex = "#7AA2F7";
        mixing.IconGlyph = "sliders";

        var splice = DeckItem.NewButton(page.Id, production.Id, nestedKeys[1], "Splice");
        splice.Col = 1;
        splice.Row = 0;
        splice.ColorHex = "#FF9E64";
        splice.IconGlyph = "download";
        splice.Action = new OpenUrlAction { Url = "https://splice.com" };

        await _repository.AddItemsAsync(new[] { mixing, splice }, ct).ConfigureAwait(false);

        await SeedAbletonGroupAsync(page.Id, ableton.Id, ct).ConfigureAwait(false);
        await SeedWorkflowAsync(profile.Id, "Producción",
            new[] { "EQ Eight", "Auto Filter", "Saturator", "Drum Buss", "Echo", "Reverb" }, ct);
        await SeedWorkflowAsync(profile.Id, "Mezcla",
            new[] { "EQ Eight", "Compressor", "Glue Compressor", "Utility", "Limiter", "Spectrum" }, ct);
    }

    private async Task SeedWorkflowAsync(string profileId, string name, string[] devices, CancellationToken ct)
    {
        var page = await _repository.CreatePageAsync(profileId, name, 6, 2, ct).ConfigureAwait(false);
        var keys = FractionalIndex.Sequence(12);
        var items = new List<DeckItem>();
        for (var i = 0; i < devices.Length; i++)
        {
            var device = AbletonEssentials.AudioEffects.FirstOrDefault(e => e.Name == devices[i]);
            var tile = DeckItem.NewButton(page.Id, null, keys[i], devices[i]);
            tile.Col = i;
            tile.Row = 0;
            tile.ColorHex = device?.ColorHex ?? "#D4EE86";
            tile.IconGlyph = device?.Icon ?? "waveform";
            tile.Action = new AbletonLoadDeviceAction
            {
                Name = devices[i], Category = AbletonBrowserCategories.AudioEffects
            };
            items.Add(tile);
        }
        var commands = new[]
        {
            ("Play / pausa", AbletonCommand.TogglePlay, "play", "#D4EE86"),
            ("Detener", AbletonCommand.Stop, "stop", "#B0B8B4"),
            ("Grabar", AbletonCommand.ToggleRecord, "record", "#EF8D85"),
            ("Metrónomo", AbletonCommand.ToggleMetronome, "music", "#E8C887"),
            ("Capturar MIDI", AbletonCommand.CaptureMidi, "midi", "#B4A4E8"),
            ("Guardar", AbletonCommand.SaveSet, "document", "#89C8BA")
        };
        for (var i = 0; i < commands.Length; i++)
        {
            var (label, command, icon, color) = commands[i];
            var tile = DeckItem.NewButton(page.Id, null, keys[i + 6], label);
            tile.Col = i;
            tile.Row = 1;
            tile.IconGlyph = icon;
            tile.ColorHex = color;
            tile.Action = new AbletonAction { Command = command };
            items.Add(tile);
        }
        await _repository.AddItemsAsync(items, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Live's browser, laid out as folders, with the audio effects already in place.
    /// </summary>
    /// <remarks>
    /// Written from a fixed list rather than from Live itself, because at first launch there is
    /// nothing to ask: the Remote Script is usually still being installed and Live is closed. See
    /// <see cref="AbletonEssentials"/> for what that costs and how a wrong name recovers.
    /// </remarks>
    private async Task SeedAbletonGroupAsync(string pageId, string groupId, CancellationToken ct)
    {
        var sections = AbletonEssentials.Sections;
        var keys = FractionalIndex.Sequence(sections.Count + 1);
        var items = new List<DeckItem>();

        // The ranking goes first, in the corner the hand lands on. It is empty until something has
        // been pressed, which is the honest state of a deck nobody has used yet.
        var frequent = DeckItem.NewFolder(pageId, groupId, keys[0], FrequentFolderService.DefaultLabel);
        frequent.Col = 0;
        frequent.Row = 0;
        frequent.ColorHex = FrequentFolderService.DefaultColorHex;
        frequent.IconGlyph = FrequentFolderService.DefaultIcon;
        items.Add(frequent);

        var sectionItems = new Dictionary<string, DeckItem>(StringComparer.Ordinal);

        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            var slot = i + 1;

            var folder = DeckItem.NewFolder(pageId, groupId, keys[slot], section.Label);
            folder.Col = slot % 6;
            folder.Row = slot / 6;
            folder.ColorHex = section.ColorHex;
            folder.IconGlyph = section.Icon;

            sectionItems[section.Category] = folder;
            items.Add(folder);
        }

        await _repository.AddItemsAsync(items, ct).ConfigureAwait(false);
        await _settings.SetAsync(SettingKeys.FrequentFolderId, frequent.Id, ct).ConfigureAwait(false);

        var effects = sectionItems[AbletonBrowserCategories.AudioEffects];
        var effectKeys = FractionalIndex.Sequence(AbletonEssentials.AudioEffects.Count);
        var tiles = new List<DeckItem>();

        for (var i = 0; i < AbletonEssentials.AudioEffects.Count; i++)
        {
            var essential = AbletonEssentials.AudioEffects[i];

            var tile = DeckItem.NewButton(pageId, effects.Id, effectKeys[i], essential.Name);
            tile.Col = i % 6;
            tile.Row = i / 6;
            tile.ColorHex = essential.ColorHex;
            tile.IconGlyph = essential.Icon;

            // No URI: it is Live that hands those out, and none has been asked yet. The script
            // falls back to the name, scoped to the category so the search stays bounded and two
            // sections holding the same name cannot be confused for each other.
            tile.Action = new AbletonLoadDeviceAction
            {
                Name = essential.Name,
                Category = AbletonBrowserCategories.AudioEffects
            };

            tiles.Add(tile);
        }

        await _repository.AddItemsAsync(tiles, ct).ConfigureAwait(false);
    }
}
