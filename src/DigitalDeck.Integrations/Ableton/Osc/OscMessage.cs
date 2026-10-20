using System.Globalization;

namespace DigitalDeck.Integrations.Ableton.Osc;

/// <summary>
/// One OSC message: an address and its arguments.
/// </summary>
/// <remarks>
/// Arguments are boxed <see cref="object"/> because that is genuinely what the wire carries — the
/// type tag decides, not the caller. The typed accessors exist so the rest of the code never has
/// to write a cast that could throw on a malformed reply from the script.
/// </remarks>
public sealed class OscMessage
{
    private static readonly object?[] NoArguments = Array.Empty<object?>();

    public OscMessage(string address, params object?[]? arguments)
    {
        if (string.IsNullOrEmpty(address) || address[0] != '/')
            throw new ArgumentException("An OSC address must start with '/'.", nameof(address));

        Address = address;
        Arguments = arguments ?? NoArguments;
    }

    public string Address { get; }

    public IReadOnlyList<object?> Arguments { get; }

    /// <summary>Argument at <paramref name="index"/> as a whole number, or the fallback.</summary>
    public int GetInt(int index, int fallback = 0) => Arguments.ElementAtOrDefault(index) switch
    {
        int value => value,
        float value => (int)value,
        double value => (int)value,
        bool value => value ? 1 : 0,
        _ => fallback
    };

    /// <summary>Argument at <paramref name="index"/> as a number, or the fallback.</summary>
    public double GetDouble(int index, double fallback = 0) => Arguments.ElementAtOrDefault(index) switch
    {
        float value => value,
        double value => value,
        int value => value,
        bool value => value ? 1 : 0,
        _ => fallback
    };

    /// <summary>Argument at <paramref name="index"/> as text, or null when it is not text.</summary>
    public string? GetString(int index) => Arguments.ElementAtOrDefault(index) as string;

    /// <summary>True when the argument reads as "on" — the script sends 1, T or 1.0 depending.</summary>
    public bool GetBool(int index, bool fallback = false) => Arguments.ElementAtOrDefault(index) switch
    {
        bool value => value,
        int value => value != 0,
        float value => value != 0,
        double value => value != 0,
        _ => fallback
    };

    public override string ToString()
    {
        if (Arguments.Count == 0) return Address;

        var rendered = Arguments.Select(Render);
        return $"{Address} {string.Join(' ', rendered)}";
    }

    private static string Render(object? argument) => argument switch
    {
        null => "nil",
        string text => $"\"{text}\"",
        byte[] blob => $"<{blob.Length} bytes>",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => argument.ToString() ?? "?"
    };
}
