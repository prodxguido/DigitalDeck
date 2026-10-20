using System.Security.Cryptography;
using DigitalDeck.Core;

namespace DigitalDeck.Data;

/// <summary>
/// Custom button images, stored as files beside the database rather than as blobs inside it.
/// Filenames are content hashes, so importing the same image twice costs nothing and a pack that
/// reuses one icon across twenty buttons still ships it once.
/// </summary>
public sealed class IconStore
{
    private const long MaxBytes = 8L * 1024 * 1024;
    private const string RelativeRoot = "assets/icons";

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".webp"
    };

    private readonly DeckPaths _paths;

    public IconStore(DeckPaths paths)
    {
        _paths = paths;
        paths.EnsureCreated();
    }

    public static IReadOnlyCollection<string> SupportedExtensions => AllowedExtensions;

    /// <summary>File-picker filter covering everything <see cref="ImportAsync(string,CancellationToken)"/> accepts.</summary>
    public static string FileDialogFilter =>
        "Imágenes|" + string.Join(";", AllowedExtensions.Select(e => "*" + e));

    /// <summary>Copies an image into the store and returns the relative path to save on the item.</summary>
    public async Task<string> ImportAsync(string sourceFile, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(sourceFile);

        if (!AllowedExtensions.Contains(extension))
            throw new NotSupportedException($"'{extension}' no es un formato de imagen admitido.");

        var info = new FileInfo(sourceFile);

        if (!info.Exists)
            throw new FileNotFoundException("No se encontró la imagen.", sourceFile);

        if (info.Length > MaxBytes)
            throw new InvalidOperationException($"La imagen supera el límite de {MaxBytes / (1024 * 1024)} MB.");

        await using var stream = File.OpenRead(sourceFile);
        return await ImportAsync(stream, extension, ct).ConfigureAwait(false);
    }

    /// <summary>Same as above but from a stream, used when pulling icons out of a .deckpack.</summary>
    public async Task<string> ImportAsync(Stream source, string extension, CancellationToken ct = default)
    {
        if (!AllowedExtensions.Contains(extension))
            throw new NotSupportedException($"'{extension}' no es un formato de imagen admitido.");

        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, ct).ConfigureAwait(false);

        if (buffer.Length > MaxBytes)
            throw new InvalidOperationException($"La imagen supera el límite de {MaxBytes / (1024 * 1024)} MB.");

        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..32];
        var fileName = hash + extension.ToLowerInvariant();
        var absolute = Path.Combine(_paths.IconsDirectory, fileName);

        // Same content means same filename, so an existing file is already the right bytes.
        if (!File.Exists(absolute))
            await File.WriteAllBytesAsync(absolute, bytes, ct).ConfigureAwait(false);

        return $"{RelativeRoot}/{fileName}";
    }

    /// <summary>
    /// Writes an image the app produced itself, under a name the caller controls.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ImportAsync(Stream,string,CancellationToken)"/> because these are
    /// not content-addressed: a tile-ready square is named after the picture it was derived from
    /// and how it was framed, which is what lets the editor find it again later and re-frame it
    /// without asking the user for the original file a second time.
    /// </remarks>
    public async Task<string> SaveDerivedAsync(byte[] png, string name, CancellationToken ct = default)
    {
        var fileName = name + ".png";
        var absolute = Path.Combine(_paths.IconsDirectory, fileName);

        await File.WriteAllBytesAsync(absolute, png, ct).ConfigureAwait(false);

        return $"{RelativeRoot}/{fileName}";
    }

    /// <summary>
    /// The imported original a derived image came from, or null when it is gone.
    /// </summary>
    /// <remarks>
    /// The extension is unknown — the original could have been a .jpg or a .webp — so this looks
    /// for whatever file carries that hash. Deriving from the original rather than from the square
    /// is what keeps switching between fitted and filled from compounding one crop onto another.
    /// </remarks>
    public string? FindOriginal(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return null;

        foreach (var extension in AllowedExtensions)
        {
            var candidate = Path.Combine(_paths.IconsDirectory, hash + extension.ToLowerInvariant());
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>Absolute path for a stored icon, or null when the file is missing.</summary>
    public string? GetAbsolutePath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;

        var absolute = _paths.ResolveIcon(relativePath);
        return File.Exists(absolute) ? absolute : null;
    }
}
