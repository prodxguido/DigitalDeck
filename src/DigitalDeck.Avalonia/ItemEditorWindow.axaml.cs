using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.Avalonia;

/// <summary>
/// Editing one button: its name, its colour, its icon, and what it does.
/// </summary>
/// <remarks>
/// Covers the action kinds that mean the same thing on any desktop. The ones that do not are not
/// offered yet — a button that loads a device from Live's browser needs the browser picker, which
/// is its own window — and a button that already carries one of those is edited without touching
/// it: the name, the colour and the icon change, and what it does is saved exactly as it was.
/// <para>
/// That last part is the rule worth keeping. An editor that quietly dropped the action it could
/// not show would turn opening a button on a Mac into losing it.
/// </para>
/// </remarks>
public partial class ItemEditorWindow : Window
{
    /// <summary>The colours offered, the same nine the WPF editor shows.</summary>
    public static readonly IReadOnlyList<string> Palette =
    [
        "#33C9DC", "#5FD88F", "#EAB54C", "#E8615F", "#C77DFF",
        "#7AA2F7", "#FF9E64", "#F7768E", "#A7AEBB"
    ];

    private readonly DeckItem _item;
    private readonly IReadOnlyCollection<string> _taken;
    private readonly IAbletonClient? _live;

    /// <summary>The device picked out of Live's browser, when this button loads one.</summary>
    private AbletonLoadDeviceAction? _device;

    /// <summary>The steps, when this button does several things.</summary>
    private MacroAction? _macro;

    private string _colour = DeckItem.DefaultColor;
    private string? _icon;

    /// <summary>The action the item arrived with, when this editor cannot show it.</summary>
    private ActionPayload? _keptAsIs;

    /// <summary>True once Save went through. What <c>IDeckDialogs.EditItem</c> answers.</summary>
    public bool Saved { get; private set; }

    public ItemEditorWindow()
        : this(DeckItem.NewButton("página", null, "a", string.Empty), [])
    {
    }

    public ItemEditorWindow(
        DeckItem item, IReadOnlyCollection<string> takenNames, IAbletonClient? live = null)
    {
        _item = item;
        _taken = takenNames;
        _live = live;

        InitializeComponent();

        Swatches.ItemsSource = Palette;
        Icons.ItemsSource = DeckIconPaths.Palette;
        CommandBox.ItemsSource = AbletonCommands.All;
        CommandBox.SelectedIndex = 0;

        // El panel de Live aparece y desaparece con su chip, sin código de por medio.
        AbletonChoice.IsCheckedChanged += (_, _) =>
            AbletonPanel.IsVisible = AbletonChoice.IsChecked == true;

        LoadChoice.IsCheckedChanged += (_, _) =>
            LoadPanel.IsVisible = LoadChoice.IsChecked == true;

        MacroChoice.IsCheckedChanged += (_, _) =>
            MacroPanel.IsVisible = MacroChoice.IsChecked == true;

        LabelBox.Text = item.Label;
        _colour = item.ColorHex;
        _icon = item.IconGlyph;

        Load(item.Action);
    }

    private void Load(ActionPayload? action)
    {
        switch (action)
        {
            case null:
                NoneChoice.IsChecked = true;
                break;

            case OpenPathAction path:
                PathChoice.IsChecked = true;
                TargetBox.Text = path.Path;
                break;

            case OpenUrlAction url:
                UrlChoice.IsChecked = true;
                UrlBox.Text = url.Url;
                break;

            case MixerPanelAction:
                MixerChoice.IsChecked = true;
                break;

            case MacroAction macro:
                MacroChoice.IsChecked = true;
                Remember(macro);
                break;

            case AbletonLoadDeviceAction device:
                LoadChoice.IsChecked = true;
                Remember(device);
                break;

            case AbletonAction saved:
                AbletonChoice.IsChecked = true;
                CommandBox.SelectedItem = AbletonCommands.All.FirstOrDefault(c => c.Command == saved.Command);
                TrackBox.Text = saved.TrackIndex.ToString(CultureInfo.InvariantCulture);
                SceneBox.Text = saved.SceneIndex.ToString(CultureInfo.InvariantCulture);
                SendBox.Text = saved.SendIndex.ToString(CultureInfo.InvariantCulture);
                ValueBox.Text = saved.Value.ToString("0.###", CultureInfo.InvariantCulture);
                StateBox.IsChecked = saved.State;
                break;

            default:
                // Something this editor does not know how to show. Kept, and said out loud.
                _keptAsIs = action;
                Kinds.IsEnabled = false;
                Unported.IsVisible = true;
                break;
        }
    }

