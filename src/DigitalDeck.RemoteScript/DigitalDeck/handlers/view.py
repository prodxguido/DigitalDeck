"""Moving around Live's own windows: Session vs Arrangement, selection, detail panes, zoom."""

import Live

from ..dispatch import as_int, as_text, clamp, ArgumentError
from ..context import SELECTED_TRACK

SESSION = "Session"
ARRANGER = "Arranger"
DETAIL = "Detail"
DETAIL_CLIP = "Detail/Clip"
DETAIL_DEVICE = "Detail/DeviceChain"
BROWSER = "Browser"

#: Zoom along the timeline -- what Live's + and - keys do.
TIME = "time"

#: Zoom the track heights -- how many tracks fit on screen at once.
TRACKS = "tracks"

#: Which way to push each axis, as NavDirection attribute names: (closer, further away).
#:
#: Live's API gives no way to ask which direction means what, and the docs only say the call
#: "silently returns" when a view cannot do what was asked -- so the mapping cannot be derived,
#: only tried. It is a table of names rather than four calls scattered through the file precisely
#: so that correcting one is a one-word edit here, with nothing else to keep in step.
ZOOM_AXES = {
    TIME: ("right", "left"),
    TRACKS: ("up", "down"),
}

#: Steps that bottom Live's zoom out from anywhere.
#:
#: Live has no "set the zoom to X": zoom_view moves one notch and reports nothing back. Bottoming
#: out first is what turns that into an absolute level -- from the floor, N notches in always lands
#: on the same place, which is the whole point of a preset button. Overshooting the floor costs
#: nothing, since a view already at its limit ignores the call.
ZOOM_FLOOR_STEPS = 60


def register(dispatcher, ctx):
    add = dispatcher.register

    add("/live/view/show_session", lambda args: _show(ctx, SESSION))
    add("/live/view/show_arrangement", lambda args: _show(ctx, ARRANGER))
    add("/live/view/toggle_session_arrangement", lambda args: _toggle_main(ctx))

    add("/live/view/show_detail_clip", lambda args: _show(ctx, DETAIL_CLIP))
    add("/live/view/show_detail_device", lambda args: _show(ctx, DETAIL_DEVICE))
    add("/live/view/toggle_detail", lambda args: _toggle(ctx, DETAIL))
    add("/live/view/toggle_browser", lambda args: _toggle(ctx, BROWSER))

    add("/live/view/select_track", lambda args: _select_track(ctx, as_int(args, 0)))
    add("/live/view/select_scene", lambda args: _select_scene(ctx, as_int(args, 0)))

    add("/digitaldeck/view/zoom",
        lambda args: _zoom(ctx, as_text(args, 0), as_int(args, 1)))
    add("/digitaldeck/view/zoom_preset",
        lambda args: _zoom_preset(ctx, as_text(args, 0), as_int(args, 1)))

    add("/live/view/get/is_session",
        lambda args: ("/live/view/get/is_session",
                      [1 if ctx.application.view.is_view_visible(SESSION) else 0]))


def _show(ctx, view_name):
    view = ctx.application.view
    view.show_view(view_name)


def _toggle(ctx, view_name):
    view = ctx.application.view

    if view.is_view_visible(view_name):
        view.hide_view(view_name)
    else:
        view.show_view(view_name)


def _toggle_main(ctx):
    """
    Session and Arrangement are two views, not one toggle.

    Asking which one is visible and showing the other is the only way to get the behaviour of
    Live's own Tab key, which is what a single deck button should do.
    """
    view = ctx.application.view
    view.show_view(ARRANGER if view.is_view_visible(SESSION) else SESSION)


def _zoom(ctx, axis, steps):
    """Moves the zoom `steps` notches: positive goes closer, negative pulls back."""
    closer, further = _axis(axis)
    direction = getattr(Live.Application.Application.View.NavDirection,
                        closer if steps >= 0 else further)

    view = ctx.application.view
    name = _main_view(ctx)

    # Capped rather than trusted: the count arrives from the wire, and a button saved with a silly
    # number should be a slow zoom, not a script stuck in a loop with Live's UI thread in its hand.
    for _ in range(int(clamp(abs(steps), 0, ZOOM_FLOOR_STEPS))):
        view.zoom_view(direction, name, False)


def _zoom_preset(ctx, axis, level):
    """
    Puts the zoom at a repeatable level, counted from fully zoomed out.

    This is what lets several buttons each hold a different zoom: pressing one always lands on the
    same view, whatever the zoom happened to be before, which a relative nudge can never promise.
    """
    _zoom(ctx, axis, -ZOOM_FLOOR_STEPS)
    _zoom(ctx, axis, int(clamp(level, 0, ZOOM_FLOOR_STEPS)))


def _axis(axis):
    try:
        return ZOOM_AXES[axis]
    except KeyError:
        raise ArgumentError("unknown zoom axis %r" % axis)


def _main_view(ctx):
    """
    Whichever of the two main views is up.

    Zooming the view the user is not looking at would be an invisible button, and Session and
    Arrangement each zoom their own way, so following the eye is the only reading that is never
    surprising.
    """
    return SESSION if ctx.application.view.is_view_visible(SESSION) else ARRANGER


def _select_track(ctx, index):
    ctx.song.view.selected_track = ctx.track(index if index >= 0 else SELECTED_TRACK)


def _select_scene(ctx, index):
    ctx.song.view.selected_scene = ctx.scene(index)
