using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DigitalDeck.App.Services;

public sealed record AbletonRecentProject(string Name, string Path, DateTime LastOpened)
{
    public string DisplayDate => LastOpened == DateTime.MinValue ? string.Empty : LastOpened.ToString("dd/MM HH:mm");
}

public static class AbletonRecentProjects
{
    public static IReadOnlyList<AbletonRecentProject> Read(int limit)
    {
        var recent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Recent");
        if (!Directory.Exists(recent)) return Array.Empty<AbletonRecentProject>();

        var result = new List<AbletonRecentProject>();
        foreach (var link in Directory.EnumerateFiles(recent, "*.lnk"))
        {
            try
            {
                var target = ResolveShortcut(link);
                if (string.IsNullOrWhiteSpace(target) || !target.EndsWith(".als", StringComparison.OrdinalIgnoreCase)) continue;
                if (!File.Exists(target)) continue;
                var opened = File.GetLastWriteTime(link);
                result.Add(new AbletonRecentProject(Path.GetFileNameWithoutExtension(target), target, opened));
            }
            catch
            {
                // A stale or malformed Windows shortcut should not stop the recent list.
            }
        }

        return result
            .GroupBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(p => p.LastOpened).First())
            .OrderByDescending(x => x.LastOpened)
            .Take(Math.Clamp(limit, 1, 100))
            .ToArray();
    }

    public static void Open(AbletonRecentProject project)
    {
        Process.Start(new ProcessStartInfo(project.Path) { UseShellExecute = true });
    }

    public static IReadOnlyList<AbletonRecentProject> ReadKnown()
    {
        var known = new Dictionary<string, AbletonRecentProject>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in ReadCatalog().Where(IsRealProject)) known[ProjectKey(project)] = project;
        foreach (var project in Read(100))
        {
            if (!IsRealProject(project)) continue;
            var key = ProjectKey(project);
            if (!known.TryGetValue(key, out var old) || project.LastOpened > old.LastOpened)
                known[key] = project;
        }
        return known.Values.OrderByDescending(x => x.LastOpened).ToArray();
    }

    public static async Task<int> RefreshCatalogAsync(IProgress<int>? progress = null)
    {
        var found = new Dictionary<string, AbletonRecentProject>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in ReadCatalog().Where(IsRealProject)) found[ProjectKey(project)] = project;
        var count = 0;
        await Task.Run(() =>
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                foreach (var file in EnumerateAbletonSets(drive.RootDirectory.FullName))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        var project = new AbletonRecentProject(info.Name[..^4], file, info.LastWriteTime);
                        if (!IsRealProject(project)) continue;
                        found[ProjectKey(project)] = project;
                        progress?.Report(++count);
                    }
                    catch { }
                }
            }
            SaveCatalog(found.Values);
        });
        return count;
    }

    public static bool CatalogNeedsRefresh
    {
        get
        {
            try { return !File.Exists(CatalogPath) || File.GetLastWriteTimeUtc(CatalogPath) < DateTime.UtcNow.AddHours(-24); }
            catch { return true; }
        }
    }

    private static string? ResolveShortcut(string path)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return null;
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(path);
        return (string?)shortcut.TargetPath;
    }

    private static IEnumerable<string> EnumerateAbletonSets(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            var name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar));
            if (name is "$Recycle.Bin" or "System Volume Information" or "Windows" or "Program Files" or "Program Files (x86)" or "AppData" or "node_modules" or ".git" or "Backup" or "Backups" or "backup") continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(directory, "*.als", SearchOption.TopDirectoryOnly); }
            catch { continue; }
            foreach (var file in files) yield return file;
            IEnumerable<string> children;
            try { children = Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly); }
            catch { continue; }
            foreach (var child in children) pending.Push(child);
        }
    }

    private static string CatalogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DigitalDeck", "ableton-projects.json");

    private static IReadOnlyList<AbletonRecentProject> ReadCatalog()
    {
        try
        {
            if (!File.Exists(CatalogPath)) return Array.Empty<AbletonRecentProject>();
            return JsonSerializer.Deserialize<List<AbletonRecentProject>>(File.ReadAllText(CatalogPath)) ?? new List<AbletonRecentProject>();
        }
        catch { return Array.Empty<AbletonRecentProject>(); }
    }

    private static bool IsRealProject(AbletonRecentProject project)
    {
        var fileName = Path.GetFileNameWithoutExtension(project.Path);
        var lowerPath = project.Path.Replace('/', '\\').ToLowerInvariant();
        if (lowerPath.Contains("\\backup\\") || lowerPath.Contains("\\backups\\") || lowerPath.Contains("\\auto-save\\") || lowerPath.Contains("\\autosave\\")) return false;
        if (Regex.IsMatch(fileName, @"\s*\[[^\]]*\b(?:19|20)\d{2}\b[^\]]*\]\s*$", RegexOptions.IgnoreCase)) return false;
        if (Regex.IsMatch(fileName, @"\s*\[[^\]]*(?:backup|auto[- ]?save|recovery)[^\]]*\]\s*$", RegexOptions.IgnoreCase)) return false;
        return !Regex.IsMatch(fileName, @"(?:^|[ _-])(backup|auto[- ]?save|recovery)(?:$|[ _-])", RegexOptions.IgnoreCase);
    }

    private static string ProjectKey(AbletonRecentProject project)
    {
        var directory = Path.GetDirectoryName(project.Path) ?? string.Empty;
        var name = Regex.Replace(Path.GetFileNameWithoutExtension(project.Path), @"\s*\[[^\]]*\]\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();
        return Path.Combine(directory, name).ToUpperInvariant();
    }

    private static void SaveCatalog(IEnumerable<AbletonRecentProject> projects)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath)!);
            var json = JsonSerializer.Serialize(projects.OrderByDescending(x => x.LastOpened), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(CatalogPath, json);
        }
        catch { }
    }
}
