using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace DigitalDeck.App.Services;

public sealed record ShareProgress(string Stage, bool IsError = false, string? Link = null, double? Fraction = null, string? Eta = null);

public static class ProjectShareService
{
    public static string ProjectFolder(AbletonRecentProject project) => Path.GetDirectoryName(project.Path) ?? project.Path;

    public static bool HasRecentAudioActivity(string folder, TimeSpan window)
    {
        var cutoff = DateTime.Now - window;
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".wav", ".aif", ".aiff", ".flac", ".mp3", ".ogg", ".m4a" };
        try
        {
            return Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(path => extensions.Contains(Path.GetExtension(path)))
                .Any(path => File.GetLastWriteTime(path) >= cutoff);
        }
        catch { return false; }
    }

    public static async Task<string> CompressAndUploadAsync(string folder, IProgress<ShareProgress> progress, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
        var shareDir = Path.Combine(Path.GetTempPath(), "DigitalDeck", "Shares");
        Directory.CreateDirectory(shareDir);
        var archive = Path.Combine(shareDir, $"{Path.GetFileName(folder)}-{DateTime.Now:yyyyMMdd-HHmmss}.rar");
        progress.Report(new ShareProgress($"Comprimiendo esta carpeta:\n{folder}"));
        await CompressWithWinRarAsync(folder, archive, progress, cancellationToken);
        progress.Report(new ShareProgress("Verificando la integridad del RAR…"));
        await VerifyWithWinRarAsync(archive, cancellationToken);

        progress.Report(new ShareProgress("Subiendo el archivo a GoFile…"));
        using var client = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        using var form = new MultipartFormDataContent();
        await using var file = File.OpenRead(archive);
        using var content = new StreamContent(file);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "file", Path.GetFileName(archive));
        using var response = await client.PostAsync("https://upload.gofile.io/uploadfile", form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(body);
        var link = json.RootElement.GetProperty("data").GetProperty("downloadPage").GetString();
        if (string.IsNullOrWhiteSpace(link)) throw new InvalidOperationException("GoFile no devolvió un enlace de descarga.");
        progress.Report(new ShareProgress("Proyecto subido correctamente.", Link: link));
        return link;
    }

    private static async Task CompressWithWinRarAsync(string folder, string archive, IProgress<ShareProgress> progress, CancellationToken cancellationToken)
    {
        var rar = FindRar();
        if (rar is null) throw new FileNotFoundException("No encontré el ejecutable de consola de WinRAR (rar.exe).");
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToArray();
        var totalBytes = files.Sum(path => new FileInfo(path).Length);
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var watch = Stopwatch.StartNew();
        var start = new ProcessStartInfo(rar) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("a");
        start.ArgumentList.Add("-ma5");
        start.ArgumentList.Add("-m3");
        start.ArgumentList.Add("-mt8");
        start.ArgumentList.Add("-ibck");
        start.ArgumentList.Add("-idp");
        start.ArgumentList.Add("-y");
        start.ArgumentList.Add("-r");
        start.ArgumentList.Add("-ep1");
        start.ArgumentList.Add(archive);
        start.ArgumentList.Add(Path.Combine(folder, "*"));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar WinRAR.");
        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            foreach (var file in files)
            {
                if (processed.Contains(file) || !e.Data.Contains(file, StringComparison.OrdinalIgnoreCase)) continue;
                processed.Add(file);
                var done = processed.Sum(path => new FileInfo(path).Length);
                var fraction = totalBytes == 0 ? 0.5 : Math.Clamp((double)done / totalBytes, 0, 0.99);
                var eta = done > 0 ? TimeSpan.FromSeconds(watch.Elapsed.TotalSeconds * (totalBytes / (double)done - 1)) : TimeSpan.Zero;
                progress.Report(new ShareProgress($"Comprimiendo: {Path.GetFileName(file)}", Fraction: fraction, Eta: eta.TotalSeconds > 1 ? $"faltan aprox. {eta:mm\\:ss}" : null));
                break;
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        process.WaitForExit();
        if (process.ExitCode != 0 || !File.Exists(archive)) throw new InvalidOperationException($"WinRAR terminó con código {process.ExitCode}.");
        progress.Report(new ShareProgress("Compresión terminada.", Fraction: 1));
    }

    private static async Task VerifyWithWinRarAsync(string archive, CancellationToken cancellationToken)
    {
        var rar = FindRar();
        if (rar is null) throw new FileNotFoundException("No encontré rar.exe para verificar el archivo.");
        var start = new ProcessStartInfo(rar) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("t");
        start.ArgumentList.Add("-idq");
        start.ArgumentList.Add(archive);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo verificar el RAR.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException("La verificación del RAR falló; el archivo no se subió.");
    }

    private static string? FindRar()
    {
        var candidates = new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinRAR", "rar.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WinRAR", "rar.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
