using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.Avalonia;

/// <summary>
/// Choosing a device, rack or preset out of Live's own browser.
/// </summary>
/// <remarks>
/// The point of picking rather than typing: a button configured from this list is a button whose
/// name Live has actually said, so it loads. A name typed by hand fails at the moment it is pressed,
/// which is the worst moment to find out.
/// <para>
/// Everything here goes over the bridge, so an answer needs Live open with the script running. When
/// it is not, the list says so instead of coming back empty and looking broken.
/// </para>
/// </remarks>
public partial class BrowserPickerWindow : Window
{
    /// <summary>One row, with the mark that says whether it opens or loads.</summary>
    public sealed record Row(AbletonBrowserEntry Entry)
    {
        public string Marker => Entry.IsFolder || Entry.HasChildren ? "▸" : "•";
    }

    private readonly IAbletonClient _live;

    private string _category = AbletonBrowserCategories.AudioEffects;
    private string _path = string.Empty;

    /// <summary>What was chosen, or null when the window was closed without choosing.</summary>
    public AbletonLoadDeviceAction? Chosen { get; private set; }

    public BrowserPickerWindow()
        : this(new NoLive())
    {
    }

    public BrowserPickerWindow(IAbletonClient live)
    {
        _live = live;

        InitializeComponent();

        CategoryBox.ItemsSource = AbletonBrowserCategories.All
            .Select(AbletonBrowserCategories.Label)
            .ToList();

        CategoryBox.SelectedIndex = AbletonBrowserCategories.All.ToList().IndexOf(_category);
    }

    /// <summary>Asks Live for what is at the current place and shows it. Public so a test can wait.</summary>
    public async Task RefreshAsync()
    {
        var query = (SearchBox.Text ?? string.Empty).Trim();

        var listing = query.Length > 0
            ? await _live.SearchAsync(_category, query).ConfigureAwait(true)
            : await _live.ListAsync(_category, _path).ConfigureAwait(true);

        Problem.Text = listing.Error ?? string.Empty;
        Problem.IsVisible = listing.Error is { Length: > 0 };

        // Rows that can be neither loaded nor opened are dead ends; the listing already leaves them
        // out, and what is left is what somebody can actually do something with.
        var rows = listing.Entries.Select(entry => new Row(entry)).ToList();

        Entries.ItemsSource = rows;
        Empty.IsVisible = rows.Count == 0 && !Problem.IsVisible;

        PathText.Text = _path.Length > 0 ? _path : AbletonBrowserCategories.Label(_category);
        UpButton.IsVisible = _path.Length > 0 && query.Length == 0;
    }

    private async void Category_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (CategoryBox.SelectedIndex < 0) return;

        _category = AbletonBrowserCategories.All[CategoryBox.SelectedIndex];
        _path = string.Empty;

        await RefreshAsync();
    }

    private async void Search_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        // Buscar desde la raíz de la sección: encontrar algo y quedar parado en otra carpeta sería
        // mentir sobre dónde está.
        _path = string.Empty;

        await RefreshAsync();
    }

    private async void Up_Click(object? sender, RoutedEventArgs e)
    {
        var slash = _path.LastIndexOf('/');

        _path = slash > 0 ? _path[..slash] : string.Empty;

        await RefreshAsync();
    }

    private async void Entry_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Row row }) return;

        await ChooseAsync(row.Entry);
    }

    /// <summary>Opens a folder, or takes a device and closes. Public so a test can press a row.</summary>
    public async Task ChooseAsync(AbletonBrowserEntry entry)
    {
        if (entry.IsFolder || entry.HasChildren)
        {
            _path = _path.Length > 0 ? $"{_path}/{entry.Name}" : entry.Name;

            SearchBox.Text = string.Empty;

            await RefreshAsync();
            return;
        }

        // The URI as well as the name: Live resolves a URI exactly, and falls back to the name only
        // when it has none. A name alone is what a button typed by hand carries.
        Chosen = new AbletonLoadDeviceAction
        {
            Name = entry.Name,
            Uri = entry.Uri is { Length: > 0 } uri ? uri : null,
            Category = _category
        };

        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    /// <summary>What the parameterless constructor gets. Never used at runtime.</summary>
    private sealed class NoLive : IAbletonClient
    {
        private const string Closed = "Live no está conectado.";

        public AbletonStatus Status { get; } = new(AbletonConnectionState.Disconnected, Closed);

        public event Action<AbletonStatus>? StatusChanged { add { } remove { } }

        public event Action<string>? ScriptFailed { add { } remove { } }

        public void Start() { }

        public Task<ActionResult> ExecuteAsync(AbletonAction action, CancellationToken ct = default) =>
            Task.FromResult(ActionResult.Failed(Closed));

        public Task<ActionResult> LoadDeviceAsync(AbletonLoadDeviceAction action, CancellationToken ct = default) =>
            Task.FromResult(ActionResult.Failed(Closed));

        public Task<AbletonBrowserListing> ListAsync(string category, string path, CancellationToken ct = default) =>
            Task.FromResult(AbletonBrowserListing.Failed(category, path, Closed));

        public Task<AbletonBrowserListing> SearchAsync(
            string category, string query, int limit = 100, CancellationToken ct = default) =>
            Task.FromResult(AbletonBrowserListing.Failed(category, query, Closed));

        public Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}
