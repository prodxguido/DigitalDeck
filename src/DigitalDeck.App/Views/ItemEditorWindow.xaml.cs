using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Voice;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Data;
using Microsoft.Win32;

namespace DigitalDeck.App.Views;

public partial class ItemEditorWindow : Window
{
    private const string NameTakenMessage =
        "Ya hay otro botón con ese nombre. Poneles nombres distintos para poder pedirlos por voz.";

    private readonly HashSet<string> _taken;

    private readonly DeckItem _item;
    private readonly IconStore _icons;
    private readonly IAbletonClient? _ableton;

    private readonly List<EditorChoice<AbletonCommandGroup>> _groups;
    private List<EditorChoice<AbletonCommandInfo>> _commands = new();
    private readonly List<EditorChoice<string>> _browserCategories;

    private string _color;
    private string? _glyph;
    private string? _iconPath;

    /// <summary>The icon this tile would get automatically for the action it opened with.</summary>
    private readonly string _openingSuggestion;

    /// <summary>
    /// The imported original the tile image was derived from, so re-framing does not need the file
    /// the user picked — which by the next session is a path we no longer know.
    /// </summary>
    private string? _iconOriginal;

    private IconFit _iconFit = IconFit.Fit;

    // ---------- The browser picker ----------
    //
    // The chosen item lives in these three fields rather than in the controls, because the picker
    // cannot show anything at all when Live is closed and the button still has to be editable and
    // saveable. What was chosen last time survives a session with no Ableton in it.

    private string _deviceName = string.Empty;
    private string? _deviceUri;
    private string? _deviceCategory;

    /// <summary>Where in the tree the picker is standing. Empty means the top of the category.</summary>
    private readonly List<AbletonBrowserEntry> _browserPath = new();

    private readonly DispatcherTimer _searchDebounce;
    private CancellationTokenSource? _browserQuery;
    private bool _browserAsked;

    /// <summary>Set while the editor fills itself in, so the Checked handlers stay out of the way.</summary>
    private bool _loading = true;

    private AbletonCommand _abletonCommand = AbletonCommand.Play;

    // ---------- Varias acciones ----------

    /// <summary>The steps of a macro, in order. Empty for every other kind of action.</summary>
    private readonly System.Collections.ObjectModel.ObservableCollection<MacroStepRow> _macro = new();

    /// <summary>
    /// True when this window is editing one step of a macro instead of a whole tile: no name, no
    /// colour, no icon, and no macro chip — a step cannot itself be a sequence.
    /// </summary>
    private readonly bool _actionOnly;

    /// <param name="takenNames">
    /// Names already spoken for by other tiles in the profile, in the assistant's normalised form.
    /// </param>
    /// <param name="actionOnly">
    /// Opens the window as a step editor: only the action half shows, and what the caller reads
    /// back is <c>item.Action</c>.
    /// </param>
    public ItemEditorWindow(
        DeckItem item,
        IconStore icons,
        IAbletonClient? ableton = null,
        IReadOnlyCollection<string>? takenNames = null,
        bool actionOnly = false)
    {
        InitializeComponent();

        _actionOnly = actionOnly;

        _taken = takenNames is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(takenNames, StringComparer.Ordinal);

        _item = item;
        _icons = icons;
        _ableton = ableton;
        _color = item.ColorHex;
        _glyph = item.IconGlyph;
        _iconPath = item.IconPath;
        _openingSuggestion = IconSuggestion.For(item.Action, item.Type);

        _groups = Enum.GetValues<AbletonCommandGroup>()
            .Select(group => new EditorChoice<AbletonCommandGroup>(group, AbletonCommands.GroupLabel(group)))
            .ToList();

        _browserCategories = AbletonBrowserCategories.All
            .Select(name => new EditorChoice<string>(name, AbletonBrowserCategories.Label(name)))
            .ToList();

        GroupList.ItemsSource = _groups;
        BrowserCategoryList.ItemsSource = _browserCategories;
        GlyphList.ItemsSource = DeckIcons.Palette;
        MacroList.ItemsSource = _macro;

        NameBox.Text = item.Label;
        ColorBox.Text = item.ColorHex;

        if (item.Type == ItemType.Folder)
        {
            TitleText.Text = "Editar grupo";
            SubtitleText.Text = "Un grupo guarda otros botones y grupos adentro.";
            ActionSection.Visibility = Visibility.Collapsed;
        }
        else
        {
            switch (item.Action)
            {
                case OpenPathAction { Path: var p } when IsLiveSet(p):
                    LiveSetChoice.IsChecked = true;
                    LiveSetBox.Text = p;
                    break;

                case OpenPathAction path:
                    PathChoice.IsChecked = true;
                    PathBox.Text = path.Path;
                    AcknowledgeCheck.IsChecked = path.Acknowledged;
                    break;

                case OpenUrlAction url:
                    UrlChoice.IsChecked = true;
                    UrlBox.Text = url.Url;
                    break;

                case MixerPanelAction:
                    MixerChoice.IsChecked = true;
                    break;

                case HotkeyAction hotkey:
                    HotkeyChoice.IsChecked = true;
                    HotkeyKeysBox.Text = hotkey.Keys;
                    HotkeyProgramBox.Text = hotkey.Program;
                    break;

                case AbletonAction saved:
                    AbletonChoice.IsChecked = true;
                    LoadAbleton(saved);
                    break;

                case MacroAction macro:
                    MacroChoice.IsChecked = true;
                    MacroContinueCheck.IsChecked = macro.ContinueOnError;
                    foreach (var step in macro.Steps)
                        _macro.Add(new MacroStepRow(step));
                    RenumberMacro();
                    break;

                case AbletonLoadDeviceAction device:
                    LoadChoice.IsChecked = true;
                    _deviceName = device.Name;
                    _deviceUri = device.Uri;
                    _deviceCategory = device.Category ?? AbletonBrowserCategories.AudioEffects;
                    Select(_browserCategories, _deviceCategory);
                    break;

                default:
                    NoneChoice.IsChecked = true;
                    break;
            }

            if (_item.Action is not AbletonAction)
                LoadAbleton(AbletonAction.For(AbletonCommand.Play));

            if (_item.Action is not AbletonLoadDeviceAction)
                Select(_browserCategories, AbletonBrowserCategories.AudioEffects);
        }

        // Each keystroke in the search box costs a walk of Live's tree, bounded but real, on
        // Live's own thread. Waiting for the typing to stop is what keeps that from happening
        // once per letter.
        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            _ = RefreshBrowserAsync();
        };

