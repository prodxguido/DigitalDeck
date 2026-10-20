"""
The address table itself, driven against a fake Live Object Model.

What this proves: every address is registered, arguments land where they should, "the selected
track" resolves, and bad input comes back as an error instead of an exception. What it cannot
prove: that Live really exposes an attribute by that name -- only the smoke run against Ableton
itself can answer that half.
"""

import unittest

import fakes  # Installs the fake Live module. Must be imported before anything from DigitalDeck.

from DigitalDeck.dispatch import ERROR_ADDRESS
from DigitalDeck.observers import SongObservers
from DigitalDeck.version import SCRIPT_VERSION


class HandlerTestCase(unittest.TestCase):
    def setUp(self):
        self.song, self.dispatcher, self.sent = fakes.build()

    def run_ok(self, address, args=()):
        """Routes a message and fails the test if the script answered with an error."""
        replies = self.dispatcher.route(address, list(args))

        for reply_address, reply_args in replies:
            if reply_address == ERROR_ADDRESS:
                self.fail("%s answered with an error: %s" % (address, reply_args))

        return replies

    def run_error(self, address, args=()):
        replies = self.dispatcher.route(address, list(args))

        self.assertEqual(1, len(replies), "expected exactly one error from %s" % address)
        self.assertEqual(ERROR_ADDRESS, replies[0][0], "expected %s to fail" % address)

        return replies[0][1][1]


class TransportTests(HandlerTestCase):
    def test_play_and_stop(self):
        self.run_ok("/live/song/start_playing")
        self.assertTrue(self.song.is_playing)

        self.run_ok("/live/song/stop_playing")
        self.assertFalse(self.song.is_playing)

    def test_toggle_playing_follows_the_current_state(self):
        self.run_ok("/live/song/toggle/playing")
        self.assertTrue(self.song.is_playing)

        self.run_ok("/live/song/toggle/playing")
        self.assertFalse(self.song.is_playing)

    def test_is_playing_can_be_queried(self):
        self.song.is_playing = True
        self.assertEqual([("/live/song/get/is_playing", [1])],
                         self.run_ok("/live/song/get/is_playing"))

    def test_record_and_session_record_are_separate_switches(self):
        self.run_ok("/live/song/toggle/record_mode")
        self.assertTrue(self.song.record_mode)
        self.assertFalse(self.song.session_record)

        self.run_ok("/live/song/set/session_record", [1])
        self.assertTrue(self.song.session_record)


class TempoTests(HandlerTestCase):
    def test_setting_a_tempo(self):
        self.run_ok("/live/song/set/tempo", [140.0])
        self.assertEqual(140.0, self.song.tempo)

    def test_a_tempo_live_would_refuse_comes_back_as_an_error(self):
        for value in (0.0, 19.0, 1000.0):
            message = self.run_error("/live/song/set/tempo", [value])
            self.assertIn("tempo", message)

        self.assertEqual(120.0, self.song.tempo, "a rejected tempo must not have been applied")

    def test_nudging_stops_at_the_ends_instead_of_failing(self):
        # A "+1 BPM" button held down should sit at the ceiling, not start erroring.
        self.run_ok("/live/song/nudge/tempo", [-200.0])
        self.assertEqual(20.0, self.song.tempo)

        self.run_ok("/live/song/nudge/tempo", [5000.0])
        self.assertEqual(999.0, self.song.tempo)

    def test_tempo_can_be_queried(self):
        self.assertEqual([("/live/song/get/tempo", [120.0])], self.run_ok("/live/song/get/tempo"))


class TrackTests(HandlerTestCase):
    def test_an_explicit_index_addresses_that_track(self):
        self.run_ok("/live/track/set/mute", [1, 1])

        self.assertTrue(self.song.tracks[1].mute)
        self.assertFalse(self.song.tracks[0].mute)

    def test_minus_one_means_the_selected_track(self):
        self.song.view.selected_track = self.song.tracks[2]
        self.run_ok("/live/track/toggle/mute", [-1])

        self.assertTrue(self.song.tracks[2].mute)

    def test_the_track_argument_defaults_to_the_selected_track(self):
        self.run_ok("/live/track/toggle/solo", [])
        self.assertTrue(self.song.tracks[0].solo)

    def test_minus_two_means_the_master_track(self):
        self.run_ok("/live/track/set/volume", [-2, 0.5])
        self.assertEqual(0.5, self.song.master_track.mixer_device.volume.value)

    def test_a_track_that_does_not_exist_comes_back_as_an_error(self):
        message = self.run_error("/live/track/set/mute", [99, 1])
        self.assertIn("99", message)

    def test_volume_is_clamped_to_what_live_accepts(self):
        self.run_ok("/live/track/set/volume", [0, 4.0])
        self.assertEqual(1.0, self.song.tracks[0].mixer_device.volume.value)

    def test_nudging_volume_moves_from_where_it_is(self):
        self.song.tracks[0].mixer_device.volume.value = 0.5
        self.run_ok("/live/track/nudge/volume", [0, 0.1])

        self.assertAlmostEqual(0.6, self.song.tracks[0].mixer_device.volume.value)

    def test_panning_keeps_its_own_range(self):
        self.run_ok("/live/track/set/panning", [0, -5.0])
        self.assertEqual(-1.0, self.song.tracks[0].mixer_device.panning.value)

    def test_a_send_that_does_not_exist_comes_back_as_an_error(self):
        self.run_ok("/live/track/set/send", [0, 0, 0.3])
        self.assertEqual(0.3, self.song.tracks[0].mixer_device.sends[0].value)

        self.run_error("/live/track/set/send", [0, 7, 0.3])

    def test_volume_can_be_queried_for_a_named_track(self):
        self.song.tracks[1].mixer_device.volume.value = 0.25
        self.assertEqual([("/live/track/get/volume", [1, 0.25])],
                         self.run_ok("/live/track/get/volume", [1]))


