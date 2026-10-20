using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace DigitalDeck.App.Services;

public sealed record UpdateCheckResult(bool Available, string Message, string? DownloadUrl = null, string? Version = null);

public static class UpdateService
{
    private static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DigitalDeck", "update-source.json");

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var repository = ReadRepository();
        if (string.IsNullOrWhiteSpace(repository))
            return new(false, "Todavía no hay un repositorio de GitHub configurado. Cuando lo publiques, se puede guardar en " + ConfigPath);
        var parts = repository.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return new(false, "La dirección del repositorio de GitHub no es válida.");
        var owner = parts[^2]; var repo = parts[^1].Replace(".git", "", StringComparison.OrdinalIgnoreCase);
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DigitalDeck-Updater/1.0");
        using var response = await client.GetAsync($"https://api.github.com/repos/{owner}/{repo}/releases/latest", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return new(false, "El repositorio no tiene una release publicada todavía.");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var tag = json.RootElement.GetProperty("tag_name").GetString() ?? "";
        var url = json.RootElement.TryGetProperty("html_url", out var html) ? html.GetString() : null;
        return new(true, $"Hay una actualización disponible: {tag}", url, tag);
    }

    private static string? ReadRepository()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return null;
            using var json = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            return json.RootElement.GetProperty("repository").GetString();
        }
        catch { return null; }
    }
}
