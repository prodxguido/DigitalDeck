using DigitalDeck.Core.Abstractions;

namespace DigitalDeck.Integrations.Startup;

/// <inheritdoc cref="IStartupRegistration"/>
/// <remarks>
/// One file in <c>~/Library/LaunchAgents</c>, written and deleted. Nothing here is macOS-specific
/// as code — it is file IO, which is why it compiles anywhere — but the folder it writes into only
/// means anything on a Mac, so it is the composition root's job to pick this one.
/// <para>
/// Failures are reported rather than thrown, the same as on Windows: not being able to open at
/// login is a setting that did not take, not a reason for the deck to stop working.
/// </para>
/// </remarks>
public sealed class MacStartupRegistration : IStartupRegistration
{
    private readonly string _plistPath;
    private readonly Func<string> _executable;

    public MacStartupRegistration(string? plistPath = null, Func<string>? executable = null)
    {
        _plistPath = plistPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents", LaunchAgent.Label + ".plist");

        _executable = executable ?? DefaultExecutable;
    }

    public StartupState Read()
    {
        try
        {
            if (!File.Exists(_plistPath)) return new StartupState(false);

            return new StartupState(true, LaunchAgent.ProgramIn(File.ReadAllText(_plistPath)));
        }
        catch (Exception ex)
        {
            return new StartupState(false, null, ex.Message);
        }
    }

    public StartupState Set(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                if (File.Exists(_plistPath)) File.Delete(_plistPath);

                return new StartupState(false);
            }

            var folder = Path.GetDirectoryName(_plistPath);

            if (folder is { Length: > 0 }) Directory.CreateDirectory(folder);

            var executable = _executable();

            File.WriteAllText(_plistPath, LaunchAgent.Plist(executable));

            return new StartupState(true, executable);
        }
        catch (Exception ex)
        {
            return new StartupState(false, null, ex.Message);
        }
    }

    /// <summary>
    /// The program launchd should open.
    /// </summary>
    /// <remarks>
    /// <c>ProcessPath</c> rather than the assembly's location: inside a <c>.app</c> bundle those
    /// are different files, and the one launchd has to be given is the executable that is actually
    /// running.
    /// </remarks>
    private static string DefaultExecutable() =>
        Environment.ProcessPath ?? Environment.GetCommandLineArgs().FirstOrDefault() ?? string.Empty;
}
