"""Mixer state for one track: mute, solo, arm, volume, pan and sends."""

from ..context import track_states
from ..observers import TRACK_STATES_ADDRESS
from ..dispatch import as_bool, as_float, as_int, ArgumentError, clamp


def register(dispatcher, ctx):
    add = dispatcher.register

    # ---------- Switches ----------

    add("/live/track/set/mute", lambda args: _set(ctx, args, "mute", as_bool(args, 1)))
    add("/live/track/toggle/mute", lambda args: _toggle(ctx, args, "mute"))

    add("/live/track/set/solo", lambda args: _set(ctx, args, "solo", as_bool(args, 1)))
    add("/live/track/toggle/solo", lambda args: _toggle(ctx, args, "solo"))

    add("/live/track/set/arm", lambda args: _set(ctx, args, "arm", as_bool(args, 1)))
    add("/live/track/toggle/arm", lambda args: _toggle(ctx, args, "arm"))

    add("/live/track/get/mute",
        lambda args: ("/live/track/get/mute", [as_int(args, 0), 1 if _track(ctx, args).mute else 0]))
    add("/live/track/get/solo",
        lambda args: ("/live/track/get/solo", [as_int(args, 0), 1 if _track(ctx, args).solo else 0]))

    # Every channel at once, and the selected one with them. Asked for when the bridge comes up and
    # announced by the observers afterwards, so a grid full of mute buttons costs one message
    # rather than one question per track.
    add(TRACK_STATES_ADDRESS, lambda args: (TRACK_STATES_ADDRESS, track_states(ctx.song)))

    # ---------- Continuous parameters ----------
    #
    # Volume and pan are the normalised 0..1 / -1..1 values Live itself uses, not decibels. The
    # editor says so in as many words, because "volume 0.85" is not a number a producer thinks in.

    add("/live/track/set/volume", lambda args: _set_parameter(ctx, args, "volume", 0.0, 1.0))
    add("/live/track/nudge/volume", lambda args: _nudge_parameter(ctx, args, "volume", 0.0, 1.0))
    add("/live/track/get/volume", lambda args: _get_parameter(ctx, args, "volume"))

    add("/live/track/set/panning", lambda args: _set_parameter(ctx, args, "panning", -1.0, 1.0))
    add("/live/track/nudge/panning", lambda args: _nudge_parameter(ctx, args, "panning", -1.0, 1.0))
    add("/live/track/get/panning", lambda args: _get_parameter(ctx, args, "panning"))

    def set_send(args):
        track = _track(ctx, args)
        index = as_int(args, 1)
        value = as_float(args, 2)

        sends = track.mixer_device.sends
        if index < 0 or index >= len(sends):
            raise ArgumentError("there is no send %d on that track" % index)

        send = sends[index]
        send.value = clamp(value, send.min, send.max)

    add("/live/track/set/send", set_send)

    # ---------- Clips on this track ----------

    add("/live/track/stop_all_clips", lambda args: _track(ctx, args).stop_all_clips())


def _track(ctx, args):
    return ctx.track(as_int(args, 0, -1))


def _set(ctx, args, name, value):
    setattr(_track(ctx, args), name, value)


def _toggle(ctx, args, name):
    track = _track(ctx, args)
    setattr(track, name, not getattr(track, name))


def _parameter(ctx, args, name):
    track = _track(ctx, args)
    mixer = getattr(track, "mixer_device", None)

    if mixer is None:
        raise ArgumentError("that track has no mixer")

    parameter = getattr(mixer, name, None)
    if parameter is None:
        raise ArgumentError("that track has no %s" % name)

    return parameter


def _set_parameter(ctx, args, name, low, high):
    parameter = _parameter(ctx, args, name)
    parameter.value = clamp(as_float(args, 1), low, high)


def _nudge_parameter(ctx, args, name, low, high):
    parameter = _parameter(ctx, args, name)
    parameter.value = clamp(parameter.value + as_float(args, 1), low, high)


def _get_parameter(ctx, args, name):
    parameter = _parameter(ctx, args, name)
    return ("/live/track/get/%s" % name, [as_int(args, 0, -1), float(parameter.value)])
