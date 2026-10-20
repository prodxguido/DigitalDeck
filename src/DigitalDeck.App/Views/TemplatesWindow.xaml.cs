using System.Windows;
using DigitalDeck.ViewModels;
using DigitalDeck.Data.Packs;

namespace DigitalDeck.App.Views;

/// <summary>
/// The list of templates that ship inside the app, with a tick beside each one and one question
/// underneath: page or group.
/// </summary>
/// <remarks>
/// A picker and nothing more: it hands back what was ticked and the answer to the question, and the
/// view model does the importing, because a page belongs to a profile and a group belongs to
/// whichever folder the deck is currently showing — neither of which is a dialog's business.
/// </remarks>
public partial class TemplatesWindow : Window
{
    private readonly List<TemplateChoice> _choices;

    public TemplatesWindow(IReadOnlyList<DeckTemplate> templates)
    {
        InitializeComponent();

        _choices = templates.Select(template => new TemplateChoice(template)).ToList();
        TemplateList.ItemsSource = _choices;

        if (_choices.Count == 0)
        {
            EmptyText.Visibility = Visibility.Visible;
            InstallButton.IsEnabled = false;
        }
    }

    /// <summary>What the user ticked. Empty when the window was cancelled.</summary>
    public IReadOnlyList<DeckTemplate> Chosen { get; private set; } = Array.Empty<DeckTemplate>();

    /// <summary>
    /// What the ticked ones should become.
    /// </summary>
    /// <remarks>
    /// A page to start with. These are shelves of a dozen or more plugins, and that is a screen
    /// rather than something to file behind a press — the other answer is there for the person
    /// building one page out of the three brands they actually use.
    /// </remarks>
    public PackImportTarget Target { get; private set; } = PackImportTarget.NewPage;

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        // Both ways round from one button: with nine templates on the list, ticking them all and
        // then changing your mind should not be nine more clicks.
        var turningOn = _choices.Any(choice => !choice.IsSelected);

        foreach (var choice in _choices) choice.IsSelected = turningOn;
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        Chosen = _choices.Where(choice => choice.IsSelected).Select(choice => choice.Template).ToList();

        if (Chosen.Count == 0) return;

        Target = AsGroupOption.IsChecked == true
            ? PackImportTarget.CurrentPage
            : PackImportTarget.NewPage;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
