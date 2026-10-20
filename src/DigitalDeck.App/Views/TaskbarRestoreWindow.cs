using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace DigitalDeck.App.Views;

/// <summary>Small always-on-top handle left above the Windows taskbar while the dock is hidden.</summary>
internal sealed class TaskbarRestoreWindow : Window
{
    private const int SwShow = 5;

    private readonly Action _restore;

    public TaskbarRestoreWindow(Action restore)
    {
        _restore = restore;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Width = 44;
        Height = 38;
        var windowsLogo = new Grid { Width = 16, Height = 16 };
        windowsLogo.ColumnDefinitions.Add(new ColumnDefinition());
        windowsLogo.ColumnDefinitions.Add(new ColumnDefinition());
        windowsLogo.RowDefinitions.Add(new RowDefinition());
        windowsLogo.RowDefinitions.Add(new RowDefinition());
        AddLogoTile(windowsLogo, 0, 0, new Thickness(0, 0, 2, 2));
        AddLogoTile(windowsLogo, 1, 0, new Thickness(2, 0, 0, 2));
        AddLogoTile(windowsLogo, 0, 1, new Thickness(0, 2, 2, 0));
        AddLogoTile(windowsLogo, 1, 1, new Thickness(2, 2, 0, 0));

        Content = new Button
        {
            Content = windowsLogo,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(30, 39, 56)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(82, 211, 193)),
            BorderThickness = new Thickness(1),
            ToolTip = "Mostrar DigitalDeck"
        };

        ((Button)Content).Click += (_, _) => _restore();
        SourceInitialized += (_, _) => PlaceAboveTaskbar();
    }

    private static void AddLogoTile(Grid logo, int column, int row, Thickness margin)
    {
        var tile = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(1),
            Margin = margin
        };
        Grid.SetColumn(tile, column);
        Grid.SetRow(tile, row);
        logo.Children.Add(tile);
    }

    private void PlaceAboveTaskbar()
    {
        var taskbar = FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero) return;
        ShowWindow(taskbar, SwShow);
        if (!GetWindowRect(taskbar, out var rect)) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        Left = (rect.Left + 8) / dpi.DpiScaleX;
        Top = Math.Max(0, rect.Top / dpi.DpiScaleY - Height);
    }

    public void Reposition() => PlaceAboveTaskbar();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr handle, int command);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out RectNative rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
