"""Wires every handler module into one dispatcher."""

from . import browser, clip, meta, mixer, song, track, view

MODULES = (meta, song, track, clip, view, browser, mixer)


def register_all(dispatcher, ctx):
    for module in MODULES:
        module.register(dispatcher, ctx)

    return dispatcher
