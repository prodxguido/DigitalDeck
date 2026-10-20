"""
Minimal OSC 1.0 codec.

Live ships no OSC library and a Remote Script cannot pull one from pip, so the wire format is
implemented here. Only the subset DigitalDeck actually sends is supported -- int32, float32,
float64, string, blob and the payload-less booleans -- which keeps this small enough to read in
one sitting and to mirror exactly on the C# side (see OscEncoder/OscDecoder).

This module deliberately imports nothing from Live, so it can be unit tested outside Ableton.
"""

import struct

# OSC pads every block to a four byte boundary with nulls.
_ALIGNMENT = 4

_INT_MIN = -2147483648
_INT_MAX = 2147483647


class OscParseError(ValueError):
    """A datagram that is not a well formed OSC message."""


def _padded_length(length):
    """Length rounded up to the next four byte boundary."""
    remainder = length % _ALIGNMENT
    return length if remainder == 0 else length + (_ALIGNMENT - remainder)


def _encode_string(value):
    raw = value.encode("utf-8") + b"\x00"
    return raw.ljust(_padded_length(len(raw)), b"\x00")


def _decode_string(data, offset):
    end = data.find(b"\x00", offset)
    if end < 0:
        raise OscParseError("unterminated string")

    value = data[offset:end].decode("utf-8", errors="replace")
    return value, offset + _padded_length(end - offset + 1)


def encode(address, args=()):
    """Builds one OSC message. Raises TypeError for an argument with no OSC equivalent."""
    if not address.startswith("/"):
        raise ValueError("an OSC address must start with '/'")

    tags = ","
    body = b""

    for arg in args:
        # bool is a subclass of int in Python, so it has to be checked first.
        if isinstance(arg, bool):
            tags += "T" if arg else "F"
        elif isinstance(arg, int):
            if arg < _INT_MIN or arg > _INT_MAX:
                # Out of int32 range: send it as a double rather than silently wrapping.
                tags += "d"
                body += struct.pack(">d", float(arg))
            else:
                tags += "i"
                body += struct.pack(">i", arg)
        elif isinstance(arg, float):
            tags += "f"
            body += struct.pack(">f", arg)
        elif isinstance(arg, str):
            tags += "s"
            body += _encode_string(arg)
        elif isinstance(arg, (bytes, bytearray)):
            raw = bytes(arg)
            tags += "b"
            body += struct.pack(">i", len(raw)) + raw.ljust(_padded_length(len(raw)), b"\x00")
        else:
            raise TypeError("cannot send %r over OSC" % type(arg).__name__)

    return _encode_string(address) + _encode_string(tags) + body


def decode(data):
    """Parses one OSC message into (address, [args]). Bundles are rejected, not unpacked."""
    if not data:
        raise OscParseError("empty datagram")

    if data[:1] == b"#":
        raise OscParseError("bundles are not supported")

    address, offset = _decode_string(data, 0)
    if not address.startswith("/"):
        raise OscParseError("address does not start with '/'")

    if offset >= len(data):
        # A message with no type tag string at all. Tolerated: treat it as no arguments.
        return address, []

    tags, offset = _decode_string(data, offset)
    if not tags.startswith(","):
        raise OscParseError("type tag string does not start with ','")

    args = []

    for tag in tags[1:]:
        if tag == "T":
            args.append(True)
        elif tag == "F":
            args.append(False)
        elif tag == "N":
            args.append(None)
        elif tag == "i":
            offset = _take(data, offset, 4)
            args.append(struct.unpack(">i", data[offset - 4:offset])[0])
        elif tag == "f":
            offset = _take(data, offset, 4)
            args.append(struct.unpack(">f", data[offset - 4:offset])[0])
        elif tag == "d":
            offset = _take(data, offset, 8)
            args.append(struct.unpack(">d", data[offset - 8:offset])[0])
        elif tag == "s":
            value, offset = _decode_string(data, offset)
            args.append(value)
        elif tag == "b":
            offset = _take(data, offset, 4)
            size = struct.unpack(">i", data[offset - 4:offset])[0]
            if size < 0:
                raise OscParseError("negative blob length")
            padded = _padded_length(size)
            offset = _take(data, offset, padded)
            args.append(data[offset - padded:offset - padded + size])
        else:
            raise OscParseError("unsupported type tag '%s'" % tag)

    return address, args


def _take(data, offset, size):
    """Advances past `size` bytes, or fails if the datagram is truncated."""
    end = offset + size
    if end > len(data):
        raise OscParseError("message truncated")

    return end
