using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace DigitalDeck.App.Tests;

/// <summary>
/// A test that needs WPF to really rasterise something, and skips when it cannot.
/// </summary>
/// <remarks>
/// <see cref="RenderTargetBitmap"/> does not fail on a session with no interactive desktop — a
/// locked machine, a service account, an agent — it quietly renders nothing. Everything downstream
/// then reads a transparent image and reports, truthfully but uselessly, that the trimming lost the
/// whole picture.
/// <para>
/// So the environment is probed rather than assumed: draw a known rectangle, read the pixel back,
/// and skip if it did not arrive. A test that fails for the machine it ran on teaches people to
/// ignore red, which is worse than not running it.
/// </para>
/// </remarks>
public sealed class DesktopFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> CanRender = new(Probe);

    public DesktopFactAttribute()
    {
        if (!CanRender.Value)
            Skip = "Necesita un escritorio interactivo: WPF no está rasterizando en esta sesión.";
    }

    private static bool Probe()
    {
        try
        {
            var visual = new DrawingVisual();

            using (var context = visual.RenderOpen())
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1, 1));

            var target = new RenderTargetBitmap(1, 1, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);

            var pixel = new byte[4];
            target.CopyPixels(pixel, 4, 0);

            // Anything at all came back: on a session that cannot rasterise, every byte is zero.
            return pixel.Any(component => component != 0);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
