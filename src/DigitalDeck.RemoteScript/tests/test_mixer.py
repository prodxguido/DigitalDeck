"""
The touch mixer: the channel roster, the index space returns live in, and the push from Live.

What this proves: the roster names every channel once and numbers it the way the handlers resolve
it back, a fader on a return reaches the return, and moving a fader inside Live is announced to the
app only while the window is subscribed. What it cannot prove is that Live really has
`return_tracks` or `add_value_listener` -- that half is the smoke run's.
"""

import unittest

import fakes  # Installs the fake Live module. Must be imported before anything from DigitalDeck.

from DigitalDeck.context import MASTER_TRACK, RETURN_TRACK_BASE, return_track_index
from DigitalDeck.dispatch import ERROR_ADDRESS
from DigitalDeck.handlers.mixer import SUBSCRIBE_ADDRESS, TRACKS_ADDRESS


class MixerTestCase(unittest.TestCase):
    def setUp(self):
        self.song, self.dispatcher, self.sent = fakes.build()

    def route(self, address, args=()):
        replies = self.dispatcher.route(address, list(args))

        for reply_address, reply_args in replies:
            if reply_address == ERROR_ADDRESS:
                self.fail("%s answered with an error: %s" % (address, reply_args))

        return replies

    def roster(self):
        """The roster reply, parsed back into a list of fields per channel."""
        replies = self.route(TRACKS_ADDRESS)
        self.assertEqual(1, len(replies))

        address, args = replies[0]
        self.assertEqual(TRACKS_ADDRESS, address)

        lines = [line.split("\t") for line in args[1].split("\n") if line]
        self.assertEqual(args[0], len(lines), "the count and the payload disagree")

        return lines

    def subscribe(self, on=True):
        return self.route(SUBSCRIBE_ADDRESS, [1 if on else 0])

    def volumes_sent(self):
        """The pushed moves as (index, value), without the reading -- asserted on its own below."""
        return [args[:2] for address, args in self.sent if address == "/live/track/get/volume"]

    def readings_sent(self):
        return [args[2] for address, args in self.sent if address == "/live/track/get/volume"]

    def rosters_sent(self):
        return [args for address, args in self.sent if address == TRACKS_ADDRESS]


class RosterTests(MixerTestCase):
    def test_every_channel_appears_once_and_in_drawing_order(self):
        lines = self.roster()

        self.assertEqual(
            ["Drums", "Bass", "Keys", "A Reverb", "B Delay", "Master"],
            [fields[1] for fields in lines])

        self.assertEqual(
            ["group", "normal", "normal", "return", "return", "master"],
            [fields[2] for fields in lines])

    def test_the_indices_are_the_ones_the_handlers_resolve_back(self):
        lines = self.roster()
        indices = [int(fields[0]) for fields in lines]

        self.assertEqual([0, 1, 2, RETURN_TRACK_BASE, RETURN_TRACK_BASE - 1, MASTER_TRACK], indices)

    def test_the_current_volume_rides_along_so_the_window_draws_it_straight_away(self):
        self.song.tracks[1].mixer_device.volume.value = 0.5

        by_name = dict((fields[1], fields) for fields in self.roster())

        self.assertEqual(0.5, float(by_name["Bass"][4]))
        self.assertEqual(0.85, float(by_name["Drums"][4]))

    def test_a_track_inside_a_group_says_how_deep_it_sits(self):
        # Without this the window can only draw a flat row of faders, which for a set built out of
        # groups is a worse picture than Live's own mixer gives.
        by_name = dict((fields[1], fields) for fields in self.roster())

        self.assertEqual(0, int(by_name["Drums"][5]))
        self.assertEqual(1, int(by_name["Bass"][5]))
        self.assertEqual(0, int(by_name["Keys"][5]))

    def test_returns_and_the_master_are_never_nested(self):
        by_name = dict((fields[1], fields) for fields in self.roster())

        self.assertEqual(0, int(by_name["A Reverb"][5]))
        self.assertEqual(0, int(by_name["Master"][5]))

    def test_a_cycle_in_the_group_chain_does_not_hang(self):
        # Live will not do this. A walk over someone else's object graph that assumes it cannot is
        # a frozen Live rather than a wrong number, so the ceiling is worth proving.
        drums, bass = self.song.tracks[0], self.song.tracks[1]
        drums.group_track = bass

        self.assertEqual(0, int(dict((f[1], f) for f in self.roster())["Keys"][5]))

    def test_live_supplies_the_reading_under_the_fader(self):
        # Worked out by Live rather than by the app: the travel is linear in the 0..1, the decibels
        # behind it are Ableton's own curve, and a reimplementation would drift from what the user
        # sees on the other screen.
        by_name = dict((fields[1], fields) for fields in self.roster())

        self.assertEqual("0.0 dB", by_name["Drums"][6])

        self.song.tracks[2].mixer_device.volume.value = 0.0
        self.assertEqual("-inf dB", dict((f[1], f) for f in self.roster())["Keys"][6])

    def test_a_fader_that_cannot_print_itself_still_makes_the_roster(self):
        # An accessor Ableton moves in some future Live must cost the label, not the channel.
        self.song.tracks[1].mixer_device.volume.str_for_value = None

        by_name = dict((fields[1], fields) for fields in self.roster())

        self.assertEqual("", by_name["Bass"][6])
        self.assertEqual(0.85, float(by_name["Bass"][4]))

    def test_a_colour_comes_through_as_a_number(self):
        by_name = dict((fields[1], fields) for fields in self.roster())

        self.assertEqual(0x3C7DB5, int(by_name["Drums"][3]))

    def test_a_tab_in_a_name_cannot_split_a_line(self):
        self.song.tracks[1].name = "Bass\tguitar"

        lines = self.roster()

        self.assertTrue(all(len(fields) == 7 for fields in lines), lines)
        self.assertEqual("Bass guitar", lines[1][1])


