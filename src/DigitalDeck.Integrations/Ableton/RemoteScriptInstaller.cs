using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using DigitalDeck.Core.Abstractions;
using IoPath = System.IO.Path;

namespace DigitalDeck.Integrations.Ableton;

/// <inheritdoc cref="IRemoteScriptInstaller"/>
public sealed class RemoteScriptInstaller : IRemoteScriptInstaller
{
    /// <summary>Prefix every embedded script file carries. Set by the .csproj, not by convention.</summary>
    private const string ResourcePrefix = "RemoteScript/";

    /// <summary>The folder name Ableton shows in its Control Surface dropdown.</summary>
    public const string ScriptFolderName = "DigitalDeck";

    /// <summary>Where Ableton keeps the scripts it loads, relative to the User Library.</summary>
    public const string RemoteScriptsFolder = "Remote Scripts";

    private static readonly Regex VersionLine =
        new(@"SCRIPT_VERSION\s*=\s*[""'](?<version>[^""']+)[""']", RegexOptions.Compiled);

    private static readonly Regex ReceiveLine =
        new(@"^RECEIVE_PORT\s*=\s*(?<port>\d+)", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex SendLine =
        new(@"^SEND_PORT\s*=\s*(?<port>\d+)", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>The file the ports live in, rewritten on the way in.</summary>
    private const string ConfigFile = "config.py";

    private readonly Assembly _assembly;
    private readonly Lazy<IReadOnlyList<string>> _userLibraries;
    private readonly Func<bool> _liveIsRunning;

    public RemoteScriptInstaller(Func<bool>? liveIsRunning = null, IEnumerable<string>? userLibraries = null)
    {
        _assembly = typeof(RemoteScriptInstaller).Assembly;
        _liveIsRunning = liveIsRunning ?? DetectLive;
        _userLibraries = new Lazy<IReadOnlyList<string>>(
            () => userLibraries?.ToList() ?? FindUserLibraries());
    }

    /// <summary>Every script file carried inside the app, keyed by its path under the script folder.</summary>
    public IReadOnlyDictionary<string, string> BundledFiles => ReadBundle();

    public string BundledVersion
    {
        get
        {
            var bundle = ReadBundle();

            if (!bundle.TryGetValue("version.py", out var source))
                return "desconocida";

            var match = VersionLine.Match(source);
            return match.Success ? match.Groups["version"].Value : "desconocida";
        }
    }

    public IReadOnlyList<string> UserLibraries => _userLibraries.Value;

    /// <summary>The pair the bundled script would use if nothing overrode it.</summary>
    public AbletonPorts BundledPorts
    {
        get
        {
            if (!ReadBundle().TryGetValue(ConfigFile, out var source)) return AbletonPorts.Default;

            var receive = ReceiveLine.Match(source);
            var send = SendLine.Match(source);

            return receive.Success && send.Success
                ? new AbletonPorts(
                    int.Parse(receive.Groups["port"].Value),
                    int.Parse(send.Groups["port"].Value))
                : AbletonPorts.Default;
        }
    }

    public bool IsLiveRunning => _liveIsRunning();

    // ---------- Looking ----------

    public RemoteScriptState Inspect(string? userLibrary = null)
    {
        var library = userLibrary ?? UserLibraries.FirstOrDefault();

        if (library is null)
        {
            return new RemoteScriptState(
                RemoteScriptStatus.Unknown, null, BundledVersion, null,
                Problem: "No encontré la User Library de Ableton. Si Live está instalado, elegí la carpeta a mano.");
        }

        var target = TargetFolder(library);

        try
        {
            var versionFile = IoPath.Combine(target, "version.py");

            if (!File.Exists(versionFile))
                return new RemoteScriptState(RemoteScriptStatus.NotInstalled, null, BundledVersion, target);

            var match = VersionLine.Match(File.ReadAllText(versionFile));
            var installed = match.Success ? match.Groups["version"].Value : null;

            var status = string.Equals(installed, BundledVersion, StringComparison.Ordinal)
                ? RemoteScriptStatus.Installed
                : RemoteScriptStatus.Outdated;

            return new RemoteScriptState(status, installed, BundledVersion, target, ReadPorts(target));
        }
        catch (Exception ex) when (IsPathProblem(ex))
        {
            return new RemoteScriptState(
                RemoteScriptStatus.Unknown, null, BundledVersion, target,
                Problem: $"No pude leer {target}: {ex.Message}");
        }
    }

    // ---------- Installing ----------

    public RemoteScriptInstallResult Install(string? userLibrary = null, AbletonPorts? ports = null)
    {
        var library = userLibrary ?? UserLibraries.FirstOrDefault();

        if (library is null)
            return new RemoteScriptInstallResult(false, "No sé dónde está la User Library de Ableton.");

        var target = TargetFolder(library);
        var bundle = ReadBundle();

        if (bundle.Count == 0)
            return new RemoteScriptInstallResult(false, "Esta copia de la app no trae el Remote Script adentro.");

        try
        {
            // Replacing rather than merging: a file left behind by an older version is a module
            // Live would still import, and the failure it causes points nowhere near the cause.
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);

            foreach (var (relative, source) in bundle)
            {
                var destination = IoPath.Combine(target, relative.Replace('/', IoPath.DirectorySeparatorChar));
                var folder = IoPath.GetDirectoryName(destination);

                if (folder is not null) Directory.CreateDirectory(folder);

                var contents = relative == ConfigFile && ports is { } chosen
                    ? WithPorts(source, chosen)
                    : source;

                File.WriteAllText(destination, contents);
            }
        }
        catch (Exception ex) when (IsPathProblem(ex))
        {
            return new RemoteScriptInstallResult(false, $"No pude escribir en {target}: {ex.Message}");
        }

        var where = ports is { } pair ? $" en los puertos {pair}" : string.Empty;

        return new RemoteScriptInstallResult(
            true,
            $"Copié {bundle.Count} archivos del Remote Script {BundledVersion}{where}.",
            target);
    }

    // ---------- Uninstalling ----------

    public RemoteScriptInstallResult Uninstall(string? userLibrary = null)
    {
        var library = userLibrary ?? UserLibraries.FirstOrDefault();

        if (library is null)
            return new RemoteScriptInstallResult(false, "No sé dónde está la User Library de Ableton.");

        var target = TargetFolder(library);

        if (!Directory.Exists(target))
            return new RemoteScriptInstallResult(true, "El script ya no estaba instalado.", target);

        try
        {
            Directory.Delete(target, recursive: true);
        }
        catch (Exception ex) when (IsPathProblem(ex))
        {
            return new RemoteScriptInstallResult(false, $"No pude borrar {target}: {ex.Message}");
        }

        return new RemoteScriptInstallResult(true, "Desinstalé el Remote Script.", target);
    }

    /// <summary>The bundled config with its two numbers swapped for the chosen pair.</summary>
    private static string WithPorts(string source, AbletonPorts ports)
    {
        source = ReceiveLine.Replace(source, $"RECEIVE_PORT = {ports.ScriptListensOn}", 1);
        source = SendLine.Replace(source, $"SEND_PORT = {ports.ScriptRepliesTo}", 1);

        return source;
    }

    /// <summary>What pair an install is set to, or null when it is too old to have the file.</summary>
    private static AbletonPorts? ReadPorts(string target)
    {
        var file = IoPath.Combine(target, ConfigFile);

        if (!File.Exists(file)) return null;

        var text = File.ReadAllText(file);
        var receive = ReceiveLine.Match(text);
        var send = SendLine.Match(text);

        if (!receive.Success || !send.Success) return null;

        return new AbletonPorts(
            int.Parse(receive.Groups["port"].Value),
            int.Parse(send.Groups["port"].Value));
    }

    /// <summary>
    /// Everything the filesystem throws at a path it will not accept.
    /// </summary>
    /// <remarks>
    /// Broader than it looks it needs to be, on purpose: this runs while somebody is trying to get
    /// the app working for the first time, and a stack trace at that moment is the worst possible
    /// answer. Anything genuinely unexpected still propagates.
    /// </remarks>
    private static bool IsPathProblem(Exception ex) =>
        ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or System.Security.SecurityException;

    public string TargetFolder(string userLibrary) =>
        IoPath.Combine(userLibrary, RemoteScriptsFolder, ScriptFolderName);

    // ---------- The bundle ----------

    private IReadOnlyDictionary<string, string> ReadBundle()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var name in _assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal)) continue;

            using var stream = _assembly.GetManifestResourceStream(name);

            if (stream is null) continue;

            using var reader = new StreamReader(stream);

            // MSBuild writes RecursiveDir with the platform separator, so a file in a subfolder
            // arrives as "handlers\browser.py". One form everywhere, chosen to survive a build
            // on anything that is not Windows.
            var relative = name[ResourcePrefix.Length..].Replace('\\', '/');

            files[relative] = reader.ReadToEnd();
        }

        return files;
    }

    // ---------- Finding Ableton ----------

    /// <summary>
    /// Where Ableton keeps its User Library on each desktop, best guess first.
    /// </summary>
    /// <remarks>
    /// The two desktops disagree about more than the separator. Windows puts it under Documents,
    /// and MyDocuments comes first there because it follows a redirected Documents folder —
    /// OneDrive moves it for a lot of people, and the literal <c>%USERPROFILE%\Documents</c> would
    /// then be the wrong answer or not exist at all. macOS puts it under Music instead, which is
    /// not a Documents folder by another name: a Mac with a Documents\Ableton in it has something
    /// somebody made by hand.
    /// <para>
    /// Pure and taking its folders as arguments so the rule can be checked for a desktop the test
    /// is not running on. Guessing wrong here is not a crash — it is an installer that quietly
    /// writes the script somewhere Live never looks.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> UserLibraryCandidates(
        bool macOS, string? documents, string? profile, string? music)
    {
        var candidates = new List<string>();

        void Consider(string? parent)
        {
            if (string.IsNullOrWhiteSpace(parent)) return;

            var library = IoPath.Combine(parent, "Ableton", "User Library");

            if (!candidates.Contains(library, StringComparer.OrdinalIgnoreCase))
                candidates.Add(library);
        }

        if (macOS)
        {
            Consider(music);

            if (!string.IsNullOrWhiteSpace(profile)) Consider(IoPath.Combine(profile, "Music"));

            return candidates;
        }

        Consider(documents);

        if (!string.IsNullOrWhiteSpace(profile))
        {
            Consider(IoPath.Combine(profile, "Documents"));
            Consider(IoPath.Combine(profile, "Documentos"));
        }

        return candidates;
    }

    private static IReadOnlyList<string> FindUserLibraries()
    {
        var candidates = UserLibraryCandidates(
            OperatingSystem.IsMacOS(),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));

        // Only folders that are really there, because the picker beside them is only worth showing
        // when there is a genuine choice to make — offering a Documentos\Ableton that exists
        // nowhere is a question with a wrong answer in it.
        var real = candidates.Where(Directory.Exists).ToList();

        // Nothing found still leaves the best guess standing: Ableton may be installed with the
        // User Library not yet created, and installing into it is what creates it.
        return real.Count > 0 ? real : candidates.Take(1).ToList();
    }

    private static bool DetectLive()
    {
        try
        {
            return Process.GetProcesses()
                .Any(process => process.ProcessName.StartsWith("Ableton Live", StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
