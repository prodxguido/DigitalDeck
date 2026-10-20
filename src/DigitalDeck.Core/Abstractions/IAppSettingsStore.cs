namespace DigitalDeck.Core.Abstractions;

/// <summary>
/// Small key/value store for things that are not part of a profile: which monitor the window was
/// on, which profile was open last, whether edit mode was left on.
/// </summary>
public interface IAppSettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
}

/// <summary>Setting keys used by the app, kept in one place so they cannot drift apart.</summary>
public static class SettingKeys
{
    public const string LastProfileId = "last_profile_id";
    public const string LastPageId = "last_page_id";
    /// <summary>
    /// The folder the usage ranking rewrites. Held by id rather than found by its label, so a user
    /// who renames it — or makes a folder of their own with the same name — keeps the right one.
    /// </summary>
    public const string FrequentFolderId = "frequent_folder_id";

    public const string WindowMonitor = "window_monitor";
    public const string WindowPlacement = "window_placement";

    /// <summary>
    /// Where the mixer window was left, kept apart from the deck's own.
    /// </summary>
    /// <remarks>
    /// The two windows are meant to live on different screens — that is the whole point of a mixer
    /// sized for a finger — so one remembered position for both would put whichever opened last on
    /// top of the other.
    /// </remarks>
    public const string MixerPlacement = "mixer_window_placement";
    public const string Fullscreen = "window_fullscreen";

    /// <summary>The port the Remote Script listens on; the app sends there.</summary>
    public const string AbletonScriptPort = "ableton_script_port";

    /// <summary>The port the Remote Script replies to; the app listens there.</summary>
    public const string AbletonAppPort = "ableton_app_port";

    /// <summary>Set once the onboarding tutorial has been shown, or dismissed with "no mostrar de nuevo".</summary>
    public const string OnboardingSeen = "onboarding_seen";

    /// <summary>Set once the first-run "¿vas a usar Ableton Live?" question has been asked.</summary>
    public const string AbletonOnboardingAsked = "ableton_onboarding_asked";

    /// <summary>Whether the voice assistant listens at all. Off until the user turns it on.</summary>
    public const string VoiceEnabled = "voice_enabled";

    /// <summary>
    /// Set once the first-run "¿instalás el asistente de voz?" question has been asked, so a "no"
    /// is not re-asked on every launch. The install button in the voice settings stays available.
    /// </summary>
    public const string VoiceOnboardingAsked = "voice_onboarding_asked";

    /// <summary>
    /// Which microphone the assistant listens through, held by endpoint id rather than by name so a
    /// driver update renaming the device does not silently move the setting to another one. Unset
    /// means whatever Windows calls the default.
    /// </summary>
    public const string VoiceInputDevice = "voice_input_device";

    /// <summary>
    /// The key combination that starts listening, written as <c>Ctrl+Alt+Space</c>. Stored as text
    /// rather than as a virtual-key number so it can be read by a person looking at the table.
    /// </summary>
    public const string VoiceHotkey = "voice_hotkey";

    /// <summary>When set, an intentional reset starts with no starter profile.</summary>
    public const string StarterDeckSuppressed = "starter_deck_suppressed";
}
