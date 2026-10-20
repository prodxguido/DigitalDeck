using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using System.IO;
using DigitalDeck.App.Services;

namespace DigitalDeck.App.Views;

public partial class AbletonRecentProjectsWindow : Window
{
    private IReadOnlyList<AbletonRecentProject> _all = Array.Empty<AbletonRecentProject>();

    public AbletonRecentProjectsWindow(Window owner)
    {
        Owner = owner;
        InitializeComponent();
        CountText.Text = "Cargando proyectos…";
        Loaded += LoadInitialProjects;
    }

    private async void LoadInitialProjects(object sender, RoutedEventArgs e)
    {
        Loaded -= LoadInitialProjects;
        await Task.Yield();
        var known = await Task.Run(AbletonRecentProjects.ReadKnown);
        _all = known;
        ApplyFilter();
        if (AbletonRecentProjects.CatalogNeedsRefresh) _ = RefreshCatalogInBackgroundAsync();
    }

    private int Limit => (LimitBox.SelectedItem as ComboBoxItem)?.Tag is string value && int.TryParse(value, out var count) ? count : 25;

    private void Refresh()
    {
        // Replace the list instead of mutating the list currently used by ItemsSource.
        // WPF's generator can otherwise observe a collection changing underneath it when
        // the user switches from 25 to 50 or 100 results.
        _all = AbletonRecentProjects.ReadKnown();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var visible = string.IsNullOrWhiteSpace(query)
            ? (Limit == 0 ? _all : _all.Take(Limit).ToList())
            : _all.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || p.Path.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        if (StartDatePicker.SelectedDate is { } start)
            visible = visible.Where(p => p.LastOpened.Date >= start.Date).ToList();
        if (EndDatePicker.SelectedDate is { } end)
            visible = visible.Where(p => p.LastOpened.Date <= end.Date).ToList();
        ProjectsList.ItemsSource = visible;
        EmptyText.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = visible.Count == 0 ? string.Empty : $"{visible.Count} proyecto(s) · clic para abrir";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void Calendar_Click(object sender, RoutedEventArgs e)
    {
        DateFilterPanel.Visibility = DateFilterPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void ClearDates_Click(object sender, RoutedEventArgs e)
    {
        StartDatePicker.SelectedDate = null;
        EndDatePicker.SelectedDate = null;
        ApplyFilter();
    }
    private void LimitBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsInitialized) Refresh();
    }

    private async Task RefreshCatalogInBackgroundAsync()
    {
        try
        {
            await AbletonRecentProjects.RefreshCatalogAsync();
            await Dispatcher.InvokeAsync(Refresh);
        }
        catch { }
    }

    private async void RefreshCatalog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CountText.Text = "Analizando las unidades…";
            await AbletonRecentProjects.RefreshCatalogAsync();
            Refresh();
            CountText.Text = $"Catálogo actualizado · {_all.Count} proyecto(s) conocidos";
        }
        catch (Exception ex)
        {
            CountText.Text = $"No se pudo actualizar: {ex.Message}";
        }
    }

    private void ProjectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not AbletonRecentProject project) return;
        try { AbletonRecentProjects.Open(project); Close(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se pudo abrir el proyecto", MessageBoxButton.OK, MessageBoxImage.Error); }
        ProjectsList.SelectedItem = null;
    }

    private void OpenProjectFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not AbletonRecentProject project) return;
        var folder = ProjectShareService.ProjectFolder(project);
        if (Directory.Exists(folder)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private void ShareProject_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not AbletonRecentProject project) return;
        new ProjectShareWindow(this, project).ShowDialog();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
