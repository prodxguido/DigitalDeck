using System.Text.Json.Serialization;

namespace DigitalDeck.Core.Model.Actions;

/// <summary>The top-level sections of Live's browser, as the Remote Script names them.</summary>
public static class AbletonBrowserCategories
{
    public const string AudioEffects = "audio_effects";

    /// <summary>Every category, in the order the editor should offer them.</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        "instruments", "sounds", "drums", AudioEffects, "midi_effects",
        "plugins", "max_for_live", "packs", "user_library", "clips", "samples", "current_project"
    };

    public static string Label(string category) => category switch
    {
        "instruments" => "Instrumentos",
        "sounds" => "Sonidos",
        "drums" => "Baterías",
        AudioEffects => "Efectos de audio",
        "midi_effects" => "Efectos MIDI",
        "plugins" => "Plugins",
        "max_for_live" => "Max for Live",
        "packs" => "Packs",
        "user_library" => "User Library",
        "clips" => "Clips",
        "samples" => "Samples",
        "current_project" => "Proyecto actual",
        _ => category
    };

    public static bool IsKnown(string? category) =>
        category is not null && All.Contains(category, StringComparer.Ordinal);
}

/// <summary>
/// Loads a device, rack or preset from Live's browser into the track selected in Live.
/// </summary>
/// <remarks>
/// Two things about this are not obvious and are worth stating where they cannot be missed.
/// <para>
/// <b>It loads into whatever track is selected in Live at that moment</b>, because that is what
/// <c>browser.load_item</c> does and there is no parameter to change it. This matches dragging
/// something out of Live's own browser, which is the gesture the button replaces. Pointing it at a
/// fixed track means putting a <see cref="AbletonCommand.SelectTrack"/> button next to it.
/// </para>
/// <para>
/// <b>Only what Live already indexed can be loaded.</b> There is no way to reach a loose .adg,
/// .fxp or .vst3 file on disk — which is why this stores a browser URI rather than a path, and why
/// the editor offers a list taken from Live rather than a file dialog.
/// </para>
/// </remarks>
public sealed class AbletonLoadDeviceAction : ActionPayload
{
    /// <summary>
    /// The browser URI, which identifies exactly one item.
    /// </summary>
    /// <remarks>
    /// Preferred over the name, and the reason the editor makes the user pick from a list once:
    /// names are not unique — two versions of the same plugin, or a user preset named like a
    /// factory one — while URIs are. Measured against a real Live: 33 racks in one folder, 33
    /// distinct URIs.
    /// </remarks>
    public string? Uri { get; set; }

    /// <summary>What the item is called. Shown in the editor, and used if the URI stops resolving.</summary>
    public required string Name { get; set; }

    /// <summary>Which browser section to look in, which keeps the fallback search bounded.</summary>
    public string? Category { get; set; }

    [JsonIgnore]
    public override string Summary =>
        string.IsNullOrWhiteSpace(Name)
            ? "Dispositivo sin elegir"
            : $"Cargar «{Name}» en la track seleccionada";

    public override string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Uri) && string.IsNullOrWhiteSpace(Name))
            return "Elegí un dispositivo del browser de Live.";

        if (Category is not null && !AbletonBrowserCategories.IsKnown(Category))
            return $"«{Category}» no es una sección del browser de Live.";

        return null;
    }
}
