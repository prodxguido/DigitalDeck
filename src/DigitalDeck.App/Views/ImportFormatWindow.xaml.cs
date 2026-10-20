using System.Windows;
using DigitalDeck.Data.Packs;

namespace DigitalDeck.App.Views;

/// <summary>
/// The one question a pack cannot answer for itself: page or group.
/// </summary>
/// <remarks>
/// Asked after the files are picked rather than before, so the question can say how many it is
/// about — and because the file picker is where somebody changes their mind and cancels.
/// </remarks>
public partial class ImportFormatWindow : Window
{
    public ImportFormatWindow(int packCount)
    {
        InitializeComponent();

        CountText.Text = packCount == 1
            ? "Un paquete puede ser una pantalla entera o una sola tecla que abre el resto. El archivo no lo dice, así que lo elegís vos."
            : $"Los {packCount} paquetes pueden ser pantallas enteras o teclas que abren el resto. El archivo no lo dice, así que lo elegís vos.";
    }

    /// <summary>What the packs should become. Only meaningful when the window was accepted.</summary>
    public PackImportTarget Target { get; private set; } = PackImportTarget.NewPage;

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        Target = AsGroupOption.IsChecked == true
            ? PackImportTarget.CurrentPage
            : PackImportTarget.NewPage;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
