"""
Everything the handlers need, in one object.

Handlers get this instead of reaching for globals, which is what lets each handler module be read
on its own and keeps the Live-specific lookups in a single place.
"""

import Live

from .dispatch import ArgumentError

#: Track index meaning "whatever track is selected right now".
SELECTED_TRACK = -1

#: Track index meaning the master track.
MASTER_TRACK = -2

#: First of the indices that mean a return track: -3 is the first return, -4 the second.
#:
#: Returns live in their own collection in Live rather than inside song.tracks, so they need
#: somewhere of their own in the index space. Counting downwards from the master is what keeps
#: every index that already exists meaning exactly what it meant before -- a button saved against
#: track 3, or against the master, is untouched by returns arriving.
RETURN_TRACK_BASE = -3

#: How far the walk up a track's parents will go before deciding the graph lied about being a tree.
MAX_GROUP_DEPTH = 8

#: The kinds of channel a fader can address, as they go on the wire.
NORMAL = "normal"
GROUP = "group"
RETURN = "return"
MASTER = "master"


def return_track_index(position):
    """The index that addresses return `position`. The inverse of what `Context.track` resolves."""
    return RETURN_TRACK_BASE - position


def group_depth(track):
    """
    How many groups a track sits inside. Zero for a track at the top of the set.

    Walked rather than read off an attribute because Live only exposes the parent, one link at a
    time, and groups nest. The ceiling is there because a walk over someone else's object graph
    that trusts it to be acyclic is a frozen Live rather than a wrong number.
    """
    depth = 0
    parent = getattr(track, "group_track", None)

    while parent is not None and depth < MAX_GROUP_DEPTH:
        depth += 1
        parent = getattr(parent, "group_track", None)

    return depth


def fader(track):
    """The volume parameter of a channel, or None for something that has no mixer of its own."""
    mixer = getattr(track, "mixer_device", None)
    return getattr(mixer, "volume", None) if mixer is not None else None


def reading(parameter):
    """
    What Live prints under its own fader at this position -- "-6.02 dB", "-inf dB".

    Asked of Live rather than worked out here on purpose. The fader travel is linear in the 0..1
    the API takes, but the decibels behind it are not, and that curve is Ableton's rather than
    something they published. Reimplementing it in the app would produce a number that looks
    authoritative and drifts from what the user reads in Live the moment Ableton adjusts it.
    """
    to_text = getattr(parameter, "str_for_value", None)

    if not callable(to_text):
        return ""

    try:
        # Tabs and newlines would split the roster line this ends up in.
        return " ".join(str(to_text(parameter.value)).split())
    except Exception:  # noqa: BLE001
        # An accessor that moved in a newer Live. The fader still works; it loses its label.
        return ""


def mixer_tracks(song):
    """
    Every channel the mixer can drive, as ``(index, track, kind, depth)``, in drawing order.

    It lives in this file rather than in the handler because this is the file that owns what an
    index means. The roster the app draws and the index it sends back have to come from one rule:
    produced by two, a fader eventually writes to its neighbour.
    """
    for position, track in enumerate(song.tracks):
        # A group in Live is a real track with a real fader of its own, not a heading.
        kind = GROUP if getattr(track, "is_foldable", False) else NORMAL

        yield position, track, kind, group_depth(track)

    # Returns and the master sit outside the track tree entirely, so neither is ever nested.
    for position, track in enumerate(getattr(song, "return_tracks", None) or []):
        yield return_track_index(position), track, RETURN, 0

    master = getattr(song, "master_track", None)

    if master is not None:
        yield MASTER_TRACK, master, MASTER, 0


def track_state(track):
    """Mute, solo and arm for one channel, as three flags. Anything a channel lacks reads as off."""
    mute = 1 if getattr(track, "mute", False) else 0
    solo = 1 if getattr(track, "solo", False) else 0

    # Returns and the master have no arm at all, and asking a group for one gets an attribute that
    # is there but never true. can_be_armed is Live's own answer to "does this make sense here".
    arm = 1 if getattr(track, "can_be_armed", False) and getattr(track, "arm", False) else 0

    return mute, solo, arm


