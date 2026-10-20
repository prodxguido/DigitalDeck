using DigitalDeck.Integrations.Ableton;
using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// Where the installer looks for Ableton's User Library on each desktop.
/// </summary>
/// <remarks>
/// The rule takes its folders as arguments precisely so it can be checked for the desktop the test
/// is not running on — which is the whole point here, because getting it wrong on macOS does not
/// crash anything: it writes the Remote Script into a folder Live never reads, and the bridge
/// simply never comes up, with nothing on screen to say why.
/// </remarks>
public class UserLibraryCandidatesTests
{
    private const string Home = "/Users/guido";
    private const string Music = "/Users/guido/Music";
    private const string Windows = @"C:\Users\guido\Documents";

    private static IReadOnlyList<string> OnMac(string? music = Music, string? profile = Home) =>
        RemoteScriptInstaller.UserLibraryCandidates(macOS: true, documents: null, profile, music);

    private static IReadOnlyList<string> OnWindows(string? documents = Windows, string? profile = null) =>
        RemoteScriptInstaller.UserLibraryCandidates(macOS: false, documents, profile, music: null);

    /// <remarks>
    /// The first candidate rather than the only one: on a real Mac the folder the system reports
    /// and <c>~/Music</c> are the same string and the second is deduplicated away, but this test
    /// runs on Windows, where Path.Combine writes a backslash and the two no longer match.
    /// </remarks>
    [Fact]
    public void OnAMacTheLibraryLivesUnderMusic()
    {
        Assert.Equal(Path.Combine(Music, "Ableton", "User Library"), OnMac()[0]);
    }

    [Fact]
    public void OnAMacDocumentsIsNeverOffered()
    {
        // Not a Documents folder by another name: a Mac with a Documents/Ableton in it has
        // something somebody made by hand, and installing there would be installing nowhere.
        var found = RemoteScriptInstaller.UserLibraryCandidates(
            macOS: true, documents: "/Users/guido/Documents", profile: Home, music: Music);

        Assert.All(found, path => Assert.DoesNotContain("Documents", path));
    }

    [Fact]
    public void OnAMacWithNoMusicFolderReportedItIsWorkedOutFromHome()
    {
        Assert.Equal(
            new[] { Path.Combine(Home, "Music", "Ableton", "User Library") },
            OnMac(music: null));
    }

    [Fact]
    public void OnWindowsTheRedirectedDocumentsComesFirst()
    {
        // OneDrive moves Documents for a lot of people. The redirected one is what Ableton follows,
        // so it has to be the first guess rather than the literal profile folder.
        var found = OnWindows(documents: @"C:\Users\guido\OneDrive\Documentos", profile: @"C:\Users\guido");

        Assert.Equal(Path.Combine(@"C:\Users\guido\OneDrive\Documentos", "Ableton", "User Library"), found[0]);
    }

    [Fact]
    public void OnWindowsTheSpanishFolderIsOfferedToo()
    {
        var found = OnWindows(profile: @"C:\Users\guido");

        Assert.Contains(Path.Combine(@"C:\Users\guido\Documentos", "Ableton", "User Library"), found);
    }

    [Fact]
    public void TheSameFolderIsNeverOfferedTwice()
    {
        // Documents and the profile's Documents are the same place on a machine without OneDrive,
        // and a picker that offers one folder twice looks broken.
        var found = OnWindows(documents: @"C:\Users\guido\Documents", profile: @"C:\Users\guido");

        Assert.Equal(found.Count, found.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void WithNothingKnownNothingIsInvented()
    {
        Assert.Empty(RemoteScriptInstaller.UserLibraryCandidates(
            macOS: false, documents: null, profile: null, music: null));
        Assert.Empty(RemoteScriptInstaller.UserLibraryCandidates(
            macOS: true, documents: null, profile: null, music: null));
    }
}
