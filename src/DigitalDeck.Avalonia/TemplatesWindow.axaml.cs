using Avalonia.Controls;
using Avalonia.Interactivity;
using DigitalDeck.Data.Packs;
using DigitalDeck.ViewModels;

namespace DigitalDeck.Avalonia;

/// <summary>
/// Choosing which of the bundled templates to install, and where they land.
/// </summary>
/// <remarks>
/// The two destinations are the same two the WPF window offers, and the default is the same: a page
/// each. A shelf of fifteen plugins really is a screen rather than a key, and the other option is
/// there for somebody building one page out of the three brands they actually use.
/// </remarks>
public partial class TemplatesWindow : Window
{
    private readonly IReadOnlyList<TemplateChoice> _choices;

    /// <summary>What the person chose. Empty stands for a cancelled dialog.</summary>
    public TemplateInstall Result { get; private set; } = new([], PackImportTarget.NewPage);

    public TemplatesWindow()
        : this([])
    {
    }

    public TemplatesWindow(IReadOnlyList<DeckTemplate> templates)
    {
        InitializeComponent();

        _choices = templates.Select(template => new TemplateChoice(template)).ToList();

        List.ItemsSource = _choices;
    }

    /// <summary>Takes what is ticked and closes. Nothing ticked answers as a cancel.</summary>
    public TemplateInstall Install()
    {
        var chosen = _choices.Where(choice => choice.IsSelected).Select(choice => choice.Template).ToList();

        Result = new TemplateInstall(
            chosen,
            AsGroups.IsChecked == true ? PackImportTarget.CurrentPage : PackImportTarget.NewPage);

        Close();

        return Result;
    }

    private void Install_Click(object? sender, RoutedEventArgs e) => Install();

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
