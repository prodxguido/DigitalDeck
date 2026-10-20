using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DigitalDeck.App.Services;

/// <summary>How an image is made to fit a square tile.</summary>
public enum IconFit
{
    /// <summary>The whole image, with breathing room around it. What a logo wants.</summary>
    Fit,

    /// <summary>Fills the square, cropping what sticks out. What a photo wants.</summary>
    Fill
}

/// <summary>
/// Turns whatever image the user picked into one that looks right on a tile.
/// </summary>
/// <remarks>
/// The work that matters here is not the resize — it is the <b>trim</b>. Almost every logo people
/// download carries its own transparent or flat-coloured margin, sometimes half the file. Drawn as
/// it comes, it lands small and visibly off-centre, because it is centred on its canvas rather than
/// on what is actually drawn in it. Cropping to the ink first is what makes twenty logos from
/// twenty different sources sit the same way on twenty tiles.
/// <para>
/// The result is always a square PNG, so the tile never has to reason about aspect ratios and a
/// deck of mixed logos and photos reads as one grid rather than as a collage.
/// </para>
/// </remarks>
public static class IconNormalizer
{
    /// <summary>Side of the produced image, in pixels.</summary>
    /// <remarks>Twice what a 2×2 tile shows on a 1080p screen, so scaling never softens it.</remarks>
    public const int TileSize = 256;

    /// <summary>Margin left around the trimmed image, as a fraction of the square.</summary>
    /// <remarks>
    /// A logo touching the edges looks like a mistake, and the tile already has padding of its own,
    /// so this is deliberately small.
    /// </remarks>
    private const double Padding = 0.06;

    /// <summary>
    /// How far a pixel may sit from the corner colour and still count as background.
    /// </summary>
    /// <remarks>
    /// Not zero, because a JPEG's flat white is not one value — compression leaves it wandering by
    /// a few counts, and an exact test would trim nothing at all on exactly the files that need it
    /// most.
    /// </remarks>
    private const int BackgroundTolerance = 12;

    /// <summary>Reads an image and returns the PNG bytes of its tile-ready square.</summary>
    public static byte[] ToTile(string sourceFile, IconFit fit)
    {
        var source = Decode(sourceFile);
        var trimmed = Trim(source);

        return Encode(Square(trimmed, fit));
    }

    private static BitmapSource Decode(string file)
    {
        var bitmap = new BitmapImage();

        bitmap.BeginInit();
        bitmap.UriSource = new Uri(file);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.EndInit();
        bitmap.Freeze();

        // Everything below reads pixels as straight BGRA; converting once here means the trim does
        // not need a branch per source format.
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        converted.Freeze();

        return converted;
    }

    /// <summary>
    /// Crops away a uniform border: transparency when the image has an alpha channel, otherwise
    /// whatever colour the four corners agree on.
    /// </summary>
    /// <remarks>
    /// Corners rather than a fixed white, because logos ship on black just as often. When the
    /// corners disagree the image has content running to its edge — a photo — and nothing is
    /// trimmed, which is the right answer for a photo.
    /// </remarks>
    private static BitmapSource Trim(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;

        if (width < 2 || height < 2) return source;

        var stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);

        var background = BackgroundOf(pixels, stride, width, height);
        if (background is null) return source;

        int left = width, top = height, right = -1, bottom = -1;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (IsBackground(pixels, y * stride + x * 4, background.Value)) continue;

                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }

        // Nothing but background, or nothing to gain: hand back what came in rather than an empty
        // crop. An image that is entirely one colour is a legitimate thing to put on a button.
        if (right < left || bottom < top) return source;
        if (left == 0 && top == 0 && right == width - 1 && bottom == height - 1) return source;

        var cropped = new CroppedBitmap(source, new Int32Rect(left, top, right - left + 1, bottom - top + 1));
        cropped.Freeze();

        return cropped;
    }

    /// <summary>The colour to treat as background, or null when the edges do not agree on one.</summary>
    private static uint? BackgroundOf(byte[] pixels, int stride, int width, int height)
    {
        var topLeft = At(pixels, 0);
        var topRight = At(pixels, (width - 1) * 4);
        var bottomLeft = At(pixels, (height - 1) * stride);
        var bottomRight = At(pixels, (height - 1) * stride + (width - 1) * 4);

        // Transparent corners settle it: alpha is unambiguous in a way a colour never is.
        if (Alpha(topLeft) == 0 && Alpha(topRight) == 0 && Alpha(bottomLeft) == 0 && Alpha(bottomRight) == 0)
            return 0;

        return Close(topLeft, topRight) && Close(topLeft, bottomLeft) && Close(topLeft, bottomRight)
            ? topLeft
            : null;
    }

    private static uint At(byte[] pixels, int offset) =>
        (uint)(pixels[offset] | (pixels[offset + 1] << 8) | (pixels[offset + 2] << 16) | (pixels[offset + 3] << 24));

    private static byte Alpha(uint pixel) => (byte)(pixel >> 24);

    private static bool IsBackground(byte[] pixels, int offset, uint background)
    {
        var pixel = At(pixels, offset);

        // A transparent background swallows every transparent pixel, whatever colour hides under it.
        return background == 0 ? Alpha(pixel) == 0 : Close(pixel, background);
    }

    private static bool Close(uint left, uint right)
    {
        for (var shift = 0; shift < 32; shift += 8)
        {
            var difference = ((int)(left >> shift) & 0xFF) - ((int)(right >> shift) & 0xFF);
            if (Math.Abs(difference) > BackgroundTolerance) return false;
        }

        return true;
    }

    /// <summary>Draws the image into a square of <see cref="TileSize"/>, fitted or filled.</summary>
    private static BitmapSource Square(BitmapSource source, IconFit fit)
    {
        var inset = fit == IconFit.Fit ? TileSize * Padding : 0;
        var box = TileSize - inset * 2;

        // Fit takes the smaller ratio so everything gets in; fill takes the larger so nothing of
        // the square is left uncovered, and the overflow is clipped by the drawing bounds.
        var scale = fit == IconFit.Fit
            ? Math.Min(box / source.PixelWidth, box / source.PixelHeight)
            : Math.Max((double)TileSize / source.PixelWidth, (double)TileSize / source.PixelHeight);

        var width = source.PixelWidth * scale;
        var height = source.PixelHeight * scale;

        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.PushClip(new RectangleGeometry(new Rect(0, 0, TileSize, TileSize)));
            context.DrawImage(source, new Rect((TileSize - width) / 2, (TileSize - height) / 2, width, height));
            context.Pop();
        }

        // Pbgra32 keeps the transparency of a logo intact; a tile draws over its own colour wash.
        var target = new RenderTargetBitmap(TileSize, TileSize, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();

        return target;
    }

    private static byte[] Encode(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));

        using var buffer = new MemoryStream();
        encoder.Save(buffer);

        return buffer.ToArray();
    }
}
