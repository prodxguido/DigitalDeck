using System.Globalization;

namespace DigitalDeck.Core.Model;

/// <summary>
/// The icon set — <see href="https://lucide.dev">Lucide</see> — as path data on a 24×24 grid.
/// </summary>
/// <remarks>
/// Line icons rather than filled shapes: a symbol, not a slab of colour. Every render site strokes
/// these with a round cap and join at 2 units, the way Lucide is meant to be drawn, and takes the
/// tile's accent as the stroke brush.
/// <para>
/// Each entry is a node list. A node is either a raw SVG path <c>d</c> string, or a primitive
/// written as <c>rect x y w h rx</c> or <c>circle cx cy r</c>. <see cref="PathData"/> turns the
/// primitives into <c>d</c> strings, so what comes out is always SVG — which is what lets this
/// live here, with no drawing type in sight, and lets each toolkit parse it with its own parser.
/// A few names Lucide has no icon for — a metronome, an arrangement, a rack of knobs for a device —
/// are drawn here in the same style.
/// </para>
/// <para>
/// Stored on an item as a name like <c>folder</c>. Anything unknown — a Segoe glyph from a deck
/// built before the set existed — is not here and falls through to the font, unchanged.
/// </para>
/// Lucide is ISC licensed; see <c>THIRD-PARTY-NOTICES.md</c>.
/// </remarks>
public static class DeckIconPaths
{
    /// <summary>The side of the grid every icon below is drawn on.</summary>
    public const double GridSize = 24;