    private void Swatch_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex }) _colour = hex;
    }

    private void Icon_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name }) _icon = name;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void Save_Click(object? sender, RoutedEventArgs e) => Save();

    /// <summary>
    /// Writes the form back onto the item and closes, or explains why it cannot.
    /// </summary>
    /// <remarks>
    /// Public so a test can press Guardar without a pointer, the same as the WPF editor's.
    /// </remarks>
    public bool Save()
    {
        var label = (LabelBox.Text ?? string.Empty).Trim();

        if (label.Length == 0)
        {
            Complain("Ponele un nombre.");
            return false;
        }

        // Compared the way the deck compares them, so "Mi Música" and "mi musica" are the same
        // name here as they are to the voice assistant.
        if (!string.Equals(label, _item.Label, StringComparison.OrdinalIgnoreCase) &&
            _taken.Contains(label, StringComparer.OrdinalIgnoreCase))
        {
            Complain($"Ya hay un botón que se llama «{label}» en este grupo.");
            return false;
        }

        var (action, problem) = ReadAction();

        if (problem is not null)
        {
            Complain(problem);
            return false;
        }

        _item.Label = label;
        _item.ColorHex = _colour;
        _item.IconGlyph = _icon;
        _item.Action = action;

        Saved = true;

        Close();

        return true;
    }

    private (ActionPayload? Action, string? Problem) ReadAction()
    {
        if (_keptAsIs is not null) return (_keptAsIs, null);

        if (PathChoice.IsChecked == true)
        {
            var path = (TargetBox.Text ?? string.Empty).Trim();

            if (path.Length == 0) return (null, "Decime qué carpeta o archivo abrir.");

            return (new OpenPathAction { Path = path, Acknowledged = true }, null);
        }

        if (UrlChoice.IsChecked == true)
        {
            var url = OpenUrlAction.Normalise(UrlBox.Text ?? string.Empty);
            var action = new OpenUrlAction { Url = url };

            return action.Validate() is { } bad ? (null, bad) : (action, null);
        }

        if (MixerChoice.IsChecked == true) return (new MixerPanelAction(), null);

        if (AbletonChoice.IsChecked == true) return ReadAbleton();

        if (MacroChoice.IsChecked == true)
            return _macro is null
                ? (null, "Agregale al menos un paso.")
                : (_macro, null);

        if (LoadChoice.IsChecked == true)
            return _device is null
                ? (null, "Elegí un dispositivo del browser de Live.")
                : (_device, null);

        return (null, null);
    }

    /// <summary>
    /// Shows only the fields the chosen command actually takes.
    /// </summary>
    /// <remarks>
    /// Most of them take none. Offering a track number next to «Reproducir» would be offering a
    /// question with no right answer, and the catalogue already knows which ones need what.
    /// </remarks>
    private void Command_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (CommandBox.SelectedItem is not AbletonCommandInfo info) return;

        TrackRow.IsVisible = info.NeedsTrack;
        SceneRow.IsVisible = info.NeedsScene;
        SendRow.IsVisible = info.NeedsSend;
        ValueRow.IsVisible = info.NeedsValue;
        StateBox.IsVisible = info.NeedsState;

        ValueLabel.Text = info.ValueLabel ?? "Valor";

        ValueHint.Text = info.ValueHint ?? string.Empty;
        ValueHint.IsVisible = info.ValueHint is { Length: > 0 };

        // Lo que el comando dice de sí mismo: que va por teclado, que Live no lo expone, etc.
        CommandNote.Text = info.Note ?? string.Empty;
        CommandNote.IsVisible = info.Note is { Length: > 0 };
    }

    private (ActionPayload? Action, string? Problem) ReadAbleton()
    {
        if (CommandBox.SelectedItem is not AbletonCommandInfo info)
            return (null, "Elegí un comando de Live.");

        var action = new AbletonAction { Command = info.Command };

        if (info.NeedsTrack && Number(TrackBox.Text) is { } track) action.TrackIndex = (int)track;
        if (info.NeedsScene && Number(SceneBox.Text) is { } scene) action.SceneIndex = (int)scene;
        if (info.NeedsSend && Number(SendBox.Text) is { } send) action.SendIndex = (int)send;
        if (info.NeedsState) action.State = StateBox.IsChecked == true;

        if (info.NeedsValue)
        {
            if (Number(ValueBox.Text) is not { } value)
                return (null, $"«{info.ValueLabel ?? "El valor"}» tiene que ser un número.");

            if (value < info.ValueMinimum || value > info.ValueMaximum)
                return (null, $"Tiene que estar entre {info.ValueMinimum:0.###} y {info.ValueMaximum:0.###}.");

            action.Value = value;
        }

        return action.Validate() is { } bad ? (null, bad) : (action, null);
    }

    /// <summary>
    /// Reads a number the way it was typed.
    /// </summary>
    /// <remarks>
    /// Invariant, and with the comma accepted as well as the dot: this app is used in Spanish, on
    /// keyboards whose numeric pad writes a comma, and refusing «0,5» would be refusing what the
    /// person's own keyboard produced.
    /// </remarks>
    private static double? Number(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim().Replace(',', '.');

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>Opens Live's browser and keeps whatever came back.</summary>
    private async void Pick_Click(object? sender, RoutedEventArgs e)
    {
        if (_live is null)
        {
            Complain("Este shell todavía no tiene el puente con Live.");
            return;
        }

        var picker = new BrowserPickerWindow(_live);

        await picker.ShowDialog(this);

        if (picker.Chosen is { } chosen) Remember(chosen);
    }

    /// <summary>Opens the steps in their own window and keeps what came back.</summary>
    private async void Macro_Click(object? sender, RoutedEventArgs e)
    {
        var window = new MacroWindow(_macro, _live);

        await window.ShowDialog(this);

        if (window.Result is { } macro) Remember(macro);
    }

    private void Remember(MacroAction macro)
    {
        _macro = macro;

        MacroText.Text = macro.Steps.Count == 1
            ? "1 paso: " + macro.Steps[0].Action.Summary
            : $"{macro.Steps.Count} pasos, en orden.";
    }

    private void Remember(AbletonLoadDeviceAction device)
    {
        _device = device;

        DeviceText.Text = device.Category is { Length: > 0 } category
            ? $"{device.Name} · {AbletonBrowserCategories.Label(category)}"
            : device.Name;
    }

    private void Complain(string message)
    {
        Problem.Text = message;
        Problem.IsVisible = true;
    }
}
