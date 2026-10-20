using DigitalDeck.Integrations.Shell;
using Xunit;

namespace DigitalDeck.Integrations.Tests;

/// <summary>
/// Turning a stored path into a real one.
/// </summary>
/// <remarks>
/// A deck outlives the machine it was made on: profiles travel between people as <c>.deckpack</c>
/// files, and the two desktops write the same idea differently. Getting this wrong turns a button
/// into "la ruta ya no existe" for a folder that is sitting right there.
/// </remarks>
public class OpenPathExpandTests
{
    private const string Home = "/Users/guido";

    [Fact]
    public void ATildeAtTheStartIsTheHomeFolder()
    {
        Assert.Equal(
            Path.Combine(Home, "Music/Ableton"),
            OpenPathHandler.Expand("~/Music/Ableton", Home));
    }

    [Fact]
    public void ATildeWrittenWithABackslashCountsToo()
    {
        // The same profile opened on the other desktop, where a person types the other separator.
        Assert.Equal(
            Path.Combine(Home, @"Music\Ableton"),
            OpenPathHandler.Expand(@"~\Music\Ableton", Home));
    }

    [Fact]
    public void ATildeAnywhereElseIsJustACharacter()
    {
        // Windows filenames are allowed to contain it, and Office leaves ~$ lockfiles all over.
        Assert.Equal(@"C:\notas\~$informe.docx", OpenPathHandler.Expand(@"C:\notas\~$informe.docx", Home));
        Assert.Equal("/tmp/~backup", OpenPathHandler.Expand("/tmp/~backup", Home));
    }

    [Fact]
    public void ATildeOnItsOwnIsLeftAlone()
    {
        // No separator after it, so there is nothing to join — and answering the home folder here
        // would be guessing at what somebody meant by a one-character path.
        Assert.Equal("~", OpenPathHandler.Expand("~", Home));
    }

    [Fact]
    public void AnOrdinaryPathComesBackUntouched()
    {
        Assert.Equal(@"C:\Program Files\Ableton", OpenPathHandler.Expand(@"C:\Program Files\Ableton", Home));
    }

    [Fact]
    public void WithNoHomeToPutItInTheTildeSurvives()
    {
        // Better than joining onto an empty string, which would silently turn an absolute path
        // into a relative one and open something else entirely.
        Assert.Equal("~/Music", OpenPathHandler.Expand("~/Music", string.Empty));
    }
}
