"""
A stand-in for the Live Object Model.

Importing this installs a fake ``Live`` module, which is what lets the handler modules -- the part
of the script with the actual address table in it -- be imported and exercised outside Ableton.
It proves routing, argument handling and the shape of each call; it cannot prove that Live really
has an attribute by that name. That part only the smoke run against Live itself can answer.
"""

import sys
import types


class Listeners(object):
    """The add/remove/fire trio Live gives every observable thing, as a mixin."""

    def _install_listener_api(self, name):
        if not hasattr(self, "listeners"):
            self.listeners = {}

        self.listeners[name] = []
        setattr(self, "add_%s_listener" % name, self.listeners[name].append)
        setattr(self, "remove_%s_listener" % name, self.listeners[name].remove)

    def fire(self, name):
        for callback in list(self.listeners.get(name, ())):
            callback()


class Parameter(Listeners):
    def __init__(self, value=0.85, minimum=0.0, maximum=1.0):
        self.value = value
        self.min = minimum
        self.max = maximum
        self._install_listener_api("value")

    def str_for_value(self, value):
        """Not Live's curve -- only its shape: unity near 0.85, silence at the bottom."""
        if value <= 0:
            return "-inf dB"

        return "%.1f dB" % ((value - 0.85) * 40.0)

    def move_to(self, value):
        """What a hand on Live's own fader does: set it, then tell whoever was listening."""
        self.value = value
        self.fire("value")


class MixerDevice(object):
    def __init__(self, sends=0):
        self.volume = Parameter(0.85)
        self.panning = Parameter(0.0, -1.0, 1.0)
        self.sends = [Parameter(0.0) for _ in range(sends)]


class ClipSlot(object):
    def __init__(self):
        self.fired = 0
        self.stopped = 0

    def fire(self):
        self.fired += 1

    def stop(self):
        self.stopped += 1


class Track(Listeners):
    def __init__(self, name, slots=2, sends=1, color=0x3C7DB5, foldable=False, group=None,
                 can_be_armed=True):
        self.name = name
        self.group_track = group
        self.mute = False
        self.solo = False
        self.arm = False
        # Live's own answer to "does arming mean anything here". False on returns and the master.
        self.can_be_armed = can_be_armed
        self.color = color
        self.is_foldable = foldable
        self.mixer_device = MixerDevice(sends)
        self.clip_slots = [ClipSlot() for _ in range(slots)]
        self.stopped_all = 0

        for attribute in ("name", "mute", "solo", "arm"):
            self._install_listener_api(attribute)

    def rename(self, name):
        self.name = name
        self.fire("name")

    def press(self, name, value=None):
        """What a hand on Live's own mute button does: set it, then tell whoever was listening."""
        setattr(self, name, (not getattr(self, name)) if value is None else value)
        self.fire(name)

    def stop_all_clips(self):
        self.stopped_all += 1


class Scene(object):
    def __init__(self, name):
        self.name = name
        self.fired = 0

    def fire(self):
        self.fired += 1


class SongView(Listeners):
    def __init__(self, track, scene):
        self.selected_track = track
        self.selected_scene = scene
        self._install_listener_api("selected_track")

    def select(self, track):
        self.selected_track = track
        self.fire("selected_track")


class Song(Listeners):
    LISTENED = ("is_playing", "tempo", "metronome", "record_mode", "tracks", "return_tracks")

    def __init__(self):
        # One group among the plain tracks, because a group is a real channel with a real fader
        # and the roster has to say so.
        # Drums is a group with Bass inside it: the shape the roster has to describe, and the one
        # a flat list of faders would lose.
        drums = Track("Drums", foldable=True)
        self.tracks = [drums, Track("Bass", group=drums), Track("Keys")]
        self.scenes = [Scene("A"), Scene("B")]
        self.return_tracks = [
            Track("A Reverb", slots=0, can_be_armed=False),
            Track("B Delay", slots=0, can_be_armed=False),
        ]
        self.master_track = Track("Master", slots=0, color=0, can_be_armed=False)

        self.tempo = 120.0
        self.is_playing = False
        self.metronome = False
        self.record_mode = False
        self.session_record = False
        self.loop = False

        self.view = SongView(self.tracks[0], self.scenes[0])
        self.calls = []
        self.listeners = {}

        for name in self.LISTENED:
            self._install_listener_api(name)

    def _record(self, name, *args):
        self.calls.append((name,) + args)

    def start_playing(self):
        self.is_playing = True
        self._record("start_playing")

    def stop_playing(self):
        self.is_playing = False
        self._record("stop_playing")

    def continue_playing(self):
        self.is_playing = True
        self._record("continue_playing")

    def stop_all_clips(self):
        self._record("stop_all_clips")

    def tap_tempo(self):
        self._record("tap_tempo")

    def capture_midi(self):
        self._record("capture_midi")

    def undo(self):
        self._record("undo")

    def redo(self):
        self._record("redo")

    def create_midi_track(self, index=-1):
        self._record("create_midi_track", index)
        self.tracks.append(Track("MIDI"))
        self.fire("tracks")

    def create_audio_track(self, index=-1):
        self._record("create_audio_track", index)
        self.tracks.append(Track("Audio"))

    def create_return_track(self):
        self._record("create_return_track")

    def create_scene(self, index=-1):
        self._record("create_scene", index)

    def duplicate_track(self, index):
        self._record("duplicate_track", index)

    def delete_track(self, index):
        self._record("delete_track", index)
        del self.tracks[index]
        self.fire("tracks")


