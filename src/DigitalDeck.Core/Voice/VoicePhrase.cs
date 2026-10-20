using System.Globalization;
using System.Text;

namespace DigitalDeck.Core.Voice;

/// <summary>
/// Reduces a button's label and a recogniser's transcript to the same shape, so the two can be
/// compared at all.
/// </summary>
/// <remarks>
/// Every rule here exists because of a specific way the two disagree. A label is written to be
/// read — <c>Pro-Q 4</c>, <c>Compresión</c>, <c>EQ Eight</c> — while a transcript is written to be
/// spoken, so it arrives with the digits as words, the punctuation gone, and the accents present or
/// missing depending on the model. Normalising both sides is what turns those into one string.
/// </remarks>
public static class VoicePhrase
{
    /// <summary>
    /// The same words, for a recogniser that has to be told everything it is allowed to hear.
    /// </summary>
    /// <remarks>
    /// They have to be in the grammar even though the matcher throws them away. A restricted
    /// recogniser can only return words it was given, so a grammar of nothing but button names
    /// leaves "abrí" with nowhere to land — and rather than dropping just that word, the decoder
    /// can fail to place the whole sentence. The button was named perfectly and nothing happens.
    /// </remarks>
    public static IReadOnlyCollection<string> CommandWords => Openers;

    /// <summary>
    /// Words that can open a spoken command, stripped from the front so the rest can be matched.
    /// </summary>
    /// <remarks>
    /// Only from the front, and only as whole words. Stripping them anywhere would quietly break
    /// every button whose own name contains one — a folder called <c>Abrir sesión</c>, a preset
    /// called <c>Ir al puente</c>.
    /// </remarks>
    private static readonly HashSet<string> Openers = new(StringComparer.Ordinal)
    {
        "abri", "abrir", "abre", "abra",
        "poner", "pone", "pon", "poneme",
        "cargar", "carga", "cargame",
        "lanzar", "lanza", "lanzame",
        "ejecutar", "ejecuta",
        "meter", "mete", "meteme",
        "dale", "anda", "andar", "ir", "ve", "vamos",
        "quiero", "necesito", "dame",
        "el", "la", "los", "las", "un", "una", "unos", "unas", "al", "a"
    };

    private static readonly string[] Digits =
    [
        "cero", "uno", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve",
        "diez", "once", "doce", "trece", "catorce", "quince", "dieciseis", "diecisiete",
        "dieciocho", "diecinueve", "veinte", "veintiuno", "veintidos", "veintitres",
        "veinticuatro", "veinticinco", "veintiseis", "veintisiete", "veintiocho", "veintinueve",
        "treinta"
    ];

    private static readonly Dictionary<int, string> Tens = new()
    {
        [40] = "cuarenta", [50] = "cincuenta", [60] = "sesenta",
        [70] = "setenta", [80] = "ochenta", [90] = "noventa", [100] = "cien"
    };

    /// <summary>
    /// The comparable form of a label or a transcript: lower case, unaccented, punctuation gone,
    /// numbers spelled out.
    /// </summary>
    public static string Normalise(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var words = Tokenise(text);

        return string.Join(' ', words);
    }

    /// <summary>
    /// Same as <see cref="Normalise"/>, with any leading command words removed.
    /// </summary>
    /// <remarks>
    /// Never strips everything: someone whose button really is called <c>Abrir</c> should still be
    /// able to say its name, so a phrase made entirely of openers keeps its last word.
    /// </remarks>
    public static string NormaliseSpoken(string? text)
    {
        var words = Tokenise(text);

        var start = 0;

        while (start < words.Count - 1 && Openers.Contains(words[start]))
            start++;

        return string.Join(' ', words.Skip(start));
    }

    private static List<string> Tokenise(string? text)
    {
        var words = new List<string>();

        if (string.IsNullOrWhiteSpace(text)) return words;

        var stripped = StripAccents(text);
        var word = new StringBuilder();

        // Digits are split off from letters rather than treated as part of a word, because a label
        // writes them joined — "EQ8", "Pro-Q4" — and nobody says them joined.
        var wasDigit = false;

        foreach (var c in stripped)
        {
            var isLetter = char.IsAsciiLetter(c);
            var isDigit = char.IsAsciiDigit(c);

            if (!isLetter && !isDigit)
            {
                Flush(words, word);
                continue;
            }

            if (word.Length > 0 && isDigit != wasDigit) Flush(words, word);

            word.Append(char.ToLowerInvariant(c));
            wasDigit = isDigit;
        }

        Flush(words, word);

        return words;
    }

    private static void Flush(List<string> words, StringBuilder word)
    {
        if (word.Length == 0) return;

        var text = word.ToString();
        word.Clear();

        words.Add(char.IsAsciiDigit(text[0]) ? SpellNumber(text) : text);
    }

    /// <summary>
    /// A number as it would be said, or as it would be read out digit by digit when it is too long
    /// to be a quantity — a year, a serial number.
    /// </summary>
    private static string SpellNumber(string digits)
    {
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            return string.Join(' ', digits.Select(d => Digits[d - '0']));

        if (value < Digits.Length) return Digits[value];

        if (value < 100)
        {
            var tens = value / 10 * 10;
            var units = value % 10;

            if (Tens.TryGetValue(tens, out var name))
                return units == 0 ? name : $"{name} y {Digits[units]}";
        }

        if (value == 100) return Tens[100];

        // Past a hundred it stops being a plugin's version number and starts being a code, and
        // nobody says "mil novecientos ochenta y cuatro" to press a button called 1984.
        return string.Join(' ', digits.Select(d => Digits[d - '0']));
    }

    private static string StripAccents(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
