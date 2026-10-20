"""
Round trips and malformed input for the OSC codec.

Run from src/DigitalDeck.RemoteScript with:  python -m unittest discover -s tests
"""

import struct
import unittest

from DigitalDeck import osc


class EncodeDecodeTests(unittest.TestCase):
    def assert_round_trip(self, address, args):
        decoded_address, decoded_args = osc.decode(osc.encode(address, args))

        self.assertEqual(address, decoded_address)
        self.assertEqual(len(args), len(decoded_args))

        return decoded_args

    def test_message_with_no_arguments(self):
        self.assertEqual(([], "/live/song/start_playing"),
                         (self.assert_round_trip("/live/song/start_playing", []),
                          "/live/song/start_playing"))

    def test_integers_survive(self):
        self.assertEqual([0, -1, 2147483647, -2147483648],
                         self.assert_round_trip("/x", [0, -1, 2147483647, -2147483648]))

    def test_floats_survive_within_single_precision(self):
        decoded = self.assert_round_trip("/live/song/set/tempo", [128.5])
        self.assertAlmostEqual(128.5, decoded[0], places=4)

    def test_booleans_use_the_payload_free_tags(self):
        encoded = osc.encode("/x", [True, False])

        # T and F carry no bytes of their own, so the whole message is address plus type tags.
        self.assertEqual(len(osc._encode_string("/x")) + len(osc._encode_string(",TF")), len(encoded))
        self.assertEqual([True, False], osc.decode(encoded)[1])

    def test_strings_survive(self):
        self.assertEqual(["Kick", "Bass 808"],
                         self.assert_round_trip("/x", ["Kick", "Bass 808"]))

    def test_blobs_survive(self):
        self.assertEqual([b"\x01\x02\x03"], self.assert_round_trip("/x", [b"\x01\x02\x03"]))

    def test_integers_beyond_int32_become_doubles(self):
        # Silently wrapping would turn a large value into a small one, which is worse than a
        # slightly larger message.
        decoded = self.assert_round_trip("/x", [2 ** 40])
        self.assertEqual(2 ** 40, decoded[0])


class PaddingTests(unittest.TestCase):
    def test_every_block_lands_on_a_four_byte_boundary(self):
        # Lengths 3, 4 and 5 are the ones that break a naive implementation: with the terminator
        # they need 0, 3 and 2 bytes of padding respectively.
        for text in ("abc", "abcd", "abcde"):
            encoded = osc.encode("/x", [text])
            self.assertEqual(0, len(encoded) % 4, "%r produced %d bytes" % (text, len(encoded)))
            self.assertEqual([text], osc.decode(encoded)[1])

    def test_addresses_of_awkward_length_round_trip(self):
        for address in ("/a", "/ab", "/abc", "/abcd", "/abcde"):
            self.assertEqual(address, osc.decode(osc.encode(address))[0])


class MalformedInputTests(unittest.TestCase):
    def test_empty_datagram_is_rejected(self):
        self.assertRaises(osc.OscParseError, osc.decode, b"")

    def test_bundles_are_rejected_rather_than_half_parsed(self):
        self.assertRaises(osc.OscParseError, osc.decode, b"#bundle\x00" + b"\x00" * 8)

    def test_address_must_start_with_a_slash(self):
        self.assertRaises(osc.OscParseError, osc.decode, osc._encode_string("live/song"))

    def test_truncated_argument_is_rejected(self):
        # A message claiming an int32 but carrying only two bytes of it.
        data = osc._encode_string("/x") + osc._encode_string(",i") + b"\x00\x00"
        self.assertRaises(osc.OscParseError, osc.decode, data)

    def test_unterminated_string_is_rejected(self):
        self.assertRaises(osc.OscParseError, osc.decode, b"/no-null-here")

    def test_unknown_type_tag_is_rejected(self):
        data = osc._encode_string("/x") + osc._encode_string(",Z")
        self.assertRaises(osc.OscParseError, osc.decode, data)

    def test_negative_blob_length_is_rejected(self):
        data = osc._encode_string("/x") + osc._encode_string(",b") + struct.pack(">i", -1)
        self.assertRaises(osc.OscParseError, osc.decode, data)

    def test_missing_type_tags_are_treated_as_no_arguments(self):
        # Some senders omit them entirely. Accepting this costs nothing and avoids a dead button.
        self.assertEqual(("/live/song/undo", []), osc.decode(osc._encode_string("/live/song/undo")))

    def test_encoding_an_unsupported_type_is_refused(self):
        self.assertRaises(TypeError, osc.encode, "/x", [{"a": 1}])

    def test_address_must_be_absolute_when_encoding(self):
        self.assertRaises(ValueError, osc.encode, "live/song", [])


if __name__ == "__main__":
    unittest.main()
