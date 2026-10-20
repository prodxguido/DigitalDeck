using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DigitalDeck.Core.Model;
using DigitalDeck.ViewModels;

namespace DigitalDeck.App.Views;

public partial class MainWindow
{
    public static readonly DependencyProperty DockRowsProperty = DependencyProperty.Register(
        nameof(DockRows), typeof(int), typeof(MainWindow), new PropertyMetadata(1));
    public int DockRows { get => (int)GetValue(DockRowsProperty); set => SetValue(DockRowsProperty, value); }
    private bool _dockBottom = true;
    private bool _overlayTaskbar;
    private bool _isDock;
    private MonitorRect? _panelBounds;
    private bool _panelWasTopmost;
    private TaskbarRestoreWindow? _taskbarRestore;
    private AbletonRecentProjectsWindow? _abletonRecentProjects;
    private TaskNotesWindow? _taskNotes;
    private UpdateWindow? _updateWindow;

    // Dock is a separate presentation of the same Items. It never rewrites the user's grid.
    internal void SetDockMode(bool enabled)
    {
        if (_isDock == enabled) return;
        if (enabled)
        {
            if (_isFullscreen) ToggleFullscreen();
            _viewModel.CloseSearchCommand.Execute(null);
            if (_viewModel.IsEditMode) _viewModel.ToggleEditModeCommand.Execute(null);
            _panelBounds = _placement.GetWindowBounds(this);
            _panelWasTopmost = Topmost;
            MinHeight = 40;
            MinWidth = 560;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            _placement.ApplyDock(this, _dockBottom, DockRows, _overlayTaskbar);
        }
        _isDock = enabled;
        StudioHeader.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        BreadcrumbBar.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        DeckContent.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        PagesBar.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        DockSurface.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        DockWindowsButton.Visibility = enabled && _dockBottom ? Visibility.Visible : Visibility.Collapsed;
        if (!enabled)
        {
            MinHeight = 360;
            MinWidth = 640;
            ResizeMode = ResizeMode.CanResize;
            Topmost = _panelWasTopmost;
            if (_panelBounds is { } bounds) _placement.ApplyBounds(this, bounds);
        }
    }

    private void DockMode_Click(object sender, RoutedEventArgs e) => SetDockMode(!_isDock);

    private void OpenProfileMenu_Click(object sender, RoutedEventArgs e) => OpenMenuFor((Button)sender);

    private void ViewMenu_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        if (button.ContextMenu is not { } menu) return;
        var activeView = _isDock ? (_dockBottom ? "bottom" : "top") : "panel";
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag is not string tag) continue;
            item.IsChecked = tag is "1" or "2"
                ? tag == DockRows.ToString()
                : tag is "above" or "overlay"
                    ? tag == (_overlayTaskbar ? "overlay" : "above")
                    : tag == activeView;
            if (tag is "1" or "2") item.IsEnabled = _isDock;
            if (tag is "above" or "overlay") item.IsEnabled = _isDock && _dockBottom;
        }
        OpenMenuFor(button);
    }

    private void SelectView_Click(object sender, RoutedEventArgs e)
    {
        var view = ((MenuItem)sender).Tag as string;
        if (view == "panel")
        {
            SetDockMode(false);
            return;
        }
        _dockBottom = view == "bottom";
        if (_isDock) _placement.ApplyDock(this, _dockBottom, DockRows, _overlayTaskbar);
        else SetDockMode(true);
    }

    private void ViewRows_Click(object sender, RoutedEventArgs e) => DockRows_Click(sender, e);

    private void DockOptions_Click(object sender, RoutedEventArgs e)
    {
        DockTopOption.IsChecked = !_dockBottom;
        DockBottomOption.IsChecked = _dockBottom;
        DockSingleOption.IsChecked = DockRows == 1;
        DockDoubleOption.IsChecked = DockRows == 2;
        DockAboveTaskbarOption.IsChecked = !_overlayTaskbar;
        DockOverlayOption.IsChecked = _overlayTaskbar;
        OpenMenuFor((Button)sender);
    }

    private void DockPosition_Click(object sender, RoutedEventArgs e)
    {
        _dockBottom = ((MenuItem)sender).Tag as string == "bottom";
        _placement.ApplyDock(this, _dockBottom, DockRows, _overlayTaskbar);
        DockWindowsButton.Visibility = _dockBottom ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DockRows_Click(object sender, RoutedEventArgs e)
    {
        DockRows = ((MenuItem)sender).Tag as string == "2" ? 2 : 1;
        _placement.ApplyDock(this, _dockBottom, DockRows, _overlayTaskbar);
    }

    private void DockTaskbarMode_Click(object sender, RoutedEventArgs e)
    {
        _overlayTaskbar = ((MenuItem)sender).Tag as string == "overlay";
        if (_isDock) _placement.ApplyDock(this, _dockBottom, DockRows, _overlayTaskbar);
    }

    private void DockWindows_Click(object sender, RoutedEventArgs e)
    {
        if (!_isDock || !_dockBottom) return;

        _taskbarRestore?.Close();
        _taskbarRestore = new TaskbarRestoreWindow(RestoreDockFromTaskbar);
        _taskbarRestore.Show();
        Topmost = false;
        WindowState = WindowState.Minimized;
    }

    private void DockAbletonRecent_Click(object sender, RoutedEventArgs e)
    {
        _abletonRecentProjects?.Close();
        _abletonRecentProjects = new AbletonRecentProjectsWindow(this);
        _abletonRecentProjects.Closed += (_, _) => _abletonRecentProjects = null;
        _abletonRecentProjects.Show();
    }

    private void DockTasks_Click(object sender, RoutedEventArgs e)
    {
        _taskNotes?.Close();
        _taskNotes = new TaskNotesWindow(this);
        _taskNotes.Closed += (_, _) => _taskNotes = null;
        _taskNotes.Show();
    }

    private void DockUpdate_Click(object sender, RoutedEventArgs e)
    {
        _updateWindow?.Close();
        _updateWindow = new UpdateWindow(this);
        _updateWindow.Closed += (_, _) => _updateWindow = null;
        _updateWindow.Show();
    }

    private void RestoreDockFromTaskbar()
    {
        _taskbarRestore?.Close();
        _taskbarRestore = null;
        WindowState = WindowState.Normal;
        Topmost = true;
        _placement.ApplyDock(this, _dockBottom, DockRows, _overlayTaskbar);
        Activate();
    }

    private void CloseTaskbarRestore()
    {
        _taskbarRestore?.Close();
        _taskbarRestore = null;
    }

    private void DockEdit_Click(object sender, RoutedEventArgs e)
    {
        SetDockMode(false);
        if (!_viewModel.IsEditMode) _viewModel.ToggleEditModeCommand.Execute(null);
    }

    private void DockPrevious_Click(object sender, RoutedEventArgs e) =>
        DockScroll.ScrollToHorizontalOffset(DockScroll.HorizontalOffset - 264);

    private void DockNext_Click(object sender, RoutedEventArgs e) =>
        DockScroll.ScrollToHorizontalOffset(DockScroll.HorizontalOffset + 264);

    private void DockTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DeckItemViewModel tile })
            _viewModel.ActivateCommand.Execute(tile);
    }

    private void DockAdd_Click(object sender, RoutedEventArgs e)
    {
        SetDockMode(false);
        _viewModel.AddButtonCommand.Execute(null);
    }

    private void DockScroll_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        DockScroll.ScrollToHorizontalOffset(DockScroll.HorizontalOffset - e.Delta);
        e.Handled = true;
    }
}
