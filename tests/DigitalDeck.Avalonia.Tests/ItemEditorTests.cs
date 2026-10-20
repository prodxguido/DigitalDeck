using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using Xunit;

namespace DigitalDeck.Avalonia.Tests;

/// <summary>
/// Editing a button in the Avalonia shell.
/// </summary>
/// <remarks>
/// The window is driven directly rather than through the modal helper: what matters here is what
/// it writes back onto the item, and pushing a dispatcher frame in a test would just be waiting
/// for a window nobody is going to close.
/// <para>
/// The rule these are really defending is the last one: a button whose action this editor cannot
/// show must come out with that action intact. An editor that quietly dropped it would turn
/// opening a button on a Mac into losing it.
/// </para>
/// </remarks>
public class ItemEditorTests
{
    private static DeckItem Button(string label = "Mi botón") =>
        DeckItem.NewButton("página", null, "a", label);

    private static ItemEditorWindow Open(DeckItem item, params string[] taken) =>
        new(item, taken);

    [AvaloniaFact]
    public void ANameAndAColourAreWrittenBackOntoTheItem()
    {
        var item = Button();
        var editor = Open(item);

        try
        {
            editor.LabelBox.Text = "Reproducir";
            editor.MixerChoice.IsChecked = true;

            editor.Save();

            Assert.True(editor.Saved);
            Assert.Equal("Reproducir", item.Label);
            Assert.IsType<MixerPanelAction>(item.Action);
        }
        finally
        {
            editor.Close();
        }
    }

    [AvaloniaFact]
    public void AButtonWithNoNameIsNotSaved()
    {
        var item = Button("Como estaba");
        var editor = Open(item);

        try
        {
            editor.LabelBox.Text = "   ";

            editor.Save();

            Assert.False(editor.Saved);
            Assert.Equal("Como estaba", item.Label);
        }
        finally
        {
            editor.Close();
        }
    }

    [AvaloniaFact]
    public void ANameSomebodyElseAlreadyHasIsRefused()
    {
        var item = Button("Mi botón");
        var editor = Open(item, "Ableton", "Web");

        try
        {
            editor.LabelBox.Text = "web";   // La misma que «Web», mirada como la mira el deck.

            editor.Save();

            Assert.False(editor.Saved);
        }
        finally
        {
            editor.Close();
        }
    }

    [AvaloniaFact]
    public void KeepingItsOwnNameIsNotACollision()
    {
        var item = Button("Web");
        var editor = Open(item, "Web", "Ableton");

        try
        {
            editor.MixerChoice.IsChecked = true;

            editor.Save();

            Assert.True(editor.Saved);
        }
        finally
        {
            editor.Close();
        }
    }

    [AvaloniaFact]
    public void AWebButtonKeepsItsAddress()
    {
        var item = Button();
        var editor = Open(item);

        try
        {
            editor.LabelBox.Text = "El diario";
            editor.UrlChoice.IsChecked = true;
            editor.UrlBox.Text = "lanacion.com.ar";

            editor.Save();

            Assert.True(editor.Saved);

            // Normalizada como la normaliza el deck, no guardada tal cual se tipeó.
            var url = Assert.IsType<OpenUrlAction>(item.Action);
            Assert.StartsWith("https://", url.Url);
        }
        finally
        {
            editor.Close();
        }
    }

    // ---------- Comandos de Live ----------

    private static AbletonCommandInfo Info(AbletonCommand command) =>
        AbletonCommands.All.First(info => info.Command == command);

    [AvaloniaFact]
    public void ASimpleLiveCommandNeedsNothingElse()
    {
        var item = Button();
        var editor = Open(item);

        try
        {
            editor.LabelBox.Text = "Reproducir";
            editor.AbletonChoice.IsChecked = true;
            editor.CommandBox.SelectedItem = Info(AbletonCommand.TogglePlay);

            // Reproducir no pide track, ni escena, ni valor: ofrecerlos sería ofrecer preguntas
            // sin respuesta correcta.
            Assert.False(editor.TrackRow.IsVisible);
            Assert.False(editor.ValueRow.IsVisible);

            editor.Save();

            var action = Assert.IsType<AbletonAction>(item.Action);
            Assert.Equal(AbletonCommand.TogglePlay, action.Command);
        }
        finally
        {
            editor.Close();
        }
    }

