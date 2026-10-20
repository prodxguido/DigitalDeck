namespace DigitalDeck.Core.Model.Actions;

/// <summary>One tile the starter Ableton group ships with, before the user has configured anything.</summary>
/// <param name="Name">Exactly what Live calls the item in its browser — the fallback resolves by it.</param>
/// <param name="Icon">Name from the app's icon set.</param>
/// <param name="ColorHex">Tile colour, chosen so a family of effects reads as one family.</param>
public sealed record AbletonEssential(string Name, string Icon, string ColorHex);

/// <summary>A section of Live's browser, as a folder of the starter group.</summary>
public sealed record AbletonSection(string Category, string Label, string Icon, string ColorHex);

/// <summary>
/// The Ableton group the app installs with: Live's browser sections as folders, and the audio
/// effects worth having on the deck before anyone has pressed anything.
/// </summary>
/// <remarks>
/// Only audio effects come filled in. The rest of the sections are deliberately empty folders
/// waiting for the picker, for two reasons: what belongs in "Instrumentos" depends entirely on
/// which packs and plugins the user owns, and a folder holds one page of tiles — mirroring the 47
/// items Live lists at the top of a category would not fit in it anyway.
/// <para>
/// Nothing here can be verified at install time. The bridge only answers while Live is running
/// with the Remote Script loaded, which on a first launch it is not, so these tiles are written
/// from a fixed list of factory names rather than from a listing. A name that this Live does not
/// have — an older version, or Intro rather than Suite — fails when the button is pressed, with
/// the script's own "Live has nothing indexed matching" message, and the user re-points it with
/// the editor's picker.
/// </para>
/// </remarks>
public static class AbletonEssentials
{
    /// <summary>Label of the group that holds everything below.</summary>
    public const string GroupLabel = "ABLETON";

    /// <summary>Browser sections offered as folders, in the order they are laid out.</summary>
    public static IReadOnlyList<AbletonSection> Sections { get; } = new[]
    {
        new AbletonSection("instruments", "Instrumentos", "piano", "#7AA2F7"),
        new AbletonSection(AbletonBrowserCategories.AudioEffects, "Efectos de audio", "equalizer", "#33C9DC"),
        new AbletonSection("midi_effects", "Efectos MIDI", "midi", "#C77DFF"),
        new AbletonSection("drums", "Baterías", "waveform", "#FF9E64"),
        new AbletonSection("sounds", "Sonidos", "music", "#5FD88F"),
        new AbletonSection("plugins", "Plugins", "device", "#F7768E"),
        new AbletonSection("max_for_live", "Max for Live", "knob", "#EAB54C"),
        new AbletonSection("user_library", "User Library", "folder", "#9AA5CE")
    };

    /// <summary>
    /// The audio effects the group ships with.
    /// </summary>
    /// <remarks>
    /// Twenty of them, which is what a 6×4 folder holds with room to spare, and they are the ones
    /// a mix actually runs on: the dynamics and EQ that go on nearly every channel, the colour
    /// boxes, the two time-based sends, and the rack that everything else gets built inside.
    /// Ordered by what they are for, not alphabetically, so the row you reach for is the row you
    /// look at.
    /// </remarks>
    public static IReadOnlyList<AbletonEssential> AudioEffects { get; } = new[]
    {
        // Tone
        new AbletonEssential("EQ Eight", "equalizer", "#33C9DC"),
        new AbletonEssential("EQ Three", "equalizer", "#33C9DC"),
        new AbletonEssential("Auto Filter", "filter", "#33C9DC"),
        new AbletonEssential("Utility", "sliders", "#33C9DC"),

        // Dynamics
        new AbletonEssential("Compressor", "knob", "#7AA2F7"),
        new AbletonEssential("Glue Compressor", "knob", "#7AA2F7"),
        new AbletonEssential("Limiter", "knob", "#7AA2F7"),
        new AbletonEssential("Gate", "knob", "#7AA2F7"),
        new AbletonEssential("Multiband Dynamics", "sliders", "#7AA2F7"),

        // Colour
        new AbletonEssential("Saturator", "waveform", "#FF9E64"),
        new AbletonEssential("Overdrive", "waveform", "#FF9E64"),
        new AbletonEssential("Drum Buss", "waveform", "#FF9E64"),
        new AbletonEssential("Redux", "waveform", "#FF9E64"),

        // Space and movement
        new AbletonEssential("Reverb", "browser", "#C77DFF"),
        new AbletonEssential("Hybrid Reverb", "browser", "#C77DFF"),
        new AbletonEssential("Delay", "clock", "#C77DFF"),
        new AbletonEssential("Echo", "clock", "#C77DFF"),
        new AbletonEssential("Chorus-Ensemble", "loop", "#C77DFF"),
        new AbletonEssential("Phaser-Flanger", "loop", "#C77DFF"),
        new AbletonEssential("Auto Pan", "volume", "#C77DFF")
    };
}
