using System.IO;
using DigitalDeck.App;
using DigitalDeck.App.Services;
using DigitalDeck.ViewModels;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using Xunit;

using Path = System.IO.Path;

namespace DigitalDeck.App.Tests;

/// <summary>
/// The icon a tile earns from what it does, so the common buttons look right without anyone
/// touching the picker.
/// </summary>
public class IconSuggestionTests
{
    [Fact]
    public void AFolderButtonGetsTheFolderIcon()
    {
        var real = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

        try
        {
            var icon = IconSuggestion.For(new OpenPathAction { Path = real.FullName }, ItemType.Button);
            Assert.Equal("folder", icon);
        }
        finally
        {
            real.Delete();
        }
    }

    [Fact]
    public void APathThatIsNotOnDiskYetIsGuessedFromItsExtension()
    {
        Assert.Equal("folder", IconSuggestion.For(new OpenPathAction { Path = @"D:\Samples\Kicks" }, ItemType.Button));
        Assert.Equal("music", IconSuggestion.For(new OpenPathAction { Path = @"D:\Samples\kick.wav" }, ItemType.Button));
        Assert.Equal("image", IconSuggestion.For(new OpenPathAction { Path = @"D:\Art\cover.png" }, ItemType.Button));
        Assert.Equal("power", IconSuggestion.For(new OpenPathAction { Path = @"C:\Tools\run.exe" }, ItemType.Button));
        Assert.Equal("document", IconSuggestion.For(new OpenPathAction { Path = @"C:\notes.txt" }, ItemType.Button));
    }

    [Fact]
    public void AWebButtonGetsTheGlobe()
    {
        Assert.Equal("globe", IconSuggestion.For(new OpenUrlAction { Url = "https://ableton.com" }, ItemType.Button));
    }

    [Fact]
    public void AnAbletonButtonGetsAnIconForItsCommandGroup()
    {
        Assert.Equal("play", IconSuggestion.For(AbletonAction.For(AbletonCommand.Play), ItemType.Button));
        Assert.Equal("tempo", IconSuggestion.For(AbletonAction.For(AbletonCommand.SetTempo), ItemType.Button));
    }

    [Fact]
    public void AFolderTileIsAlwaysAFolderRegardlessOfAction()
    {
        Assert.Equal("folder", IconSuggestion.For(null, ItemType.Folder));
    }

    [Fact]
    public void APlainButtonWithNoActionFallsBackToTheDefault()
    {
        Assert.Equal(DeckIcons.DefaultButton, IconSuggestion.For(null, ItemType.Button));
    }
}
