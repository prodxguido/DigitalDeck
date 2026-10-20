using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using Xunit;

using Path = System.IO.Path;

namespace DigitalDeck.App.Tests;

/// <summary>
/// What happens to an image between the file the user picked and the tile it ends up on.
/// </summary>
public class IconNormalizerTests : IDisposable
{
    private readonly string _root;

    public IconNormalizerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "digitaldeck-icon-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
    }

    [DesktopFact]
    public void ALogoStrandedInATransparentCanvasComesBackCentredAndLarge()
    {
        // The shape sits in the top-left eighth of a big empty canvas — the state of most logos
        // people download, and the one that makes a tile look broken.
        var file = WriteImage(400, 400, Colors.Transparent, new Rect(20, 20, 60, 60), Colors.Red);

        var tile = Read(IconNormalizer.ToTile(file, IconFit.Fit));

        Assert.Equal(IconNormalizer.TileSize, tile.PixelWidth);
        Assert.Equal(IconNormalizer.TileSize, tile.PixelHeight);

        var ink = InkBounds(tile);

        // Centred on the square: the gap left of the ink matches the gap right of it.
        Assert.InRange(ink.Left, ink.Right - 6, ink.Right + 6);
        Assert.InRange(ink.Top, ink.Bottom - 6, ink.Bottom + 6);

        // And it grew to fill the tile rather than staying a speck in the corner.
        Assert.InRange(ink.Width, IconNormalizer.TileSize * 0.85, IconNormalizer.TileSize);
    }

    [DesktopFact]
    public void AFlatColouredBorderIsTrimmedToo()
    {
        // Same shape, but on opaque white — a JPEG logo, where there is no alpha to go by.
        var file = WriteImage(300, 300, Colors.White, new Rect(10, 130, 40, 40), Colors.Black);

        var ink = InkBounds(Read(IconNormalizer.ToTile(file, IconFit.Fit)), background: Colors.White);

        Assert.InRange(ink.Width, IconNormalizer.TileSize * 0.85, IconNormalizer.TileSize);
    }

    [DesktopFact]
    public void FillingCoversTheWholeSquareAndFittingDoesNot()
    {
        // A wide picture with content everywhere: nothing to trim, so the two framings differ.
        var file = WriteImage(400, 100, Colors.Green, new Rect(0, 0, 400, 100), Colors.Green);

        var filled = Read(IconNormalizer.ToTile(file, IconFit.Fill));
        var fitted = Read(IconNormalizer.ToTile(file, IconFit.Fit));

        // Filled: the corners are painted, because the sides were cropped away.
        Assert.True(IsOpaque(filled, 2, 2));

        // Fitted: the whole picture is there, so the tile has empty bands above and below it.
        Assert.False(IsOpaque(fitted, 2, 2));
        Assert.True(IsOpaque(fitted, IconNormalizer.TileSize / 2, IconNormalizer.TileSize / 2));
    }

    [DesktopFact]
    public void AnImageThatIsOneColourSurvivesInsteadOfBeingTrimmedToNothing()
    {
        var file = WriteImage(120, 120, Colors.Blue, new Rect(0, 0, 120, 120), Colors.Blue);

        var tile = Read(IconNormalizer.ToTile(file, IconFit.Fill));

        Assert.Equal(IconNormalizer.TileSize, tile.PixelWidth);
        Assert.True(IsOpaque(tile, IconNormalizer.TileSize / 2, IconNormalizer.TileSize / 2));
    }

    // ---------- Helpers ----------

    private string WriteImage(int width, int height, Color background, Rect shape, Color ink)
    {
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, width, height));
            context.DrawRectangle(new SolidColorBrush(ink), null, shape);
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);

        var file = Path.Combine(_root, Guid.NewGuid().ToString("n") + ".png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));

        using var stream = File.Create(file);
        encoder.Save(stream);

        return file;
    }

    private static BitmapSource Read(byte[] png)
    {
        using var stream = new MemoryStream(png);

        var decoded = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
        converted.Freeze();

        return converted;
    }

    /// <summary>Margins between the edges of the tile and the drawing inside it.</summary>
    private static (double Left, double Top, double Right, double Bottom, double Width) InkBounds(
        BitmapSource image, Color? background = null)
    {
        var width = image.PixelWidth;
        var height = image.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];

        image.CopyPixels(pixels, stride, 0);

        int left = width, top = height, right = -1, bottom = -1;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = y * stride + x * 4;

                var isBackground = background is { } colour
                    ? Math.Abs(pixels[offset] - colour.B) < 16
                      && Math.Abs(pixels[offset + 1] - colour.G) < 16
                      && Math.Abs(pixels[offset + 2] - colour.R) < 16
                    : pixels[offset + 3] < 8;

                if (isBackground) continue;

                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }

        return (left, top, width - 1 - right, height - 1 - bottom, right - left + 1);
    }

    private static bool IsOpaque(BitmapSource image, int x, int y)
    {
        var pixel = new byte[4];
        image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);

        return pixel[3] > 200;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }
}
