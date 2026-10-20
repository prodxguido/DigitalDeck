using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DigitalDeck.Core.Abstractions;

namespace DigitalDeck.App;

/// <summary>Shows an element only when the bound value is not null.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <summary>Set to true to invert: visible only when the value <em>is</em> null.</summary>
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value is not null;
        return hasValue != Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Turns an icon name into the shape that draws it.</summary>
public sealed class IconGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        DeckIcons.Get(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Highlights the entry that matches the current selection.</summary>
public sealed class SelectionBrushConverter : IMultiValueConverter
{
    public Brush SelectedBrush { get; set; } = Brushes.Transparent;
    public Brush UnselectedBrush { get; set; } = Brushes.Transparent;

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && ReferenceEquals(values[0], values[1]) ? SelectedBrush : UnselectedBrush;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Highlights whichever grid-size button matches the page actually on screen.</summary>
public sealed class GridSizeSelectionConverter : IMultiValueConverter
{
    public Brush SelectedBrush { get; set; } = Brushes.Transparent;
    public Brush UnselectedBrush { get; set; } = Brushes.Transparent;

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [int columns, int rows, string preset]) return UnselectedBrush;

        var parts = preset.Split('x');

        return parts.Length == 2
               && int.TryParse(parts[0], out var presetColumns)
               && int.TryParse(parts[1], out var presetRows)
               && presetColumns == columns
               && presetRows == rows
            ? SelectedBrush
            : UnselectedBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Colours the Ableton indicator by how the bridge is doing.</summary>
/// <remarks>
/// Four states rather than a green/red pair, because the three ways of being disconnected call for
/// different actions from the user: open Live, install the script, or free the port. A single red
/// dot would hide which one it is.
/// </remarks>
public sealed class AbletonStateBrushConverter : IValueConverter
{
    public Brush Connected { get; set; } = Brushes.LimeGreen;

    /// <summary>Looking for Live — normal while Ableton is closed, so it must not read as an error.</summary>
    public Brush Searching { get; set; } = Brushes.Gray;

    /// <summary>Something is wrong that waiting will not fix.</summary>
    public Brush Broken { get; set; } = Brushes.OrangeRed;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            AbletonConnectionState.Connected => Connected,
            AbletonConnectionState.VersionMismatch or AbletonConnectionState.Unavailable => Broken,
            _ => Searching
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Selects one of two colours depending on whether the status line is an error.</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public Brush TrueBrush { get; set; } = Brushes.Red;
    public Brush FalseBrush { get; set; } = Brushes.Gray;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TrueBrush : FalseBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows the placeholder that sits behind the search box while it is still empty.</summary>
public sealed class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Turns Live's <c>0xRRGGBB</c> into a brush, and no colour into no brush.</summary>
/// <remarks>
/// Null stays null so a binding's <c>TargetNullValue</c> keeps working: the master and the return
/// tracks have no colour of their own, and the view falls back to the theme's border rather than to
/// an invented one.
/// </remarks>
public sealed class RgbToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int rgb) return null;

        var brush = new SolidColorBrush(Color.FromRgb(
            (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF)));

        brush.Freeze();

        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Turns a number of pixels into a margin on the left only.</summary>
public sealed class LeftIndentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new Thickness(value is double pixels ? pixels : 0, 0, 0, 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Turns a tile's <c>#RRGGBB</c> into a brush, optionally softened to a tint.</summary>
/// <remarks>
/// One converter with three instances rather than three properties on the view model: the tile
/// keeps saying what colour it is, and how strongly to paint it is the view's business — a wash
/// behind the whole key, a stronger glow while it is lit, and the full colour for the icon.
/// </remarks>
public sealed class HexToBrushConverter : IValueConverter
{
    /// <summary>The default when the stored colour cannot be read. The app's own accent.</summary>
    private static readonly Color Fallback = Color.FromRgb(0x33, 0xC9, 0xDC);

    /// <summary>How opaque to paint it. 255 is the colour as the user picked it.</summary>
    public byte Alpha { get; set; } = 255;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var colour = Parse(value as string);

        var brush = new SolidColorBrush(Color.FromArgb(Alpha, colour.R, colour.G, colour.B));
        brush.Freeze();

        return brush;
    }

    private static Color Parse(string? hex)
    {
        if (hex is null) return Fallback;

        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color parsed) return parsed;
        }
        catch (FormatException)
        {
            // A colour saved by a build that spelled it differently is not worth a broken tile.
        }

        return Fallback;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Loads a tile's own image from disk.</summary>
/// <remarks>
/// Cached because the same handful of logos are drawn on every page, and loaded up front
/// (<c>OnLoad</c>) so the file is not held open — icons can be replaced while the app is running.
/// A corrupt or unreadable file becomes null, and the tile falls back to its glyph.
/// </remarks>
public sealed class IconImageConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, BitmapImage?> Cache = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not string path || path.Length == 0 ? null : Cache.GetOrAdd(path, Load);

    private static BitmapImage? Load(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
