using System.Windows.Media;
using DigitalDeck.Core.Model;

namespace DigitalDeck.App;

/// <summary>
/// The icon set, drawn for WPF.
/// </summary>
/// <remarks>
/// The set itself — the names and their path data — is <see cref="DeckIconPaths"/>, in Core, where
/// nothing knows how to draw. All that is left here is turning those <c>d</c> strings into
/// something WPF can stroke, which is the one part that cannot be shared with another toolkit.
/// </remarks>
public static class DeckIcons
{
    private static readonly Dictionary<string, Geometry> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc cref="DeckIconPaths.GridSize"/>
    public const double GridSize = DeckIconPaths.GridSize;

    /// <inheritdoc cref="DeckIconPaths.Palette"/>
    public static IReadOnlyList<string> Palette => DeckIconPaths.Palette;

    /// <inheritdoc cref="DeckIconPaths.DefaultButton"/>
    public const string DefaultButton = DeckIconPaths.DefaultButton;

    /// <inheritdoc cref="DeckIconPaths.DefaultFolder"/>
    public const string DefaultFolder = DeckIconPaths.DefaultFolder;

    /// <inheritdoc cref="DeckIconPaths.Has"/>
    public static bool Has(string? name) => DeckIconPaths.Has(name);

    /// <summary>
    /// The drawing for an icon, or null when the name belongs to the old font.
    /// </summary>
    /// <remarks>
    /// Frozen and cached: the same handful of shapes are drawn on every tile of every page, and a
    /// frozen <see cref="Geometry"/> can be shared across them without WPF copying it per use. The
    /// group is meant to be <em>stroked</em>, never filled — its figures are open.
    /// </remarks>
    public static Geometry? Get(string? name)
    {
        if (name is null) return null;

        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var cached)) return cached;

            if (DeckIconPaths.PathData(name) is not { } figures) return null;

            var group = new GeometryGroup { FillRule = FillRule.Nonzero };

            foreach (var figure in figures)
                group.Children.Add(Geometry.Parse(figure));

            group.Freeze();
            Cache[name] = group;
            return group;
        }
    }
}
