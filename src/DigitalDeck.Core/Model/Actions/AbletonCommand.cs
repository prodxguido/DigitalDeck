namespace DigitalDeck.Core.Model.Actions;

/// <summary>
/// Every Ableton action a button can be set to.
/// </summary>
/// <remarks>
/// One enum rather than one payload class per command, on purpose: these are thirty-odd variations
/// on the same shape — an optional track, an optional scene, an optional number — and a class each
/// would mean thirty <c>JsonDerivedType</c> lines and thirty editor panels for no gain. The
/// per-command differences that actually matter live in <see cref="AbletonCommands"/>.
/// <para>
/// The names are serialised into the <c>data</c> column, so renaming one silently breaks every
/// button already saved with it. Add new values at the end; never repurpose an existing name.
/// </para>
/// </remarks>
public enum AbletonCommand
{
    // ---------- Transport ----------
    Play,
    Stop,
    Continue,
    TogglePlay,
    StopAllClips,

    // The explicit forms of the toggles below. A toggle depends on where Live happens to be, which
    // is fine for a button you watch and wrong for one that has to leave the set in a known state.
    SetRecord,
    SetSessionRecord,
    ToggleRecord,
    ToggleSessionRecord,
    CaptureMidi,
    Undo,
    Redo,

    // ---------- Tempo ----------
    SetTempo,
    NudgeTempo,
    TapTempo,
    ToggleMetronome,
    SetMetronome,
    ToggleLoop,
    SetLoop,

    // ---------- Tracks ----------
    SelectTrack,
    ToggleMute,
    SetMute,
    ToggleSolo,
    SetSolo,
    ToggleArm,
    SetArm,
    SetVolume,
    NudgeVolume,
    SetPanning,
    NudgePanning,
    SetSend,
    StopTrackClips,
    CreateMidiTrack,
    CreateAudioTrack,
    CreateReturnTrack,
    DuplicateTrack,
    DeleteTrack,

    // ---------- Clips ----------
    FireClip,
    StopClip,
    FireScene,
    FireSelectedScene,
    SelectScene,
    CreateScene,

    // ---------- Views ----------
    ShowSession,
    ShowArrangement,
    ToggleSessionArrangement,
    ShowDetailClip,
    ShowDetailDevice,
    ToggleDetail,
    ToggleBrowser,

    // ---------- Zoom ----------
    ZoomTimeIn,
    ZoomTimeOut,
    ZoomTimePreset,
    ZoomTracksIn,
    ZoomTracksOut,
    ZoomTracksPreset,

    // ---------- File ----------
    // Not in the Live Object Model, so the Remote Script cannot reach them. Driven by pressing
    // Live's factory keyboard shortcut instead — see AbletonCommandInfo.ViaKeyboard.
    ExportAudioVideo,
    SaveSet,
    SaveSetAs
}

/// <summary>How the commands are grouped in the editor.</summary>
public enum AbletonCommandGroup
{
    Transport,
    Tempo,
    Tracks,
    Clips,
    Views,
    Zoom,
    File
}

/// <summary>Which of the optional fields a command actually reads.</summary>
[Flags]
public enum AbletonParameters
{
    None = 0,
    Track = 1,
    Scene = 2,
    Value = 4,

    /// <summary>An on/off choice, for the commands that set rather than toggle.</summary>
    State = 8,

    /// <summary>Which send of the track — A, B, C — the command acts on.</summary>
    Send = 16
}
