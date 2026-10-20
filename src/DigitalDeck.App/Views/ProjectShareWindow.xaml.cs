using System.Windows;
using System.Windows.Input;
using DigitalDeck.App.Services;

namespace DigitalDeck.App.Views;

public partial class ProjectShareWindow : Window
{
    private readonly AbletonRecentProject _project;
    private string? _link;

    public ProjectShareWindow(Window owner, AbletonRecentProject project)
    {
        Owner = owner;
        _project = project;
        InitializeComponent();
        ProjectName.Text = project.Name;
        Loaded += StartShare;
    }

    private async void StartShare(object sender, RoutedEventArgs e)
    {
        Loaded -= StartShare;
        var folder = ProjectShareService.ProjectFolder(_project);
        if (!ProjectShareService.HasRecentAudioActivity(folder, TimeSpan.FromMinutes(10)))
        {
            var answer = MessageBox.Show(this,
                "No detecté audios modificados o recopilados en los últimos 10 minutos. Verificá que Ableton haya hecho Collect All and Save antes de continuar.\n\n¿Querés comprimir y subir de todos modos?",
                "Revisar audios recopilados", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) { Close(); return; }
        }
        try
        {
            var progress = new Progress<ShareProgress>(UpdateProgress);
            _link = await ProjectShareService.CompressAndUploadAsync(folder, progress);
            LinkBox.Text = _link;
            LinkBox.Visibility = Visibility.Visible;
            CopyButton.Visibility = Visibility.Visible;
            Progress.IsIndeterminate = false;
            Progress.Value = 100;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            StatusText.Foreground = System.Windows.Media.Brushes.IndianRed;
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateProgress(ShareProgress progress)
    {
        StatusText.Text = progress.Eta is null ? progress.Stage : $"{progress.Stage}\n{progress.Eta}";
        if (progress.Fraction is { } fraction)
        {
            Progress.IsIndeterminate = false;
            Progress.Value = fraction * 100;
        }
    }
    private void Copy_Click(object sender, RoutedEventArgs e) { if (_link is not null) Clipboard.SetText(_link); }
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
