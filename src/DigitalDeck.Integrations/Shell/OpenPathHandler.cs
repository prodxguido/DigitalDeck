using System.Diagnostics;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.Integrations.Shell;

/// <summary>
/// Opens a folder, document or program through the desktop's own shell.
/// </summary>
/// <remarks>
/// <see cref="ProcessStartInfo.UseShellExecute"/> is deliberately true and no argument string is
/// ever built: the path goes to the shell as a single value, exactly as a double-click in Explorer
/// or the Finder would send it, so there is no command line for a crafted path to break out of.
/// That flag is also what makes this portable — .NET turns it into <c>ShellExecute</c> on Windows
/// and into <c>open</c> on macOS, which is the same idea spelled two ways.
/// </remarks>
public sealed class OpenPathHandler : ActionHandler<OpenPathAction>
{
    protected override Task ExecuteAsync(OpenPathAction payload, CancellationToken ct)
    {
        var path = Expand(payload.Path.Trim());

        if (!File.Exists(path) && !Directory.Exists(path))
            throw new FileNotFoundException($"La ruta ya no existe: {path}");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Turns a stored path into a real one, whichever way the person wrote it.
    /// </summary>
    /// <remarks>
    /// Both spellings, because a deck outlives the machine it was made on: <c>%APPDATA%</c> is how
    /// a Windows user writes it and <c>~</c> is how a Mac user does, and a profile exported from
    /// one and imported on the other should not turn into a button that says the folder is gone.
    /// The tilde is only a home folder at the very start of a path — anywhere else it is an
    /// ordinary character, and Windows filenames are allowed to contain it.
    /// </remarks>
    internal static string Expand(string path, string? home = null)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);

        if (expanded is not ['~', var next, ..] || (next != '/' && next != '\\')) return expanded;

        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return home.Length == 0 ? expanded : Path.Combine(home, expanded[2..]);
    }
}