class TrackManagementTests(HandlerTestCase):
    def test_creating_tracks(self):
        self.run_ok("/live/song/create_midi_track")
        self.run_ok("/live/song/create_audio_track", [0])

        self.assertIn(("create_midi_track", -1), self.song.calls)
        self.assertIn(("create_audio_track", 0), self.song.calls)

    def test_duplicating_resolves_the_selected_track_to_a_real_index(self):
        self.song.view.selected_track = self.song.tracks[2]
        self.run_ok("/live/song/duplicate_track", [-1])

        self.assertIn(("duplicate_track", 2), self.song.calls)

    def test_duplicating_the_master_track_is_refused(self):
        # Live has no index for it, so guessing one would act on the wrong track.
        self.song.view.selected_track = self.song.master_track
        self.run_error("/live/song/duplicate_track", [-1])

    def test_the_last_track_cannot_be_deleted(self):
        del self.song.tracks[1:]
        message = self.run_error("/live/song/delete_track", [0])

        self.assertIn("at least one track", message)


class ClipTests(HandlerTestCase):
    def test_firing_a_slot(self):
        self.run_ok("/live/clip_slot/fire", [1, 1])
        self.assertEqual(1, self.song.tracks[1].clip_slots[1].fired)

    def test_firing_uses_the_selection_when_no_index_is_given(self):
        self.song.view.selected_track = self.song.tracks[2]
        self.song.view.selected_scene = self.song.scenes[1]
        self.run_ok("/live/clip_slot/fire")

        self.assertEqual(1, self.song.tracks[2].clip_slots[1].fired)

    def test_stopping_a_slot(self):
        self.run_ok("/live/clip_slot/stop", [0, 0])
        self.assertEqual(1, self.song.tracks[0].clip_slots[0].stopped)

    def test_a_slot_that_does_not_exist_comes_back_as_an_error(self):
        self.run_error("/live/clip_slot/fire", [0, 9])

    def test_firing_a_scene(self):
        self.run_ok("/live/scene/fire", [1])
        self.assertEqual(1, self.song.scenes[1].fired)

    def test_stopping_every_clip_on_a_track(self):
        self.run_ok("/live/track/stop_all_clips", [1])
        self.assertEqual(1, self.song.tracks[1].stopped_all)


class ViewTests(HandlerTestCase):
    @property
    def view(self):
        return fakes.APPLICATION.view

    def test_showing_the_arrangement(self):
        self.run_ok("/live/view/show_arrangement")
        self.assertTrue(self.view.is_view_visible("Arranger"))

    def test_toggling_swaps_session_and_arrangement_like_the_tab_key(self):
        self.run_ok("/live/view/toggle_session_arrangement")
        self.assertTrue(self.view.is_view_visible("Arranger"))

        self.run_ok("/live/view/toggle_session_arrangement")
        self.assertTrue(self.view.is_view_visible("Session"))

    def test_toggling_a_pane_hides_it_when_it_is_already_up(self):
        self.run_ok("/live/view/toggle_browser")
        self.assertTrue(self.view.is_view_visible("Browser"))

        self.run_ok("/live/view/toggle_browser")
        self.assertFalse(self.view.is_view_visible("Browser"))

    def test_selecting_a_track(self):
        self.run_ok("/live/view/select_track", [2])
        self.assertIs(self.song.tracks[2], self.song.view.selected_track)

    def test_selecting_a_scene_that_does_not_exist_comes_back_as_an_error(self):
        self.run_error("/live/view/select_scene", [9])


