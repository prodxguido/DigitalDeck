using System.IO;
using System.Windows.Controls;
using DigitalDeck.App.Views;
using DigitalDeck.Core;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Data;
using Xunit;

namespace DigitalDeck.App.Tests;

/// <summary>
/// That an action chosen in the editor is still there after saving and reopening it.
/// </summary>
/// <remarks>
/// This exists because it went wrong. A chip lives in three places — the XAML, the branch that
/// reads a saved action back into it, and the branch that builds the action on save — and missing
/// the third compiles, shows the chip, accepts the click, and quietly saves nothing. The button
/// then does nothing at all and the editor reopens saying «Ninguna», with no error anywhere.
/// <para>
/// Driven over every action with no fields of its own, rather than over the one that broke: those
/// are exactly the ones where a missing branch is silent, because the others fail validation loudly
/// the moment something is left empty.
/// </para>
/// </remarks>
public sealed class ItemEditorRoundTripTests : IClassFixture<WpfThread>, IDisposable
{
    private readonly WpfThread _ui;
    private readonly string _root;
    private readonly IconStore _icons;

    public ItemEditorRoundTripTests(WpfThread ui)
    {
        _ui = ui;
        _root = Path.Combine(Path.GetTempPath(), "digitaldeck-editor-tests", Guid.NewGuid().ToString("n"));
        _icons = new IconStore(new DeckPaths(_root));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* A leftover temp folder is not worth failing a run over. */ }
    }

    public static TheoryData<string, Type> PanelActions => new()
    {
        { "MixerChoice", typeof(MixerPanelAction) },
    };

    [Theory]
    [MemberData(nameof(PanelActions))]
    public void AChosenActionSurvivesSavingAndReopening(string chip, Type expected)
    {
        var item = DeckItem.NewButton("página", null, "a", "Probando");

        var (saved, reopened) = _ui.Run(() =>
        {
            // Choose the chip and save, exactly as a click on it and on «Guardar» would.
            var editor = new ItemEditorWindow(item, _icons);

            try
            {
                Chip(editor, chip).IsChecked = true;
                editor.Save();
            }
            finally
            {
                editor.Close();
            }

            // Then open it again on the same item, which is what the user does to check.
            var second = new ItemEditorWindow(item, _icons);

            try
            {
                return (item.Action, Chip(second, chip).IsChecked == true);
            }
            finally
            {
                second.Close();
            }
        });

        Assert.NotNull(saved);
        Assert.IsType(expected, saved);
        Assert.True(reopened, $"«{chip}» se guardó pero volvió apagado al reabrir el editor.");
    }

    [Fact]
    public void SavingWithNoActionChosenReallyMeansNoAction()
    {
        // The other half of the same contract: «Ninguna» has to be a choice the user made, and not
        // what a chip nobody wired up looks like.
        var item = DeckItem.NewButton("página", null, "a", "Probando");

        var saved = _ui.Run(() =>
        {
            var editor = new ItemEditorWindow(item, _icons);

            try
            {
                Chip(editor, "NoneChoice").IsChecked = true;
                editor.Save();

                return item.Action;
            }
            finally
            {
                editor.Close();
            }
        });

        Assert.Null(saved);
    }

    /// <summary>
    /// The same round trip for the hotkey chip, which has fields and so cannot ride the theory
    /// above: saved empty it would fail validation, which is the loud failure, not the silent one.
    /// </summary>
    [Fact]
    public void AHotkeyButtonKeepsItsKeysAndItsProgram()
    {
        var item = DeckItem.NewButton("página", null, "a", "Exportar");

        var (saved, reopened) = _ui.Run(() =>
        {
            var editor = new ItemEditorWindow(item, _icons);

            try
            {
                Chip(editor, "HotkeyChoice").IsChecked = true;
                Box(editor, "HotkeyKeysBox").Text = "Ctrl+Shift+E";
                Box(editor, "HotkeyProgramBox").Text = "Photoshop";
                editor.Save();
            }
            finally
            {
                editor.Close();
            }

            var second = new ItemEditorWindow(item, _icons);

            try
            {
                return (item.Action, (
                    Chip(second, "HotkeyChoice").IsChecked == true,
                    Box(second, "HotkeyKeysBox").Text,
                    Box(second, "HotkeyProgramBox").Text));
            }
            finally
            {
                second.Close();
            }
        });

        var hotkey = Assert.IsType<HotkeyAction>(saved);
        Assert.Equal("Ctrl+Shift+E", hotkey.Keys);
        Assert.Equal("Photoshop", hotkey.Program);

        Assert.True(reopened.Item1, "«Atajo de teclado» se guardó pero volvió apagado al reabrir.");
        Assert.Equal("Ctrl+Shift+E", reopened.Item2);
        Assert.Equal("Photoshop", reopened.Item3);
    }

    [Fact]
    public void AHotkeyButtonWithNoProgramIsNotSaved()
    {
        var item = DeckItem.NewButton("página", null, "a", "Sin destino");

        var saved = _ui.Run(() =>
        {
            var editor = new ItemEditorWindow(item, _icons);

            try
            {
                Chip(editor, "HotkeyChoice").IsChecked = true;
                Box(editor, "HotkeyKeysBox").Text = "Ctrl+S";
                editor.Save();
            }
            finally
            {
                editor.Close();
            }

            return item.Action;
        });

        Assert.Null(saved);
    }

    private static RadioButton Chip(ItemEditorWindow editor, string name) =>
        (RadioButton)editor.FindName(name)!;

    private static TextBox Box(ItemEditorWindow editor, string name) =>
        (TextBox)editor.FindName(name)!;
}
