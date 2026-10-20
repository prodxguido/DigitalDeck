using System.Globalization;
using DigitalDeck.Core.Abstractions;
using DigitalDeck.Core.Model;
using DigitalDeck.Core.Model.Actions;
using DigitalDeck.Integrations.Ableton.Osc;

namespace DigitalDeck.Integrations.Ableton;

/// <summary>
/// Turns an <see cref="AbletonAction"/> into the OSC message the Remote Script expects.
/// </summary>
/// <remarks>
/// This is the only place in the .NET side that knows what an address looks like. The catalogue in
/// Core carries no addresses on purpose, so the two tables cannot drift into disagreement about
/// what a command <em>means</em>; a test asserts every enum value is mapped here, which is what
/// catches the other kind of drift — a command added to Core and forgotten on the wire.
/// <para>
/// The address space mirrors AbletonOSC's <c>/live/...</c> wherever an equivalent exists, so
/// anything learned from its documentation transfers. Extensions of our own live under
/// <c>/digitaldeck/...</c>.
/// </para>
/// </remarks>
public static class AbletonCommandMap
{
    public const string PingAddress = "/digitaldeck/ping";
    public const string PongAddress = "/digitaldeck/pong";
    public const string ErrorAddress = "/digitaldeck/error";

    /// <summary>Where a browser item gets loaded into the selected track.</summary>
    public const string LoadAddress = "/digitaldeck/browser/load";

    /// <summary>One level of the browser tree. The script answers on the same address.</summary>
    public const string ListAddress = "/digitaldeck/browser/list";

    /// <summary>A search across a category. The script answers on the same address.</summary>
    public const string SearchAddress = "/digitaldeck/browser/search";

    /// <summary>Which categories this Live exposes. The script answers on the same address.</summary>
    public const string CategoriesAddress = "/digitaldeck/browser/categories";

    /// <summary>Moves the zoom a number of notches; the sign says which way.</summary>
    public const string ZoomAddress = "/digitaldeck/view/zoom";

    /// <summary>Puts the zoom at a level counted from fully zoomed out.</summary>
    public const string ZoomPresetAddress = "/digitaldeck/view/zoom_preset";

    /// <summary>Zoom along the timeline. Matches <c>TIME</c> in the script's view handler.</summary>
    public const string TimeAxis = "time";

    /// <summary>Zoom the track heights. Matches <c>TRACKS</c> in the script's view handler.</summary>
    public const string TracksAxis = "tracks";

    /// <summary>What the script says after a load actually went through.</summary>
    public const string LoadedAddress = "/digitaldeck/browser/loaded";

    /// <summary>
    /// Every channel of Live's mixer at once. Asked on, answered on, and pushed on unprompted
    /// whenever the set of tracks changes.
    /// </summary>
    public const string MixerTracksAddress = "/digitaldeck/mixer/tracks";

    /// <summary>Turns Live's unsolicited fader updates on and off. Answers with the roster.</summary>
    public const string MixerSubscribeAddress = "/digitaldeck/mixer/subscribe";

    /// <summary>
    /// One channel's fader position.
    /// </summary>
    /// <remarks>
    /// The same address the script answers a volume query on, because the script publishes changes
    /// there too — the convention its song observers already follow. Nothing in the app asks this
    /// question, so anything arriving here is Live saying a fader moved.
    /// </remarks>
    public const string TrackVolumeAddress = "/live/track/get/volume";

    /// <summary>
    /// The four things about the set that the script watches for its whole life: whether it is
    /// playing, the tempo, the metronome, and the Arrangement record.
    /// </summary>
    /// <remarks>
    /// Each is both a question and an announcement, the same convention as
    /// <see cref="TrackVolumeAddress"/>: the app asks once when the bridge comes up and is told
    /// afterwards, and both arrive in the same shape on the same address.
    /// </remarks>
    public const string SongIsPlayingAddress = "/live/song/get/is_playing";

