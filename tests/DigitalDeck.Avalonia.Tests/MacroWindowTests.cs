using Avalonia.Headless.XUnit;
using DigitalDeck.Core.Model.Actions;
using Xunit;

namespace DigitalDeck.Avalonia.Tests;

/// <summary>
/// Un botón que hace varias cosas, en orden.
/// </summary>
/// <remarks>
/// «Arrancá la mañana» es exactamente esto: la playlist, el diario, Live y Spotify, de un botón.
/// La espera es lo que deja que un paso que necesita un programa ya abierto lo encuentre ahí.
/// </remarks>
public class MacroWindowTests
{
    private static OpenUrlAction Url(string url) => new() { Url = url };

    private static MacroAction Existing() => new()
    {
        ContinueOnError = false,
        Steps =
        {
            new MacroStep { Action = Url("https://youtube.com") },
            new MacroStep { Action = new MixerPanelAction(), DelayMs = 500 }
        }
    };

    [AvaloniaFact]
    public void ItOpensWithTheStepsItAlreadyHad()
    {
        var window = new MacroWindow(Existing(), null);

        try
        {
            Assert.Equal(2, window.Current.Count);
            Assert.Equal("500", window.Current[1].DelayText);
            Assert.False(window.ContinueBox.IsChecked);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TheOrderIsWhatGetsSaved()
    {
        var window = new MacroWindow(null, null);

        try
        {
            window.Add(Url("https://uno.com"));
            window.Add(Url("https://dos.com"));

            Assert.True(window.Save());

            var steps = window.Result!.Steps;

            Assert.Equal(2, steps.Count);
            Assert.Contains("uno.com", steps[0].Action.Summary);
            Assert.Contains("dos.com", steps[1].Action.Summary);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AMacroWithNoStepsIsRefused()
    {
        // Un botón de varias acciones sin ninguna es un botón que no hace nada y parece que sí.
        var window = new MacroWindow(null, null);

        try
        {
            Assert.False(window.Save());
            Assert.True(window.Problem.IsVisible);
            Assert.Null(window.Result);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AWaitThatIsNotANumberIsRefusedAndTheStepIsNamed()
    {
        var window = new MacroWindow(null, null);

        try
        {
            window.Add(Url("https://uno.com"));
            window.Current[0].DelayText = "un rato";

            Assert.False(window.Save());
            Assert.Contains("uno.com", window.Problem.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AWaitLongerThanTheCeilingIsRefused()
    {
        var window = new MacroWindow(null, null);

        try
        {
            window.Add(Url("https://uno.com"));
            window.Current[0].DelayText = (MacroStep.MaxDelayMs + 1).ToString();

            Assert.False(window.Save());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CarryingOnAfterAFailureIsWhatItOffersFirst()
    {
        // Que Spotify no esté instalado no es motivo para quedarte sin el resto.
        var window = new MacroWindow(null, null);

        try
        {
            Assert.True(window.ContinueBox.IsChecked);

            window.Add(Url("https://uno.com"));
            window.Save();

            Assert.True(window.Result!.ContinueOnError);
        }
        finally
        {
            window.Close();
        }
    }
}
