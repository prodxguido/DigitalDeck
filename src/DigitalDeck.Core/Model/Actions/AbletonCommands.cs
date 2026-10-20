namespace DigitalDeck.Core.Model.Actions;

/// <summary>
/// What each <see cref="AbletonCommand"/> is called, which fields it reads, and what its number
/// means.
/// </summary>
/// <param name="ValueLabel">Shown next to the number field, or null when the command takes none.</param>
/// <param name="ValueHint">One line explaining the unit, for values that are not self-evident.</param>
public sealed record AbletonCommandInfo(
    AbletonCommand Command,
    AbletonCommandGroup Group,
    string Label,
    AbletonParameters Parameters,
    string? ValueLabel = null,
    double ValueMinimum = 0,
    double ValueMaximum = 1,
    double ValueDefault = 0,
    string? ValueHint = null,
    bool ViaKeyboard = false,
    string? Note = null)
{
    public bool NeedsTrack => Parameters.HasFlag(AbletonParameters.Track);
    public bool NeedsScene => Parameters.HasFlag(AbletonParameters.Scene);
    public bool NeedsValue => Parameters.HasFlag(AbletonParameters.Value);
    public bool NeedsState => Parameters.HasFlag(AbletonParameters.State);
    public bool NeedsSend => Parameters.HasFlag(AbletonParameters.Send);
}

/// <summary>
/// The catalogue the editor builds itself from.
/// </summary>
/// <remarks>
/// Deliberately free of OSC: nothing here knows an address. The enum is the contract Core speaks,
/// and turning it into something on the wire belongs to the Ableton integration
/// (<c>AbletonCommandMap</c>), which is what keeps the editor and the domain testable without a
/// socket in sight.
/// </remarks>
public static class AbletonCommands
{
    private const string Normalised = "Live trabaja de 0 a 1, no en decibeles.";

    private const string ZoomStep = "Cuántas veces mover el zoom, como apretar + o - varias veces.";

    private const string ZoomPreset =
        "0 es todo afuera. El botón siempre llega al mismo lugar, venga de donde venga.";