    /// <inheritdoc cref="SongIsPlayingAddress"/>
    public const string SongTempoAddress = "/live/song/get/tempo";

    /// <inheritdoc cref="SongIsPlayingAddress"/>
    public const string SongMetronomeAddress = "/live/song/get/metronome";

    /// <inheritdoc cref="SongIsPlayingAddress"/>
    public const string SongRecordAddress = "/live/song/get/record_mode";

    /// <summary>
    /// Every channel's mute, solo and arm, plus the selected channel, in one message.
    /// </summary>
    /// <remarks>
    /// Whole rather than per-channel, and for the same reason as <see cref="MixerTracksAddress"/>:
    /// indices are positional, so one track arriving renumbers its neighbours and a deck told about
    /// them one at a time would be holding a map that is quietly wrong.
    /// </remarks>
    public const string TrackStatesAddress = "/digitaldeck/track/states";

    /// <summary>Separates the fields of one result line. The script promises tabs never appear in a name.</summary>
    private const char FieldSeparator = '\t';

    /// <summary>Separates the parts of a correlation key. Not a character an address or path carries.</summary>
    private const char KeySeparator = '\u001f';

    /// <summary>
    /// Builds the load message.
    /// </summary>
    /// <remarks>
    /// All three fields go out together on purpose: the script matches on the URI when it has
    /// one and falls back to the name inside the given category otherwise. That fallback is what
    /// keeps a button working after a Pack is reinstalled and every URI in it changes.
    /// </remarks>
    public static OscMessage ToMessage(AbletonLoadDeviceAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return new OscMessage(
            LoadAddress,
            action.Uri?.Trim() ?? string.Empty,
            action.Category?.Trim() ?? string.Empty,
            action.Name?.Trim() ?? string.Empty);
    }

    // ---------- Questions, and the answers to them ----------
    //
    // The script replies on the same address it was asked on, and echoes back what identified the
    // question: the category and path for a listing, the category and query for a search. That
    // echo is the correlation key -- enough to tell two answers apart without adding a request id
    // to a protocol that does not have one, and without the script needing to change.

    public static OscMessage ListRequest(string category, string path) =>
        new(ListAddress, category, path);

    public static OscMessage SearchRequest(string category, string query, int limit) =>
        new(SearchAddress, category, query, limit);

    public static OscMessage CategoriesRequest() => new(CategoriesAddress);

    // ---------- The mixer ----------

    public static OscMessage MixerTracksRequest() => new(MixerTracksAddress);

    public static OscMessage MixerSubscribeRequest(bool on) => new(MixerSubscribeAddress, on ? 1 : 0);

    /// <summary>Moves one fader. The value is Live's normalised 0..1, not decibels.</summary>
    public static OscMessage SetVolumeRequest(int trackIndex, double value) =>
        new("/live/track/set/volume", trackIndex, (float)value);

