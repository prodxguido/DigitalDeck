using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.Core.Services;

/// <summary>
/// A stable key for "the same thing this button does", across tiles, folders and profiles.
/// </summary>
/// <remarks>
/// Usage is counted per signature rather than per item id, and that choice is what makes the
/// ranking behave. The "most used" folder holds copies of buttons that live elsewhere; keyed by
/// id, a copy would start from zero and then compete with its own original, so pressing the copy
/// would slowly push the original out of the list it was copied into. Keyed by what the button
/// does, both presses land on the same counter and the folder ranks actions, not tiles.
/// </remarks>
public static class ActionSignature
{
    /// <summary>The key for a payload, or null when the action is not worth ranking.</summary>
    public static string? For(ActionPayload? payload) => payload switch
    {
        // The URI identifies exactly one browser item; the name is the fallback for buttons made
        // before a URI was captured, and is scoped by category so two sections cannot collide.
        AbletonLoadDeviceAction load =>
            string.IsNullOrWhiteSpace(load.Uri)
                ? $"load:{load.Category ?? "?"}/{load.Name.Trim().ToLowerInvariant()}"
                : $"load:{load.Uri}",

        // Two transport buttons that differ only in which track they target are different buttons.
        AbletonAction live =>
            $"live:{live.Command}:{live.TrackIndex}:{live.SceneIndex}:{live.SendIndex}:{live.State}",

        OpenUrlAction url => $"url:{url.Url.Trim().ToLowerInvariant()}",
        OpenPathAction path => $"path:{path.Path.Trim().ToLowerInvariant()}",
        _ => null
    };
}
