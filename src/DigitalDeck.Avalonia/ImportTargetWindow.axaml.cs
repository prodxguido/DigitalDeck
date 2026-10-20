using Avalonia.Controls;
using Avalonia.Interactivity;
using DigitalDeck.Data.Packs;

namespace DigitalDeck.Avalonia;

/// <summary>
/// Where an imported pack lands: a page of its own, or a group on the page being looked at.
/// </summary>
/// <remarks>
/// Asked rather than assumed because both answers are right for different packs. A brand's shelf of
/// fifteen plugins is a screen; three buttons somebody sent over are a group. Cancelling answers
/// null, and the deck reads that as "do not import" — which is why the question is worth asking
/// even when there is only one pack.
/// </remarks>
public partial class ImportTargetWindow : Window
{
    /// <summary>What was chosen, or null when the window was closed without choosing.</summary>
    public PackImportTarget? Target { get; private set; }

    public ImportTargetWindow()
        : this(1)
    {
    }

    public ImportTargetWindow(int packCount)
    {
        InitializeComponent();

        Message.Text = packCount == 1
            ? "¿Dónde querés que entre el paquete?"
            : $"¿Dónde querés que entren los {packCount} paquetes?";
    }

    /// <summary>Takes the choice and closes. Public so a test can press Importar.</summary>
    public PackImportTarget Import()
    {
        var target = AsGroups.IsChecked == true ? PackImportTarget.CurrentPage : PackImportTarget.NewPage;

        Target = target;

        Close();

        return target;
    }

    private void Import_Click(object? sender, RoutedEventArgs e) => Import();

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