    /// <summary>
    /// Reads the channel roster.
    /// </summary>
    /// <remarks>
    /// A malformed line is dropped rather than thrown over: the roster is what the whole window is
    /// drawn from, and one channel Live described in a way a future version changed is a missing
    /// fader, not a reason to show nothing at all.
    /// </remarks>
    public static IReadOnlyList<LiveTrack> ParseRoster(OscMessage reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        var tracks = new List<LiveTrack>();

        foreach (var line in (reply.GetString(1) ?? string.Empty).Split('\n'))
        {
            if (line.Length == 0) continue;

            var fields = line.Split(FieldSeparator);
            if (fields.Length < 5) continue;

            if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                continue;

            tracks.Add(new LiveTrack(
                index,
                fields[1],
                ParseKind(fields[2]),
                int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var colour)
                    ? colour
                    : 0,
                double.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var volume)
                    ? Math.Clamp(volume, 0, 1)
                    : 0,
                // Scripts before 2.5.0 do not send the depth. A flat mixer is the right fallback:
                // wrong indentation would be a worse lie than none.
                fields.Length > 5
                && int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var depth)
                    ? Math.Clamp(depth, 0, 8)
                    : 0,
                fields.Length > 6 ? fields[6] : string.Empty));
        }

        return tracks;
    }

    /// <summary>Reads one track-states message: the selected channel and every channel's switches.</summary>
    public static LiveTracksState ParseTrackStates(OscMessage reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        var states = new List<LiveTrackState>();

        foreach (var line in (reply.GetString(2) ?? string.Empty).Split('\n'))
        {
            if (line.Length == 0) continue;

            var fields = line.Split(FieldSeparator);
            if (fields.Length < 4) continue;

            if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                continue;

            states.Add(new LiveTrackState(index, Flag(fields[1]), Flag(fields[2]), Flag(fields[3])));
        }

        return new LiveTracksState(states, reply.GetInt(0));
    }

    /// <summary>The script writes these as 0 and 1. Anything else is read as off.</summary>
    private static bool Flag(string field) => field == "1";

    /// <summary>Anything unrecognised is drawn as an ordinary channel rather than dropped.</summary>
    private static LiveTrackKind ParseKind(string kind) => kind switch
    {
        "group" => LiveTrackKind.Group,
        "return" => LiveTrackKind.Return,
        "master" => LiveTrackKind.Master,
        _ => LiveTrackKind.Normal
    };

    public static string ListKey(string category, string path) => Key(category, path);

    /// <summary>
    /// The key a search reply will carry.
    /// </summary>
    /// <remarks>
    /// Lower-cased and trimmed because the script does exactly that to the query before echoing
    /// it back. Building the key from the raw text instead would mean no reply ever matched the
    /// question that asked for it.
    /// </remarks>
    public static string SearchKey(string category, string query) =>
        Key(category, query.Trim().ToLowerInvariant());

    /// <summary>The correlation key a reply carries, or null when it is not an answer to a question.</summary>
    public static string? KeyOf(OscMessage reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        return reply.Address switch
        {
            ListAddress or SearchAddress => Key(reply.GetString(0) ?? string.Empty, reply.GetString(1) ?? string.Empty),
            CategoriesAddress => string.Empty,
            _ => null
        };
    }

    private static string Key(string category, string rest) => $"{category}{KeySeparator}{rest}";

    /// <summary>
    /// Reads a listing or search reply.
    /// </summary>
    /// <remarks>
    /// The third argument means opposite things in the two replies -- a listing reports how many
    /// it had to cut, a search reports whether it got to the end -- so both are normalised here
    /// into one honest flag rather than left for the UI to remember.
    /// </remarks>
    public static AbletonBrowserListing ParseListing(OscMessage reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        var category = reply.GetString(0) ?? string.Empty;
        var path = reply.GetString(1) ?? string.Empty;
        var flag = reply.GetInt(2);
        var complete = reply.Address == SearchAddress ? flag != 0 : flag == 0;

        var entries = new List<AbletonBrowserEntry>();
        var payload = reply.GetString(3) ?? string.Empty;

        foreach (var line in payload.Split('\n'))
        {
            if (line.Length == 0) continue;

            var fields = line.Split(FieldSeparator);
            var name = fields.Length > 0 ? fields[0] : string.Empty;

            if (name.Length == 0) continue;

            var isFolder = fields.Length > 3 && fields[3] == "1";

            entries.Add(new AbletonBrowserEntry(
                name,
                fields.Length > 1 ? fields[1] : string.Empty,
                fields.Length > 2 && fields[2] == "1",
                isFolder,
                // Scripts before 2.2.0 do not send this field. Falling back to is_folder gives
                // them the behaviour they had rather than a picker that refuses to open anything.
                fields.Length > 4 ? fields[4] == "1" : isFolder));
        }

        return AbletonBrowserListing.Ok(category, path, entries, complete);
    }

    /// <summary>Reads the categories reply, which is one newline-separated list and nothing else.</summary>
    public static IReadOnlyList<string> ParseCategories(OscMessage reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        return (reply.GetString(0) ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static OscMessage ToMessage(AbletonAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var track = action.TrackIndex;
        var scene = action.SceneIndex;
        var send = action.SendIndex;

        // The script coerces with as_bool, which reads an int the way everything else on the wire
        // does. Sending a bool type tag instead would work too, and would be one more shape for
        // the codec on both sides to agree about for no gain.
        var state = action.State ? 1 : 0;

        // Live's own calls take a float, and sending an int where a float is expected is the kind
        // of mismatch that fails silently rather than loudly.
        var value = (float)action.Value;

        // Zoom is counted in notches, so it goes out as a whole number: the script loops on it.
        var steps = (int)Math.Round(action.Value, MidpointRounding.AwayFromZero);

        return action.Command switch
        {
            // ---------- Transport ----------
            AbletonCommand.Play => new OscMessage("/live/song/start_playing"),
            AbletonCommand.Stop => new OscMessage("/live/song/stop_playing"),
            AbletonCommand.Continue => new OscMessage("/live/song/continue_playing"),
            AbletonCommand.TogglePlay => new OscMessage("/live/song/toggle/playing"),
            AbletonCommand.StopAllClips => new OscMessage("/live/song/stop_all_clips"),
            AbletonCommand.ToggleRecord => new OscMessage("/live/song/toggle/record_mode"),
            AbletonCommand.ToggleSessionRecord => new OscMessage("/live/song/toggle/session_record"),
            AbletonCommand.SetRecord => new OscMessage("/live/song/set/record_mode", state),
            AbletonCommand.SetSessionRecord => new OscMessage("/live/song/set/session_record", state),
            AbletonCommand.CaptureMidi => new OscMessage("/live/song/capture_midi"),
            AbletonCommand.Undo => new OscMessage("/live/song/undo"),
            AbletonCommand.Redo => new OscMessage("/live/song/redo"),

            // ---------- Tempo ----------
            AbletonCommand.SetTempo => new OscMessage("/live/song/set/tempo", value),
            AbletonCommand.NudgeTempo => new OscMessage("/live/song/nudge/tempo", value),
            AbletonCommand.TapTempo => new OscMessage("/live/song/tap_tempo"),
            AbletonCommand.ToggleMetronome => new OscMessage("/live/song/toggle/metronome"),
            AbletonCommand.SetMetronome => new OscMessage("/live/song/set/metronome", state),
            AbletonCommand.ToggleLoop => new OscMessage("/live/song/toggle/loop"),
            AbletonCommand.SetLoop => new OscMessage("/live/song/set/loop", state),

            // ---------- Tracks ----------
            AbletonCommand.SelectTrack => new OscMessage("/live/view/select_track", track),
            AbletonCommand.ToggleMute => new OscMessage("/live/track/toggle/mute", track),
            AbletonCommand.ToggleSolo => new OscMessage("/live/track/toggle/solo", track),
            AbletonCommand.ToggleArm => new OscMessage("/live/track/toggle/arm", track),
            AbletonCommand.SetMute => new OscMessage("/live/track/set/mute", track, state),
            AbletonCommand.SetSolo => new OscMessage("/live/track/set/solo", track, state),
            AbletonCommand.SetArm => new OscMessage("/live/track/set/arm", track, state),
            AbletonCommand.SetVolume => new OscMessage("/live/track/set/volume", track, value),
            AbletonCommand.NudgeVolume => new OscMessage("/live/track/nudge/volume", track, value),
            AbletonCommand.SetPanning => new OscMessage("/live/track/set/panning", track, value),
            AbletonCommand.NudgePanning => new OscMessage("/live/track/nudge/panning", track, value),
            AbletonCommand.SetSend => new OscMessage("/live/track/set/send", track, send, value),
            AbletonCommand.StopTrackClips => new OscMessage("/live/track/stop_all_clips", track),
            AbletonCommand.CreateMidiTrack => new OscMessage("/live/song/create_midi_track", -1),
            AbletonCommand.CreateAudioTrack => new OscMessage("/live/song/create_audio_track", -1),
            AbletonCommand.CreateReturnTrack => new OscMessage("/live/song/create_return_track"),
            AbletonCommand.DuplicateTrack => new OscMessage("/live/song/duplicate_track", track),
            AbletonCommand.DeleteTrack => new OscMessage("/live/song/delete_track", track),

            // ---------- Clips ----------
            AbletonCommand.FireClip => new OscMessage("/live/clip_slot/fire", track, scene),
            AbletonCommand.StopClip => new OscMessage("/live/clip_slot/stop", track, scene),
            AbletonCommand.FireScene => new OscMessage("/live/scene/fire", scene),
            AbletonCommand.FireSelectedScene => new OscMessage("/live/scene/fire_selected"),
            AbletonCommand.SelectScene => new OscMessage("/live/view/select_scene", scene),

            // -1 puts it at the end, which is where "another scene" almost always means.
            AbletonCommand.CreateScene => new OscMessage("/live/song/create_scene", -1),

            // ---------- Views ----------
            AbletonCommand.ShowSession => new OscMessage("/live/view/show_session"),
            AbletonCommand.ShowArrangement => new OscMessage("/live/view/show_arrangement"),
            AbletonCommand.ToggleSessionArrangement =>
                new OscMessage("/live/view/toggle_session_arrangement"),
            AbletonCommand.ShowDetailClip => new OscMessage("/live/view/show_detail_clip"),
            AbletonCommand.ShowDetailDevice => new OscMessage("/live/view/show_detail_device"),
            AbletonCommand.ToggleDetail => new OscMessage("/live/view/toggle_detail"),
            AbletonCommand.ToggleBrowser => new OscMessage("/live/view/toggle_browser"),

            // ---------- Zoom ----------
            //
            // Live has no zoom in its own OSC vocabulary and AbletonOSC never gave it one, so these
            // are ours. The sign carries the direction, which keeps one address doing both halves
            // of a job that is symmetrical anyway.
            AbletonCommand.ZoomTimeIn => new OscMessage(ZoomAddress, TimeAxis, steps),
            AbletonCommand.ZoomTimeOut => new OscMessage(ZoomAddress, TimeAxis, -steps),
            AbletonCommand.ZoomTimePreset => new OscMessage(ZoomPresetAddress, TimeAxis, steps),
            AbletonCommand.ZoomTracksIn => new OscMessage(ZoomAddress, TracksAxis, steps),
            AbletonCommand.ZoomTracksOut => new OscMessage(ZoomAddress, TracksAxis, -steps),
            AbletonCommand.ZoomTracksPreset => new OscMessage(ZoomPresetAddress, TracksAxis, steps),

            _ when AbletonCommands.Describe(action.Command).ViaKeyboard => throw new InvalidOperationException(
                $"«{AbletonCommands.Describe(action.Command).Label}» no va por el puente OSC: se manda como atajo de teclado a Live."),

            _ => throw new ArgumentOutOfRangeException(
                nameof(action), action.Command, "Ese comando de Ableton no está mapeado.")
        };
    }

    /// <summary>
    /// The Live shortcut a keyboard-driven command stands for.
    /// </summary>
    /// <remarks>
    /// These commands never reach <see cref="ToMessage"/> — the action handler checks
    /// <see cref="AbletonCommandInfo.ViaKeyboard"/> first and routes them here instead.
    /// </remarks>
    public static LiveShortcut ToShortcut(AbletonCommand command) => command switch
    {
        AbletonCommand.ExportAudioVideo => LiveShortcut.ExportAudioVideo,
        AbletonCommand.SaveSet => LiveShortcut.SaveSet,
        AbletonCommand.SaveSetAs => LiveShortcut.SaveSetAs,
        _ => throw new ArgumentOutOfRangeException(
            nameof(command), command, "Ese comando de Ableton no se manda por teclado.")
    };
}
