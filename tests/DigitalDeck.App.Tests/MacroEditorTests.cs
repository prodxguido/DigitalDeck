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
/// The «varias acciones» chip in the editor: that a saved routine comes back whole.
/// </summary>
/// <remarks>
/// The same trap <see cref="ItemEditorRoundTripTests"/> exists for, one level deeper: a routine is
/// read back into a list, redrawn from that list and rebuilt from it on save, so a step lost
/// anywhere along the way is silent — the button keeps working, with one action missing.
/// </remarks>
public sealed class MacroEditorTests : IClassFixture<WpfThread>, IDisposable
{
    private readonly WpfThread _ui;
    private readonly string _root;
    private readonly IconStore _icons;

    public MacroEditorTests(WpfThread ui)
    {
        _ui = ui;
        _root = Path.Combine(Path.GetTempPath(), "digitaldeck-macro-tests", Guid.NewGuid().ToString("n"));
        _icons = new IconStore(new DeckPaths(_root));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* A leftover temp folder is not worth failing a run over. */ }
    }

    private static MacroAction Morning() => new()
    {
        Acknowledged = true,
        ContinueOnError = false,
        Steps =
        {
            new MacroStep { Action = new OpenUrlAction { Url = "https://youtube.com/playlist?list=x" } },
            new MacroStep { Action = new OpenUrlAction { Url = "https://lanacion.com.ar" }, DelayMs = 2000 },
            new MacroStep { Action = AbletonAction.For(AbletonCommand.Play) }
        }
    };

    [Fact]
    public void ARoutineSurvivesOpeningAndSavingTheEditor()
    {
        var item = DeckItem.NewButton("página", null, "a", "Arrancá la mañana");
        item.Action = Morning();

        var saved = _ui.Run(() =>
        {
            var editor = new ItemEditorWindow(item, _icons);

            try
            {
                // The chip has to come back on by itself: that is the branch that reads a saved
                // routine, and it is the one that goes missing.
                Assert.True(((RadioButton)editor.FindName("MacroChoice")!).IsChecked);
                Assert.False(((CheckBox)editor.FindName("MacroContinueCheck")!).IsChecked);

                editor.Save();
                return item.Action;
            }
            finally
            {
                editor.Close();
            }
        });

        var macro = Assert.IsType<MacroAction>(saved);

        Assert.Equal(3, macro.Steps.Count);
        Assert.Equal("https://youtube.com/playlist?list=x", Assert.IsType<OpenUrlAction>(macro.Steps[0].Action).Url);
        Assert.Equal(2000, macro.Steps[1].DelayMs);
        Assert.Equal(AbletonCommand.Play, Assert.IsType<AbletonAction>(macro.Steps[2].Action).Command);
        Assert.False(macro.ContinueOnError);
        Assert.Null(macro.Validate());
    }

    /// <summary>
    /// The step editor is this same window with its top half hidden. If the identity fields were
    /// still there, a step would be asking for a name, a colour and an icon it has nowhere to put.
    /// </summary>
    [Fact]
    public void TheStepEditorShowsOnlyTheActionHalf()
    {
        var item = DeckItem.NewButton("página", null, "a", "paso");

        _ui.Run(() =>
        {
            var editor = new ItemEditorWindow(item, _icons, ableton: null, takenNames: null, actionOnly: true);

            try
            {
                Assert.Equal(System.Windows.Visibility.Collapsed,
                    ((Panel)editor.FindName("IdentitySection")!).Visibility);

                // And no chip for a routine, because a routine inside a routine is a cycle.
                Assert.Equal(System.Windows.Visibility.Collapsed,
                    ((RadioButton)editor.FindName("MacroChoice")!).Visibility);

                // A step has no name of its own, so the name check must not block saving one.
                ((RadioButton)editor.FindName("UrlChoice")!).IsChecked = true;
                ((TextBox)editor.FindName("UrlBox")!).Text = "spotify.com";

                Assert.True(editor.Save());
            }
            finally
            {
                editor.Close();
            }

            return 0;
        });

        Assert.Equal("https://spotify.com", Assert.IsType<OpenUrlAction>(item.Action).Url);
    }
}
