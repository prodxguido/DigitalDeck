using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.Avalonia;

/// <summary>
/// A button that does several things in order, with a wait between them.
/// </summary>
/// <remarks>
/// «Arrancá la mañana» is exactly this: the YouTube playlist, the newspaper, Live and Spotify, in
/// order, off one key. The wait is what lets a step that needs a program already open actually find
/// it there.
/// <para>
/// A step is chosen with the ordinary button editor, because a step <em>is</em> an action and the
/// editor already knows how to build one. What comes back is its action; the name, colour and icon
/// it asks for belong to the button, not to the step, and are simply ignored.
/// </para>
/// </remarks>
public partial class MacroWindow : Window
{
    /// <summary>One step in the list, with its wait as text so it can be typed into.</summary>
    public sealed class Step : ObservableObject
    {
        private string _delayText;

        public Step(MacroStep step)
        {
            Action = step.Action;
            _delayText = step.DelayMs.ToString(CultureInfo.InvariantCulture);
        }

        public ActionPayload Action { get; }

        public string Summary => Action.Summary;

        public string DelayText
        {
            get => _delayText;
            set => SetProperty(ref _delayText, value);
        }

        /// <summary>The wait as a number, or null when what was typed is not one.</summary>
        public int? Delay =>
            int.TryParse(DelayText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)
                ? ms
                : null;
    }

    private readonly ObservableCollection<Step> _steps = new();
    private readonly IAbletonClient? _live;

    /// <summary>What was built, or null when the window was closed without saving.</summary>
    public MacroAction? Result { get; private set; }

    public MacroWindow()
        : this(null, null)
    {
    }

    public MacroWindow(MacroAction? existing, IAbletonClient? live)
    {
        _live = live;

        InitializeComponent();

        Steps.ItemsSource = _steps;

        foreach (var step in existing?.Steps ?? []) _steps.Add(new Step(step));

        ContinueBox.IsChecked = existing?.ContinueOnError ?? true;

        _steps.CollectionChanged += (_, _) => Empty.IsVisible = _steps.Count == 0;

        Empty.IsVisible = _steps.Count == 0;
    }

    /// <summary>The steps as they stand. Public so a test can look without a pointer.</summary>
    public IReadOnlyList<Step> Current => _steps;

    /// <summary>Adds a step built with the ordinary editor. Public so a test can add one.</summary>
    public void Add(ActionPayload action) => _steps.Add(new Step(new MacroStep { Action = action }));

    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        // Un item de mentira: el editor pide nombre, y el del paso no se usa para nada.
        var carrier = DeckItem.NewButton("paso", null, "a", "Paso");

        var editor = new ItemEditorWindow(carrier, [], _live);

        await editor.ShowDialog(this);

        if (editor.Saved && carrier.Action is { } action) Add(action);
    }

    private void Up_Click(object? sender, RoutedEventArgs e) => Move(sender, -1);

    private void Down_Click(object? sender, RoutedEventArgs e) => Move(sender, +1);

    private void Move(object? sender, int by)
    {
        if (sender is not Button { Tag: Step step }) return;

        var at = _steps.IndexOf(step);
        var to = at + by;

        if (at < 0 || to < 0 || to >= _steps.Count) return;

        _steps.Move(at, to);
    }

    private void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Step step }) _steps.Remove(step);
    }

    private void Save_Click(object? sender, RoutedEventArgs e) => Save();

    /// <summary>Builds the macro, or explains why it cannot. Public so a test can press Guardar.</summary>
    public bool Save()
    {
        if (_steps.Count == 0)
        {
            Complain("Un botón de varias acciones necesita al menos un paso.");
            return false;
        }

        if (_steps.Count > MacroAction.MaxSteps)
        {
            Complain($"Como mucho {MacroAction.MaxSteps} pasos.");
            return false;
        }

        foreach (var step in _steps)
        {
            if (step.Delay is not { } delay || delay < 0 || delay > MacroStep.MaxDelayMs)
            {
                Complain($"La espera de «{step.Summary}» tiene que ser un número entre 0 y {MacroStep.MaxDelayMs}.");
                return false;
            }
        }

        var macro = new MacroAction
        {
            ContinueOnError = ContinueBox.IsChecked == true,
            Steps = _steps
                .Select(step => new MacroStep { Action = step.Action, DelayMs = step.Delay!.Value })
                .ToList()
        };

        if (macro.Validate() is { } problem)
        {
            Complain(problem);
            return false;
        }

        Result = macro;

        Close();

        return true;
    }

    private void Complain(string message)
    {
        Problem.Text = message;
        Problem.IsVisible = true;
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
