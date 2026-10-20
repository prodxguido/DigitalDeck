using System.Text.Json.Serialization;

namespace DigitalDeck.Core.Model.Actions;

/// <summary>
/// Opens a web address in the default browser. Only http and https are accepted — without that
/// check a malformed or hostile value could trigger an unrelated protocol handler.
/// </summary>
public sealed class OpenUrlAction : ActionPayload
{
    public required string Url { get; set; }

    [JsonIgnore]
    public override string Summary => string.IsNullOrWhiteSpace(Url) ? "URL sin configurar" : Url;

    public override string? Validate() =>
        TryParse(Url, out _) ? null : "La URL debe empezar con http:// o https://.";

    /// <summary>Parses and enforces the http/https restriction.</summary>
    public static bool TryParse(string? url, out Uri? uri)
    {
        uri = null;

        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;

        uri = parsed;
        return true;
    }

    /// <summary>Adds an https prefix to input like "youtube.com" so the editor is forgiving.</summary>
    public static string Normalise(string input)
    {
        var trimmed = input.Trim();
        if (trimmed.Length == 0) return trimmed;

        return trimmed.Contains("://", StringComparison.Ordinal) ? trimmed : "https://" + trimmed;
    }
}