    private static readonly Dictionary<string, string[]> Nodes = new(StringComparer.OrdinalIgnoreCase)
    {
        // ---------- Files and places ----------
        ["folder"] = ["M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"],
        ["folder-open"] = ["m6 14 1.5-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1-1.95 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2"],
        ["document"] =
        [
            "M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z",
            "M14 2v5a1 1 0 0 0 1 1h5", "M10 9H8", "M16 13H8", "M16 17H8",
        ],
        ["image"] = ["rect 3 3 18 18 2", "circle 9 9 2", "m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21"],
        ["globe"] = ["circle 12 12 10", "M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20", "M2 12h20"],
        ["link"] =
        [
            "M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71",
            "M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71",
        ],
        ["window"] = ["rect 2 4 20 16 2", "M10 4v4", "M2 8h20", "M6 4v4"],
        ["home"] =
        [
            "M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8",
            "M3 10a2 2 0 0 1 .709-1.528l7-6a2 2 0 0 1 2.582 0l7 6A2 2 0 0 1 21 10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z",
        ],
        ["search"] = ["m21 21-4.34-4.34", "circle 11 11 8"],
        ["settings"] =
        [
            "M9.671 4.136a2.34 2.34 0 0 1 4.659 0 2.34 2.34 0 0 0 3.319 1.915 2.34 2.34 0 0 1 2.33 4.033 2.34 2.34 0 0 0 0 3.831 2.34 2.34 0 0 1-2.33 4.033 2.34 2.34 0 0 0-3.319 1.915 2.34 2.34 0 0 1-4.659 0 2.34 2.34 0 0 0-3.32-1.915 2.34 2.34 0 0 1-2.33-4.033 2.34 2.34 0 0 0 0-3.831A2.34 2.34 0 0 1 6.35 6.051a2.34 2.34 0 0 0 3.319-1.915",
            "circle 12 12 3",
        ],
        ["star"] = ["M11.525 2.295a.53.53 0 0 1 .95 0l2.31 4.679a2.123 2.123 0 0 0 1.595 1.16l5.166.756a.53.53 0 0 1 .294.904l-3.736 3.638a2.123 2.123 0 0 0-.611 1.878l.882 5.14a.53.53 0 0 1-.771.56l-4.618-2.428a2.122 2.122 0 0 0-1.973 0L6.396 21.01a.53.53 0 0 1-.77-.56l.881-5.139a2.122 2.122 0 0 0-.611-1.879L2.16 9.795a.53.53 0 0 1 .294-.906l5.165-.755a2.122 2.122 0 0 0 1.597-1.16z"],
        ["info"] = ["circle 12 12 10", "M12 16v-4", "M12 8h.01"],
        ["clock"] = ["circle 12 12 10", "M12 6v6l4 2"],
        ["download"] = ["M12 15V3", "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4", "m7 10 5 5 5-5"],
        ["copy"] = ["rect 8 8 14 14 2", "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"],
        ["mail"] = ["m22 7-8.991 5.727a2 2 0 0 1-2.009 0L2 7", "rect 2 4 20 16 2"],
        ["trash"] = ["M10 11v6", "M14 11v6", "M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6", "M3 6h18", "M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"],
        ["lock"] = ["rect 3 11 18 11 2", "M7 11V7a5 5 0 0 1 10 0v4"],

        // ---------- Transport ----------
        ["play"] = ["M5 5a2 2 0 0 1 3.008-1.728l11.997 6.998a2 2 0 0 1 .003 3.458l-12 7A2 2 0 0 1 5 19z"],
        ["pause"] = ["rect 14 3 5 18 1", "rect 5 3 5 18 1"],
        ["stop"] = ["rect 4 4 16 16 2"],
        ["record"] = ["circle 12 12 7"],
        ["skip-forward"] = ["M21 4v16", "M6.029 4.285A2 2 0 0 0 3 6v12a2 2 0 0 0 3.029 1.715l9.997-5.998a2 2 0 0 0 .003-3.432z"],
        ["skip-back"] = ["M17.971 4.285A2 2 0 0 1 21 6v12a2 2 0 0 1-3.029 1.715l-9.997-5.998a2 2 0 0 1-.003-3.432z", "M3 20V4"],
        ["loop"] = ["m17 2 4 4-4 4", "M3 11v-1a4 4 0 0 1 4-4h14", "m7 22-4-4 4-4", "M21 13v1a4 4 0 0 1-4 4H3"],
        ["undo"] = ["M9 14 4 9l5-5", "M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5a5.5 5.5 0 0 1-5.5 5.5H11"],
        ["redo"] = ["m15 14 5-5-5-5", "M20 9H9.5A5.5 5.5 0 0 0 4 14.5A5.5 5.5 0 0 0 9.5 20H13"],

        // ---------- Sound ----------
        ["volume"] =
        [
            "M11 4.702a.705.705 0 0 0-1.203-.498L6.413 7.587A1.4 1.4 0 0 1 5.416 8H3a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h2.416a1.4 1.4 0 0 1 .997.413l3.383 3.384A.705.705 0 0 0 11 19.298z",
            "M16 9a5 5 0 0 1 0 6", "M19.364 18.364a9 9 0 0 0 0-12.728",
        ],
        ["mute"] =
        [
            "M11 4.702a.7.7 0 0 0-1.203-.498L6.413 7.587A1.4 1.4 0 0 1 5.416 8H3a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h2.416a1.4 1.4 0 0 1 .997.413l3.383 3.384A.7.7 0 0 0 11 19.298z",
            "m16.5 14.5 5-5", "m16.5 9.5 5 5",
        ],
        ["sliders"] = ["M10 5H3", "M12 19H3", "M14 3v4", "M16 17v4", "M21 12h-9", "M21 19h-5", "M21 5h-7", "M8 10v4", "M8 12H3"],
        ["equalizer"] = ["M10 8h4", "M12 21v-9", "M12 8V3", "M17 16h4", "M19 12V3", "M19 21v-5", "M3 14h4", "M5 10V3", "M5 21v-7"],
        ["waveform"] = ["M2 10v3", "M6 6v11", "M10 3v18", "M14 8v7", "M18 5v13", "M22 10v3"],
        ["metronome"] = ["M11.03 3.3a1 1 0 0 1 1.94 0l4.82 16.43a1 1 0 0 1-.96 1.27H7.17a1 1 0 0 1-.96-1.27z", "m12 7 3.4 9", "M8.4 16h7.2"],
        ["headphones"] = ["M3 14h3a2 2 0 0 1 2 2v3a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a9 9 0 0 1 18 0v7a2 2 0 0 1-2 2h-1a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2h3"],
        ["microphone"] = ["M12 19v3", "M19 10v2a7 7 0 0 1-14 0v-2", "rect 9 2 6 13 3"],
        ["piano"] =
        [
            "M10 13v4", "M14 13v4", "M18 13v4", "M2 13h20",
            "M22 11.5A3.5 3.5 0 0 0 18.5 8a3.52 3.52 0 0 1-3.173-2A7 7 0 0 0 2 9v10a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2z",
            "M6 13v4",
        ],
        ["midi"] =
        [
            "M17 19a1 1 0 0 1-1-1v-2a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2v2a1 1 0 0 1-1 1z", "M17 21v-2",
            "M19 14V6.5a1 1 0 0 0-7 0v11a1 1 0 0 1-7 0V10", "M21 21v-2", "M3 5V3",
            "M4 10a2 2 0 0 1-2-2V6a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2a2 2 0 0 1-2 2z", "M7 5V3",
        ],
        ["filter"] = ["M10 20a1 1 0 0 0 .553.895l2 1A1 1 0 0 0 14 21v-7a2 2 0 0 1 .517-1.341L21.74 4.67A1 1 0 0 0 21 3H3a1 1 0 0 0-.742 1.67l7.225 7.989A2 2 0 0 1 10 14z"],
        ["knob"] = ["circle 12 12 1", "circle 12 12 10"],

        // ---------- Ableton ----------
        ["session"] = ["rect 3 3 18 18 2", "M3 9h18", "M3 15h18", "M9 3v18", "M15 3v18"],
        ["arrangement"] = ["rect 3 5 13 3 1.5", "rect 7 10.5 14 3 1.5", "rect 3 16 8 3 1.5"],
        ["clip"] = ["rect 3 3 18 18 2", "M9 9.003a1 1 0 0 1 1.517-.859l4.997 2.997a1 1 0 0 1 0 1.718l-4.997 2.997A1 1 0 0 1 9 14.996z"],
        ["scene"] = ["rect 3 3 18 18 2", "M21 9H3", "M21 15H3"],
        ["track"] = ["M2 13a2 2 0 0 0 2-2V7a2 2 0 0 1 4 0v13a2 2 0 0 0 4 0V4a2 2 0 0 1 4 0v13a2 2 0 0 0 4 0v-4a2 2 0 0 1 2-2"],
        ["device"] = ["rect 2 7 20 10 2", "circle 7 12 1.4", "circle 12 12 1.4", "circle 17 12 1.4"],
        ["browser"] = ["rect 3 3 18 18 2", "M9 3v18"],
        ["tempo"] = ["m12 14 4-4", "M3.34 19a10 10 0 1 1 17.32 0"],
        ["music"] = ["M9 18V5l12-2v13", "circle 6 18 3", "circle 18 16 3"],
        ["power"] = ["M12 2v10", "M18.4 6.6a9 9 0 1 1-12.77.04"],
        ["keyboard"] = ["rect 2 6 20 12 2", "circle 7 10.5 0.9", "circle 12 10.5 0.9", "circle 17 10.5 0.9", "M8 15h8"],
    };