        if (_ableton is not null)
        {
            _ableton.StatusChanged += OnAbletonStatusChanged;
            Closed += (_, _) => _ableton.StatusChanged -= OnAbletonStatusChanged;
        }

        Closed += (_, _) =>
        {
            _searchDebounce.Stop();
            _browserQuery?.Cancel();
        };

        if (_actionOnly)
        {
            IdentitySection.Visibility = Visibility.Collapsed;
            MacroChoice.Visibility = Visibility.Collapsed;
            TitleText.Text = "Paso de la secuencia";
            SubtitleText.Text = "Qué hace este paso cuando le toca el turno.";

            // Save() still asks for a name; a step has none, so it carries a placeholder that is
            // never shown and never written to a tile.
            if (NameBox.Text.Length == 0) NameBox.Text = "paso";
        }

        _loading = false;

        UpdatePreview();
        UpdateAbletonPanel();
        UpdateLoadPanel();

        if (LoadChoice.IsChecked == true && !_browserAsked)
            _ = RefreshBrowserAsync();

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    /// <summary>
    /// Live turning up while the editor is open.
    /// </summary>
    /// <remarks>
    /// Worth reacting to: the common order of events is to open the editor, discover the picker is
    /// empty, start Live, and come back. Filling the list in on its own beats making the user close
    /// the window and open it again to get the same result.
    /// </remarks>
    private void OnAbletonStatusChanged(AbletonStatus status) =>
        Dispatcher.BeginInvoke(() =>
        {
            UpdateAbletonPanel();

            if (status.IsUsable && LoadChoice.IsChecked == true && !HasBrowserRows)
                _ = RefreshBrowserAsync();
        });

    private bool HasBrowserRows =>
        BrowserList?.ItemsSource is IReadOnlyCollection<BrowserRow> { Count: > 0 };

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    // ---------- Appearance ----------

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePreview();