def selected_track_index(song):
    """
    The selected channel, in the same index space ``mixer_tracks`` hands out.

    Buttons are configured against "the selected track" far more often than against a number, so
    without this the deck knows a track is muted but not whether it is the one the button means.
    Falls back to SELECTED_TRACK, which the app reads as "not known", rather than to 0 -- guessing
    here would light the wrong button rather than none.
    """
    try:
        selected = song.view.selected_track
    except Exception:  # noqa: BLE001
        return SELECTED_TRACK

    for index, track, _kind, _depth in mixer_tracks(song):
        if track is selected:
            return index

    return SELECTED_TRACK


def track_states(song):
    """
    Every channel's mute, solo and arm plus the selected one, as the arguments of one message.

    The whole roster goes out rather than the value that moved, the same rule mixer.py follows for
    its own: indices here are positional, so one track arriving renumbers its neighbours, and a
    deck that had been told about them one at a time would be holding a map that is quietly wrong.
    """
    lines = []

    for index, track, _kind, _depth in mixer_tracks(song):
        mute, solo, arm = track_state(track)
        lines.append("\t".join((str(index), str(mute), str(solo), str(arm))))

    return [selected_track_index(song), len(lines), "\n".join(lines)]


class Context(object):
    def __init__(self, c_instance, send, log):
        self._c_instance = c_instance
        self.send = send
        self.log = log
        self._closers = []

    def on_close(self, callback):
        """
        Registers something to undo when the script shuts down.

        A handler that attaches Live listeners needs somewhere to give them back. Live tears the
        Song down without asking, and a listener still holding onto it is the kind of dangling
        reference that only a restart of Live clears.
        """
        self._closers.append(callback)

    def close(self):
        for callback in self._closers:
            try:
                callback()
            except Exception as error:  # noqa: BLE001
                self.log("could not close cleanly (%s)" % error)

        self._closers = []

    @property
    def application(self):
        return Live.Application.get_application()

    @property
    def song(self):
        return self._c_instance.song()

    @property
    def view(self):
        return self.song.view

    def track(self, index):
        """
        Resolves a track index the way the app means it.

        The negative values matter for usability: a deck button is configured once and then pressed
        while working, so "the selected track" is far more often what the user wants than "track 3".
        """
        song = self.song

        if index == SELECTED_TRACK:
            track = song.view.selected_track
            if track is None:
                raise ArgumentError("no track is selected")
            return track

        if index == MASTER_TRACK:
            return song.master_track

        if index <= RETURN_TRACK_BASE:
            returns = getattr(song, "return_tracks", None) or []
            position = RETURN_TRACK_BASE - index

            if position >= len(returns):
                raise ArgumentError(
                    "there is no return %d (the set has %d)" % (position, len(returns)))

            return returns[position]

        if index < 0 or index >= len(song.tracks):
            raise ArgumentError("there is no track %d (the set has %d)" % (index, len(song.tracks)))

        return song.tracks[index]

    def scene(self, index):
        song = self.song

        if index < 0:
            scene = song.view.selected_scene
            if scene is None:
                raise ArgumentError("no scene is selected")
            return scene

        if index >= len(song.scenes):
            raise ArgumentError("there is no scene %d (the set has %d)" % (index, len(song.scenes)))

        return song.scenes[index]

    def clip_slot(self, track_index, scene_index):
        track = self.track(track_index)
        slots = getattr(track, "clip_slots", None)

        if not slots:
            raise ArgumentError("that track has no clip slots")

        if scene_index < 0:
            # Mirror the scene rule: a negative index means the selected row.
            scene_index = list(self.song.scenes).index(self.song.view.selected_scene)

        if scene_index >= len(slots):
            raise ArgumentError("there is no clip slot %d on that track" % scene_index)

        return slots[scene_index]
