namespace DigitalDeck.Core;

/// <summary>
/// Where DigitalDeck keeps its data. The database stays small and fast because icons live beside
/// it as files rather than as blobs inside it; portability is handled by the .deckpack format,
/// not by making the database file itself the unit you copy between machines.
/// </summary>
public sealed class DeckPaths
{
    public DeckPaths(string? rootOverride = null)
    {
        Root = rootOverride ?? DefaultRoot(
            OperatingSystem.IsMacOS(),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        DatabaseFile = Path.Combine(Root, "deck.db");
        IconsDirectory = Path.Combine(Root, "assets", "icons");
        BackupsDirectory = Path.Combine(Root, "backups");
        VoiceDirectory = Path.Combine(Root, "voice");
    }

    /// <summary>
    /// Where the deck's data belongs on each desktop.
    /// </summary>
    /// <remarks>
    /// Not the same folder with a different separator. On Windows the answer is
    /// <c>%APPDATA%</c>, which is what <c>ApplicationData</c> reports. On macOS that same
    /// <c>ApplicationData</c> reports <c>~/.config</c> — a Unix convention .NET carries over, and
    /// not where a Mac application is supposed to keep anything: the answer there is
    /// <c>~/Library/Application Support</c>, which is the folder Time Machine backs up and the one
    /// a person can actually find.
    /// <para>
    /// Takes its folders as arguments so the rule can be checked for the desktop the test is not
    /// running on, the same as the Remote Script installer's.
    /// </para>
    /// </remarks>
    public static string DefaultRoot(bool macOS, string appData, string home) =>
        macOS
            ? Path.Combine(home, "Library", "Application Support", "DigitalDeck")
            : Path.Combine(appData, "DigitalDeck");

    public string Root { get; }
    public string DatabaseFile { get; }
    public string IconsDirectory { get; }
    public string BackupsDirectory { get; }

    /// <summary>
    /// Where the speech model lives once someone installs one.
    /// </summary>
    /// <remarks>
    /// Not created by <see cref="EnsureCreated"/>, unlike everything else here. The folder
    /// existing is what says a model is installed, and an empty one made at startup would make
    /// every launch look like a half-finished download.
    /// </remarks>
    public string VoiceDirectory { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(IconsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
    }

    /// <summary>Turns a stored relative icon path into an absolute one.</summary>
    public string ResolveIcon(string relativePath) => Path.Combine(Root, relativePath);
}
