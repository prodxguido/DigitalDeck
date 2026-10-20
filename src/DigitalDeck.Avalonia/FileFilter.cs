using Avalonia.Platform.Storage;

namespace DigitalDeck.Avalonia;

/// <summary>
/// Reads the file filter the shared code passes around, which is written the way Windows writes it.
/// </summary>
/// <remarks>
/// <c>"Paquete de DigitalDeck|*.deckpack"</c> — name, bar, patterns — is the shape
/// <c>IDeckDialogs</c> has always carried, because it is the shape the Windows dialog takes. Rather
/// than change that interface and every caller, it is parsed here into what Avalonia wants. The
/// string is a description of a filter, not a Windows API call, and it reads the same on a Mac.
/// <para>
/// A filter that cannot be read becomes no filter at all: showing every file is a worse dialog,
/// but a dialog that shows nothing is a broken one.
/// </para>
/// </remarks>
public static class FileFilter
{
    /// <summary>The file types in a filter, in the order they were written.</summary>
    public static IReadOnlyList<FilePickerFileType> Parse(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return [];

        var parts = filter.Split('|', StringSplitOptions.TrimEntries);
        var types = new List<FilePickerFileType>();

        // Name, patterns, name, patterns… An odd trailing piece is a filter somebody mistyped, and
        // the rest of it is still worth honouring.
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            var name = parts[i];
            var patterns = parts[i + 1]
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (name.Length == 0 || patterns.Count == 0) continue;

            types.Add(new FilePickerFileType(name)
            {
                Patterns = patterns,

                // macOS filters by extension rather than by glob, so it needs them without the star.
                AppleUniformTypeIdentifiers = null,
                MimeTypes = null
            });
        }

        return types;
    }

    /// <summary>The extension a save dialog should default to, or null when the filter has none.</summary>
    /// <remarks>
    /// Without this a file saved on a Mac comes out with no extension at all, and a
    /// <c>.deckpack</c> with no <c>.deckpack</c> on the end is a file the app will not offer to
    /// open again.
    /// </remarks>
    public static string? DefaultExtension(string? filter)
    {
        var pattern = Parse(filter).FirstOrDefault()?.Patterns?.FirstOrDefault();

        if (pattern is null) return null;

        var dot = pattern.LastIndexOf('.');

        return dot >= 0 && dot < pattern.Length - 1 ? pattern[(dot + 1)..] : null;
    }
}
