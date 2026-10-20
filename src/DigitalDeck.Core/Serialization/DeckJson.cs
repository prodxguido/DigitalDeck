using System.Text.Json;
using System.Text.Json.Serialization;

namespace DigitalDeck.Core.Serialization;

/// <summary>
/// One set of JSON settings shared by the action payload column and the .deckpack export format,
/// so a payload written by one is always readable by the other.
/// </summary>
public static class DeckJson
{
    /// <summary>Compact form, used for the item <c>data</c> column.</summary>
    public static readonly JsonSerializerOptions Compact = Create(indented: false);

    /// <summary>Indented form, used inside exported packs so they stay diffable and inspectable.</summary>
    public static readonly JsonSerializerOptions Readable = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = indented,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };
}
