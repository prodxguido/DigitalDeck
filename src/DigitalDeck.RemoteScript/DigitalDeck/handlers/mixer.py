"""
The channel roster behind the touch mixer, and the switch that keeps Live pushing to it.

Two things live here that no other handler needs. One is a single reply describing every channel at
once: the mixer window draws twenty faders in one go, and twenty round trips to build one screen is
twenty chances for a datagram to go missing over a protocol that will not tell anyone it did. The
other is a subscription switch, because the volume listeners behind it cost a message per move --
a deck sitting on its grid with the mixer closed should pay nothing for a window nobody opened.
"""

from ..context import fader, mixer_tracks, reading
from ..dispatch import as_bool
from ..observers import TrackObservers

TRACKS_ADDRESS = "/digitaldeck/mixer/tracks"
SUBSCRIBE_ADDRESS = "/digitaldeck/mixer/subscribe"

#: Separator between the fields of one channel, as in the browser listings. Live will not let a
#: track name contain a tab.
FIELD = "\t"


def register(dispatcher, ctx):
    observers = TrackObservers(ctx, lambda: roster(ctx))

    # Live tears the Song down on its own schedule; the listeners have to come off before it does.
    ctx.on_close(observers.stop)

    dispatcher.register(TRACKS_ADDRESS, lambda args: roster(ctx))

    def subscribe(args):
        # Defaults to on: a message with no argument can only have come from a window opening.
        if not as_bool(args, 0, True):
            observers.stop()
            return None

        observers.start()

        # Answered rather than merely acknowledged, so the window that just opened has something to
        # draw without asking a second question straight away.
        return roster(ctx)

    dispatcher.register(SUBSCRIBE_ADDRESS, subscribe)


def roster(ctx):
    """Every channel a fader can drive, one per line, in the order they should be drawn."""
    lines = [_describe(*channel) for channel in mixer_tracks(ctx.song)]

    return (TRACKS_ADDRESS, [len(lines), "\n".join(lines)])


def _describe(index, track, kind, depth):
    # The current volume rides along with the identity on purpose. Splitting them would mean the
    # window drawing a screen of faders parked at zero and then jumping once the values landed.
    return FIELD.join((
        str(index),
        _name(track),
        kind,
        str(_colour(track)),
        "%g" % _volume(track),
        str(depth),
        reading(fader(track)),
    ))


def _name(track):
    name = getattr(track, "name", "") or ""

    # Live promises a name has no tab in it. Promises about someone else's data are worth a strip.
    return name.replace(FIELD, " ").replace("\n", " ")


def _colour(track):
    """Live's 0xRRGGBB for the channel, or 0 when this kind of track has no colour of its own."""
    try:
        return int(getattr(track, "color", 0) or 0)
    except (TypeError, ValueError):
        return 0


def _volume(track):
    parameter = fader(track)

    try:
        return float(parameter.value)
    except (AttributeError, TypeError, ValueError):
        # A channel with no fader of its own still belongs on the roster; it just cannot move.
        return 0.0
