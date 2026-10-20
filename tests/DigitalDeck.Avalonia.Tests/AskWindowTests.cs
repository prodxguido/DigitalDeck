using Avalonia.Headless.XUnit;
using Xunit;

namespace DigitalDeck.Avalonia.Tests;

/// <summary>
/// The small windows: saying something, asking yes or no, asking for a word.
/// </summary>
/// <remarks>
/// Small but worth pinning, because the failure is silent: a deck that ignores «¿seguro que querés
/// eliminarlo?» either deletes nothing or deletes everything, and either way the person never saw
/// the question.
/// </remarks>
public class AskWindowTests
{
    [AvaloniaFact]
    public void SayingSomethingHasNothingToCancelAndNothingToType()
    {
        var window = new AskWindow("DigitalDeck", "No pude abrir esa carpeta.");

        try
        {
            Assert.False(window.CancelButton.IsVisible, "un aviso no se cancela, se cierra");
            Assert.False(window.Answer.IsVisible);
            Assert.Equal("No pude abrir esa carpeta.", window.Message.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AskingYesOrNoStartsAsNo()
    {
        // Cerrar la ventana sin contestar tiene que valer que no: la alternativa es un borrado que
        // nadie confirmó.
        var window = new AskWindow("Eliminar", "¿Seguro?", cancellable: true);

        try
        {
            Assert.True(window.CancelButton.IsVisible);
            Assert.False(window.Accepted);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AcceptingIsWhatTurnsItIntoAYes()
    {
        var window = new AskWindow("Eliminar", "¿Seguro?", cancellable: true);

        window.Accept();

        Assert.True(window.Accepted);
    }

    [AvaloniaFact]
    public void AskingForAWordStartsFromWhatWasThere()
    {
        var window = new AskWindow("Renombrar", "Nombre de la página", "Inicio");

        try
        {
            Assert.True(window.Answer.IsVisible);
            Assert.Equal("Inicio", window.Text);

            // Y se puede cancelar: renombrar sin querer es más molesto que no renombrar.
            Assert.True(window.CancelButton.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }
}