    [AvaloniaFact]
    public void ACommandThatTakesANumberAsksForItAndKeepsIt()
    {
        var item = Button();
        var editor = Open(item);

        try
        {
            editor.LabelBox.Text = "128 BPM";
            editor.AbletonChoice.IsChecked = true;
            editor.CommandBox.SelectedItem = Info(AbletonCommand.SetTempo);

            Assert.True(editor.ValueRow.IsVisible);

            editor.ValueBox.Text = "128";
            editor.Save();

            var action = Assert.IsType<AbletonAction>(item.Action);
            Assert.Equal(128, action.Value);
        }
        finally
        {
            editor.Close();
        }
    }

    [AvaloniaFact]
    public void ANumberWrittenWithACommaIsAccepted()
    {
        // El teclado numérico en español escribe coma. Rechazarla sería rechazar lo que el teclado
        // de la persona produjo.
        var item = Button();
        var editor = Open(item);

        try
        {
            editor.LabelBox.Text = "Medio volumen";
            editor.AbletonChoice.IsChecked = true;
            editor.CommandBox.SelectedItem = Info(AbletonCommand.SetVolume);
            editor.ValueBox.Text = "0,5";

            editor.Save();

            var action = Assert.IsType<AbletonAction>(item.Action);
            Assert.Equal(0.5, action.Value);
        }
        finally
        {
            editor.Close();
        }
    }

    [AvaloniaFact]
    public void ANumberOutsideWhatLiveAcceptsIsRefused()
    {
        var item = Button();
        var editor = Open(item);

        try
        {
            editor.LabelBox.Text = "Imposible";
            editor.AbletonChoice.IsChecked = true;
            editor.CommandBox.SelectedItem = Info(AbletonCommand.SetTempo);
            editor.ValueBox.Text = "9000";

            editor.Save();

            Assert.False(editor.Saved);
            Assert.True(editor.Problem.IsVisible);
            Assert.Null(item.Action);
        }
        finally
        {
            editor.Close();
        }
    }

    [AvaloniaFact]
    public void ALiveButtonComesBackWithItsCommandSelected()
    {
        // Reabrir un botón guardado: el comando y el track tienen que estar donde se los dejó.
        var item = Button("Mutear");
        item.Action = new AbletonAction { Command = AbletonCommand.ToggleMute, TrackIndex = 3 };

        var editor = Open(item);

        try
        {
            Assert.True(editor.AbletonChoice.IsChecked);
            Assert.Equal(Info(AbletonCommand.ToggleMute), editor.CommandBox.SelectedItem);
            Assert.Equal("3", editor.TrackBox.Text);
        }
        finally
        {
            editor.Close();
        }
    }

    [AvaloniaFact]
    public void AnActionThisEditorCannotShowSurvivesBeingEdited()
    {
        // Un botón de Live abierto en Mac: se le puede cambiar el nombre, y lo que hace queda.
        // Un atajo de teclado: en macOS mandar teclas a otra app es un evento de Quartz con el
        // permiso de accesibilidad detrás, y eso todavía no existe de este lado.
        var item = Button("Exportar");
        var original = new HotkeyAction { Keys = "Ctrl+Shift+R", Program = "Ableton Live" };
        item.Action = original;

        var editor = Open(item);

        try
        {
            Assert.True(editor.Unported.IsVisible, "tendría que avisar que no sabe mostrar esta acción");
            Assert.False(editor.Kinds.IsEnabled, "no tendría que dejar cambiarla a otra cosa");

            editor.LabelBox.Text = "Reproducir";

            editor.Save();

            Assert.True(editor.Saved);
            Assert.Equal("Reproducir", item.Label);
            Assert.Same(original, item.Action);
        }
        finally
        {
            editor.Close();
        }
    }

    [AvaloniaFact]
    public void TheColoursAndTheIconsOnOfferAreTheOnesTheDeckKnows()
    {
        var editor = Open(Button());

        try
        {
            Assert.Equal(ItemEditorWindow.Palette, (IEnumerable<string>)editor.Swatches.ItemsSource!);
            Assert.Equal(DeckIconPaths.Palette, (IEnumerable<string>)editor.Icons.ItemsSource!);
        }
        finally
        {
            editor.Close();
        }
    }
}
