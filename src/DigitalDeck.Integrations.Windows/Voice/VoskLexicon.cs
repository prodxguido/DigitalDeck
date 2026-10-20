using System.Globalization;
using System.Text;
using DigitalDeck.Core.Abstractions;

namespace DigitalDeck.Integrations.Voice;

/// <summary>
/// The word list a Vosk model can produce, read straight out of the model on disk.
/// </summary>
/// <remarks>
/// Vosk offers no way to ask "can you say this word", and the small models ship no <c>words.txt</c>
/// beside them — the list lives inside <c>graph/Gr.fst</c> as an OpenFst symbol table. That file is
/// a documented binary format with its own magic number, so reading it is a fixed-layout parse
/// rather than guesswork, and every field is checked before it is trusted.
/// <para>
/// The alternative was to turn Vosk's log level back up and scrape the warning it prints for each
/// word it drops. That would mean reading native stderr from a WPF app and re-parsing English prose
/// that is not part of anyone's contract; this reads the same list from the file that defines it.
/// </para>
/// </remarks>
public sealed class VoskLexicon : IVoiceLexicon
{
    /// <summary>OpenFst's file header magic, as written by <c>Fst::Write</c>.</summary>
    private const int FstMagic = 2125659606;

    /// <summary>The magic that opens an embedded symbol table.</summary>
    private const int SymbolTableMagic = 2125658996;

    /// <summary>Header flag saying an input symbol table follows the header.</summary>
    private const int HasInputSymbols = 1;

    /// <summary>
    /// Refuses a symbol table larger than any real model would ship, so a misread length cannot
    /// turn into an allocation big enough to matter.
    /// </summary>
    private const long MaxSymbols = 5_000_000;

    private readonly HashSet<string> _words;
    private readonly HashSet<string> _unaccented;

    private VoskLexicon(HashSet<string> words, HashSet<string> unaccented)
    {
        _words = words;
        _unaccented = unaccented;
    }

    public int Count => _words.Count;

    public bool Knows(string word) =>
        !string.IsNullOrEmpty(word) && _words.Contains(word);

    public bool KnowsIgnoringAccents(string word) =>
        !string.IsNullOrEmpty(word) && _unaccented.Contains(word);

    /// <summary>
    /// Reads the vocabulary of the model at <paramref name="modelPath"/>, or null when it cannot be
    /// read.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception on every failure, including a corrupt or unexpected file. This
    /// backs a hint in a settings screen; a model whose layout has moved on should cost the hint,
    /// never the assistant itself.
    /// </remarks>
    public static VoskLexicon? TryLoad(string modelPath)
    {
        try
        {
            var graph = Path.Combine(modelPath, "graph", "Gr.fst");

            if (!File.Exists(graph)) return null;

            using var stream = File.OpenRead(graph);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            if (!SkipToSymbolTable(reader)) return null;

            return ReadSymbols(reader);
        }
        catch (Exception)
        {
            // A truncated file, a layout that moved, a model on a drive that went away. The hint is
            // not worth a crash.
            return null;
        }
    }

    /// <summary>
    /// Walks the FST header and stops just before the symbol table, or returns false when the file
    /// is not an FST or carries no symbols.
    /// </summary>
    /// <remarks>
    /// The header is a fixed sequence: magic, the two type names, version, flags, then five counts.
    /// It has to be walked rather than skipped by a constant, because the two type names are
    /// length-prefixed strings whose size differs between models.
    /// </remarks>
    private static bool SkipToSymbolTable(BinaryReader reader)
    {
        if (reader.ReadInt32() != FstMagic) return false;

        ReadString(reader);             // fst type, e.g. "ngram"
        ReadString(reader);             // arc type, e.g. "standard"

        reader.ReadInt32();             // version
        var flags = reader.ReadInt32();

        reader.ReadInt64();             // properties
        reader.ReadInt64();             // start state
        reader.ReadInt64();             // number of states
        reader.ReadInt64();             // number of arcs

        return (flags & HasInputSymbols) != 0;
    }

    private static VoskLexicon? ReadSymbols(BinaryReader reader)
    {
        if (reader.ReadInt32() != SymbolTableMagic) return null;

        ReadString(reader);             // the table's own name, e.g. "words.txt"

        reader.ReadInt64();             // available key
        var size = reader.ReadInt64();

        if (size < 0 || size > MaxSymbols) return null;

        var words = new HashSet<string>(StringComparer.Ordinal);
        var unaccented = new HashSet<string>(StringComparer.Ordinal);

        for (long i = 0; i < size; i++)
        {
            var symbol = ReadString(reader);

            reader.ReadInt64();         // the symbol's id, which nothing here needs

            // Vosk's own markers: the empty label, silence, and the catch-all for "not in the
            // grammar". None of them is a word anybody can name a button after.
            if (symbol.Length == 0 || symbol[0] is '<' or '[' or '!') continue;

            var lower = symbol.ToLowerInvariant();

            words.Add(lower);
            unaccented.Add(StripAccents(lower));
        }

        return words.Count == 0 ? null : new VoskLexicon(words, unaccented);
    }

    /// <summary>Reads OpenFst's length-prefixed string.</summary>
    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();

        if (length is < 0 or > 4096) throw new InvalidDataException($"Largo de cadena inesperado: {length}.");

        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    /// <summary>
    /// The same accent-stripping the spoken form goes through, so the two can be compared.
    /// </summary>
    /// <remarks>
    /// Duplicated from <c>VoicePhrase</c> rather than shared, because this assembly must not depend
    /// on it: the lexicon is about what a file contains, not about how the deck says things.
    /// </remarks>
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
