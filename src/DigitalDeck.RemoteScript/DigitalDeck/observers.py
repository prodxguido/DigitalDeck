"""
Pushes state changes to the app without being asked.

Polling from C# would mean either a slow indicator or a stream of UDP traffic every frame. Live
already publishes these as listeners, so the app is told once when something actually changes and
can keep a cache that is correct the rest of the time.
"""

from .context import fader, mixer_tracks, reading, track_states


class SongObservers(object):
    #: (attribute on Song, add/remove suffix, OSC address, how to convert the value)
    WATCHED = (
        ("is_playing", "is_playing", "/live/song/get/is_playing", lambda value: 1 if value else 0),
        ("tempo", "tempo", "/live/song/get/tempo", float),
        ("metronome", "metronome", "/live/song/get/metronome", lambda value: 1 if value else 0),
        ("record_mode", "record_mode", "/live/song/get/record_mode", lambda value: 1 if value else 0),
    )

    def __init__(self, song, send, log):
        self._song = song
        self._send = send
        self._log = log
        self._attached = []

        for attribute, listener, address, convert in self.WATCHED:
            self._attach(attribute, listener, address, convert)

    def _attach(self, attribute, listener, address, convert):
        add = getattr(self._song, "add_%s_listener" % listener, None)

        if not callable(add):
            # An attribute that moved in a newer Live. The bridge still works; only the unsolicited
            # update for this one value is lost, and the app can still query it.
            self._log("no listener for %s on this version of Live" % attribute)
            return

        def on_change():
            try:
                self._send(address, [convert(getattr(self._song, attribute))])
            except Exception as error:  # noqa: BLE001
                self._log("could not publish %s (%s)" % (attribute, error))

        try:
            add(on_change)
        except Exception as error:  # noqa: BLE001
            self._log("could not observe %s (%s)" % (attribute, error))
            return

        self._attached.append((listener, on_change))

    def publish_all(self):
        """Sends the current value of everything watched. Used right after a client connects."""
        for attribute, _listener, address, convert in self.WATCHED:
            try:
                self._send(address, [convert(getattr(self._song, attribute))])
            except Exception as error:  # noqa: BLE001
                self._log("could not publish %s (%s)" % (attribute, error))

    def disconnect(self):
        for listener, callback in self._attached:
            remove = getattr(self._song, "remove_%s_listener" % listener, None)

            try:
                if callable(remove):
                    remove(callback)
            except Exception:  # noqa: BLE001
                pass

        self._attached = []


#: Where a volume change is announced. The same address the query in track.py answers on, so the
#: app reads one shape whether it asked or was told -- the rule SongObservers already follows.
VOLUME_ADDRESS = "/live/track/get/volume"


class TrackObservers(object):
    """
    Volume changes made inside Live, pushed to the mixer window while it is open.

    Attached on demand rather than for the life of the script, which is the difference that matters
    here: a set with forty channels means forty listeners, and every one of them is a datagram the
    app has nowhere to draw when the mixer window is closed. Nothing is attached until the window
    asks, and everything comes off when it goes away.
    """

    def __init__(self, ctx, roster):
        self._ctx = ctx
        self._roster = roster
        self._parameters = []
        self._names = []
        self._song = []
        self._running = False

    @property
    def is_running(self):
        return self._running

    # ---------- Switching on and off ----------

    def start(self):
        # A repeated subscribe is normal: the app re-sends it after the bridge reconnects, and the
        # window it belongs to has no way to know whether the last one arrived. Re-attaching on top
        # of what is already there would double every message the set sends.
        if self._running:
            self._attach_tracks()
            return

        self._running = True
        self._watch_roster()
        self._attach_tracks()

    def stop(self):
        self._running = False
        self._detach_tracks()

        for name, callback in self._song:
            self._remove(self._ctx.song, name, callback)

        self._song = []

    # ---------- The roster ----------

    def publish_roster(self):
        """Sends the whole channel list. Used on subscribe, and whenever Live's changes."""
        try:
            address, args = self._roster()
        except Exception as error:  # noqa: BLE001
            self._ctx.log("could not build the mixer roster (%s)" % error)
            return

        self._ctx.send(address, args)

    def _watch_roster(self):
        song = self._ctx.song

        for name in ("tracks", "return_tracks"):
            if self._add(song, name, self._on_roster_changed):
                self._song.append((name, self._on_roster_changed))

    def _on_roster_changed(self):
        # Indices are positional, so a track appearing or leaving renumbers everything to its
        # right. The listeners have to be rebuilt against the new numbering before anything is
        # published, or the next volume change is announced under its neighbour's index.
        self._attach_tracks()
        self.publish_roster()

    # ---------- Per-channel listeners ----------

    def _attach_tracks(self):
        self._detach_tracks()

        for index, track, _kind, _depth in mixer_tracks(self._ctx.song):
            self._watch_volume(index, track)
            self._watch_name(track)

    def _detach_tracks(self):
        for parameter, callback in self._parameters:
            self._remove(parameter, "value", callback)

        for track, callback in self._names:
            self._remove(track, "name", callback)

        self._parameters = []
        self._names = []

    def _watch_volume(self, index, track):
        parameter = fader(track)

        if parameter is None:
            return

        def on_change():
            try:
                self._ctx.send(
                    VOLUME_ADDRESS, [index, float(parameter.value), reading(parameter)])
            except Exception as error:  # noqa: BLE001
                self._ctx.log("could not publish the volume of %d (%s)" % (index, error))

        if self._add(parameter, "value", on_change):
            self._parameters.append((parameter, on_change))

    def _watch_name(self, track):
        # The name rides in the roster line, so a rename in Live has to resend the roster rather
        # than leave the window labelling a fader with something that is no longer true.
        def on_change():
            self.publish_roster()

        if self._add(track, "name", on_change):
            self._names.append((track, on_change))

    # ---------- Plumbing ----------

    def _add(self, target, name, callback):
        add = getattr(target, "add_%s_listener" % name, None)

        if not callable(add):
            # An accessor that moved in a newer Live. The mixer still drives Live; only the
            # unsolicited update is lost, and the window can still be reopened to re-read.
            self._ctx.log("no listener for %s on this version of Live" % name)
            return False

        try:
            add(callback)
        except Exception as error:  # noqa: BLE001
            self._ctx.log("could not observe %s (%s)" % (name, error))
            return False

        return True

    @staticmethod
    def _remove(target, name, callback):
        remove = getattr(target, "remove_%s_listener" % name, None)

        try:
            if callable(remove):
                remove(callback)
        except Exception:  # noqa: BLE001
            pass


