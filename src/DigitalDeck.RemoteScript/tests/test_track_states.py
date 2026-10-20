"""
The mute, solo and arm of every channel, and which one is selected.

What this proves: the snapshot numbers channels the same way the mixer roster does, a press inside
Live is announced without anyone subscribing, and the listeners are rebuilt when the set of tracks
changes so an index never means the wrong track. What it cannot prove is that Live really has
``can_be_armed`` or ``add_mute_listener`` -- that half is the smoke run's.
"""

import unittest

import fakes  # Installs the fake Live module. Must be imported before anything from DigitalDeck.

from DigitalDeck.context import MASTER_TRACK, SELECTED_TRACK, return_track_index, track_states
from DigitalDeck.observers import TRACK_STATES_ADDRESS, TrackStateObservers


def parse(args):
    """One announcement, back into (selected, {index: (mute, solo, arm)})."""
    selected, count, payload = args
    lines = [line.split("\t") for line in payload.split("\n") if line]

    assert count == len(lines), "the count and the payload disagree"

    return selected, dict(
        (int(index), (int(mute), int(solo), int(arm))) for index, mute, solo, arm in lines)


class SnapshotTests(unittest.TestCase):
    def setUp(self):
        self.song, self.ctx, self.sent = fakes.build_context()

    def snapshot(self):
        return parse(track_states(self.song))

    def test_every_channel_appears_in_the_mixers_index_space(self):
        _selected, states = self.snapshot()

        # Three plain tracks, two returns counted down from the master, and the master itself.
        self.assertEqual(
            sorted([0, 1, 2, return_track_index(0), return_track_index(1), MASTER_TRACK]),
            sorted(states.keys()))

    def test_the_three_flags_read_off_the_track(self):
        self.song.tracks[1].mute = True
        self.song.tracks[1].solo = True
        self.song.tracks[2].arm = True

        _selected, states = self.snapshot()

        self.assertEqual((1, 1, 0), states[1])
        self.assertEqual((0, 0, 1), states[2])
        self.assertEqual((0, 0, 0), states[0])

    def test_a_channel_that_cannot_be_armed_never_reads_as_armed(self):
        # Live keeps the attribute on the master; arming it is meaningless and can_be_armed says so.
        self.song.master_track.arm = True

        _selected, states = self.snapshot()

        self.assertEqual((0, 0, 0), states[MASTER_TRACK])

    def test_the_selected_channel_is_reported_in_the_same_space(self):
        self.song.view.selected_track = self.song.tracks[2]
        selected, _states = self.snapshot()
        self.assertEqual(2, selected)

        self.song.view.selected_track = self.song.master_track
        selected, _states = self.snapshot()
        self.assertEqual(MASTER_TRACK, selected)

    def test_a_selection_outside_the_set_is_reported_as_unknown(self):
        # Better than guessing at 0, which would light the wrong button rather than none.
        self.song.view.selected_track = fakes.Track("Somewhere else")

        selected, _states = self.snapshot()

        self.assertEqual(SELECTED_TRACK, selected)


class ObserverTests(unittest.TestCase):
    def setUp(self):
        self.song, self.ctx, self.sent = fakes.build_context()
        self.observers = TrackStateObservers(self.ctx)
        self.observers.start()
        self.addCleanup(self.observers.stop)

    def announcements(self):
        return [args for address, args in self.sent if address == TRACK_STATES_ADDRESS]

    def latest(self):
        return parse(self.announcements()[-1])

    def test_starting_announces_the_state_without_anyone_asking(self):
        self.assertEqual(1, len(self.announcements()))

    def test_a_mute_pressed_in_live_is_announced(self):
        self.song.tracks[1].press("mute")

        _selected, states = self.latest()

        self.assertEqual((1, 0, 0), states[1])

    def test_solo_and_arm_are_watched_too(self):
        self.song.tracks[0].press("solo")
        self.song.tracks[2].press("arm")

        _selected, states = self.latest()

        self.assertEqual((0, 1, 0), states[0])
        self.assertEqual((0, 0, 1), states[2])

    def test_selecting_another_track_is_announced(self):
        self.song.view.select(self.song.tracks[2])

        selected, _states = self.latest()

        self.assertEqual(2, selected)

    def test_a_track_arriving_is_watched_as_well(self):
        # The listeners hang off the track objects. Without rebuilding them, a mute on the new
        # track would be silent -- and every index to its right would have shifted underneath.
        self.song.create_midi_track()

        before = len(self.announcements())
        self.song.tracks[-1].press("mute")

        self.assertEqual(before + 1, len(self.announcements()))
        _selected, states = self.latest()
        self.assertEqual((1, 0, 0), states[len(self.song.tracks) - 1])

    def test_stopping_leaves_nothing_listening(self):
        self.observers.stop()

        before = len(self.announcements())
        self.song.tracks[0].press("mute")
        self.song.view.select(self.song.tracks[1])

        self.assertEqual(before, len(self.announcements()))


if __name__ == "__main__":
    unittest.main()
