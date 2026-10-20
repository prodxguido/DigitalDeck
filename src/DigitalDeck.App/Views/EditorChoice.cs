using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DigitalDeck.App.Views;

/// <summary>
/// One selectable option in a list of chips.
/// </summary>
/// <remarks>
/// Exists so the editor can set the selection by assigning to a model object instead of digging a
/// generated <c>RadioButton</c> out of an <c>ItemsControl</c> — which is unreliable, because the
/// containers do not exist until the panel has laid out, and the editor needs to restore a saved
/// selection the moment it opens.
/// </remarks>
public sealed class EditorChoice<T> : INotifyPropertyChanged
{
    private bool _isSelected;

    public EditorChoice(T value, string label)
    {
        Value = value;
        Label = label;
    }

    public T Value { get; }

    public string Label { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
