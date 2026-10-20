using DigitalDeck.Core;
using Xunit;

namespace DigitalDeck.Tests;

/// <summary>
/// Where the deck keeps its data on each desktop.
/// </summary>
/// <remarks>
/// Worth pinning because the wrong answer is silent: the app would come up on a Mac with an empty
/// deck, having written a perfectly good database somewhere nobody thinks to look.
/// </remarks>
public class DeckPathsTests
{
    [Fact]
    public void OnWindowsItIsUnderAppData()
    {
        Assert.Equal(
            Path.Combine(@"C:\Users\guido\AppData\Roaming", "DigitalDeck"),
            DeckPaths.DefaultRoot(macOS: false, @"C:\Users\guido\AppData\Roaming", @"C:\Users\guido"));
    }

    [Fact]
    public void OnAMacItIsUnderApplicationSupportAndNotDotConfig()
    {
        // .NET reports ~/.config for ApplicationData on macOS, which is a Unix convention rather
        // than where a Mac application belongs. Following it would put the deck somewhere Time
        // Machine does not back up and nobody can find.
        var root = DeckPaths.DefaultRoot(macOS: true, "/Users/guido/.config", "/Users/guido");

        Assert.Equal(
            Path.Combine("/Users/guido", "Library", "Application Support", "DigitalDeck"), root);
        Assert.DoesNotContain(".config", root);
    }

    [Fact]
    public void AnOverrideStillWins()
    {
        // What the Avalonia shell uses to open a throwaway deck instead of somebody's real one.
        var paths = new DeckPaths("/tmp/deck");

        Assert.Equal("/tmp/deck", paths.Root);
        Assert.Equal(Path.Combine("/tmp/deck", "deck.db"), paths.DatabaseFile);
    }
}