        // Warned while typing rather than only on save, so the collision is visible at the moment
        // there is still a half-written name to change.
        if (IsNameTaken()) Warn(NameTakenMessage);
        else if (WarningText.Text == NameTakenMessage) WarningText.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Whether another tile already answers to this name.
    /// </summary>
    /// <remarks>
    /// Compared the way the voice assistant compares, not as plain text. That is what makes the
    /// rule mean something precise — <c>ESTUDIO</c> and <c>Estudio</c> collide because nothing said
    /// out loud could tell them apart, while <c>Reverb 1</c> and <c>Reverb 2</c> are free to
    /// coexist because saying either one is unambiguous.
    /// </remarks>
    private bool IsNameTaken() => _taken.Contains(VoicePhrase.Normalise(NameBox.Text));

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string hex }) return;

        _color = hex;
        ColorBox.Text = hex;
        UpdatePreview();
    }

    private void ColorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var value = ColorBox.Text.Trim();
        if (TryParseColor(value, out _)) _color = value;

        UpdatePreview();
    }

    private void Glyph_Click(object sender, RoutedEventArgs e)
    {
        // The name is on Tag rather than Content, because Content is the drawing now.
        if (sender is not Button { Tag: string glyph }) return;

        _glyph = glyph;
        _iconPath = null; // A chosen glyph replaces any custom image.
        UpdatePreview();
    }

    private async void PickImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Elegir imagen",
            Filter = IconStore.FileDialogFilter,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            // The original is kept as well as the square made from it. It costs one file and it is
            // what lets the two framing buttons work later, on a picture whose source path is long
            // gone — and it means switching framing re-derives from the picture rather than
            // cropping an already cropped copy.
            _iconOriginal = _icons.GetAbsolutePath(await _icons.ImportAsync(dialog.FileName));

            await ApplyFitAsync();
        }
        catch (Exception ex)
        {
            Warn(ex.Message);
        }
    }

    private async void IconFit_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || FitOption is null) return;

        _iconFit = FillOption.IsChecked == true ? IconFit.Fill : IconFit.Fit;

        await ApplyFitAsync();
    }

    /// <summary>Re-derives the tile image from the original at the current framing.</summary>
    private async Task ApplyFitAsync()
    {
        var original = _iconOriginal ??= OriginalBehind(_iconPath);

        if (original is null) return;

        try
        {
            var png = IconNormalizer.ToTile(original, _iconFit);
            var name = Path.GetFileNameWithoutExtension(original) + "-" + _iconFit.ToString().ToLowerInvariant();

            _iconPath = await _icons.SaveDerivedAsync(png, name);
            _glyph = null; // A picture replaces the icon from the set.

            UpdatePreview();
        }
        catch (Exception ex)
        {
            Warn($"No se pudo preparar la imagen. {ex.Message}");
        }
    }

    /// <summary>
    /// Walks back from a stored tile image to the original it was derived from.
    /// </summary>
    /// <remarks>
    /// The link is the filename — <c>&lt;hash&gt;-fit.png</c> — rather than a column, so a deck
    /// saved by an older version still opens and simply has no original to go back to. Buttons
    /// whose image came in through a .deckpack are in that position too, and there the square is
    /// all there is; the framing buttons stay out of the way rather than pretending.
    /// </remarks>
    private string? OriginalBehind(string? tileImage)
    {
        if (tileImage is null) return null;

        var name = Path.GetFileNameWithoutExtension(tileImage);
        var dash = name.LastIndexOf('-');

        if (dash <= 0) return null;

        var suffix = name[(dash + 1)..];

        if (suffix is not ("fit" or "fill")) return null;

        _iconFit = suffix == "fill" ? IconFit.Fill : IconFit.Fit;

        return _icons.FindOriginal(name[..dash]);
    }

    private void ClearImage_Click(object sender, RoutedEventArgs e)
    {
        _iconPath = null;
        _iconOriginal = null;
        UpdatePreview();
    }

    /// <summary>
    /// What the preview should draw: the user's own icon when they set one, otherwise the icon the
    /// action currently in the editor would earn on its own.
    /// </summary>
    private string EffectiveIconName()
    {
        if (_glyph is { Length: > 0 } chosen && chosen != _openingSuggestion)
            return chosen;

        if (_item.Type == ItemType.Folder)
            return DeckIcons.DefaultFolder;

        if (PathChoice.IsChecked == true)
            return IconSuggestion.For(new OpenPathAction { Path = PathBox.Text.Trim() }, _item.Type);
        if (LiveSetChoice.IsChecked == true)
            return IconSuggestion.For(new OpenPathAction { Path = LiveSetBox.Text.Trim() }, _item.Type);
        if (UrlChoice.IsChecked == true)
            return IconSuggestion.For(new OpenUrlAction { Url = UrlBox.Text.Trim() }, _item.Type);
        if (MixerChoice.IsChecked == true)
            return IconSuggestion.For(new MixerPanelAction(), _item.Type);

        if (HotkeyChoice.IsChecked == true)
            return IconSuggestion.For(new HotkeyAction(), _item.Type);
        if (AbletonChoice.IsChecked == true)
            return IconSuggestion.For(AbletonAction.For(_abletonCommand), _item.Type);
        if (LoadChoice.IsChecked == true)
            return IconSuggestion.For(new AbletonLoadDeviceAction { Name = _deviceName }, _item.Type);
        if (MacroChoice.IsChecked == true)
            return IconSuggestion.For(BuildMacro(), _item.Type);

        return _glyph is { Length: > 0 } fallback ? fallback : DeckIcons.DefaultButton;
    }

    private void UpdatePreview()
    {
        var colour = TryParseColor(_color, out var parsed) ? parsed : Color.FromRgb(0x33, 0xC9, 0xDC);

        PreviewTile.Background = new SolidColorBrush(Color.FromArgb(0x1F, colour.R, colour.G, colour.B));

        var name = EffectiveIconName();

        // One of the two shows: the drawing for an icon from the set, or the character for a glyph
        // saved before the set existed.
        var drawing = DeckIcons.Get(name);

        PreviewIcon.Data = drawing;
        PreviewIcon.Stroke = new SolidColorBrush(colour);
        PreviewGlyph.Foreground = new SolidColorBrush(colour);
        PreviewGlyph.Text = drawing is null ? name : string.Empty;

        var absolute = _icons.GetAbsolutePath(_iconPath);

        if (absolute is null)
        {
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewIcon.Visibility = drawing is null ? Visibility.Collapsed : Visibility.Visible;
            PreviewGlyph.Visibility = drawing is null ? Visibility.Visible : Visibility.Collapsed;
            ClearImageButton.Visibility = Visibility.Collapsed;
            FitPanel.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();

            PreviewImage.Source = bitmap;
            PreviewImage.Visibility = Visibility.Visible;
            PreviewIcon.Visibility = Visibility.Collapsed;
            PreviewGlyph.Visibility = Visibility.Collapsed;
            ClearImageButton.Visibility = Visibility.Visible;

            // Only offered when there is an original to re-frame from; see OriginalBehind.
            FitPanel.Visibility = (_iconOriginal ?? OriginalBehind(_iconPath)) is null
                ? Visibility.Collapsed
                : Visibility.Visible;

            var wasLoading = _loading;
            _loading = true;
            FitOption.IsChecked = _iconFit == IconFit.Fit;
            FillOption.IsChecked = _iconFit == IconFit.Fill;
            _loading = wasLoading;
        }
        catch (Exception)
        {
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewIcon.Visibility = PreviewIcon.Data is null ? Visibility.Collapsed : Visibility.Visible;
            PreviewGlyph.Visibility = PreviewIcon.Data is null ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ---------- Action ----------

    /// <summary>
    /// Captures the combination the user presses into the box, instead of typing its name.
    /// </summary>
    /// <remarks>
    /// The same capture the voice assistant's settings use, and for the same reason: nobody should
    /// have to know that the key next to Enter is called <c>OEM_7</c>. What is stored is the text
    /// the parser hands back, so a combination this app cannot read again is refused here rather
    /// than saved and silently lost.
    /// </remarks>
    private void HotkeyKeys_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        // Alt combinations arrive as SystemKey, and reading Key there gives Key.System rather than
        // the key the user actually pressed.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        // A modifier on its own is the user halfway through the combination, not their answer.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
            return;

        var modifiers = HotkeyModifiers.None;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Windows;

        var spec = new HotkeySpec(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));

        if (!HotkeySpec.TryParse(spec.ToString(), out var usable))
        {
            HotkeyKeysHint.Text =
                "Esa tecla no se puede usar. Probá con una letra, un número o una F.";
            return;
        }

        HotkeyKeysBox.Text = usable.ToString();
        HotkeyKeysHint.Text = "Listo. Ese es el atajo que va a apretar el botón.";
    }

    private void ActionKind_Changed(object sender, RoutedEventArgs e)
    {
        // Fired during InitializeComponent, before the panels exist.
        if (PathPanel is null || UrlPanel is null || AbletonPanel is null || LoadPanel is null || LiveSetPanel is null) return;

        PathPanel.Visibility = PathChoice.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        LiveSetPanel.Visibility = LiveSetChoice.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UrlPanel.Visibility = UrlChoice.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        AbletonPanel.Visibility = AbletonChoice.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        LoadPanel.Visibility = LoadChoice.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        MacroPanel.Visibility = MacroChoice.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        HotkeyPanel.Visibility = HotkeyChoice.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (LoadPanel.Visibility == Visibility.Visible && !_browserAsked)
            _ = RefreshBrowserAsync();

        UpdateExecutableWarning();
        if (!_loading) UpdatePreview();
    }

    private void PathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateExecutableWarning();
        if (!_loading) UpdatePreview();
    }

    private void UpdateExecutableWarning()
    {
        if (ExecutableWarning is null) return;

        var isExecutable = OpenPathAction.IsExecutable(PathBox.Text.Trim());
        ExecutableWarning.Visibility = isExecutable ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Elegir archivo", CheckFileExists = true };

        if (dialog.ShowDialog(this) == true)
        {
            PathBox.Text = dialog.FileName;
            if (NameBox.Text.Trim().Length == 0)
                NameBox.Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    /// <summary>True when a path points at an Ableton Live set, which gets its own action chip.</summary>
    private static bool IsLiveSet(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        path.Trim().EndsWith(".als", StringComparison.OrdinalIgnoreCase);

    private void BrowseLiveSet_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Elegir proyecto de Ableton Live",
            Filter = "Proyecto de Ableton Live (*.als)|*.als",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true) return;

        LiveSetBox.Text = dialog.FileName;

        if (NameBox.Text.Trim().Length == 0)
            NameBox.Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);

        if (!_loading) UpdatePreview();
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Elegir carpeta" };

        if (dialog.ShowDialog(this) == true)
        {
            PathBox.Text = dialog.FolderName;
            if (NameBox.Text.Trim().Length == 0)
                NameBox.Text = new System.IO.DirectoryInfo(dialog.FolderName).Name;
        }
    }

    // ---------- Ableton ----------
    //
    // Every field below is driven by the command catalogue rather than hard-coded: which controls
    // appear, what they are labelled and what range they accept all come from AbletonCommands. A
    // command added there shows up here on its own.

    private void LoadAbleton(AbletonAction action)
    {
        _abletonCommand = action.Command;

        var info = AbletonCommands.Describe(action.Command);

        ShowGroup(info.Group);
        Select(_groups, info.Group);
        Select(_commands, info);

        TrackSelectedChoice.IsChecked = action.TrackIndex == AbletonAction.SelectedTrack;
        TrackMasterChoice.IsChecked = action.TrackIndex == AbletonAction.MasterTrack;
        TrackNumberChoice.IsChecked = action.TrackIndex >= 0;

        // The stored index is zero based; a musician counts tracks from one.
        TrackNumberBox.Text = action.TrackIndex >= 0
            ? (action.TrackIndex + 1).ToString(CultureInfo.CurrentCulture)
            : string.Empty;

        SceneBox.Text = action.SceneIndex >= 0
            ? (action.SceneIndex + 1).ToString(CultureInfo.CurrentCulture)
            : string.Empty;

        SendBox.Text = (action.SendIndex + 1).ToString(CultureInfo.CurrentCulture);

        StateOnChoice.IsChecked = action.State;
        StateOffChoice.IsChecked = !action.State;

        ValueBox.Text = action.Value.ToString("0.###", CultureInfo.CurrentCulture);
    }

    private void ShowGroup(AbletonCommandGroup group)
    {
        _commands = AbletonCommands.InGroup(group)
            .Select(info => new EditorChoice<AbletonCommandInfo>(info, info.Label))
            .ToList();

        CommandList.ItemsSource = _commands;
    }

    private static void Select<T>(IEnumerable<EditorChoice<T>> choices, T value)
    {
        foreach (var choice in choices)
            choice.IsSelected = EqualityComparer<T>.Default.Equals(choice.Value, value);
    }

    private void AbletonGroup_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: AbletonCommandGroup group }) return;

        ShowGroup(group);

        // Landing on a group with nothing chosen would leave the panel in a state that cannot be
        // saved, so the first command of the group is picked for the user.
        var first = _commands.FirstOrDefault();
        if (first is null) return;

        first.IsSelected = true;
        ApplyCommand(first.Value, resetValue: true);
    }

    private void AbletonCommand_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: AbletonCommandInfo info }) return;

        ApplyCommand(info, resetValue: info.Command != _abletonCommand);
    }

    private void ApplyCommand(AbletonCommandInfo info, bool resetValue)
    {
        _abletonCommand = info.Command;

        // Ranges differ wildly between commands — 20..999 for a tempo, 0..1 for a volume — so a
        // number left over from the previous command is almost always wrong for the new one.
        if (resetValue)
            ValueBox.Text = info.ValueDefault.ToString("0.###", CultureInfo.CurrentCulture);

        UpdateAbletonPanel();
        if (!_loading) UpdatePreview();
    }

    private void TrackKind_Checked(object sender, RoutedEventArgs e)
    {
        if (TrackNumberBox is null) return;

        TrackNumberBox.Visibility = TrackNumberChoice.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!_loading) UpdateAbletonPanel();
    }

    private void AbletonField_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loading) UpdateAbletonPanel();
    }

    private void AbletonState_Checked(object sender, RoutedEventArgs e)
    {
        if (!_loading) UpdateAbletonPanel();
    }

    private void UpdateAbletonPanel()
    {
        if (AbletonSummaryText is null) return;

        var info = AbletonCommands.Describe(_abletonCommand);

        TrackRow.Visibility = info.NeedsTrack ? Visibility.Visible : Visibility.Collapsed;
        SceneRow.Visibility = info.NeedsScene ? Visibility.Visible : Visibility.Collapsed;
        SendRow.Visibility = info.NeedsSend ? Visibility.Visible : Visibility.Collapsed;
        StateRow.Visibility = info.NeedsState ? Visibility.Visible : Visibility.Collapsed;
        ValueRow.Visibility = info.NeedsValue ? Visibility.Visible : Visibility.Collapsed;

        // Echoes the letter back, because "2" and "send B" are the same thing and only one of them
        // is what the mixer in Live is showing.
        SendHint.Text = TryReadPosition(SendBox.Text, out var sendIndex)
            ? AbletonAction.DescribeSend(sendIndex)
            : string.Empty;

        ValueLabel.Text = info.ValueLabel ?? string.Empty;
        ValueHint.Text = info.ValueHint ?? string.Empty;
        ValueHint.Visibility = info.ValueHint is null ? Visibility.Collapsed : Visibility.Visible;

        TrackNumberBox.Visibility = TrackNumberChoice.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;

        var (action, problem) = ReadAbleton();

        AbletonSummaryText.Text = problem ?? action?.Summary ?? info.Label;
        AbletonSummaryText.Foreground = problem is null
            ? (Brush)FindResource("TextBrush")
            : (Brush)FindResource("DangerBrush");

        AbletonConnectionText.Text = DescribeConnection();
    }

    /// <summary>
    /// Says plainly whether the button will work right now.
    /// </summary>
    /// <remarks>
    /// Deliberately never blocks saving. Configuring a deck with Live closed is completely normal,
    /// and refusing to save would be worse than a button that waits for Ableton to show up.
    /// </remarks>
    private string DescribeConnection()
    {
        // File commands (Export, Save…) go straight to Live's window as keystrokes, so the OSC
        // bridge status says nothing about whether they will work.
        if (AbletonCommands.Describe(_abletonCommand) is { ViaKeyboard: true } keyboard)
            return keyboard.Note ?? "Se manda como atajo de teclado a Live.";

        if (_ableton is null) return string.Empty;

        var status = _ableton.Status;

        return status.IsUsable
            ? $"Live está conectado ({status.LiveVersion}). El botón va a funcionar apenas lo guardes."
            : $"{AbletonAdapterMessage(status)} El botón se guarda igual y funciona cuando Live esté disponible.";
    }

    private static string AbletonAdapterMessage(AbletonStatus status) =>
        status.State switch
        {
            AbletonConnectionState.Connecting => "Todavía no encuentro a Ableton Live.",
            AbletonConnectionState.VersionMismatch => "El Remote Script instalado no es el que esta app espera.",
            AbletonConnectionState.Unavailable => status.Detail,
            _ => "Ableton Live no está conectado."
        };

    /// <summary>Builds the action from the fields, or explains what is wrong with them.</summary>
    private (AbletonAction? Action, string? Problem) ReadAbleton()
    {
        var info = AbletonCommands.Describe(_abletonCommand);
        var action = AbletonAction.For(_abletonCommand);

        if (info.NeedsTrack)
        {
            if (TrackMasterChoice.IsChecked == true)
            {
                action.TrackIndex = AbletonAction.MasterTrack;
            }
            else if (TrackNumberChoice.IsChecked == true)
            {
                if (!TryReadPosition(TrackNumberBox.Text, out var index))
                    return (null, "El número de track tiene que ser 1 o mayor.");

                action.TrackIndex = index;
            }
            else
            {
                action.TrackIndex = AbletonAction.SelectedTrack;
            }
        }

        if (info.NeedsScene)
        {
            if (SceneBox.Text.Trim().Length == 0)
            {
                action.SceneIndex = AbletonAction.SelectedScene;
            }
            else
            {
                if (!TryReadPosition(SceneBox.Text, out var index))
                    return (null, "El número de escena tiene que ser 1 o mayor.");

                action.SceneIndex = index;
            }
        }

        if (info.NeedsSend)
        {
            if (!TryReadPosition(SendBox.Text, out var index))
                return (null, "El send tiene que ser 1 o mayor: 1 es el A, 2 el B.");

            action.SendIndex = index;
        }

        if (info.NeedsState)
            action.State = StateOffChoice.IsChecked != true;

        if (info.NeedsValue)
        {
            if (!TryReadNumber(ValueBox.Text, out var value))
                return (null, $"{info.ValueLabel ?? "El valor"} tiene que ser un número.");

            action.Value = value;
        }

        return action.Validate() is { } problem ? (null, problem) : (action, null);
    }

    /// <summary>Reads a one-based position from the editor into the zero-based index Live uses.</summary>
    private static bool TryReadPosition(string text, out int index)
    {
        index = 0;

        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var position))
            return false;

        if (position < 1) return false;

        index = position - 1;
        return true;
    }

    /// <summary>
    /// Parses a decimal typed by hand.
    /// </summary>
    /// <remarks>
    /// Both separators are accepted: the keyboard's decimal key produces a period on most layouts
    /// while the locale here expects a comma, and rejecting "0.85" because of that would be absurd.
    /// </remarks>
    private static bool TryReadNumber(string text, out double value)
    {
        var trimmed = text.Trim();

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
               || double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    // ---------- Load a device ----------
    //
    // The list here is Live's own browser, asked for one level at a time over the bridge. It is not
    // a file dialog and cannot be one: load_item reaches only what Live has indexed, so anything
    // picked from the disk would be a button that fails at press time rather than at edit time.

    /// <summary>
    /// Results asked for in one search.
    /// </summary>
    /// <remarks>
    /// Well under the script's own ceiling of 500. The reply is a single UDP datagram, and 500
    /// long URIs is close enough to the 64K limit to be worth not finding out about in front of a
    /// user. Anyone who needs to see more than this wants a narrower search, not a longer list.
    /// </remarks>
    private const int SearchLimit = 150;

    private void BrowserCategory_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        // A category is a different tree, so the path into the old one means nothing here.
        _browserPath.Clear();
        _ = RefreshBrowserAsync();
        UpdateLoadPanel();
    }

    private void BrowserSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;

        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void Crumb_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int depth }) return;

        while (_browserPath.Count > depth)
            _browserPath.RemoveAt(_browserPath.Count - 1);

        ClearSearch();
        _ = RefreshBrowserAsync();
    }

    /// <summary>
    /// The row itself: choose it, or open it when there is nothing to choose.
    /// </summary>
    /// <remarks>
    /// Most of what Live lists is loadable and has presets underneath at the same time, so these
    /// two cannot be the same gesture — the chevron is the other one.
    /// </remarks>
    private void BrowserRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BrowserRow row }) return;

        if (row.ChoosesOnClick)
        {
            Choose(row.Entry);
            return;
        }

        Open(row);
    }

    private void BrowserOpen_Click(object sender, RoutedEventArgs e)
    {
        // Marked handled because Click bubbles, and this button sits inside the row's own button:
        // without this, one click on the chevron both walks into the row and chooses it.
        e.Handled = true;

        if (sender is Button { Tag: BrowserRow row }) Open(row);
    }

    /// <summary>Walks into a row.</summary>
    private void Open(BrowserRow row)
    {
        if (!row.CanOpen) return;

        _browserPath.Add(row.Entry);
        _ = RefreshBrowserAsync();
    }

    /// <summary>Takes an item as the one the button will load.</summary>
    private void Choose(AbletonBrowserEntry entry)
    {
        _deviceName = entry.Name;
        _deviceUri = entry.Uri.Length == 0 ? null : entry.Uri;
        _deviceCategory = SelectedBrowserCategory();

        // Naming the button after what it loads is right often enough to be worth doing, and only
        // when the user has not already named it something of their own.
        if (NameBox.Text.Trim().Length == 0)
            NameBox.Text = System.IO.Path.GetFileNameWithoutExtension(entry.Name);

        // Redraw so the tick moves to the row just clicked.
        RedrawBrowserRows();
        UpdateLoadPanel();
    }

    private void ClearSearch()
    {
        _searchDebounce.Stop();

        if (BrowserSearchBox.Text.Length == 0) return;

        var wasLoading = _loading;
        _loading = true;
        BrowserSearchBox.Text = string.Empty;
        _loading = wasLoading;
    }

    private bool IsSearching => BrowserSearchBox.Text.Trim().Length > 0;

    private string SelectedBrowserCategory() =>
        _browserCategories.FirstOrDefault(choice => choice.IsSelected)?.Value
        ?? AbletonBrowserCategories.AudioEffects;

    /// <summary>Asks Live for whatever the picker should be showing now.</summary>
    private async Task RefreshBrowserAsync()
    {
        if (BrowserList is null) return;

        _browserAsked = true;

        // Whatever was in flight is about to be the answer to a question nobody is asking.
        _browserQuery?.Cancel();

        var query = new CancellationTokenSource();
        _browserQuery = query;

        var category = SelectedBrowserCategory();
        var term = BrowserSearchBox.Text.Trim();
        var searching = IsSearching;
        var path = string.Join("/", _browserPath.Select(entry => entry.Name));

        RenderCrumbs(searching);
        BrowserList.ItemsSource = null;
        BrowserStatusText.Text = string.Empty;
        ShowBrowserMessage("Preguntándole a Live…");

        if (_ableton is null)
        {
            ShowBrowserMessage("Esta ventana se abrió sin el puente a Live.");
            return;
        }

        AbletonBrowserListing listing;

        try
        {
            listing = searching
                ? await _ableton.SearchAsync(category, term, SearchLimit, query.Token)
                : await _ableton.ListAsync(category, path, query.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // A newer question was asked while this one was in the air; its answer is the current one.
        if (!ReferenceEquals(_browserQuery, query)) return;

        ShowListing(listing, searching, term);
    }

    private void ShowListing(AbletonBrowserListing listing, bool searching, string term)
    {
        if (!listing.Success)
        {
            BrowserList.ItemsSource = null;
            ShowBrowserMessage(Degraded(listing.Error!));
            return;
        }

        var rows = BuildRows(listing, searching);

        BrowserList.ItemsSource = rows;

        if (rows.Count == 0)
        {
            var where = AbletonBrowserCategories.Label(SelectedBrowserCategory());

            ShowBrowserMessage(searching
                ? $"Live no tiene nada indexado que se llame «{term}» en {where}."
                : "Acá no hay nada que se pueda cargar.");
        }
        else
        {
            ShowBrowserMessage(null);
        }

        BrowserStatusText.Text = Describe(rows.Count, listing.Complete, searching);
    }

    private List<BrowserRow> BuildRows(AbletonBrowserListing listing, bool searching)
    {
        var rows = new List<BrowserRow>();

        // Standing inside something that can itself be loaded: offer it, or it is unreachable.
        if (!searching && _browserPath.Count > 0 && _browserPath[^1].IsLoadable)
            rows.Add(new BrowserRow(_browserPath[^1], IsChosen(_browserPath[^1]), isSelf: true));

        rows.AddRange(listing.Entries
            .Where(entry => entry.IsUseful)
            .Select(entry => new BrowserRow(entry, IsChosen(entry), inSearch: searching)));

        return rows;
    }

    /// <summary>Re-wraps the rows on screen so the tick lands on whatever was just chosen.</summary>
    private void RedrawBrowserRows()
    {
        if (BrowserList?.ItemsSource is not IEnumerable<BrowserRow> rows) return;

        BrowserList.ItemsSource = rows
            .Select(row => new BrowserRow(row.Entry, IsChosen(row.Entry), row.IsSelf, row.InSearch))
            .ToList();
    }

    /// <summary>
    /// Whether this row is the one the button already loads.
    /// </summary>
    /// <remarks>
    /// By URI when there is one, because that is the whole reason the URI is stored: two presets
    /// with the same name are a real thing and the name alone would tick both of them.
    /// </remarks>
    private bool IsChosen(AbletonBrowserEntry entry) =>
        _deviceUri is { Length: > 0 } uri
            ? entry.Uri == uri
            : _deviceName.Length > 0 && string.Equals(entry.Name, _deviceName, StringComparison.Ordinal);

    private void RenderCrumbs(bool searching)
    {
        var label = AbletonBrowserCategories.Label(SelectedBrowserCategory());
        var crumbs = new List<BrowserCrumb>();

        if (searching)
        {
            // A search reaches everywhere under the category, so showing a path would be a lie.
            crumbs.Add(new BrowserCrumb(0, $"Resultados en {label}", true));
        }
        else
        {
            crumbs.Add(new BrowserCrumb(0, label, _browserPath.Count == 0));

            for (var depth = 0; depth < _browserPath.Count; depth++)
            {
                crumbs.Add(new BrowserCrumb(
                    depth + 1,
                    _browserPath[depth].Name,
                    depth == _browserPath.Count - 1));
            }
        }

        BrowserCrumbs.ItemsSource = crumbs;
    }

    /// <summary>
    /// The line under the list.
    /// </summary>
    /// <remarks>
    /// The script's traversals stop at a ceiling so they cannot freeze Live, and it says when it
    /// hit one. Passing that on matters: "nothing else matched" and "I stopped looking" are very
    /// different answers to give somebody hunting for a preset.
    /// </remarks>
    private static string Describe(int count, bool complete, bool searching)
    {
        if (count == 0) return string.Empty;

        var items = count == 1 ? "1 ítem" : $"{count} ítems";

        if (complete) return $"{items}.";

        return searching
            ? $"{items}. La búsqueda se cortó antes de terminar: hay más, afiná el texto."
            : $"{items}. La carpeta tiene más de los que entran en una respuesta.";
    }

    /// <summary>Adds what still works to whatever went wrong, so the message is not just a dead end.</summary>
    private string Degraded(string error) =>
        _deviceName.Length == 0
            ? error
            : $"{error}\n\nEl botón sigue cargando «{_deviceName}», y se puede guardar así.";

    private void ShowBrowserMessage(string? message)
    {
        BrowserMessage.Text = message ?? string.Empty;
        BrowserMessage.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateLoadPanel()
    {
        if (LoadWarningText is null) return;

        var problem = ReadLoadDevice().Problem;

        DeviceChoiceText.Text = _deviceName.Length > 0
            ? _deviceName
            : "Todavía no elegiste ninguno.";

        DeviceChoiceDetail.Text = DescribeChoice();

        LoadWarningText.Text = problem ?? string.Empty;
        LoadWarningText.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private string DescribeChoice()
    {
        if (_deviceName.Length == 0)
            return "Elegilo de la lista de arriba. Todo lo que aparece ahí es del browser de Live, así que es cargable.";

        var where = AbletonBrowserCategories.Label(_deviceCategory ?? SelectedBrowserCategory());

        return _deviceUri is { Length: > 0 }
            ? $"En {where}. Guardado con su URI, así que otro preset con el mismo nombre no lo confunde."
            : $"En {where}, sólo por nombre. Si hay más de uno igual, Live se va a negar a elegir.";
    }

    private (AbletonLoadDeviceAction? Action, string? Problem) ReadLoadDevice()
    {
        var action = new AbletonLoadDeviceAction
        {
            Name = _deviceName,
            Uri = _deviceUri is { Length: > 0 } uri ? uri : null,
            Category = _deviceCategory ?? SelectedBrowserCategory()
        };

        return action.Validate() is { } problem ? (null, problem) : (action, null);
    }

    // ---------- Varias acciones ----------

    /// <summary>Reads the step list into a payload, applying whatever delays are typed in.</summary>
    private MacroAction BuildMacro()
    {
        foreach (var row in _macro) row.TryApplyDelay();

        return new MacroAction
        {
            Steps = _macro.Select(row => row.Step).ToList(),
            ContinueOnError = MacroContinueCheck.IsChecked == true,

            // A macro is only as dangerous as its steps, and each one was acknowledged in its own
            // editor. Asking again on the outer button would ask the same thing twice.
            Acknowledged = true
        };
    }

    private void RenumberMacro()
    {
        for (var i = 0; i < _macro.Count; i++) _macro[i].Position = i + 1;

        MacroEmptyText.Visibility = _macro.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (!_loading) UpdatePreview();
    }

    /// <summary>Opens this same editor cut down to its action half, and returns what was chosen.</summary>
    private ActionPayload? AskStepAction(ActionPayload? existing)
    {
        // Never stored: it exists so the editor has something to write the chosen action onto.
        var scratch = DeckItem.NewButton(_item.PageId, _item.ParentId, _item.SortKey, "paso");
        scratch.Action = existing;

        var editor = new ItemEditorWindow(scratch, _icons, _ableton, takenNames: null, actionOnly: true)
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        return editor.ShowDialog() == true ? scratch.Action : null;
    }

    private void MacroAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_macro.Count >= MacroAction.MaxSteps)
        {
            Warn($"Una secuencia admite hasta {MacroAction.MaxSteps} acciones.");
            return;
        }

        if (AskStepAction(null) is not { } chosen) return;

        if (chosen is MacroAction)
        {
            Warn("Una secuencia no puede tener otra secuencia adentro.");
            return;
        }

        _macro.Add(new MacroStepRow(new MacroStep { Action = chosen }));
        RenumberMacro();
    }

    private void MacroEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: MacroStepRow row }) return;
        if (AskStepAction(row.Step.Action) is not { } chosen) return;

        if (chosen is MacroAction)
        {
            Warn("Una secuencia no puede tener otra secuencia adentro.");
            return;
        }

        row.Step.Action = chosen;
        row.Refresh();

        if (!_loading) UpdatePreview();
    }

    private void MacroRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: MacroStepRow row }) return;

        _macro.Remove(row);
        RenumberMacro();
    }

    private void MacroUp_Click(object sender, RoutedEventArgs e) => MoveStep(sender, -1);

    private void MacroDown_Click(object sender, RoutedEventArgs e) => MoveStep(sender, +1);

    private void MoveStep(object sender, int offset)
    {
        if (sender is not FrameworkElement { Tag: MacroStepRow row }) return;

        var from = _macro.IndexOf(row);
        var to = from + offset;

        if (from < 0 || to < 0 || to >= _macro.Count) return;

        _macro.Move(from, to);
        RenumberMacro();
    }

    private void MacroDelay_Changed(object sender, TextChangedEventArgs e)
    {
        // Checked as it is typed so the warning shows before Save, but a value that does not parse
        // yet is left on screen rather than snapped back from under the cursor.
        if (sender is not FrameworkElement { Tag: MacroStepRow row }) return;

        if (row.TryApplyDelay())
        {
            if (WarningText.Text.StartsWith("La espera", StringComparison.Ordinal))
                WarningText.Visibility = Visibility.Collapsed;
        }
        else
        {
            Warn($"La espera de cada paso va de 0 a {MacroStep.MaxDelayMs / 1000} segundos.");
        }
    }

    // ---------- Save ----------

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // DialogResult only exists on a window shown as a dialog, which is every real use and no
        // test. Splitting it off is what lets the round trip — chip in, action out — be checked.
        if (Save()) DialogResult = true;
    }

    /// <summary>
    /// Writes the form onto the item. False when something was wrong and a warning is now showing.
    /// </summary>
    /// <remarks>
    /// Internal so a test can drive it: an action chip that is never read here saves nothing at
    /// all, silently, and the only symptom is the editor reopening on «Ninguna».
    /// </remarks>
    internal bool Save()
    {
        var label = NameBox.Text.Trim();

        if (label.Length == 0)
        {
            Warn("Poné un nombre para reconocerlo en la grilla.");
            return false;
        }

        if (!_actionOnly && IsNameTaken())
        {
            Warn(NameTakenMessage);
            NameBox.Focus();
            NameBox.SelectAll();
            return false;
        }

        ActionPayload? action = null;

        if (_item.Type == ItemType.Button)
        {
            if (PathChoice.IsChecked == true)
            {
                action = new OpenPathAction
                {
                    Path = PathBox.Text.Trim(),
                    Acknowledged = AcknowledgeCheck.IsChecked == true
                };
            }
            else if (LiveSetChoice.IsChecked == true)
            {
                var set = LiveSetBox.Text.Trim();

                if (set.Length == 0)
                {
                    Warn("Elegí el archivo .als del proyecto.");
                    return false;
                }

                // A .als is opened by the shell like a double-click — Live launches with it. It is
                // not an executable, so no acknowledgement is needed.
                action = new OpenPathAction { Path = set };
            }
            else if (UrlChoice.IsChecked == true)
            {
                action = new OpenUrlAction { Url = OpenUrlAction.Normalise(UrlBox.Text) };
            }
            else if (MixerChoice.IsChecked == true)
            {
                action = new MixerPanelAction();
            }
            else if (AbletonChoice.IsChecked == true)
            {
                var (abletonAction, abletonProblem) = ReadAbleton();

                if (abletonAction is null)
                {
                    Warn(abletonProblem ?? "Revisá la acción de Ableton.");
                    return false;
                }

                action = abletonAction;
            }
            else if (MacroChoice.IsChecked == true)
            {
                action = BuildMacro();
            }
            else if (HotkeyChoice.IsChecked == true)
            {
                action = new HotkeyAction
                {
                    Keys = HotkeyKeysBox.Text.Trim(),
                    Program = HotkeyProgramBox.Text.Trim()
                };
            }
            else if (LoadChoice.IsChecked == true)
            {
                var (device, deviceProblem) = ReadLoadDevice();

                if (device is null)
                {
                    Warn(deviceProblem ?? "Elegí un dispositivo del browser de Live.");
                    return false;
                }

                action = device;
            }

            if (action?.Validate() is { } problem)
            {
                Warn(problem);
                return false;
            }
        }

        // An icon that matches what the button does, unless the user picked one or dropped in an
        // image. Left alone once it differs from the automatic one, so a deliberate choice sticks.
        if (_iconPath is null && (_glyph is null || _glyph == _openingSuggestion))
            _glyph = IconSuggestion.For(action, _item.Type);

        _item.Label = label;
        _item.ColorHex = TryParseColor(_color, out _) ? _color : DeckItem.DefaultColor;
        _item.IconGlyph = _glyph;
        _item.IconPath = _iconPath;
        _item.Action = action;

        return true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Warn(string message)
    {
        WarningText.Text = message;
        WarningText.Visibility = Visibility.Visible;
    }

    private static bool TryParseColor(string? value, out Color colour)
    {
        colour = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        try
        {
            if (ColorConverter.ConvertFromString(value) is not Color parsed) return false;

            colour = parsed;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
