using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DigitalDeck.Core.Model;

namespace DigitalDeck.Avalonia;

/// <summary>
/// A page's name and how big its grid is.
/// </summary>
/// <remarks>
/// The grid belongs to the page rather than to the app, so a page with six buttons can be small
/// and show them big without shrinking every other page. The four presets are there because they
/// are what almost everybody picks; the free boxes are for the screen that is not shaped like any
/// of them.
/// <para>
/// Shrinking is allowed even when buttons would fall outside it. The deck pins those to the last
/// cell rather than losing them, and the person can see there are too many for the size — which is
/// a problem they can fix, unlike a dialog that refuses and does not say which button is in the way.
/// </para>
/// </remarks>
public partial class PageWindow : Window
{
    private readonly Page _page;

    /// <summary>True once Guardar went through. What <c>IDeckDialogs.EditPage</c> answers.</summary>
    public bool Saved { get; private set; }

    public PageWindow()
        : this(Page.New("perfil", "Página", "a"))
    {
    }

    public PageWindow(Page page)
    {
        _page = page;

        InitializeComponent();

        NameBox.Text = page.Name;
        ColumnsBox.Text = page.Columns.ToString(CultureInfo.InvariantCulture);
        RowsBox.Text = page.Rows.ToString(CultureInfo.InvariantCulture);
    }

    private void Preset_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string size }) return;

        var parts = size.Split(',');

        ColumnsBox.Text = parts[0];
        RowsBox.Text = parts[1];
    }

    private void Save_Click(object? sender, RoutedEventArgs e) => Save();

    /// <summary>Writes the form back onto the page. Public so a test can press Guardar.</summary>
    public bool Save()
    {
        var name = (NameBox.Text ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            Complain("Ponele un nombre a la página.");
            return false;
        }

        if (Number(ColumnsBox.Text) is not { } columns ||
            columns < Page.MinColumns || columns > Page.MaxColumns)
        {
            Complain($"Las columnas tienen que ser un número entre {Page.MinColumns} y {Page.MaxColumns}.");
            return false;
        }

        if (Number(RowsBox.Text) is not { } rows || rows < Page.MinRows || rows > Page.MaxRows)
        {
            Complain($"Las filas tienen que ser un número entre {Page.MinRows} y {Page.MaxRows}.");
            return false;
        }

        _page.Name = name;
        _page.Columns = columns;
        _page.Rows = rows;

        Saved = true;

        Close();

        return true;
    }

    private static int? Number(string? text) =>
        int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private void Complain(string message)
    {
        Problem.Text = message;
        Problem.IsVisible = true;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
