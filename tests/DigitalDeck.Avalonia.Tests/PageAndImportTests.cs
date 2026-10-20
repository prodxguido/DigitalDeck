using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using DigitalDeck.Core.Model;
using DigitalDeck.Data.Packs;
using Xunit;

namespace DigitalDeck.Avalonia.Tests;

/// <summary>
/// Configurar una página, y decidir dónde entra un paquete importado.
/// </summary>
public class PageAndImportTests
{
    private static Page NewPage(string name = "Inicio", int columns = 6, int rows = 4) =>
        Page.New("perfil", name, "a", columns, rows);

    // ---------- La página ----------

    [AvaloniaFact]
    public void ThePresetsWriteThemselvesIntoTheBoxes()
    {
        var page = NewPage();
        var window = new PageWindow(page);

        try
        {
            // Mostrada primero: los botones existen en el árbol visual recién después del layout.
            window.Show();
            window.UpdateLayout();

            var preset = window.GetVisualDescendants()
                .OfType<global::Avalonia.Controls.Button>()
                .First(b => (b.Tag as string) == "8,5");

            preset.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(
                global::Avalonia.Controls.Button.ClickEvent));

            Assert.Equal("8", window.ColumnsBox.Text);
            Assert.Equal("5", window.RowsBox.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ANameAndASizeAreWrittenBackOntoThePage()
    {
        var page = NewPage();
        var window = new PageWindow(page);

        try
        {
            window.NameBox.Text = "Mezcla";
            window.ColumnsBox.Text = "10";
            window.RowsBox.Text = "6";

            Assert.True(window.Save());

            Assert.Equal("Mezcla", page.Name);
            Assert.Equal(10, page.Columns);
            Assert.Equal(6, page.Rows);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ASizeLiveWouldNotAcceptIsRefusedWithTheRangeSaid()
    {
        var page = NewPage();
        var window = new PageWindow(page);

        try
        {
            window.ColumnsBox.Text = "40";

            Assert.False(window.Save());
            Assert.True(window.Problem.IsVisible);
            Assert.Contains(Page.MaxColumns.ToString(), window.Problem.Text);

            // Y la página quedó como estaba.
            Assert.Equal(6, page.Columns);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void APageWithNoNameIsRefused()
    {
        var page = NewPage("Inicio");
        var window = new PageWindow(page);

        try
        {
            window.NameBox.Text = "  ";

            Assert.False(window.Save());
            Assert.Equal("Inicio", page.Name);
        }
        finally
        {
            window.Close();
        }
    }

    // ---------- El destino de un paquete ----------

    [AvaloniaFact]
    public void ClosingWithoutChoosingIsNotAnImport()
    {
        // La app lee null como «no importar». Cerrar la ventana no puede terminar importando.
        var window = new ImportTargetWindow(3);

        try
        {
            Assert.Null(window.Target);
            Assert.Contains("3", window.Message.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void APageEachIsWhatItOffersFirst()
    {
        var window = new ImportTargetWindow(1);

        try
        {
            Assert.True(window.AsPages.IsChecked);
            Assert.Equal(PackImportTarget.NewPage, window.Import());
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void AskingForAGroupInsteadIsRespected()
    {
        var window = new ImportTargetWindow(1);

        try
        {
            window.AsGroups.IsChecked = true;

            Assert.Equal(PackImportTarget.CurrentPage, window.Import());
        }
        finally
        {
            window.Close();
        }
    }
}
