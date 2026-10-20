using CommunityToolkit.Mvvm.ComponentModel;
using DigitalDeck.Data.Packs;

namespace DigitalDeck.ViewModels;

/// <summary>One template in the install list, with the tick beside it.</summary>
public sealed class TemplateChoice : ObservableObject
{
    private bool _isSelected;

    public TemplateChoice(DeckTemplate template) => Template = template;

    public DeckTemplate Template { get; }

    public string Name => Template.Name;

    public string Summary => Template.Summary;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
