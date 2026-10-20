using DigitalDeck.Core.Abstractions;
using Microsoft.Win32;

namespace DigitalDeck.Integrations.Windows;

/// <summary>
/// Opens the deck at login through the per-user Run key.
/// </summary>
/// <remarks>
/// <c>HKEY_CURRENT_USER</c> and never <c>HKEY_LOCAL_MACHINE</c>: the machine-wide key starts the app
/// for everyone who logs in and needs administrator rights to write, and the deck has never asked
/// for those. Turning a checkbox on must not be the moment it starts.
/// <para>
/// This is the same list Windows shows under Inicio in the Task Manager, so whatever the app does
/// here the user can see and undo from a place they already know — which is the point of the
/// setting existing in the app at all, not a reason to hide it somewhere more clever.
/// </para>
/// </remarks>
public sealed class WindowsStartupRegistration : IStartupRegistration
{
    /// <summary>The key Windows reads at login. Per user; no administrator involved.</summary>
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>How the entry is named in the Task Manager's startup list.</summary>
    public const string ValueName = "DigitalDeck";

    private readonly string _keyPath;
    private readonly Func<string?> _executable;

    /// <param name="keyPath">
    /// Which key to write. Only a test passes anything else, and only ever a scratch key of its own
    /// — the real one is shared with every other program on the machine.
    /// </param>
    /// <param name="executable">The path to register. Defaults to whatever is running right now.</param>
    public WindowsStartupRegistration(string? keyPath = null, Func<string?>? executable = null)
    {
        _keyPath = keyPath ?? RunKey;
        _executable = executable ?? (() => Environment.ProcessPath);
    }

    public StartupState Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: false);
            var command = key?.GetValue(ValueName) as string;

            return new StartupState(!string.IsNullOrWhiteSpace(command), command);
        }
        catch (Exception ex)
        {
            return new StartupState(false, null, Explain(ex));
        }
    }

    public StartupState Set(bool enabled)
    {
        var executable = _executable();

        if (enabled && string.IsNullOrWhiteSpace(executable))
        {
            return new StartupState(
                false, null, "No pude averiguar desde dónde se está ejecutando la app.");
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true)
                            ?? throw new InvalidOperationException("no se pudo abrir la clave");

            if (enabled)
            {
                // Quoted, and not optionally: the path this app ships in has spaces in it, and an
                // unquoted command line would have Windows try to run the first word of it.
                key.SetValue(ValueName, Quote(executable!), RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            return Read() with { Problem = Explain(ex) };
        }

        return Read();
    }

    private static string Quote(string path) =>
        path.StartsWith('"') ? path : $"\"{path}\"";

    private static string Explain(Exception ex) => ex switch
    {
        UnauthorizedAccessException =>
            "Windows no me deja cambiar el arranque automático en esta cuenta.",
        System.Security.SecurityException =>
            "Windows no me deja cambiar el arranque automático en esta cuenta.",
        _ => $"No pude cambiar el arranque automático: {ex.Message}"
    };
}