class ZoomTests(HandlerTestCase):
    @property
    def view(self):
        return fakes.APPLICATION.view

    def test_zooming_in_and_out_along_the_timeline(self):
        self.run_ok("/digitaldeck/view/zoom", ["time", 3])
        self.assertEqual(3, self.view.zoom["horizontal"])

        self.run_ok("/digitaldeck/view/zoom", ["time", -2])
        self.assertEqual(1, self.view.zoom["horizontal"])

    def test_the_track_axis_moves_separately_from_the_timeline(self):
        self.run_ok("/digitaldeck/view/zoom", ["tracks", 2])

        self.assertEqual(2, self.view.zoom["vertical"])
        self.assertEqual(0, self.view.zoom["horizontal"])

    def test_a_preset_lands_on_the_same_level_whatever_it_started_from(self):
        # The whole point of a preset button: two of them pressed in any order still disagree only
        # about where they land, never about where they started.
        self.run_ok("/digitaldeck/view/zoom", ["time", 8])
        self.run_ok("/digitaldeck/view/zoom_preset", ["time", 3])
        self.assertEqual(3, self.view.zoom["horizontal"])

        self.run_ok("/digitaldeck/view/zoom", ["time", -8])
        self.run_ok("/digitaldeck/view/zoom_preset", ["time", 3])
        self.assertEqual(3, self.view.zoom["horizontal"])

    def test_a_preset_of_zero_is_all_the_way_out(self):
        self.run_ok("/digitaldeck/view/zoom", ["time", 5])
        self.run_ok("/digitaldeck/view/zoom_preset", ["time", 0])

        self.assertEqual(0, self.view.zoom["horizontal"])

    def test_the_zoom_follows_whichever_main_view_is_up(self):
        self.run_ok("/digitaldeck/view/zoom", ["time", 1])
        self.assertEqual(("zoom", "right", "Session", False), self.view.calls[-1])

        self.run_ok("/live/view/show_arrangement")
        self.run_ok("/digitaldeck/view/zoom", ["time", 1])
        self.assertEqual(("zoom", "right", "Arranger", False), self.view.calls[-1])

    def test_a_silly_step_count_is_capped_rather_than_looped_over(self):
        self.run_ok("/digitaldeck/view/zoom", ["time", 10 ** 9])

        self.assertEqual(fakes.ApplicationView.ZOOM_LIMIT, self.view.zoom["horizontal"])

    def test_an_axis_nobody_defined_comes_back_as_an_error(self):
        self.run_error("/digitaldeck/view/zoom", ["diagonal", 1])


class HandshakeTests(HandlerTestCase):
    def test_ping_answers_with_both_versions(self):
        replies = self.run_ok("/digitaldeck/ping")
        address, args = replies[0]

        self.assertEqual("/digitaldeck/pong", address)
        self.assertEqual(SCRIPT_VERSION, args[0])
        self.assertEqual("12.3.0", args[1])

    def test_ping_says_which_ports_it_is_on(self):
        # Two halves of the bridge installed on different ports look exactly like Live being
        # closed. Reporting the pair is what lets the app say which it is.
        from DigitalDeck.config import RECEIVE_PORT, SEND_PORT

        args = self.run_ok("/digitaldeck/ping")[0][1]

        self.assertEqual(RECEIVE_PORT, args[3])
        self.assertEqual(SEND_PORT, args[4])

    def test_the_default_ports_stay_clear_of_abletonosc(self):
        # AbletonOSC owns 11000/11001 in every tutorial the community has written. Both scripts can
        # be loaded in Live at once, so the only thing that has to differ is the pair of ports.
        from DigitalDeck.config import RECEIVE_PORT, SEND_PORT

        self.assertNotIn(11000, (RECEIVE_PORT, SEND_PORT))
        self.assertNotIn(11001, (RECEIVE_PORT, SEND_PORT))
        self.assertNotEqual(RECEIVE_PORT, SEND_PORT)

    def test_every_address_can_be_listed_for_diagnostics(self):
        replies = self.run_ok("/digitaldeck/addresses")
        listed = replies[0][1][0].split("\n")

        self.assertIn("/live/song/start_playing", listed)
        self.assertEqual(sorted(listed), listed)


class ObserverTests(unittest.TestCase):
    def test_changes_are_published_and_listeners_are_released_on_disconnect(self):
        song = fakes.Song()
        sent = []
        observers = SongObservers(song,
                                  lambda address, args: sent.append((address, args)),
                                  lambda _: None)

        self.assertEqual(1, len(song.listeners["tempo"]))

        song.tempo = 132.0
        song.listeners["tempo"][0]()
        self.assertEqual([("/live/song/get/tempo", [132.0])], sent)

        observers.disconnect()
        self.assertEqual(0, len(song.listeners["tempo"]))

    def test_a_value_this_live_cannot_observe_is_skipped_rather_than_fatal(self):
        # A future Live dropping one listener should cost that one push, not the whole bridge.
        song = fakes.Song()
        del song.add_tempo_listener

        notes = []
        SongObservers(song, lambda address, args: None, notes.append)

        self.assertTrue(any("tempo" in note for note in notes))


if __name__ == "__main__":
    unittest.main()
