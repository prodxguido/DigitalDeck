using System.Windows;
using DigitalDeck.App.Views;
using DigitalDeck.Core.Model.Actions;
using Xunit;

namespace DigitalDeck.App.Tests;

/// <summary>
/// How a browser entry is presented in the picker, and what a click on it means.
/// </summary>
/// <remarks>
/// The picker itself is a window and needs a running WPF application to exercise; these two
/// classes are where its decisions actually live, and they are plain objects.
/// </remarks>
public class BrowserRowTests
{
    private static AbletonBrowserEntry Entry(
        string name,
        bool loadable = true,
        bool folder = false,
        bool children = false,
        string uri = "query:AudioFx#FileId_5472") =>
        new(name, uri, loadable, folder, children);

    [Fact]
    public void ClickingARowThatCanBeLoadedChoosesItRatherThanOpeningIt()
    {
        // Measured against Live 12.3, 44 of the 47 items at the top of Audio Effects are loadable
        // and have presets underneath at once. If one click had to mean both, loading a Compressor
        // would cost two of them — worse than the typed name this picker replaced.
        var device = new BrowserRow(Entry("Compressor", children: true), isChosen: false);

        Assert.True(device.ChoosesOnClick);
        Assert.True(device.CanOpen);
        Assert.Equal(Visibility.Visible, device.OpenVisibility);
    }

    [Fact]
    public void ARowThereIsNothingToLoadInOpensOnClickInstead()
    {
        var folder = new BrowserRow(Entry("Space", loadable: false, children: true), isChosen: false);

        Assert.False(folder.ChoosesOnClick);
        Assert.True(folder.CanOpen);
    }

    [Fact]
    public void WhatLiveCallsAFolderIsNotWhatDecidesWhetherARowOpens()
    {
        // "Audio Effect Rack" reports is_folder false and holds six folders of the user's own
        // racks. Going by that flag would have hidden every one of them.
        var rack = new BrowserRow(Entry("Audio Effect Rack", folder: false, children: true), isChosen: false);
        var flaggedButEmpty = new BrowserRow(Entry("Vacía", folder: true, children: false), isChosen: false);

        Assert.True(rack.CanOpen);
        Assert.False(flaggedButEmpty.CanOpen);
        Assert.Equal(Visibility.Collapsed, flaggedButEmpty.OpenVisibility);
    }

    [Fact]
    public void ASearchResultHasNowhereToBeOpenedInto()
    {
        // A search reaches anywhere under the category while the path the picker keeps is relative
        // to its root, so there is no path to walk to from a result.
        var result = new BrowserRow(Entry("Reverb", children: true), isChosen: false, inSearch: true);

        Assert.False(result.CanOpen);
        Assert.True(result.ChoosesOnClick);
    }

    [Fact]
    public void TheChosenItemIsTickedWithoutLosingItsWayIn()
    {
        var row = new BrowserRow(Entry("Reverb", children: true), isChosen: true);

        Assert.NotEqual(string.Empty, row.Tick);
        Assert.True(row.CanOpen);
    }

    [Fact]
    public void ADeviceWithPresetsUnderneathCanStillBeChosenItself()
    {
        // Walking into one would put the device itself out of reach, so it comes along as the
        // first row of its own listing — and that row must not offer to open what you are in.
        var self = new BrowserRow(Entry("Reverb", children: true), isChosen: false, isSelf: true);

        Assert.Contains("Reverb", self.Name);
        Assert.NotEqual("Reverb", self.Name);
        Assert.False(self.CanOpen);
        Assert.True(self.ChoosesOnClick);
    }

    [Fact]
    public void ARowThatCanBeNeitherLoadedNorOpenedIsNotWorthShowing()
    {
        var deadEnd = Entry("Nada", loadable: false, folder: true, children: false);

        Assert.False(deadEnd.IsUseful);
        Assert.True(Entry("Chorus Echo.adg").IsUseful);
        Assert.True(Entry("Space", loadable: false, children: true).IsUseful);
    }

    [Fact]
    public void TheUriIsOnHoverBecauseItIsWhatTellsTwoPresetsApart()
    {
        var row = new BrowserRow(Entry("Chorus Echo.adg"), isChosen: false);
        var withoutUri = new BrowserRow(Entry("Chorus Echo.adg", uri: string.Empty), isChosen: false);

        Assert.Equal("query:AudioFx#FileId_5472", row.Tip);
        Assert.Null(withoutUri.Tip);
    }

    [Fact]
    public void TheCrumbYouAreStandingOnIsNotAPlaceToGo()
    {
        var root = new BrowserCrumb(0, "Efectos de audio", isLast: false);
        var here = new BrowserCrumb(1, "Space", isLast: true);

        Assert.True(root.IsClickable);
        Assert.False(here.IsClickable);
    }
}
