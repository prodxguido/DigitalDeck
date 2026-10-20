"""
The UDP side of the bridge.

Two rules shape this file:

* **Loopback only.** The socket binds 127.0.0.1, never 0.0.0.0. Without that, any machine on the
  same network could drive the user's Live set.
* **Never touch Live from another thread.** The Live Object Model is only safe on Live's own
  thread, and calling into it from a worker crashes the application rather than raising. So there
  is no receive thread here at all: the socket is non-blocking and gets drained from the control
  surface's tick, which Live calls on the right thread.
"""

import socket

from . import osc
from .config import RECEIVE_PORT, SEND_PORT

LOOPBACK = "127.0.0.1"
#: Kept as names so the rest of the module reads the same; the numbers come from config.py,
#: which the installer rewrites with whatever pair the app is set to.
DEFAULT_RECEIVE_PORT = RECEIVE_PORT
DEFAULT_SEND_PORT = SEND_PORT

# Upper bound on messages handled per tick. A flood of datagrams must not be able to stall Live's
# thread indefinitely; anything left over is picked up on the next tick a few milliseconds later.
MAX_MESSAGES_PER_TICK = 128


class OscServer(object):
    def __init__(self, dispatcher, log,
                 receive_port=DEFAULT_RECEIVE_PORT,
                 send_port=DEFAULT_SEND_PORT):
        self._dispatcher = dispatcher
        self._log = log
        self._send_address = (LOOPBACK, send_port)
        self._socket = None

        try:
            self._socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            self._socket.setblocking(False)
            self._socket.bind((LOOPBACK, receive_port))
        except Exception as error:  # noqa: BLE001
            # Almost always the port already being held by another control surface script.
            self._socket = None
            self._log("could not open UDP %s:%d (%s). Is AbletonOSC or a second copy of "
                      "DigitalDeck also enabled?" % (LOOPBACK, receive_port, error))
            return

        self._log("listening on %s:%d, replying to %s:%d"
                  % (LOOPBACK, receive_port, LOOPBACK, send_port))

    @property
    def is_open(self):
        return self._socket is not None

    def send(self, address, args=()):
        """Fire and forget. A failed send is logged, never raised into a Live callback."""
        if self._socket is None:
            return

        try:
            self._socket.sendto(osc.encode(address, args), self._send_address)
        except Exception as error:  # noqa: BLE001
            self._log("could not send %s (%s)" % (address, error))

    def process(self):
        """Drains whatever has arrived and runs it. Called from Live's thread, every tick."""
        if self._socket is None:
            return

        for _ in range(MAX_MESSAGES_PER_TICK):
            try:
                data, _sender = self._socket.recvfrom(65536)
            except (BlockingIOError, socket.error):
                # Nothing left to read this tick -- the common case, every tick.
                return

            try:
                address, args = osc.decode(data)
            except osc.OscParseError as error:
                self._log("ignored a malformed datagram (%s)" % error)
                continue

            for reply_address, reply_args in self._dispatcher.route(address, args):
                self.send(reply_address, reply_args)

    def close(self):
        if self._socket is None:
            return

        try:
            self._socket.close()
        except Exception:  # noqa: BLE001
            pass
        finally:
            self._socket = None
