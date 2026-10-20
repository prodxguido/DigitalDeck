"""
The object Live actually loads.

Live instantiates one of these when the user picks DigitalDeck as a Control Surface, calls into it
on its own thread, and disconnects it on shutdown or when the script is deselected. Everything
here is written on the assumption that an exception escaping into Live is unrecoverable without a
restart, so failures are logged and swallowed rather than raised.
"""

import Live

from .context import Context
from .dispatch import Dispatcher
from .handlers import register_all
from .handlers.meta import live_version
from .observers import SongObservers, TrackStateObservers
from .server import OscServer
from .version import SCRIPT_VERSION

# How often the socket is drained, in milliseconds. Live also calls update_display roughly every
# 100ms, which is a fallback if the timer is unavailable -- but 100ms of lag is audible when the
# button being pressed is Play, hence the timer.
TICK_INTERVAL_MS = 10


def _base_class():
    """
    Picks whichever ControlSurface base this Live has.

    _Framework is the one every community script has used for a decade and is still present in
    Live 12; ableton.v2 is where Ableton's own newer scripts live. Trying both means a future
    version dropping one of them costs a line here instead of a script that will not load.
    """
    try:
        from _Framework.ControlSurface import ControlSurface
        return ControlSurface
    except ImportError:
        from ableton.v2.control_surface import ControlSurface
        return ControlSurface


class DigitalDeckSurface(_base_class()):
    def __init__(self, c_instance):
        super(DigitalDeckSurface, self).__init__(c_instance)

        self._server = None
        self._observers = None
        self._track_states = None
        self._context = None
        self._timer = None

        with self.component_guard():
            self._start(c_instance)

    # ---------- Lifecycle ----------

    def _start(self, c_instance):
        ctx = Context(c_instance, self._send, self._log)
        self._context = ctx

        dispatcher = Dispatcher()
        register_all(dispatcher, ctx)

        self._server = OscServer(dispatcher, self._log)

        if not self._server.is_open:
            self._log("the bridge is not listening -- see the message above")
            return

        self._log("DigitalDeck %s ready on Live %s (%d addresses)"
                  % (SCRIPT_VERSION, live_version(ctx.application), len(dispatcher.addresses)))

        try:
            self._observers = SongObservers(ctx.song, self._send, self._log)
        except Exception as error:  # noqa: BLE001
            self._log("could not attach observers (%s)" % error)

        # The same idea one level down: what every channel's mute, solo and arm are doing, so the
        # buttons on the grid can show it without asking.
        try:
            self._track_states = TrackStateObservers(ctx)
            self._track_states.start()
        except Exception as error:  # noqa: BLE001
            self._log("could not attach the track state observers (%s)" % error)

        # Announce ourselves, so an app that was already running notices Live came up without
        # having to wait for its next heartbeat.
        self._send("/digitaldeck/pong", [SCRIPT_VERSION, live_version(ctx.application), "12"])

        self._start_timer()

    def _start_timer(self):
        timer_class = getattr(getattr(Live, "Base", None), "Timer", None)

        if timer_class is None:
            self._log("Live.Base.Timer is unavailable; falling back to update_display")
            return

        try:
            self._timer = timer_class(callback=self._tick, interval=TICK_INTERVAL_MS, repeat=True)
            self._timer.start()
        except Exception as error:  # noqa: BLE001
            self._timer = None
            self._log("could not start the timer (%s); falling back to update_display" % error)

    def disconnect(self):
        if self._timer is not None:
            try:
                self._timer.stop()
            except Exception:  # noqa: BLE001
                pass
            self._timer = None

        if self._observers is not None:
            self._observers.disconnect()
            self._observers = None

        if self._track_states is not None:
            self._track_states.stop()
            self._track_states = None

        # Handlers that attached listeners of their own get them back here -- the mixer's, today.
        if self._context is not None:
            self._context.close()
            self._context = None

        if self._server is not None:
            self._server.close()
            self._server = None

        self._log("disconnected")
        super(DigitalDeckSurface, self).disconnect()

    # ---------- Pumping ----------

    def _tick(self):
        if self._server is None:
            return

        try:
            self._server.process()
        except Exception as error:  # noqa: BLE001
            # One bad message must not stop the timer, or the bridge dies until Live restarts.
            self._log("error while processing (%s)" % error)

    def update_display(self):
        """Live calls this about ten times a second. Also drains, in case the timer never started."""
        super(DigitalDeckSurface, self).update_display()
        self._tick()

    # ---------- Plumbing ----------

    def _send(self, address, args=()):
        if self._server is not None:
            self._server.send(address, args)

    def _log(self, message):
        try:
            self.log_message("DigitalDeck: %s" % message)
        except Exception:  # noqa: BLE001
            pass
