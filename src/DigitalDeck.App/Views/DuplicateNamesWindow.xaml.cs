using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Services;
using DigitalDeck.Core.Voice;

namespace DigitalDeck.App.Views;

/// <summary>
/// Lists the buttons the voice assistant cannot tell apart, and lets one of each pair be renamed
/// out of the tie.
/// </summary>
/// <remarks>
/// The favourites folder and an imported pack both let duplicate names in on purpose — this is
/// where they get cleaned up afterwards. A rename is refused only when it would land on another
/// name that already exists, so it can never trade one collision for a different one.
/// </remarks>
public partial class DuplicateNamesWindow : Window
{
    private readonly IDeckRepository _repository;
    private readonly string _profileId;

    private readonly ObservableCollection<CollisionRow> _rows = new();

    public DuplicateNamesWindow(IDeckRepository repository, string profileId)
    {
        _repository = repository;
        _profileId = profileId;

        InitializeComponent();

        CollisionList.ItemsSource = _rows;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        ErrorHint.Text = string.Empty;
        _rows.Clear();

        var items = await _repository.GetProfileItemsAsync(_profileId);
        var pages = await _repository.GetPagesAsync(_profileId);

        var hits = DeckSearch.All(items, pages);

        foreach (var collision in NameCollisions.Find(hits))
        {
            var tiles = collision.Tiles
                .Select((hit, index) => new TileRow(hit.Item, hit.Location, isWinner: index == 0))
                .ToList();

            _rows.Add(new CollisionRow(collision.Name, tiles));
        }

        EmptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TileRow row }) return;

        var wanted = row.NewName.Trim();

        if (wanted.Length == 0)
        {
            ErrorHint.Text = "El nombre no puede quedar vacío.";
            return;
        }

        var spoken = VoicePhrase.Normalise(wanted);

        var items = await _repository.GetProfileItemsAsync(_profileId);

        // The rename is only worth doing if it breaks the tie without making another one, so it is
        // refused whenever some other tile already answers to the new spoken form.
        var clashes = items.Any(item =>
            item.Id != row.Item.Id
            && VoicePhrase.Normalise(item.Label) == spoken
            && spoken.Length > 0);

        if (clashes)
        {
            ErrorHint.Text = $"«{wanted}» ya lo usa otro botón. Probá otro nombre.";
            return;
        }

        row.Item.Label = wanted;
        await _repository.UpdateItemAsync(row.Item);

        await ReloadAsync();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record CollisionRow(string Name, IReadOnlyList<TileRow> Tiles);

    private sealed class TileRow : INotifyPropertyChanged
    {
        public TileRow(DeckItem item, string location, bool isWinner)
        {
            Item = item;
            Location = location;
            IsWinner = isWinner;
            _newName = item.Label;
        }

        public DeckItem Item { get; }

        public string Location { get; }

        public bool IsWinner { get; }

        public Visibility IsWinnerVisibility => IsWinner ? Visibility.Visible : Visibility.Collapsed;

        private string _newName;

        public string NewName
        {
            get => _newName;
            set
            {
                if (_newName == value) return;
                _newName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NewName)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
