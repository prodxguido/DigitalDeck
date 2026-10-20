using Avalonia.Headless.XUnit;
using DigitalDeck.Data.Packs;
using Xunit;

namespace DigitalDeck.Avalonia.Tests;

/// <summary>
/// El filtro de archivos que el código compartido pasa, leído del lado de Avalonia.
/// </summary>
/// <remarks>
/// «Nombre|*.ext» es la forma que tiene IDeckDialogs desde siempre, porque es la que toma el
/// diálogo de Windows. Es una descripción de un filtro, no una llamada al API de Windows, así que
/// se lee igual en una Mac — pero hay que leerla.
/// </remarks>
public class FileFilterTests
{
    [AvaloniaFact]
    public void TheFilterTheAppActuallyUsesIsUnderstood()
    {
        // El de verdad, no uno inventado: si esto cambia, exportar deja de ofrecer .deckpack.
        var types = FileFilter.Parse(DeckPackService.FileDialogFilter);

        Assert.Single(types);
        Assert.Equal("Paquete de DigitalDeck", types[0].Name);
        Assert.Equal(["*.deckpack"], types[0].Patterns);
    }

    [AvaloniaFact]
    public void TheDefaultExtensionComesWithoutItsStarOrItsDot()
    {
        // Sin esto, un archivo guardado en Mac sale sin extensión, y un .deckpack sin .deckpack es
        // un archivo que la app no va a ofrecer abrir de nuevo.
        Assert.Equal("deckpack", FileFilter.DefaultExtension(DeckPackService.FileDialogFilter));
    }

    [AvaloniaFact]
    public void SeveralKindsAndSeveralPatternsAreKeptInOrder()
    {
        var types = FileFilter.Parse("Imágenes|*.png;*.jpg|Todos|*.*");

        Assert.Equal(2, types.Count);
        Assert.Equal(["*.png", "*.jpg"], types[0].Patterns);
        Assert.Equal("Todos", types[1].Name);
    }

    [AvaloniaFact]
    public void SomethingUnreadableBecomesNoFilterRatherThanABrokenOne()
    {
        // Mostrar todos los archivos es un diálogo peor; mostrar ninguno es uno roto.
        Assert.Empty(FileFilter.Parse(null));
        Assert.Empty(FileFilter.Parse(""));
        Assert.Empty(FileFilter.Parse("sin barra ni patrones"));
        Assert.Null(FileFilter.DefaultExtension("sin barra"));
    }

    [AvaloniaFact]
    public void AHalfWrittenFilterKeepsTheHalfThatIsThere()
    {
        var types = FileFilter.Parse("Imágenes|*.png|Sobra");

        Assert.Single(types);
        Assert.Equal("Imágenes", types[0].Name);
    }
}
