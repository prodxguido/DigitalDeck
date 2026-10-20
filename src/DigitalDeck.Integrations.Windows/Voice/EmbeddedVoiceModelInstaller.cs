using System.IO.Compression;
using System.Reflection;
using DigitalDeck.Core.Abstractions;

namespace DigitalDeck.Integrations.Voice;

/// <inheritdoc cref="IVoiceModelInstaller"/>
public sealed class EmbeddedVoiceModelInstaller : IVoiceModelInstaller
{
    /// <summary>Prefix the .csproj gives the embedded model archive. Matched exactly here.</summary>
    private const string ResourcePrefix = "VoiceModel/";

    private readonly Assembly _assembly;
    private readonly string? _resourceName;

    public EmbeddedVoiceModelInstaller()
    {
        _assembly = typeof(EmbeddedVoiceModelInstaller).Assembly;
        _resourceName = _assembly.GetManifestResourceNames()
            .FirstOrDefault(name =>
                name.StartsWith(ResourcePrefix, StringComparison.Ordinal) &&
                name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    public bool HasBundle => _resourceName is not null;

    public string BundledModelName => _resourceName is null
        ? "modelo de voz"
        : Path.GetFileNameWithoutExtension(_resourceName[ResourcePrefix.Length..]);

    public bool IsInstalled(string voiceDirectory) =>
        VoskRecogniser.FindModel(voiceDirectory) is not null;

    public VoiceModelInstallResult Install(string voiceDirectory, IProgress<double>? progress = null)
    {
        if (_resourceName is null)
            return new VoiceModelInstallResult(false, "Esta copia de la app no trae el modelo de voz adentro.");

        var destination = Path.Combine(voiceDirectory, BundledModelName);

        // Unpacked next door and moved in one step, so a crash mid-unpack never leaves a folder
        // that looks installed — FindModel keys off the marker file, and a truncated copy could
        // still have written it before dying.
        var staging = Path.Combine(voiceDirectory, "." + BundledModelName + "-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(voiceDirectory);

            using (var stream = _assembly.GetManifestResourceStream(_resourceName))
            {
                if (stream is null)
                    return new VoiceModelInstallResult(false, "No pude abrir el modelo de voz que viene con la app.");

                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

                var total = archive.Entries.Sum(entry => entry.Length);
                long done = 0;

                foreach (var entry in archive.Entries)
                {
                    // Windows PowerShell's Compress-Archive writes '\' as the separator, .NET's
                    // ZipFile writes '/'. Normalise so both kinds of bundle unpack the same way.
                    var full = entry.FullName.Replace('\\', '/');

                    var isDirectory = full.EndsWith('/');

                    // The archive stores everything under a top folder named after the model; drop
                    // it so the model lands at <voice>/<model>, not <voice>/<model>/<model>.
                    var relative = StripLeadingFolder(full);

                    if (string.IsNullOrEmpty(relative)) continue;

                    var target = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));

                    if (isDirectory)
                    {
                        Directory.CreateDirectory(target);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);

                    done += entry.Length;
                    if (total > 0) progress?.Report((double)done / total);
                }
            }

            if (!VoskRecogniser.IsModel(staging) && VoskRecogniser.FindModel(staging) is null)
                return new VoiceModelInstallResult(false, "El modelo que venía adentro de la app salió incompleto.");

            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            Directory.Move(staging, destination);

            progress?.Report(1);

            return new VoiceModelInstallResult(true, $"Instalé el asistente de voz ({BundledModelName}).", destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or ArgumentException or System.Security.SecurityException)
        {
            return new VoiceModelInstallResult(false, $"No pude instalar el modelo de voz: {ex.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Drops the first path segment: <c>model/am/final.mdl</c> becomes <c>am/final.mdl</c>.</summary>
    private static string StripLeadingFolder(string path)
    {
        var slash = path.IndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }
}
