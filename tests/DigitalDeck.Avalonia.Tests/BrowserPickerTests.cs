using Avalonia.Headless.XUnit;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model.Actions;
using Xunit;

namespace DigitalDeck.Avalonia.Tests;

/// <summary>
/// Elegir un dispositivo del browser de Live.
/// </summary>
/// <remarks>
/// El sentido de elegir en vez de escribir: un botón armado desde esta lista lleva un nombre que
/// Live dijo, así que carga. Uno escrito a mano falla recién cuando se lo aprieta, que es el peor
/// momento para enterarse.
/// </remarks>
public class BrowserPickerTests
{
    /// <summary>Un Live de mentira con un browser adentro.</summary>
    private sealed class FakeLive : IAbletonClient
    {
        private readonly Dictionary<string, AbletonBrowserEntry[]> _tree = new()
        {
            [""] =
            [
                new AbletonBrowserEntry("Dynamics", "query:Dynamics", IsLoadable: false, IsFolder: true, HasChildren: true),
                new AbletonBrowserEntry("Reverb", "query:Reverb", IsLoadable: true, IsFolder: false)
            ],
            ["Dynamics"] =
            [
                new AbletonBrowserEntry("Compressor", "query:Compressor", IsLoadable: true, IsFolder: false),
                new AbletonBrowserEntry("Limiter", "query:Limiter", IsLoadable: true, IsFolder: false)
            ]
        };

        public string? AskedPath { get; private set; }

        public string? AskedQuery { get; private set; }

        public AbletonStatus Status { get; } = new(AbletonConnectionState.Connected, "Live está conectado.");

        public event Action<AbletonStatus>? StatusChanged { add { } remove { } }

        public event Action<string>? ScriptFailed { add { } remove { } }

        public void Start() { }

        public Task<ActionResult> ExecuteAsync(AbletonAction action, CancellationToken ct = default) =>
            Task.FromResult(ActionResult.Ok());

        public Task<ActionResult> LoadDeviceAsync(AbletonLoadDeviceAction action, CancellationToken ct = default) =>
            Task.FromResult(ActionResult.Ok());

        public Task<AbletonBrowserListing> ListAsync(string category, string path, CancellationToken ct = default)
        {
            AskedPath = path;

            var entries = _tree.TryGetValue(path, out var found) ? found : [];

            return Task.FromResult(AbletonBrowserListing.Ok(category, path, entries, complete: true));
        }

        public Task<AbletonBrowserListing> SearchAsync(
            string category, string query, int limit = 100, CancellationToken ct = default)
        {
            AskedQuery = query;

            var hits = _tree.Values.SelectMany(e => e)
                .Where(e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return Task.FromResult(AbletonBrowserListing.Ok(category, query, hits, complete: true));
        }

        public Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(AbletonBrowserCategories.All);
    }

    private static async Task<(BrowserPickerWindow Window, FakeLive Live)> Open()
    {
        var live = new FakeLive();
        var window = new BrowserPickerWindow(live);

        window.Show();
        await window.RefreshAsync();

        return (window, live);
    }

    [AvaloniaFact]
    public async Task ItShowsWhatLiveHasAtTheTopOfTheSection()
    {
        var (window, _) = await Open();

        try
        {
            var rows = window.Entries.ItemsSource!.Cast<BrowserPickerWindow.Row>().ToList();

            Assert.Equal(["Dynamics", "Reverb"], rows.Select(r => r.Entry.Name));

            // La carpeta se marca distinto del dispositivo: una se abre, el otro se elige.
            Assert.Equal("▸", rows[0].Marker);
            Assert.Equal("•", rows[1].Marker);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task OpeningAFolderGoesInsideIt()
    {
        var (window, live) = await Open();

        try
        {
            var folder = window.Entries.ItemsSource!.Cast<BrowserPickerWindow.Row>().First().Entry;

            await window.ChooseAsync(folder);

            Assert.Equal("Dynamics", live.AskedPath);
            Assert.Equal(
                ["Compressor", "Limiter"],
                window.Entries.ItemsSource!.Cast<BrowserPickerWindow.Row>().Select(r => r.Entry.Name));

            // Y aparece la forma de volver.
            Assert.True(window.UpButton.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ChoosingADeviceKeepsItsUriAndNotJustItsName()
    {
        // Live resuelve un URI exacto, y cae al nombre sólo cuando no tiene. Un nombre solo es lo
        // que lleva un botón escrito a mano.
        var (window, _) = await Open();

        try
        {
            var device = window.Entries.ItemsSource!.Cast<BrowserPickerWindow.Row>()
                .First(r => r.Entry.Name == "Reverb").Entry;

            await window.ChooseAsync(device);

            Assert.NotNull(window.Chosen);
            Assert.Equal("Reverb", window.Chosen.Name);
            Assert.Equal("query:Reverb", window.Chosen.Uri);
            Assert.Equal(AbletonBrowserCategories.AudioEffects, window.Chosen.Category);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ClosingWithoutChoosingLeavesTheButtonAlone()
    {
        var (window, _) = await Open();

        Assert.Null(window.Chosen);

        window.Close();
    }

    [AvaloniaFact]
    public async Task WithLiveClosedTheListSaysSoInsteadOfLookingEmpty()
    {
        // Una lista vacía y una lista que no se pudo pedir se ven igual, y no son lo mismo.
        var window = new BrowserPickerWindow();

        try
        {
            window.Show();
            await window.RefreshAsync();

            Assert.True(window.Problem.IsVisible);
            Assert.Contains("Live", window.Problem.Text);
        }
        finally
        {
            window.Close();
        }
    }
}
