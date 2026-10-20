using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Integrations.Ableton;
using DigitalDeck.Integrations.Ableton.Osc;
using Xunit;
using Xunit.Abstractions;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// Checks the plugin names in the shipped templates against the browser of a real Live.
/// </summary>
/// <remarks>
/// A template button reaches a plugin by name, so a template is only as good as whether those names
/// are the ones this machine's Live actually lists — and that is not something any amount of unit
/// testing can answer. This asks Live directly, and prints what it finds.
/// <para>
/// It reports rather than judges, on purpose. Which bundles a machine owns is not a property of the
/// repository: a missing plugin is far more likely to mean "not installed here" than "the template
/// is wrong", and a run that failed for the first would drown out the second. What it does assert
/// is the part that is genuinely invariant — that Live answered at all. Read the output; a name
/// listed as missing next to a near-miss that differs only in a version number is a template to fix.
/// </para>
/// <code>
/// $env:DIGITALDECK_LIVE = "1"
/// dotnet test --filter FullyQualifiedName~TemplateNames --logger "console;verbosity=detailed"
/// </code>
/// </remarks>
public sealed class TemplateNamesLiveTests : IDisposable
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(5);

    private readonly ITestOutputHelper _output;
    private readonly UdpOscTransport _transport = new();
    private readonly AbletonAdapter _adapter;

    public TemplateNamesLiveTests(ITestOutputHelper output)
    {
        _output = output;
        _adapter = new AbletonAdapter(_transport, AbletonAdapter.ManualHeartbeat);
    }

    public void Dispose() => _adapter.Dispose();

    [LiveFact]
    public async Task EveryTemplateNameIsCheckedAgainstTheBrowser()
    {
        await ConnectAsync();

        var templates = Directory
            .GetFiles(TemplatesFolder(), "*.deckpack")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.NotEmpty(templates);

        var answered = false;

        foreach (var template in templates)
        {
            var names = PluginNames(template);
            var missing = new List<(string Name, string Nearest)>();

            foreach (var (name, category) in names)
            {
                // Searched in the section the button actually declares: the plugin packs say
                // «plugins», and the factory-device pack says audio_effects, midi_effects or
                // max_for_live. Looking for «Reverb» among the plugins would report every stock
                // device as missing.
                var listing = await _adapter.SearchAsync(category, name);

                // Live answering is the invariant. Whether it answers with a match is a fact about
                // this machine's plugin folder, which the repository has no business asserting.
                if (!listing.Success) continue;

                answered = true;

                if (listing.Entries.Any(entry =>
                        string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var nearest = listing.Entries
                    .Where(entry => entry.IsLoadable)
                    .Select(entry => entry.Name)
                    .FirstOrDefault();

                missing.Add((name, nearest ?? "—"));
            }

            var found = names.Count - missing.Count;
            _output.WriteLine($"{Path.GetFileName(template),-22} {found,3}/{names.Count,-3} encontrados");

            foreach (var (name, nearest) in missing)
                _output.WriteLine($"    falta: {name,-34} lo más parecido: {nearest}");

            if (missing.Count == names.Count && names.Count > 0)
                _output.WriteLine("    (ninguno: este bundle no parece estar instalado en esta máquina)");

            _output.WriteLine("");
        }

        Assert.True(answered, "Live no contestó ninguna búsqueda del browser.");
    }

    /// <summary>Every name a template's buttons ask Live for, with the browser section to ask in.</summary>
    private static IReadOnlyList<(string Name, string Category)> PluginNames(string packPath)
    {
        using var archive = ZipFile.OpenRead(packPath);

        var entry = archive.GetEntry("manifest.json");
        Assert.NotNull(entry);

        using var stream = entry.Open();
        using var document = JsonDocument.Parse(stream);

        var names = new List<(string Name, string Category)>();

        foreach (var page in document.RootElement.GetProperty("profile").GetProperty("pages").EnumerateArray())
            Collect(page.GetProperty("items"), names);

        return names;
    }

    private static void Collect(JsonElement items, List<(string Name, string Category)> names)
    {
        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("action", out var action)
                && action.ValueKind is JsonValueKind.Object
                && action.TryGetProperty("name", out var name)
                && name.GetString() is { Length: > 0 } text)
            {
                var category = action.TryGetProperty("category", out var section)
                    ? section.GetString()
                    : null;

                names.Add((text, string.IsNullOrWhiteSpace(category) ? "plugins" : category));
            }

            if (item.TryGetProperty("children", out var children))
                Collect(children, names);
        }
    }

    /// <summary>Walks up from the test binary to the templates folder in the repository.</summary>
    private static string TemplatesFolder()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "plantillas");
            if (Directory.Exists(candidate)) return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("No se encontró la carpeta «plantillas».");
    }

    private async Task ConnectAsync()
    {
        _adapter.Start();

        Assert.True(_transport.IsOpen,
            _transport.LastError ?? $"No se pudo abrir el puerto UDP {UdpOscTransport.DefaultReceivePort}.");

        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < HandshakeTimeout)
        {
            if (_adapter.Status.State == AbletonConnectionState.Connected) return;

            await Task.Delay(100);
            _adapter.Tick();
        }

        Assert.Fail($"El Remote Script no contestó en {HandshakeTimeout.TotalSeconds:0} segundos.");
    }
}
