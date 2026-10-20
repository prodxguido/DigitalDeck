using System.Text.Json.Serialization;

namespace DigitalDeck.Core.Model.Actions;

/// <summary>
/// Opens a folder, document or program with the Windows shell — the same thing a double-click in
/// Explorer does. The path is handed to the shell verbatim; it is never passed through a command
/// interpreter, so there is no argument string for an injected value to escape into.
/// </summary>
public sealed class OpenPathAction : ActionPayload
{
    /// <summary>Extensions that start a program rather than open a document.</summary>
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse",
        ".wsf", ".wsh", ".msi", ".msp", ".scr", ".cpl", ".reg", ".lnk", ".hta", ".jar"
    };

    public required string Path { get; set; }

    [JsonIgnore]
    public override string Summary => string.IsNullOrWhiteSpace(Path) ? "Ruta sin configurar" : Path;

    [JsonIgnore]
    public override bool IsPotentiallyDangerous => IsExecutable(Path);

    public override string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Path))
            return "La ruta está vacía.";

        if (Path.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
            return "La ruta contiene caracteres no válidos.";

        if (IsPotentiallyDangerous && !Acknowledged)
            return "Esta ruta ejecuta un programa. Confirmá la acción en el editor antes de usarla.";

        return null;
    }

    /// <summary>True when the path points at something that runs rather than opens.</summary>
    public static bool IsExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            return ExecutableExtensions.Contains(System.IO.Path.GetExtension(path));
        }
        catch (ArgumentException)
        {
            // Malformed path — treated as unsafe so it has to be acknowledged.
            return true;
        }
    }
}
