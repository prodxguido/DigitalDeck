using Avalonia.Headless.XUnit;
using DigitalDeck.Data.Packs;
using Xunit;

namespace DigitalDeck.Avalonia.Tests;

/// <summary>
/// Elegir qué plantillas instalar y a dónde van.
/// </summary>
/// <remarks>
/// Es la puerta por la que un deck vacío en una Mac se llena: las plantillas viajan adentro de la
/// app, y sin esta ventana no hay forma de sacarlas.
/// </remarks>
public class TemplatesWindowTests
{
    private static DeckTemplate Template(string name) =>
        new($"{name}.deckpack", name, Groups: 4, Buttons: 30);

    private static TemplatesWindow Open(params string[] names) =>
        new(names.Select(Template).ToList());

    [AvaloniaFact]
    public void NothingTickedIsTheSameAsCancelar()
    {
        // La app lee una lista vacía como «no instalar nada», así que abrir y aceptar sin tocar
        // nada no puede terminar instalando todo.
        var window = Open("FabFilter", "Waves");

        try
        {
            var result = window.Install();

            Assert.Empty(result.Templates);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void OnlyWhatWasTickedComesBack()
    {
        var window = Open("FabFilter", "Waves", "Valhalla");

        try
        {
            var choices = window.List.ItemsSource!.Cast<DigitalDeck.ViewModels.TemplateChoice>().ToList();

            choices[0].IsSelected = true;
            choices[2].IsSelected = true;

            var result = window.Install();

            Assert.Equal(["FabFilter", "Valhalla"], result.Templates.Select(t => t.Name));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void APageEachIsWhatItOffersFirst()
    {
        // Una estantería de quince plugins es una pantalla, no una tecla. La otra opción está para
        // quien arma una sola página con las tres marcas que usa de verdad.
        var window = Open("FabFilter");

        try
        {
            Assert.True(window.AsPages.IsChecked);

            var chosen = window.List.ItemsSource!.Cast<DigitalDeck.ViewModels.TemplateChoice>().First();
            chosen.IsSelected = true;

            Assert.Equal(PackImportTarget.NewPage, window.Install().Target);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AskingForAGroupInsteadIsRespected()
    {
        var window = Open("FabFilter");

        try
        {
            window.AsGroups.IsChecked = true;

            var chosen = window.List.ItemsSource!.Cast<DigitalDeck.ViewModels.TemplateChoice>().First();
            chosen.IsSelected = true;

            Assert.Equal(PackImportTarget.CurrentPage, window.Install().Target);
        }
        finally
        {
            window.Close();
        }
    }
}
