using System.Windows;
using System.Windows.Input;

namespace DigitalDeck.App.Views;

public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string prompt, string? initialValue)
    {
        InitializeComponent();

        TitleText.Text = title;
        PromptText.Text = prompt;
        ValueBox.Text = initialValue ?? string.Empty;

        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value => ValueBox.Text.Trim();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void ValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }

    private void Accept_Click(object sender, RoutedEventArgs e) => DialogResult = Value.Length > 0;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