    /// <summary>The names offered in the editor's icon picker, in the order they are shown.</summary>
    public static IReadOnlyList<string> Palette { get; } = Nodes.Keys.ToList();

    /// <summary>What a tile falls back to when it has no icon of its own.</summary>
    public const string DefaultButton = "window";

    public const string DefaultFolder = "folder";

    /// <summary>True when <paramref name="name"/> is one of these icons rather than a legacy glyph.</summary>
    public static bool Has(string? name) => name is not null && Nodes.ContainsKey(name);

    /// <summary>
    /// The figures of one icon as SVG <c>d</c> strings, or null when the name belongs to the old font.
    /// </summary>
    /// <remarks>
    /// A list rather than one glued string, and that matters: Lucide's later paths start with a
    /// relative <c>m</c>, which only lands where Lucide means it to when the parser starts fresh.
    /// Concatenated after the previous figure it would take off from wherever that one ended.
    /// </remarks>
    public static IReadOnlyList<string>? PathData(string? name)
    {
        if (name is null || !Nodes.TryGetValue(name, out var specs)) return null;

        return specs.Select(ToPathData).ToArray();
    }

    private static string ToPathData(string spec)
    {
        if (spec.StartsWith("circle ", StringComparison.Ordinal))
        {
            var n = Numbers(spec);
            double cx = n[0], cy = n[1], r = n[2];

            return Format(
                "M {0} {1} A {2} {2} 0 1 1 {0} {3} A {2} {2} 0 1 1 {0} {1} Z",
                cx, cy - r, r, cy + r);
        }

        if (spec.StartsWith("rect ", StringComparison.Ordinal))
        {
            var n = Numbers(spec);
            double x = n[0], y = n[1], w = n[2], h = n[3];
            var k = n.Length > 4 ? n[4] : 0;

            if (k <= 0)
                return Format("M {0} {1} H {2} V {3} H {0} Z", x, y, x + w, y + h);

            return Format(
                "M {0} {1} H {2} A {3} {3} 0 0 1 {4} {5} V {6} A {3} {3} 0 0 1 {2} {7} H {0} " +
                "A {3} {3} 0 0 1 {8} {6} V {5} A {3} {3} 0 0 1 {0} {1} Z",
                x + k, y, x + w - k, k, x + w, y + k, y + h - k, y + h, x);
        }

        return spec;
    }

    private static string Format(string template, params double[] values) =>
        string.Format(
            CultureInfo.InvariantCulture,
            template,
            values.Select(v => (object)v.ToString("0.###", CultureInfo.InvariantCulture)).ToArray());

    private static double[] Numbers(string spec) =>
        spec[(spec.IndexOf(' ') + 1)..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => double.Parse(part, CultureInfo.InvariantCulture))
            .ToArray();
}
