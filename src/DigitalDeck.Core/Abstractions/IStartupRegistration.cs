namespace DigitalDeck.Core.Abstractions;

/// <param name="Enabled">Whether the deck is set to open on its own when the machine starts.</param>
/// <param name="Command">
/// What is registered to run, or null when nothing is. Worth carrying because it can point at a
/// copy of the app that has since been moved or deleted — an entry that exists and does nothing.
/// </param>
/// <param name="Problem">
/// Why the setting could not be read or changed, or null. A value rather than an exception: a
/// locked-down machine is an ordinary thing to run on, and the answer is a line in the menu rather
/// than a crash.
/// </param>
public readonly record struct StartupState(bool Enabled, string? Command = null, string? Problem = null)
{
    /// <summary>True when something is registered but it is not this copy of the app.</summary>
    public bool PointsElsewhere(string executable) =>
        Enabled && Command is { Length: > 0 } && !Command.Contains(executable, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Whether the deck opens by itself when the user logs in.
/// </summary>
/// <remarks>
/// Core stops at the question. Where the answer is kept — a registry value, a shortcut in a folder,
/// a launch agent — is the platform's business, and naming it here is what would make this the one
/// thing standing between the app and a port to another one.
/// <para>
/// Everything is per-user and needs no administrator: turning this on must never be the moment the
/// app asks for a privilege it has done without until then.
/// </para>
/// </remarks>
public interface IStartupRegistration
{
    /// <summary>Reads the current setting. Never throws.</summary>
    StartupState Read();

    /// <summary>
    /// Turns opening at login on or off.
    /// </summary>
    /// <returns>The state afterwards, carrying an explanation when it could not be changed.</returns>
    StartupState Set(bool enabled);
}
