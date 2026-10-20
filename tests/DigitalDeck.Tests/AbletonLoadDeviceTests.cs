using System.Text.Json;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Core.Serialization;
using Xunit;

namespace DigitalDeck.Tests;

/// <summary>
/// The payload for a button that loads a rack, preset or plugin from Live's browser.
/// </summary>
public class AbletonLoadDeviceTests
{
    private static AbletonLoadDeviceAction Rack() => new()
    {
        Name = "Chorus Echo.adg",
        Uri = "query:AudioFx#Audio%20Effect%20Rack:Space:FileId_5479",
        Category = AbletonBrowserCategories.AudioEffects
    };

    [Fact]
    public void ItSurvivesTheDataColumnUnderItsOwnDiscriminator()
    {
        var json = JsonSerializer.Serialize<ActionPayload>(Rack(), DeckJson.Compact);

        Assert.Contains("\"kind\":\"ableton_load\"", json);

        var restored = Assert.IsType<AbletonLoadDeviceAction>(
            JsonSerializer.Deserialize<ActionPayload>(json, DeckJson.Compact));

        Assert.Equal("Chorus Echo.adg", restored.Name);
        Assert.Equal(Rack().Uri, restored.Uri);
        Assert.Equal("audio_effects", restored.Category);
    }

    [Fact]
    public void TheUriIsKeptBecauseNamesAreNotUnique()
    {
        // Measured against a real Live: one rack folder held 33 items with 33 distinct URIs, while
        // duplicate names across folders are commonplace. Storing only the name would make a button
        // load whichever preset happened to be found first.
        var restored = JsonSerializer.Deserialize<ActionPayload>(
            JsonSerializer.Serialize<ActionPayload>(Rack(), DeckJson.Compact), DeckJson.Compact);

        Assert.False(string.IsNullOrEmpty(Assert.IsType<AbletonLoadDeviceAction>(restored).Uri));
    }

    [Fact]
    public void AnActionWithOnlyANameIsStillValid()
    {
        // The URI is the better key, but a button typed by hand has to work too — the script falls
        // back to searching by name inside the category.
        var action = new AbletonLoadDeviceAction { Name = "Reverb" };

        Assert.Null(action.Validate());
    }

    [Fact]
    public void AnActionWithNeitherNameNorUriIsRefused()
    {
        Assert.NotNull(new AbletonLoadDeviceAction { Name = "" }.Validate());
    }

    [Fact]
    public void AnUnknownBrowserSectionIsRefused()
    {
        var action = new AbletonLoadDeviceAction { Name = "Reverb", Category = "vst_folder" };

        Assert.Contains("vst_folder", action.Validate());
    }

    [Fact]
    public void EveryCategoryTheEditorOffersIsAcceptedAndHasALabel()
    {
        foreach (var category in AbletonBrowserCategories.All)
        {
            Assert.True(AbletonBrowserCategories.IsKnown(category));
            Assert.False(string.IsNullOrWhiteSpace(AbletonBrowserCategories.Label(category)));

            Assert.Null(new AbletonLoadDeviceAction { Name = "x", Category = category }.Validate());
        }
    }

    [Fact]
    public void TheSummarySaysWhereItLands()
    {
        // The target is not configurable, so the summary is the only place the user is told.
        Assert.Equal("Cargar «Chorus Echo.adg» en la track seleccionada", Rack().Summary);
    }

    [Fact]
    public void LoadingADeviceNeedsNoConfirmation()
    {
        Assert.False(Rack().IsPotentiallyDangerous);
    }
}