    public static IReadOnlyList<AbletonCommandInfo> All { get; } = new[]
    {
        // ---------- Transport ----------
        New(AbletonCommand.Play, AbletonCommandGroup.Transport, "Reproducir"),
        New(AbletonCommand.Stop, AbletonCommandGroup.Transport, "Detener"),
        New(AbletonCommand.Continue, AbletonCommandGroup.Transport, "Continuar"),
        New(AbletonCommand.TogglePlay, AbletonCommandGroup.Transport, "Reproducir / detener"),
        New(AbletonCommand.StopAllClips, AbletonCommandGroup.Transport, "Detener todos los clips"),
        New(AbletonCommand.ToggleRecord, AbletonCommandGroup.Transport, "Grabar en Arrangement"),
        New(AbletonCommand.ToggleSessionRecord, AbletonCommandGroup.Transport, "Grabar en Session"),
        New(AbletonCommand.SetRecord, AbletonCommandGroup.Transport, "Rec Arrangement on/off",
            AbletonParameters.State),
        New(AbletonCommand.SetSessionRecord, AbletonCommandGroup.Transport, "Rec Session on/off",
            AbletonParameters.State),
        New(AbletonCommand.CaptureMidi, AbletonCommandGroup.Transport, "Capturar MIDI"),
        New(AbletonCommand.Undo, AbletonCommandGroup.Transport, "Deshacer"),
        New(AbletonCommand.Redo, AbletonCommandGroup.Transport, "Rehacer"),

        // ---------- Tempo ----------
        New(AbletonCommand.SetTempo, AbletonCommandGroup.Tempo, "Fijar tempo",
            AbletonParameters.Value, "BPM", TempoMinimum, TempoMaximum, 120,
            "Salta al valor, no hace rampa."),
        New(AbletonCommand.NudgeTempo, AbletonCommandGroup.Tempo, "Mover el tempo",
            AbletonParameters.Value, "BPM a sumar", -50, 50, 1,
            "Usá un valor negativo para bajarlo."),
        New(AbletonCommand.TapTempo, AbletonCommandGroup.Tempo, "Tap tempo"),
        New(AbletonCommand.ToggleMetronome, AbletonCommandGroup.Tempo, "Metrónomo"),
        New(AbletonCommand.SetMetronome, AbletonCommandGroup.Tempo, "Metrónomo on/off",
            AbletonParameters.State),
        New(AbletonCommand.ToggleLoop, AbletonCommandGroup.Tempo, "Loop"),
        New(AbletonCommand.SetLoop, AbletonCommandGroup.Tempo, "Loop on/off",
            AbletonParameters.State),

        // ---------- Tracks ----------
        New(AbletonCommand.SelectTrack, AbletonCommandGroup.Tracks, "Seleccionar track",
            AbletonParameters.Track),
        New(AbletonCommand.ToggleMute, AbletonCommandGroup.Tracks, "Mute", AbletonParameters.Track),
        New(AbletonCommand.ToggleSolo, AbletonCommandGroup.Tracks, "Solo", AbletonParameters.Track),
        New(AbletonCommand.ToggleArm, AbletonCommandGroup.Tracks, "Armar para grabar",
            AbletonParameters.Track),
        New(AbletonCommand.SetMute, AbletonCommandGroup.Tracks, "Mute on/off",
            AbletonParameters.Track | AbletonParameters.State),
        New(AbletonCommand.SetSolo, AbletonCommandGroup.Tracks, "Solo on/off",
            AbletonParameters.Track | AbletonParameters.State),
        New(AbletonCommand.SetArm, AbletonCommandGroup.Tracks, "Armar on/off",
            AbletonParameters.Track | AbletonParameters.State),
        New(AbletonCommand.SetVolume, AbletonCommandGroup.Tracks, "Fijar volumen",
            AbletonParameters.Track | AbletonParameters.Value, "Volumen", 0, 1, 0.85, Normalised),
        New(AbletonCommand.NudgeVolume, AbletonCommandGroup.Tracks, "Mover el volumen",
            AbletonParameters.Track | AbletonParameters.Value, "Cuánto sumar", -1, 1, 0.05, Normalised),
        New(AbletonCommand.SetPanning, AbletonCommandGroup.Tracks, "Fijar paneo",
            AbletonParameters.Track | AbletonParameters.Value, "Paneo", -1, 1, 0,
            "-1 es todo a la izquierda, 1 todo a la derecha."),
        New(AbletonCommand.NudgePanning, AbletonCommandGroup.Tracks, "Mover el paneo",
            AbletonParameters.Track | AbletonParameters.Value, "Cuánto sumar", -2, 2, 0.1,
            "Negativo mueve hacia la izquierda."),
        New(AbletonCommand.SetSend, AbletonCommandGroup.Tracks, "Fijar un send",
            AbletonParameters.Track | AbletonParameters.Send | AbletonParameters.Value,
            "Nivel del send", 0, 1, 0.35, Normalised),
        New(AbletonCommand.StopTrackClips, AbletonCommandGroup.Tracks, "Detener los clips del track",
            AbletonParameters.Track),
        New(AbletonCommand.CreateMidiTrack, AbletonCommandGroup.Tracks, "Crear track MIDI"),
        New(AbletonCommand.CreateAudioTrack, AbletonCommandGroup.Tracks, "Crear track de audio"),
        New(AbletonCommand.CreateReturnTrack, AbletonCommandGroup.Tracks, "Crear track de return"),
        New(AbletonCommand.DuplicateTrack, AbletonCommandGroup.Tracks, "Duplicar track",
            AbletonParameters.Track),
        New(AbletonCommand.DeleteTrack, AbletonCommandGroup.Tracks, "Eliminar track",
            AbletonParameters.Track),

        // ---------- Clips ----------
        New(AbletonCommand.FireClip, AbletonCommandGroup.Clips, "Disparar clip",
            AbletonParameters.Track | AbletonParameters.Scene),
        New(AbletonCommand.StopClip, AbletonCommandGroup.Clips, "Detener clip",
            AbletonParameters.Track | AbletonParameters.Scene),
        New(AbletonCommand.FireScene, AbletonCommandGroup.Clips, "Disparar escena",
            AbletonParameters.Scene),
        New(AbletonCommand.FireSelectedScene, AbletonCommandGroup.Clips,
            "Disparar la escena seleccionada"),
        New(AbletonCommand.SelectScene, AbletonCommandGroup.Clips, "Seleccionar escena",
            AbletonParameters.Scene),
        New(AbletonCommand.CreateScene, AbletonCommandGroup.Clips, "Crear escena"),

        // ---------- Views ----------
        New(AbletonCommand.ShowSession, AbletonCommandGroup.Views, "Ver Session"),
        New(AbletonCommand.ShowArrangement, AbletonCommandGroup.Views, "Ver Arrangement"),
        New(AbletonCommand.ToggleSessionArrangement, AbletonCommandGroup.Views,
            "Alternar Session / Arrangement"),
        New(AbletonCommand.ShowDetailClip, AbletonCommandGroup.Views, "Ver detalle del clip"),
        New(AbletonCommand.ShowDetailDevice, AbletonCommandGroup.Views, "Ver cadena de dispositivos"),
        New(AbletonCommand.ToggleDetail, AbletonCommandGroup.Views, "Mostrar / ocultar el detalle"),
        New(AbletonCommand.ToggleBrowser, AbletonCommandGroup.Views, "Mostrar / ocultar el Browser"),

        // ---------- Zoom ----------
        New(AbletonCommand.ZoomTimeIn, AbletonCommandGroup.Zoom, "Acercar en el tiempo",
            AbletonParameters.Value, "Pasos", 1, ZoomLevelMaximum, 3, ZoomStep),
        New(AbletonCommand.ZoomTimeOut, AbletonCommandGroup.Zoom, "Alejar en el tiempo",
            AbletonParameters.Value, "Pasos", 1, ZoomLevelMaximum, 3, ZoomStep),
        New(AbletonCommand.ZoomTimePreset, AbletonCommandGroup.Zoom, "Zoom fijo en el tiempo",
            AbletonParameters.Value, "Nivel", 0, ZoomLevelMaximum, 12, ZoomPreset),
        New(AbletonCommand.ZoomTracksIn, AbletonCommandGroup.Zoom, "Agrandar el alto de los tracks",
            AbletonParameters.Value, "Pasos", 1, ZoomLevelMaximum, 3, ZoomStep),
        New(AbletonCommand.ZoomTracksOut, AbletonCommandGroup.Zoom, "Achicar el alto de los tracks",
            AbletonParameters.Value, "Pasos", 1, ZoomLevelMaximum, 3, ZoomStep),
        New(AbletonCommand.ZoomTracksPreset, AbletonCommandGroup.Zoom, "Alto fijo de los tracks",
            AbletonParameters.Value, "Nivel", 0, ZoomLevelMaximum, 8, ZoomPreset),

        // ---------- File ----------
        // Live's API has no save/export, so these press the factory keyboard shortcut instead:
        // DigitalDeck trae Live al frente y manda las teclas. Andan con cualquier usuario mientras
        // no se haya cambiado el atajo en Preferencias de Live.
        New(AbletonCommand.ExportAudioVideo, AbletonCommandGroup.File, "Exportar audio/video",
            viaKeyboard: true,
            note: "Manda Ctrl+Shift+R a Live y aparece su diálogo de exportar. Necesita Live abierto."),
        New(AbletonCommand.SaveSet, AbletonCommandGroup.File, "Guardar el set",
            viaKeyboard: true,
            note: "Manda Ctrl+S a Live. Necesita Live abierto."),
        New(AbletonCommand.SaveSetAs, AbletonCommandGroup.File, "Guardar el set como…",
            viaKeyboard: true,
            note: "Manda Ctrl+Shift+S a Live y aparece su diálogo. Necesita Live abierto.")
    };

