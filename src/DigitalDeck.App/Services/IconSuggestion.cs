using System.IO;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;

namespace DigitalDeck.App.Services;

/// <summary>
/// The icon a tile takes on its own from what it does, so a button that opens a folder looks like a
/// folder without anyone choosing one.
/// </summary>
/// <remarks>
/// Only a starting point. The editor's icon picker always wins, and the suggestion is re-applied
/// only while a tile is still wearing the icon it was given automatically — so changing a button
/// from a folder to a link updates the icon, while a hand-picked one is never touched.
/// </remarks>
public static class IconSuggestion
{
    public static string For(ActionPayload? action, ItemType type)
    {
        if (type == ItemType.Folder) return DeckIcons.DefaultFolder;

        return action switch
        {
            OpenPathAction path => ForPath(path.Path),
            OpenUrlAction => "globe",
            MixerPanelAction => "sliders",
            HotkeyAction => "keyboard",
            // A macro takes the icon of what it starts with, so «arrancá la mañana» still looks
            // like the playlist it opens instead of like every macro in the deck.
            MacroAction macro => macro.Steps.Count > 0 ? For(macro.Steps[0].Action, type) : "loop",
            AbletonLoadDeviceAction => "device",
            AbletonAction ableton => ForAbleton(ableton.Command),
            _ => DeckIcons.DefaultButton,
        };
    }

    private static string ForPath(string? raw)
    {
        var path = (raw ?? string.Empty).Trim();
        if (path.Length == 0) return DeckIcons.DefaultFolder;

        try
        {
            if (Directory.Exists(path)) return DeckIcons.DefaultFolder;
            if (File.Exists(path)) return ForExtension(Path.GetExtension(path));
        }
        catch (Exception)
        {
            // A path the shell can still open but System.IO trips on — fall through to the guess.
        }

        // Not on disk yet (a deck configured before the drive is plugged in, a typo halfway
        // through): no extension reads as a folder, an extension picks by kind.
        var ext = SafeExtension(path);
        return ext.Length == 0 ? DeckIcons.DefaultFolder : ForExtension(ext);
    }

    private static string SafeExtension(string path)
    {
        try
        {
            return Path.GetExtension(path);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static string ForExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico"
            or ".tif" or ".tiff" or ".heic" => "image",
        ".wav" or ".mp3" or ".flac" or ".aif" or ".aiff" or ".ogg" or ".m4a" or ".wma"
            or ".opus" or ".aac" => "music",
        ".als" or ".alp" or ".adg" or ".adv" or ".agr" or ".amxd" => "device",
        ".exe" or ".com" or ".bat" or ".cmd" or ".ps1" or ".msi" or ".lnk" or ".scr" => "power",
        _ => "document",
    };

    private static string ForAbleton(AbletonCommand command) =>
        AbletonCommands.Describe(command).Group switch
        {
            AbletonCommandGroup.Transport => "play",
            AbletonCommandGroup.Tempo => "tempo",
            AbletonCommandGroup.Tracks => "sliders",
            AbletonCommandGroup.Clips => "clip",
            AbletonCommandGroup.Views => "browser",
            AbletonCommandGroup.Zoom => "search",
            _ => "session",
        };
}
