using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DigitalDeck.App;
using Xunit;

namespace DigitalDeck.App.Tests;

/// <summary>
/// Draws every icon onto one sheet so they can be looked at.
/// </summary>
/// <remarks>
/// Icons are the one thing here no assertion can judge: a path that parses, fills and scales
/// correctly can still be an unrecognisable smudge. This renders the whole set offscreen to a PNG
/// so a person can see it, without opening the app or photographing anyone's desktop.
/// <para>
/// Off by default. Set DIGITALDECK_ICON_SHEET to a file path to produce it.
/// </para>
/// </remarks>
public class IconSheet
{
    private const double Cell = 96;
    private const double IconSize = 52;
    private const int Columns = 8;

    [Fact]
    public void EveryIconParsesIntoAShapeWithArea()
    {
        // What can be asserted: that each path is valid and encloses something. An icon whose data
        // is subtly wrong usually collapses to an empty or hairline bounding box.
        foreach (var name in DeckIcons.Palette)
        {
            var geometry = DeckIcons.Get(name);

            Assert.True(geometry is not null, $"«{name}» no se pudo interpretar");

            var bounds = geometry!.Bounds;

            Assert.True(bounds.Width > 4, $"«{name}» mide {bounds.Width:n1} de ancho");
            Assert.True(bounds.Height > 4, $"«{name}» mide {bounds.Height:n1} de alto");

            // Everything is drawn on a 24×24 grid; straying outside means it will be clipped or
            // will shrink every other icon sharing a Viewbox with it.
            Assert.True(bounds.Left >= -1 && bounds.Top >= -1,
                        $"«{name}» empieza en {bounds.Left:n1},{bounds.Top:n1}");

            Assert.True(bounds.Right <= DeckIcons.GridSize + 1 && bounds.Bottom <= DeckIcons.GridSize + 1,
                        $"«{name}» termina en {bounds.Right:n1},{bounds.Bottom:n1}");
        }
    }

    [Fact]
    public void RenderTheSheet()
    {
        var destination = Environment.GetEnvironmentVariable("DIGITALDECK_ICON_SHEET");

        if (string.IsNullOrWhiteSpace(destination)) return;

        var thread = new Thread(() => Draw(destination));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.True(File.Exists(destination));
    }

    private static void Draw(string destination)
    {
        var names = DeckIcons.Palette;
        var rows = (int)Math.Ceiling(names.Count / (double)Columns);

        var width = Columns * Cell;
        var height = rows * Cell;

        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(
                new SolidColorBrush(Color.FromRgb(0x17, 0x1B, 0x24)), null, new Rect(0, 0, width, height));

            var ink = new SolidColorBrush(Color.FromRgb(0x33, 0xC9, 0xDC));
            var label = new SolidColorBrush(Color.FromRgb(0xA7, 0xAE, 0xBB));

            // The icons are line art: stroked, never filled, the way the app draws them.
            var pen = new Pen(ink, 2)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            pen.Freeze();

            for (var index = 0; index < names.Count; index++)
            {
                var geometry = DeckIcons.Get(names[index]);
                if (geometry is null) continue;

                var column = index % Columns;
                var row = index / Columns;

                var originX = column * Cell;
                var originY = row * Cell;

                var scale = IconSize / DeckIcons.GridSize;

                context.PushTransform(new TranslateTransform(
                    originX + (Cell - IconSize) / 2,
                    originY + 14));
                context.PushTransform(new ScaleTransform(scale, scale));
                context.DrawGeometry(null, pen, geometry);
                context.Pop();
                context.Pop();

                var text = new FormattedText(
                    names[index],
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    10,
                    label,
                    1.0);

                context.DrawText(text, new Point(originX + (Cell - text.Width) / 2, originY + Cell - 22));
            }
        }

        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(destination);
        encoder.Save(stream);
    }
}
