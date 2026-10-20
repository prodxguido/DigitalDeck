using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using DigitalDeck.App.Services;

namespace DigitalDeck.App.Views;

public partial class UpdateWindow : Window
{
    private string? _url;
    public UpdateWindow(Window owner)
    {
        Owner = owner;
        InitializeComponent();
        Loaded += CheckAsync;
    }
    private async void CheckAsync(object sender, RoutedEventArgs e)
    {
        Loaded -= CheckAsync;
        try
        {
            var result = await UpdateService.CheckAsync();
            StatusText.Text = result.Message;
            _url = result.DownloadUrl;
            OpenButton.Visibility = _url is null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex) { StatusText.Text = "No se pudo consultar GitHub: " + ex.Message; }
        finally { Progress.IsIndeterminate = false; Progress.Visibility = Visibility.Collapsed; }
    }
    private void Open_Click(object sender, RoutedEventArgs e) { if (_url is not null) Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true }); }
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
