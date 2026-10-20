using System.Windows;
using System.Windows.Input;
using DigitalDeck.Core.Model;

namespace DigitalDeck.App.Views;

public partial class PageEditorWindow : Window
{
    private readonly Page _page;

    public PageEditorWindow(Page page)
    {
        InitializeComponent();

        _page = page;
        NameBox.Text = page.Name;
        ColumnsBox.Text = page.Columns.ToString();
        RowsBox.Text = page.Rows.ToString();

        Loaded += (_, _) => NameBox.Focus();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();

        if (name.Length == 0)
        {
            Warn("La página necesita un nombre.");
            return;
        }

        if (!int.TryParse(ColumnsBox.Text, out var columns) || columns < Page.MinColumns || columns > Page.MaxColumns)
        {
            Warn($"Las columnas tienen que estar entre {Page.MinColumns} y {Page.MaxColumns}.");
            return;
        }

        if (!int.TryParse(RowsBox.Text, out var rows) || rows < Page.MinRows || rows > Page.MaxRows)
        {
            Warn($"Las filas tienen que estar entre {Page.MinRows} y {Page.MaxRows}.");
            return;
        }

        // Shrinking a page would strand any tile that sits outside the new bounds, which would
        // leave items in the database with nowhere to appear.
        if (columns < _page.Columns || rows < _page.Rows)
        {
            var message = $"Reducir la grilla puede dejar botones fuera de la vista. " +
                          $"Movelos antes de achicar de {_page.Columns}×{_page.Rows} a {columns}×{rows}.";

            if (MessageBox.Show(this, message, "Reducir la grilla",
                    MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                return;
        }

        _page.Name = name;
        _page.Columns = columns;
        _page.Rows = rows;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Warn(string message)
    {
        WarningText.Text = message;
        WarningText.Visibility = Visibility.Visible;
    }
}