class ReturnTrackTests(MixerTestCase):
    def test_a_return_can_be_faded(self):
        self.route("/live/track/set/volume", [return_track_index(1), 0.25])

        self.assertEqual(0.25, self.song.return_tracks[1].mixer_device.volume.value)
        # The regular tracks are numbered from zero and must not have been touched.
        self.assertEqual(0.85, self.song.tracks[0].mixer_device.volume.value)

    def test_a_return_can_be_read_back(self):
        self.song.return_tracks[0].mixer_device.volume.value = 0.4

        replies = self.route("/live/track/get/volume", [return_track_index(0)])

        self.assertEqual([("/live/track/get/volume", [RETURN_TRACK_BASE, 0.4])], replies)

    def test_a_return_that_is_not_there_is_a_clean_error(self):
        replies = self.dispatcher.route("/live/track/set/volume", [return_track_index(9), 0.5])

        self.assertEqual(ERROR_ADDRESS, replies[0][0])
        self.assertIn("no return 9", replies[0][1][1])

    def test_the_master_still_means_the_master(self):
        self.route("/live/track/set/volume", [MASTER_TRACK, 0.6])

        self.assertEqual(0.6, self.song.master_track.mixer_device.volume.value)


class SubscriptionTests(MixerTestCase):
    def test_nothing_is_pushed_before_the_window_opens(self):
        self.song.tracks[0].mixer_device.volume.move_to(0.3)

        self.assertEqual([], self.volumes_sent())

    def test_subscribing_answers_with_the_roster(self):
        replies = self.subscribe()

        self.assertEqual(1, len(replies))
        self.assertEqual(TRACKS_ADDRESS, replies[0][0])

    def test_a_fader_moved_inside_live_is_announced(self):
        self.subscribe()
        self.song.tracks[1].mixer_device.volume.move_to(0.3)

        self.assertEqual([[1, 0.3]], self.volumes_sent())

    def test_the_push_carries_the_reading_too(self):
        # So the number under the fader keeps up with a move made on Live's side, without the app
        # having to ask a second question for every pixel of someone else's drag.
        self.subscribe()
        self.song.tracks[1].mixer_device.volume.move_to(0.85)

        self.assertEqual(["0.0 dB"], self.readings_sent())

    def test_a_return_moved_inside_live_is_announced_under_its_own_index(self):
        self.subscribe()
        self.song.return_tracks[0].mixer_device.volume.move_to(0.7)

        self.assertEqual([[RETURN_TRACK_BASE, 0.7]], self.volumes_sent())

    def test_unsubscribing_stops_the_push(self):
        self.subscribe()
        self.subscribe(on=False)

        self.song.tracks[0].mixer_device.volume.move_to(0.3)

        self.assertEqual([], self.volumes_sent())

    def test_subscribing_twice_does_not_double_every_message(self):
        # The app re-sends its subscribe after a reconnect, having no way to know the first arrived.
        self.subscribe()
        self.subscribe()

        self.song.tracks[0].mixer_device.volume.move_to(0.3)

        self.assertEqual([[0, 0.3]], self.volumes_sent())

    def test_a_new_track_republishes_the_roster_and_gets_a_listener(self):
        self.subscribe()
        before = len(self.rosters_sent())

        self.song.create_midi_track()

        self.assertEqual(before + 1, len(self.rosters_sent()))

        self.song.tracks[-1].mixer_device.volume.move_to(0.2)
        self.assertEqual([[3, 0.2]], self.volumes_sent())

    def test_a_deleted_track_renumbers_the_ones_after_it(self):
        self.subscribe()
        keys = self.song.tracks[2]

        self.song.delete_track(0)

        # Keys was index 2 and is now index 1. Announcing it under the old number would move the
        # wrong fader on screen.
        keys.mixer_device.volume.move_to(0.1)
        self.assertEqual([[1, 0.1]], self.volumes_sent())

    def test_a_rename_inside_live_republishes_the_roster(self):
        self.subscribe()
        before = len(self.rosters_sent())

        self.song.tracks[0].rename("Bombo")

        self.assertEqual(before + 1, len(self.rosters_sent()))
        self.assertIn("Bombo", self.rosters_sent()[-1][1])


if __name__ == "__main__":
    unittest.main()
