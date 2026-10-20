"""Transport, tempo, metronome, undo and track creation -- everything that hangs off Song."""

from ..dispatch import as_bool, as_float, as_int, ArgumentError, clamp

# Live refuses anything outside this and the failure is not obvious from the app side, so it is
# checked here as well as in C#.
TEMPO_MIN = 20.0
TEMPO_MAX = 999.0


def register(dispatcher, ctx):
    add = dispatcher.register

    # ---------- Transport ----------

    add("/live/song/start_playing", lambda args: _call(ctx.song.start_playing))
    add("/live/song/stop_playing", lambda args: _call(ctx.song.stop_playing))
    add("/live/song/continue_playing", lambda args: _call(ctx.song.continue_playing))
    add("/live/song/stop_all_clips", lambda args: _call(ctx.song.stop_all_clips))

    def toggle_playing(args):
        song = ctx.song
        if song.is_playing:
            song.stop_playing()
        else:
            song.start_playing()

    add("/live/song/toggle/playing", toggle_playing)

    add("/live/song/get/is_playing",
        lambda args: ("/live/song/get/is_playing", [1 if ctx.song.is_playing else 0]))

    # ---------- Record ----------
    #
    # Two different records, and confusing them is a classic support question: record_mode arms the
    # Arrangement, session_record arms the Session grid. Both are exposed under their own name.

    add("/live/song/set/record_mode", lambda args: _set(ctx.song, "record_mode", as_bool(args, 0)))
    add("/live/song/toggle/record_mode", lambda args: _toggle(ctx.song, "record_mode"))
    add("/live/song/get/record_mode",
        lambda args: ("/live/song/get/record_mode", [1 if ctx.song.record_mode else 0]))

    add("/live/song/set/session_record",
        lambda args: _set(ctx.song, "session_record", as_bool(args, 0)))
    add("/live/song/toggle/session_record", lambda args: _toggle(ctx.song, "session_record"))

    add("/live/song/capture_midi", lambda args: _call(ctx.song.capture_midi))

    # ---------- Tempo ----------

    def set_tempo(args):
        value = as_float(args, 0)
        if value < TEMPO_MIN or value > TEMPO_MAX:
            raise ArgumentError("tempo must be between %g and %g" % (TEMPO_MIN, TEMPO_MAX))

        ctx.song.tempo = value

    add("/live/song/set/tempo", set_tempo)

    def nudge_tempo(args):
        song = ctx.song
        song.tempo = clamp(song.tempo + as_float(args, 0), TEMPO_MIN, TEMPO_MAX)

    add("/live/song/nudge/tempo", nudge_tempo)

    add("/live/song/get/tempo", lambda args: ("/live/song/get/tempo", [float(ctx.song.tempo)]))
    add("/live/song/tap_tempo", lambda args: _call(ctx.song.tap_tempo))

    # ---------- Metronome and loop ----------

    add("/live/song/set/metronome", lambda args: _set(ctx.song, "metronome", as_bool(args, 0)))
    add("/live/song/toggle/metronome", lambda args: _toggle(ctx.song, "metronome"))
    add("/live/song/get/metronome",
        lambda args: ("/live/song/get/metronome", [1 if ctx.song.metronome else 0]))

    add("/live/song/set/loop", lambda args: _set(ctx.song, "loop", as_bool(args, 0)))
    add("/live/song/toggle/loop", lambda args: _toggle(ctx.song, "loop"))

    # ---------- Edit ----------

    add("/live/song/undo", lambda args: _call(ctx.song.undo))
    add("/live/song/redo", lambda args: _call(ctx.song.redo))

    # ---------- Tracks ----------
    #
    # Index -1 means "at the end", which is what Live's own create_* calls expect.

    add("/live/song/create_midi_track",
        lambda args: _call(ctx.song.create_midi_track, as_int(args, 0, -1)))
    add("/live/song/create_audio_track",
        lambda args: _call(ctx.song.create_audio_track, as_int(args, 0, -1)))
    add("/live/song/create_return_track", lambda args: _call(ctx.song.create_return_track))
    add("/live/song/create_scene", lambda args: _call(ctx.song.create_scene, as_int(args, 0, -1)))

    def duplicate_track(args):
        song = ctx.song
        index = _existing_track_index(ctx, as_int(args, 0, -1))
        song.duplicate_track(index)

    add("/live/song/duplicate_track", duplicate_track)

    def delete_track(args):
        song = ctx.song
        index = _existing_track_index(ctx, as_int(args, 0, -1))
        if len(song.tracks) <= 1:
            raise ArgumentError("a set needs at least one track")

        song.delete_track(index)

    add("/live/song/delete_track", delete_track)


def _existing_track_index(ctx, index):
    """Turns the app's -1 ("selected") into a real index, which is what Song's calls need."""
    song = ctx.song

    if index >= 0:
        if index >= len(song.tracks):
            raise ArgumentError("there is no track %d (the set has %d)" % (index, len(song.tracks)))
        return index

    selected = song.view.selected_track
    tracks = list(song.tracks)

    if selected not in tracks:
        # Return and master tracks are selectable but cannot be duplicated or deleted this way.
        raise ArgumentError("the selected track is not a regular track")

    return tracks.index(selected)


def _call(fn, *args):
    fn(*args)


def _set(target, name, value):
    setattr(target, name, value)


def _toggle(target, name):
    setattr(target, name, not getattr(target, name))
