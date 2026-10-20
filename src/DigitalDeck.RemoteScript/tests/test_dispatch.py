"""Routing and argument coercion, with handlers that have nothing to do with Live."""

import unittest

from DigitalDeck import dispatch


class RoutingTests(unittest.TestCase):
    def setUp(self):
        self.dispatcher = dispatch.Dispatcher()

    def test_a_handler_that_returns_nothing_sends_nothing_back(self):
        self.dispatcher.register("/quiet", lambda args: None)
        self.assertEqual([], self.dispatcher.route("/quiet", []))

    def test_a_handler_can_reply(self):
        self.dispatcher.register("/ask", lambda args: ("/answer", [42]))
        self.assertEqual([("/answer", [42])], self.dispatcher.route("/ask", []))

    def test_a_handler_can_reply_more_than_once(self):
        self.dispatcher.register("/ask", lambda args: [("/a", [1]), ("/b", [2])])
        self.assertEqual([("/a", [1]), ("/b", [2])], self.dispatcher.route("/ask", []))

    def test_an_unknown_address_comes_back_as_an_error(self):
        replies = self.dispatcher.route("/nope", [])

        self.assertEqual(1, len(replies))
        self.assertEqual(dispatch.ERROR_ADDRESS, replies[0][0])
        self.assertEqual("/nope", replies[0][1][0])

    def test_a_raising_handler_becomes_an_error_rather_than_escaping(self):
        # This is the whole point of the try/except in route: an exception loose in a Live callback
        # takes the script down, and getting it back means restarting Live.
        self.dispatcher.register("/boom", lambda args: 1 / 0)
        replies = self.dispatcher.route("/boom", [])

        self.assertEqual(dispatch.ERROR_ADDRESS, replies[0][0])
        self.assertIn("ZeroDivisionError", replies[0][1][1])

    def test_registering_the_same_address_twice_is_refused(self):
        self.dispatcher.register("/x", lambda args: None)
        self.assertRaises(ValueError, self.dispatcher.register, "/x", lambda args: None)

    def test_addresses_are_listed_sorted(self):
        for address in ("/c", "/a", "/b"):
            self.dispatcher.register(address, lambda args: None)

        self.assertEqual(["/a", "/b", "/c"], self.dispatcher.addresses)


class CoercionTests(unittest.TestCase):
    def test_numbers_are_accepted_in_whichever_form_they_arrive(self):
        self.assertEqual(1, dispatch.as_int([1], 0))
        self.assertEqual(1, dispatch.as_int([1.9], 0))
        self.assertEqual(1, dispatch.as_int([True], 0))

    def test_a_missing_argument_uses_the_default(self):
        self.assertEqual(-1, dispatch.as_int([], 0, -1))

    def test_a_missing_argument_without_a_default_is_an_error(self):
        self.assertRaises(dispatch.ArgumentError, dispatch.as_int, [], 0)

    def test_text_where_a_number_belongs_is_an_error(self):
        self.assertRaises(dispatch.ArgumentError, dispatch.as_int, ["nope"], 0)
        self.assertRaises(dispatch.ArgumentError, dispatch.as_float, ["nope"], 0)

    def test_booleans_accept_the_cheapest_encoding_the_app_had(self):
        for value in (1, 1.0, True, "1", "true", "on"):
            self.assertTrue(dispatch.as_bool([value], 0), repr(value))

        for value in (0, 0.0, False, "0", "false", ""):
            self.assertFalse(dispatch.as_bool([value], 0), repr(value))

    def test_text_arguments_must_actually_be_text(self):
        self.assertEqual("Kick", dispatch.as_text(["Kick"], 0))
        self.assertRaises(dispatch.ArgumentError, dispatch.as_text, [7], 0)

    def test_clamp_holds_the_ends(self):
        self.assertEqual(0.0, dispatch.clamp(-3.0, 0.0, 1.0))
        self.assertEqual(1.0, dispatch.clamp(9.0, 0.0, 1.0))
        self.assertEqual(0.5, dispatch.clamp(0.5, 0.0, 1.0))


if __name__ == "__main__":
    unittest.main()
