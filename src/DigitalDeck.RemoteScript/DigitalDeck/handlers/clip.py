"""Firing and stopping clips and scenes."""

from ..dispatch import as_int


def register(dispatcher, ctx):
    add = dispatcher.register

    add("/live/clip_slot/fire", lambda args: ctx.clip_slot(as_int(args, 0, -1), as_int(args, 1, -1)).fire())
    add("/live/clip_slot/stop", lambda args: _stop_slot(ctx, args))

    add("/live/scene/fire", lambda args: ctx.scene(as_int(args, 0, -1)).fire())
    add("/live/scene/fire_selected", lambda args: ctx.scene(-1).fire())


def _stop_slot(ctx, args):
    """
    Stops one clip slot.

    ClipSlot.stop() is present in the Live versions this was written against, but it is not part of
    any contract we can rely on across updates, so a missing method falls back to stopping the
    track it belongs to rather than raising at the user.
    """
    track_index = as_int(args, 0, -1)
    slot = ctx.clip_slot(track_index, as_int(args, 1, -1))

    stop = getattr(slot, "stop", None)
    if callable(stop):
        stop()
        return

    ctx.track(track_index).stop_all_clips()