class NavDirection(object):
    """Live's enum. The values are the names themselves, which makes a failed test readable."""

    up = "up"
    down = "down"
    left = "left"
    right = "right"


class ApplicationView(object):
    #: Where each direction runs out, so the fake can bottom out the way Live does.
    ZOOM_LIMIT = 8

    def __init__(self):
        self.visible = {"Session"}
        self.calls = []

        # A notch count per axis, clamped at both ends. Live gives no way to read the zoom back;
        # the fake keeps one anyway, because "ends up at the same level whatever it started at" is
        # the promise a preset makes and the only thing worth asserting about it.
        self.zoom = {"horizontal": 0, "vertical": 0}

    def show_view(self, name):
        self.calls.append(("show", name))
        self.visible.add(name)
        # Session and Arranger are mutually exclusive, exactly as in Live.
        if name == "Session":
            self.visible.discard("Arranger")
        elif name == "Arranger":
            self.visible.discard("Session")

    def hide_view(self, name):
        self.calls.append(("hide", name))
        self.visible.discard(name)

    def is_view_visible(self, name):
        return name in self.visible

    def zoom_view(self, direction, name, modifier_pressed):
        self.calls.append(("zoom", direction, name, modifier_pressed))

        axis = "vertical" if direction in (NavDirection.up, NavDirection.down) else "horizontal"
        step = 1 if direction in (NavDirection.up, NavDirection.right) else -1

        self.zoom[axis] = max(0, min(self.ZOOM_LIMIT, self.zoom[axis] + step))


class BrowserItem(object):
    def __init__(self, name, uri="", loadable=False, folder=False, children=()):
        self.name = name
        self.uri = uri
        self.is_loadable = loadable
        self.is_folder = folder
        self.children = list(children)


class Browser(object):
    """A small stand-in for Live's browser, with one duplicated name to exercise ambiguity."""

    def __init__(self):
        self.instruments = BrowserItem("Instruments", folder=True, children=[
            BrowserItem("Analog", "query:Analog", loadable=True),
            BrowserItem("Operator", "query:Operator", loadable=True),
            BrowserItem("Drum Rack", folder=True, children=[
                BrowserItem("808 Core Kit", "query:808", loadable=True),
            ]),
        ])
        self.audio_effects = BrowserItem("Audio Effects", folder=True, children=[
            BrowserItem("Reverb", "query:Reverb", loadable=True),
            # Shaped like what Live 12.3 really returns: loadable, NOT flagged as a folder, and
            # full of children anyway. The picker's navigation rule depends on telling these apart.
            BrowserItem("Audio Effect Rack", "query:AudioFx#Rack", loadable=True, children=[
                BrowserItem("Space", folder=True, children=[
                    BrowserItem("Chorus Echo.adg", "query:AudioFx#FileId_5472", loadable=True),
                ]),
            ]),
        ])
        # Same name in two places: exactly the case the blueprint calls out as the main risk.
        self.plugins = BrowserItem("Plugins", folder=True, children=[
            BrowserItem("Serum", "query:VST3:Serum", loadable=True),
            BrowserItem("Legacy", folder=True, children=[
                BrowserItem("Serum", "query:VST2:Serum", loadable=True),
            ]),
        ])
        self.sounds = BrowserItem("Sounds", folder=True)
        self.drums = BrowserItem("Drums", folder=True)
        self.midi_effects = BrowserItem("MIDI Effects", folder=True)
        self.packs = BrowserItem("Packs", folder=True)
        self.user_library = BrowserItem("User Library", folder=True)
        self.clips = BrowserItem("Clips", folder=True)
        self.samples = BrowserItem("Samples", folder=True)
        self.current_project = BrowserItem("Project", folder=True)

        # Deliberately absent, like a category a future Live drops: max_for_live.
        self.loaded = []

    def load_item(self, item):
        self.loaded.append(item)


class Application(object):
    def __init__(self):
        self.view = ApplicationView()
        self.browser = Browser()

    def get_major_version(self):
        return 12

    def get_minor_version(self):
        return 3

    def get_bugfix_version(self):
        return 0


class CInstance(object):
    """What Live hands the control surface. Only song() is used by the handlers."""

    def __init__(self, song):
        self._song = song

    def song(self):
        return self._song


APPLICATION = Application()


def _install():
    module = types.ModuleType("Live")

    application = types.ModuleType("Live.Application")
    application.get_application = lambda: APPLICATION

    # Live reaches NavDirection through the class rather than the instance, and so does the zoom
    # handler, so the fake has to offer the same path: Live.Application.Application.View.
    class _View(object):
        NavDirection = NavDirection

    class _Application(object):
        View = _View

    application.Application = _Application
    module.Application = application

    base = types.ModuleType("Live.Base")
    base.Timer = None  # The control surface falls back to update_display when this is missing.
    module.Base = base

    sys.modules["Live"] = module
    sys.modules["Live.Application"] = application
    sys.modules["Live.Base"] = base


_install()


def build_context():
    """A fresh song and context, for the observers that live outside the dispatcher."""
    from DigitalDeck.context import Context

    song = Song()
    APPLICATION.view = ApplicationView()
    APPLICATION.browser = Browser()

    sent = []
    ctx = Context(CInstance(song), lambda address, args=(): sent.append((address, list(args))), lambda _: None)

    return song, ctx, sent


def build():
    """A fresh song, context and fully registered dispatcher for one test."""
    from DigitalDeck.dispatch import Dispatcher
    from DigitalDeck.handlers import register_all

    song, ctx, sent = build_context()

    dispatcher = Dispatcher()
    register_all(dispatcher, ctx)

    return song, dispatcher, sent
