"""
The handshake, plus whatever else is about the bridge rather than about Live.

Everything here lives under /digitaldeck/ instead of /live/, so it stays obvious which addresses
are our own extension and which mirror the ones AbletonOSC established.
"""

from ..config import RECEIVE_PORT, SEND_PORT
from ..version import SCRIPT_VERSION, TESTED_WITH

PONG_ADDRESS = "/digitaldeck/pong"


def register(dispatcher, ctx):
    add = dispatcher.register

    def ping(args):
        # The ports ride along as confirmation, not as mismatch detection: if the two halves are
        # on different pairs then the ping never arrives, or the reply goes somewhere nobody is
        # listening, and either way the app hears nothing at all. Catching that is the installer's
        # job, by reading this pair off the disk. What these are good for is the app being able to
        # say which ports it is connected on, and a line in Live's log worth reading.
        return (PONG_ADDRESS, [
            SCRIPT_VERSION,
            live_version(ctx.application),
            TESTED_WITH,
            RECEIVE_PORT,
            SEND_PORT,
        ])

    add("/digitaldeck/ping", ping)

    def addresses(args):
        # One newline-separated string rather than one message per address: this is a diagnostic
        # aid, and a single reply is far easier to read in a log.
        return ("/digitaldeck/addresses", ["\n".join(dispatcher.addresses)])

    add("/digitaldeck/addresses", addresses)


def live_version(application):
    """
    Live's version as "12.3.0".

    Read defensively: these accessors are exactly the kind of undocumented surface that can move
    between releases, and not knowing the version is far better than the script failing to load.
    """
    parts = []

    for name in ("get_major_version", "get_minor_version", "get_bugfix_version"):
        getter = getattr(application, name, None)

        try:
            parts.append(str(getter()) if callable(getter) else "?")
        except Exception:  # noqa: BLE001
            parts.append("?")

    return ".".join(parts)
