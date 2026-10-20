using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DigitalDeck.Core.Model;

namespace DigitalDeck.Avalonia;

/// <summary>Turns a tile's <c>#RRGGBB</c> into a brush, optionally softened to a tint.</summary>
/// <remarks>
/// The same three instances the WPF shell declares, for the same reason: the tile keeps saying what
/// colour it is, and how strongly to paint it is the view's business. That both shells need this
/// converter and neither needs it on the view model is the whole point of the split.
/// </remarks>
public sealed class HexToBrushConverter : IValueConverter
{
    /// <summary>The default when the stored colour cannot be read. The app's own accent.</summary>
    private static readonly Color Fallback = Color.FromRgb(0x33, 0xC9, 0xDC);

    /// <summary>How opaque to paint it. 255 is the colour as the user picked it.</summary>
    public byte Alpha { get; set; } = 255;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var colour = Color.TryParse(value as string, out var parsed) ? parsed : Fallback;

        return new SolidColorBrush(Color.FromArgb(Alpha, colour.R, colour.G, colour.B));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Turns an icon's name into something Avalonia can stroke.</summary>
/// <remarks>
/// Reads the same path data the WPF shell reads, out of <see cref="DeckIconPaths"/> in Core. The
/// figures are SVG <c>d</c> strings, which both toolkits parse with their own parser — that is what
/// lets one icon set serve both without either of them owning it.
/// </remarks>
public sealed class IconGeometryConverter : IValueConverter
{
    private static readonly Dictionary<string, Geometry> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string name) return null;

        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var cached)) return cached;

            if (DeckIconPaths.PathData(name) is not { } figures) return null;

            var group = new GeometryGroup { FillRule = FillRule.NonZero };

            foreach (var figure in figures)
                group.Children.Add(Geometry.Parse(figure));

            Cache[name] = group;

            return group;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Loads a tile's own image from disk, or nothing when it has none.</summary>
public sealed class IconImageConverter : IValueConverter
{
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || path.Length == 0) return null;

        lock (Cache)
        {
            if (Cache.TryGetValue(path, out var cached)) return cached;

            Bitmap? bitmap;

            try
            {
                bitmap = new Bitmap(path);
            }
            catch (Exception)
            {
                // A corrupt or unreadable image falls back to the glyph, as it does in WPF.
                bitmap = null;
            }

            Cache[path] = bitmap;

            return bitmap;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Turns Live's <c>0xRRGGBB</c> into a brush, and no colour into no brush.</summary>
/// <remarks>
/// Null stays null so a binding's <c>TargetNullValue</c> keeps working: the master and the return
/// tracks have no colour of their own, and the view falls back to the theme's border rather than
/// to an invented one.
/// </remarks>
public sealed class RgbToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int rgb) return null;

        return new SolidColorBrush(Color.FromRgb(
            (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF)));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Turns a number of pixels into a margin on the left only.</summary>
/// <remarks>
/// How far a channel is indented is a number on the view model — every toolkit spells a margin
/// differently — and turning it into one is the view's job. This is the Avalonia half of that.
/// </remarks>
public sealed class LeftIndentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new Thickness(value is double pixels ? pixels : 0, 0, 0, 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