#: Where the mute, solo and arm of every channel is announced, and answered when asked.
TRACK_STATES_ADDRESS = "/digitaldeck/track/states"


class TrackStateObservers(object):
    """
    Mute, solo and arm for every channel, and which one is selected, pushed to the deck.

    Attached for the whole life of the script, unlike the volume listeners above, and the
    difference between the two is what they cost. A fader moves with a finger and produces a
    message every frame, so it is only worth watching while a window is drawing it. Mute, solo and
    arm move when somebody presses them, which is as often as a person can press -- cheap enough
    that every button on the grid can stay lit without asking anyone to subscribe first.

    What is watched has to be rebuilt whenever the set of tracks changes, because the listeners are
    attached to the track objects themselves and Live hands out new ones.
    """

    def __init__(self, ctx):
        self._ctx = ctx
        self._attached = []
        self._song = []

    def start(self):
        self._watch_song()
        self._attach_tracks()
        self.publish()

    def stop(self):
        self._detach_tracks()

        for target, name, callback in self._song:
            _unlisten(target, name, callback)

        self._song = []

    def publish(self):
        """Sends the state of every channel. Used on any change, and to announce the first one."""
        try:
            args = track_states(self._ctx.song)
        except Exception as error:  # noqa: BLE001
            self._ctx.log("could not read the track states (%s)" % error)
            return

        self._ctx.send(TRACK_STATES_ADDRESS, args)

    # ---------- What is watched ----------

    def _watch_song(self):
        song = self._ctx.song

        for name in ("tracks", "return_tracks"):
            if _listen(song, name, self._on_roster_changed, self._ctx.log):
                self._song.append((song, name, self._on_roster_changed))

        view = getattr(song, "view", None)

        if view is not None and _listen(view, "selected_track", self.publish, self._ctx.log):
            self._song.append((view, "selected_track", self.publish))

    def _on_roster_changed(self):
        # The listeners hang off the track objects, and a set that gained or lost one has a new
        # list of them. Rebuilding before publishing is what stops a mute arriving under the index
        # of the track that used to be there.
        self._attach_tracks()
        self.publish()

    def _attach_tracks(self):
        self._detach_tracks()

        for _index, track, _kind, _depth in mixer_tracks(self._ctx.song):
            for name in ("mute", "solo", "arm"):
                if _listen(track, name, self.publish, self._ctx.log):
                    self._attached.append((track, name, self.publish))

    def _detach_tracks(self):
        for target, name, callback in self._attached:
            _unlisten(target, name, callback)

        self._attached = []


def _listen(target, name, callback, log):
    """Attaches one listener. False when this version of Live does not have it, which is not fatal."""
    add = getattr(target, "add_%s_listener" % name, None)

    if not callable(add):
        # Master and return tracks have no arm, and an accessor may have moved in a newer Live.
        # Either way the deck still drives Live; it only loses the light on that one thing.
        return False

    try:
        add(callback)
    except Exception as error:  # noqa: BLE001
        log("could not observe %s (%s)" % (name, error))
        return False

    return True


def _unlisten(target, name, callback):
    remove = getattr(target, "remove_%s_listener" % name, None)

    try:
        if callable(remove):
            remove(callback)
    except Exception:  # noqa: BLE001
        pass
