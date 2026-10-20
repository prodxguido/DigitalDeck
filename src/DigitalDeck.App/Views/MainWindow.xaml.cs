using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DigitalDeck.App.Controls;
using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;

namespace DigitalDeck.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly WindowPlacementService _placement;
    private readonly VoiceService _voice;
    private readonly IWindowEffects _effects;
    private readonly IAppSettingsStore? _modeSettings;

    private DeckGridPanel? _panel;

    private Point _pressOrigin;
    private DeckItemViewModel? _pressedItem;
    private ContentPresenter? _pressedContainer;
    private bool _isDragging;
    private DropPreview _dropPreview;

    private (int Col, int Row)? _menuCell;
    private bool _isFullscreen;
    private MonitorRect? _windowedBounds;
    private bool _isClosing;

    public MainWindow(
        MainViewModel viewModel,
        WindowPlacementService placement,
        VoiceService voice,
        IWindowEffects effects,
        IAppSettingsStore? modeSettings = null)
    {
        _viewModel = viewModel;
        _placement = placement;
        _voice = voice;
        _effects = effects;
        _modeSettings = modeSettings;

        // The assistant has nowhere to put its messages of its own, so they go where every
        // other outcome in the app goes.
        _voice.Reported += viewModel.SetStatus;

        InitializeComponent();

        DataContext = viewModel;

        Loaded += OnLoaded;
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _effects.RoundCorners(new WindowInteropHelper(this).Handle);
            _isFullscreen = await _placement.RestoreAsync(this);
            await _viewModel.InitializeAsync();
            if (_modeSettings is not null)
            {
                _dockBottom = await _modeSettings.GetAsync("bar_edge") != "top";
                DockRows = await _modeSettings.GetAsync("bar_rows") == "2" ? 2 : 1;
                _overlayTaskbar = await _modeSettings.GetAsync("bar_taskbar") == "overlay";
                if (await _modeSettings.GetAsync("presentation_mode") == "bar")
                    SetDockMode(true);
            }

            // After the window is up, because claiming a global hotkey needs a real handle to
            // deliver the message to. A failure here disables the assistant and says so; it is
            // never a reason to fail opening the deck.
            await _voice.AttachAsync(this, _viewModel);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "No se pudo abrir el deck", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Closing is synchronous but saving the placement is not, so the first close is cancelled,
    /// the save awaited, and the window closed again once it is safely on disk.
    /// </summary>
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_isClosing) return;

        e.Cancel = true;
        _isClosing = true;

        try
        {
            CloseTaskbarRestore();
            if (_modeSettings is not null)
            {
                await _modeSettings.SetAsync("presentation_mode", _isDock ? "bar" : "panel");
                await _modeSettings.SetAsync("bar_edge", _dockBottom ? "bottom" : "top");
                await _modeSettings.SetAsync("bar_rows", DockRows.ToString());
                await _modeSettings.SetAsync("bar_taskbar", _overlayTaskbar ? "overlay" : "above");
            }
            if (_isDock) SetDockMode(false);
            await _placement.SaveAsync(this, _isFullscreen, _windowedBounds);
        }
        catch (Exception)
        {
            // Losing the remembered position is not a reason to block the app from closing.
        }

        // Posted rather than called here. If the save finishes synchronously — which it does when
        // nothing had to go to disk — this continuation runs inside the close that was just
        // cancelled, and WPF refuses to close a window that is already closing. Waiting for the
        // first one to unwind is the difference between shutting down and an unhandled exception.
        Dispatcher.BeginInvoke(new Action(Close));
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_isDock && (e.Key == Key.F11 || e.Key == Key.Escape ||
            (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.F or Key.E)))
        {
            SetDockMode(false);
            if (e.Key == Key.Escape) { e.Handled = true; return; }
        }
        switch (e.Key)
        {
            case Key.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;

            // Closing the search comes before everything else Escape does: it is the thing that is
            // in the way, and it is on screen saying so.
            case Key.Escape when _viewModel.IsSearchOpen:
                _viewModel.CloseSearchCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F when Keyboard.Modifiers == ModifierKeys.Control:
                _viewModel.OpenSearchCommand.Execute(null);
                e.Handled = true;
                break;

            // Cancelling the ticking comes first: leaving fullscreen with a batch armed would leave
            // clicks still meaning "tick" with the button that says so scrolled out of mind.
            case Key.Escape when _viewModel.IsMultiSelectMode:
                _viewModel.CancelMultiSelectCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape when _isFullscreen:
                ToggleFullscreen();
                e.Handled = true;
                break;

            case Key.Z when Keyboard.Modifiers == ModifierKeys.Control && _viewModel.UndoCommand.CanExecute(null):
                _viewModel.UndoCommand.Execute(null);
                e.Handled = true;
                break;

            // Y for the usual Windows habit, Ctrl+Shift+Z for the one carried over from everywhere
            // else — both do the same thing, so nobody has to remember which this app picked.
            case Key.Y when Keyboard.Modifiers == ModifierKeys.Control:
            case Key.Z when Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                if (_viewModel.RedoCommand.CanExecute(null)) _viewModel.RedoCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.E when Keyboard.Modifiers == ModifierKeys.Control:
                _viewModel.ToggleEditModeCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Back when _viewModel.CanGoBack:
                _viewModel.GoBackCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // ---------- Search ----------

    /// <summary>
    /// Puts the caret in the box the moment the panel appears, so Ctrl+F is followed by typing.
    /// </summary>
    private void SearchBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) SearchBox.Focus();
    }

    /// <summary>
    /// Enter takes the best match.
    /// </summary>
    /// <remarks>
    /// The list is ordered so the top row is the one meant, which makes typing a few letters and
    /// pressing Enter the whole interaction — no reaching for the mouse to confirm what is already
    /// first.
    /// </remarks>
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        if (_viewModel.SearchResults.FirstOrDefault() is { } best)
            _viewModel.OpenSearchResultCommand.Execute(best);

        e.Handled = true;
    }

    // ---------- Window chrome ----------

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Bubbling clicks from controls must not turn into a window drag.
        if (e.OriginalSource is DependencyObject origin)
        {
            for (var current = origin; current is not null; current = VisualTreeHelper.GetParent(current))
                if (current is System.Windows.Controls.Primitives.ButtonBase || current is ComboBox)
                    return;
        }
        if (e.ClickCount == 2)
        {
            if (_isDock) { SetDockMode(false); return; }
            ToggleFullscreen();
            return;
        }

        if (!_isFullscreen) DragMove();
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void Minimise_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            ResizeMode = ResizeMode.CanResize;
            if (_windowedBounds is { } bounds) _placement.ApplyBounds(this, bounds);
            _isFullscreen = false;
            return;
        }

        _windowedBounds = _placement.GetWindowBounds(this);
        _placement.ApplyFullscreen(this);
        ResizeMode = ResizeMode.NoResize;
        _isFullscreen = true;
    }

    private void ProfileButton_Click(object sender, RoutedEventArgs e) => OpenMenuFor(ProfileButton);

    private void MenuButton_Click(object sender, RoutedEventArgs e) => OpenMenuFor(MenuButton);

    private void RouteProfile_Click(object sender, RoutedEventArgs e) => OpenMenuFor((Button)sender);

    private static void OpenMenuFor(Button button)
    {
        if (button.ContextMenu is not { } menu) return;

        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // ---------- Tile interaction ----------

    private void TileHost_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressOrigin = e.GetPosition(TileHost);
        _pressedContainer = FindContainer(e.OriginalSource as DependencyObject);
        _pressedItem = _pressedContainer?.DataContext as DeckItemViewModel;
        _dropPreview = default;

        // The tile goes down under the finger and stays down until the button comes up. On a
        // hardware deck that feedback is the travel of the key itself; here it has to be drawn.
        Press(_pressedContainer, 0.955);
    }

    private void TileHost_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressedItem is null || !_viewModel.IsEditMode) return;

        // While ticking, the press belongs to the tick — rearranging the grid mid-selection would
        // only make it harder to see what is marked.
        if (_viewModel.IsMultiSelectMode) return;

        var current = e.GetPosition(TileHost);

        if (!_isDragging)
        {
            var movedFarEnough =
                Math.Abs(current.X - _pressOrigin.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(current.Y - _pressOrigin.Y) >= SystemParameters.MinimumVerticalDragDistance;

            if (!movedFarEnough) return;

            _isDragging = true;
            TileHost.CaptureMouse();

            // The tile fades rather than following the cursor: the drop indicator already shows
            // exactly where it lands, and a ghost would just cover the cell being aimed at.
            if (_pressedContainer is not null) _pressedContainer.Opacity = 0.35;
        }

        UpdateDropPreview(e);
    }

    private async void TileHost_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var panel = GetPanel();

        // Held on to across the await: ResetDragState clears the field before the animation that
        // says the button fired would otherwise have anything left to play on.
        var released = _pressedContainer;

        try
        {
            if (_isDragging)
            {
                TileHost.ReleaseMouseCapture();

                if (_pressedContainer is not null) _pressedContainer.Opacity = 1;
                if (panel is not null) panel.DropIndicator = null;

                Press(released, 1);

                if (_dropPreview.IsAllowed && _pressedItem is not null)
                    await _viewModel.ApplyDropAsync(_pressedItem, _dropPreview);
            }
            else if (_pressedItem is not null)
            {
                if (_viewModel.IsMultiSelectMode)
                {
                    Press(released, 1);
                    _viewModel.ToggleSelectionCommand.Execute(_pressedItem);
                }
                else if (_viewModel.IsEditMode)
                {
                    Press(released, 1);
                    _viewModel.EditItemCommand.Execute(_pressedItem);
                }
                else
                {
                    // Firing is the one outcome with nothing on screen to show for it — the thing
                    // that happens, happens inside Live. The tile springing back past its resting
                    // size is the whole acknowledgement the user gets.
                    Fire(released);
                    _viewModel.ActivateCommand.Execute(_pressedItem);
                }
            }
            else if (_viewModel.IsEditMode && !_viewModel.IsMultiSelectMode && panel is not null)
            {
                // Clicking an empty cell while editing puts a new button exactly there.
                if (panel.CellFromPoint(e.GetPosition(panel)) is { } cell)
                    _viewModel.AddButtonCommand.Execute($"{cell.Col},{cell.Row}");
            }
        }
        catch (Exception ex)
        {
            _viewModel.SetStatus(ex.Message, isError: true);
        }
        finally
        {
            ResetDragState();
        }
    }

    private void TileHost_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Ticking owns the grid until it is confirmed or cancelled; a per-tile menu offering its own
        // Eliminar on top of that is two answers to the same question.
        if (_viewModel.IsMultiSelectMode)
        {
            e.Handled = true;
            return;
        }

        var panel = GetPanel();
        _menuCell = panel is null ? null : panel.CellFromPoint(e.GetPosition(panel));

        var container = FindContainer(e.OriginalSource as DependencyObject);

        if (container?.DataContext is DeckItemViewModel item)
        {
            _viewModel.SelectedItem = item;
            ShowMenu("TileMenu", menu =>
            {
                foreach (var entry in menu.Items.OfType<MenuItem>())
                    if (entry.Tag as string == "export-folder")
                        entry.IsEnabled = item.IsFolder;
            });
            e.Handled = true;
            return;
        }

        if (!_viewModel.IsEditMode) return;

        ShowMenu("EmptyCellMenu");
        e.Handled = true;
    }

    private void UpdateDropPreview(MouseEventArgs e)
    {
        var panel = GetPanel();
        if (panel is null || _pressedItem is null) return;

        if (panel.CellFromPoint(e.GetPosition(panel)) is not { } cell)
        {
            _dropPreview = default;
            panel.DropIndicator = null;
            return;
        }

        _dropPreview = _viewModel.EvaluateDrop(_pressedItem, cell.Col, cell.Row);

        var footprint = _dropPreview.Kind == DropKind.IntoFolder && _dropPreview.Target is { } folder
            ? (folder.Width, folder.Height)
            : (_pressedItem.Width, _pressedItem.Height);

        panel.DropIndicator = new Int32Rect(_dropPreview.Col, _dropPreview.Row, footprint.Item1, footprint.Item2);
        panel.IsDropAllowed = _dropPreview.IsAllowed;
    }

    // ---------- Tile feedback ----------
    //
    // Scaling the item container rather than anything inside the template, for two reasons: the
    // template's own hover lift owns the transform on its root Grid and the two would overwrite
    // each other, and a transform on the container composes with that lift instead of fighting
    // it. None of this touches layout, so the grid never reflows because a key was pressed.

    private static ScaleTransform ScaleOf(UIElement element)
    {
        if (element.RenderTransform is ScaleTransform existing) return existing;

        element.RenderTransformOrigin = new Point(0.5, 0.5);
        var scale = new ScaleTransform(1, 1);
        element.RenderTransform = scale;
        return scale;
    }

    private static void Press(UIElement? container, double to)
    {
        if (container is null) return;

        var scale = ScaleOf(container);
        var dip = new DoubleAnimation(to, TimeSpan.FromMilliseconds(70))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, dip);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, dip);
    }

    private static void Fire(UIElement? container)
    {
        if (container is null) return;

        var scale = ScaleOf(container);

        // Overshooting on the way back is what makes this read as a key being released rather
        // than as a tile fading between two sizes. BackEase is the cheapest way to say "spring".
        var pulse = new DoubleAnimationUsingKeyFrames();
        pulse.KeyFrames.Add(new LinearDoubleKeyFrame(0.94, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(50))));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(
            1,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280)),
            new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 }));

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
    }

    private void ResetDragState()
    {
        _isDragging = false;
        _pressedItem = null;
        _pressedContainer = null;
        _dropPreview = default;
    }

    // ---------- Context menu commands ----------

    private void MenuEdit_Click(object sender, RoutedEventArgs e) =>
        _viewModel.EditItemCommand.Execute(_viewModel.SelectedItem);

    private void MenuDuplicate_Click(object sender, RoutedEventArgs e) =>
        _viewModel.DuplicateItemCommand.Execute(_viewModel.SelectedItem);

    private void MenuDelete_Click(object sender, RoutedEventArgs e) =>
        _viewModel.DeleteItemCommand.Execute(_viewModel.SelectedItem);

    private void MenuExportFolder_Click(object sender, RoutedEventArgs e) =>
        _viewModel.ExportFolderCommand.Execute(_viewModel.SelectedItem);

    private void MenuResize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string size })
            _viewModel.ResizeItemCommand.Execute(size);
    }

    private void MenuAddButton_Click(object sender, RoutedEventArgs e) =>
        _viewModel.AddButtonCommand.Execute(CellParameter());

    private void MenuAddFolder_Click(object sender, RoutedEventArgs e) =>
        _viewModel.AddFolderCommand.Execute(CellParameter());

    private void MenuAddFrequentFolder_Click(object sender, RoutedEventArgs e) =>
        _viewModel.AddFrequentFolderCommand.Execute(CellParameter());

    private string? CellParameter() => _menuCell is { } cell ? $"{cell.Col},{cell.Row}" : null;

    private void ShowMenu(string resourceKey, Action<ContextMenu>? configure = null)
    {
        if (Resources[resourceKey] is not ContextMenu menu) return;

        configure?.Invoke(menu);

        menu.DataContext = _viewModel;
        menu.PlacementTarget = TileHost;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    // ---------- Visual tree helpers ----------

    private DeckGridPanel? GetPanel() => _panel ??= FindDescendant<DeckGridPanel>(TileHost);

    /// <summary>Walks up to the item container that owns the clicked visual.</summary>
    private static ContentPresenter? FindContainer(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ContentPresenter { DataContext: DeckItemViewModel } presenter) return presenter;
            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }

        return null;
    }
}
