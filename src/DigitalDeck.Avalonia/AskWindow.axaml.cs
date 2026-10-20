using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DigitalDeck.Avalonia;

/// <summary>
/// The small windows: saying something, asking yes or no, asking for a word.
/// </summary>
/// <remarks>
/// One window for the four because they are the same window with different buttons, and because
/// the alternative — leaving them as no-ops until each gets its own file — is the shape that
/// actually hurts: a deck that silently ignores "¿seguro que querés eliminarlo?" either deletes
/// nothing or deletes everything, and either way the person never saw the question.
/// </remarks>
public partial class AskWindow : Window
{
    /// <summary>True when the person pressed Aceptar rather than Cancelar or the close button.</summary>
    public bool Accepted { get; private set; }

    /// <summary>What they typed, when this was asking for a word.</summary>
    public string Text => Answer.Text ?? string.Empty;

    public AskWindow()
        : this("DigitalDeck", string.Empty)
    {
    }

    public AskWindow(string title, string message, string? initialValue = null, bool cancellable = false)
    {
        InitializeComponent();

        Title = title;
        Message.Text = message;

        if (initialValue is not null)
        {
            Answer.IsVisible = true;
            Answer.Text = initialValue;
        }

        CancelButton.IsVisible = cancellable || initialValue is not null;
    }

    public void Accept()
    {
        Accepted = true;
        Close();
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Accept();

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();
}
