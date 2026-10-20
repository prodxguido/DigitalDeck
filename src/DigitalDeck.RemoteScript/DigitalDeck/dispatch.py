"""
Address routing and argument coercion.

Kept free of Live imports on purpose: this is the part of the script with branches worth testing,
and it can be exercised from a normal Python interpreter with fake handlers.
"""

ERROR_ADDRESS = "/digitaldeck/error"


class ArgumentError(ValueError):
    """A message whose arguments do not fit what the handler needs."""


class Dispatcher(object):
    """
    Maps an OSC address to the function that carries it out.

    A handler receives the decoded argument list and returns either nothing, one
    ``(address, args)`` reply, or a list of them. Anything a handler raises is turned into an
    error reply rather than propagating: a bad message from the app must never take down the
    script, because a dead script means Live has to be restarted to get it back.
    """

    def __init__(self):
        self._handlers = {}

    def register(self, address, handler):
        if address in self._handlers:
            raise ValueError("address already registered: %s" % address)

        self._handlers[address] = handler

    @property
    def addresses(self):
        return sorted(self._handlers.keys())

    def route(self, address, args):
        """Runs the handler for `address` and returns the list of replies to send back."""
        handler = self._handlers.get(address)

        if handler is None:
            return [(ERROR_ADDRESS, [address, "unknown address"])]

        try:
            reply = handler(list(args))
        except Exception as error:  # noqa: BLE001 - deliberately broad, see docstring.
            return [(ERROR_ADDRESS, [address, _describe(error)])]

        return _normalise(reply)


def _describe(error):
    text = str(error) or error.__class__.__name__
    return "%s: %s" % (error.__class__.__name__, text) if str(error) else text


def _normalise(reply):
    if reply is None:
        return []

    # One reply, given as (address, args).
    if isinstance(reply, tuple) and len(reply) == 2 and isinstance(reply[0], str):
        return [reply]

    if isinstance(reply, list):
        return [item for item in reply if item is not None]

    raise TypeError("a handler returned %r, which is not a reply" % type(reply).__name__)


# ---------- Argument coercion ----------
#
# Values arrive from the wire, so every one of these has to assume the worst. Handlers call them
# instead of indexing the list directly, which is what keeps "wrong argument" a clean error reply
# rather than an IndexError buried in a Live callback.

_MISSING = object()


def _at(args, index, default):
    if index < len(args):
        return args[index]

    if default is _MISSING:
        raise ArgumentError("missing argument %d" % (index + 1))

    return default


def as_int(args, index, default=_MISSING):
    value = _at(args, index, default)
    if value is default and default is not _MISSING:
        return value

    if isinstance(value, bool):
        return 1 if value else 0

    try:
        return int(value)
    except (TypeError, ValueError):
        raise ArgumentError("argument %d should be a whole number" % (index + 1))


def as_float(args, index, default=_MISSING):
    value = _at(args, index, default)
    if value is default and default is not _MISSING:
        return value

    try:
        return float(value)
    except (TypeError, ValueError):
        raise ArgumentError("argument %d should be a number" % (index + 1))


def as_bool(args, index, default=_MISSING):
    """Accepts 0/1, a float, or a real boolean -- the app sends whichever is cheapest to encode."""
    value = _at(args, index, default)
    if value is default and default is not _MISSING:
        return value

    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return value != 0
    if isinstance(value, str):
        return value.strip().lower() in ("1", "true", "on", "yes")

    raise ArgumentError("argument %d should be 0 or 1" % (index + 1))


def as_text(args, index, default=_MISSING):
    value = _at(args, index, default)
    if value is default and default is not _MISSING:
        return value

    if not isinstance(value, str):
        raise ArgumentError("argument %d should be text" % (index + 1))

    return value


def clamp(value, low, high):
    return low if value < low else (high if value > high else value)