    /// <summary>
    /// Zoom notches from fully out to fully in.
    /// </summary>
    /// <remarks>
    /// Live's API only moves the zoom one notch at a time and never says where it ended up, so a
    /// preset gets there by bottoming out and counting back in. This has to be the same number the
    /// script bottoms out with — <c>ZOOM_FLOOR_STEPS</c> in <c>handlers/view.py</c> — or a preset
    /// would stop short of the level it promises. A test holds the two together.
    /// </remarks>
    public const double ZoomLevelMaximum = 60;

    /// <summary>Tempo bounds Live itself enforces; rejected here so the failure is legible.</summary>
    public const double TempoMinimum = 20;

    public const double TempoMaximum = 999;

    private static readonly Dictionary<AbletonCommand, AbletonCommandInfo> ByCommand =
        All.ToDictionary(info => info.Command);

    /// <summary>The catalogue entry for a command. Throws for a value outside the enum.</summary>
    public static AbletonCommandInfo Describe(AbletonCommand command) =>
        ByCommand.TryGetValue(command, out var info)
            ? info
            : throw new ArgumentOutOfRangeException(nameof(command), command, "Comando desconocido.");

    /// <summary>Commands in one group, in the order the editor should list them.</summary>
    public static IEnumerable<AbletonCommandInfo> InGroup(AbletonCommandGroup group) =>
        All.Where(info => info.Group == group);

    public static string GroupLabel(AbletonCommandGroup group) => group switch
    {
        AbletonCommandGroup.Transport => "Transporte",
        AbletonCommandGroup.Tempo => "Tempo",
        AbletonCommandGroup.Tracks => "Tracks",
        AbletonCommandGroup.Clips => "Clips",
        AbletonCommandGroup.Views => "Vistas",
        AbletonCommandGroup.Zoom => "Zoom",
        AbletonCommandGroup.File => "Archivo",
        _ => group.ToString()
    };

    private static AbletonCommandInfo New(
        AbletonCommand command,
        AbletonCommandGroup group,
        string label,
        AbletonParameters parameters = AbletonParameters.None,
        string? valueLabel = null,
        double minimum = 0,
        double maximum = 1,
        double @default = 0,
        string? hint = null,
        bool viaKeyboard = false,
        string? note = null) =>
        new(command, group, label, parameters, valueLabel, minimum, maximum, @default, hint, viaKeyboard, note);
}
