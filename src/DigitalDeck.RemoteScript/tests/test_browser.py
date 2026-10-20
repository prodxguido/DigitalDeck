"""
The Browser bridge: listing, searching, and refusing to guess.

The blueprint calls resolution by name the single biggest technical risk in the project, so most of
what is here is about what happens when a name is not enough.
"""

import unittest

import fakes

from DigitalDeck.dispatch import ERROR_ADDRESS
from DigitalDeck.handlers import browser


class BrowserTestCase(unittest.TestCase):
    def setUp(self):
        self.song, self.dispatcher, self.sent = fakes.build()
        self.browser = fakes.APPLICATION.browser

    def run_ok(self, address, args=()):
        replies = self.dispatcher.route(address, list(args))

        for reply_address, reply_args in replies:
            if reply_address == ERROR_ADDRESS:
                self.fail("%s answered with an error: %s" % (address, reply_args))

        return replies[0][1]

    def run_error(self, address, args=()):
        replies = self.dispatcher.route(address, list(args))

        self.assertEqual(ERROR_ADDRESS, replies[0][0], "expected %s to fail" % address)
        return replies[0][1][1]

    @staticmethod
    def names(payload):
        return [line.split(browser.FIELD)[0] for line in payload.split("\n") if line]


class CategoryTests(BrowserTestCase):
    def test_only_categories_this_live_actually_has_are_offered(self):
        # max_for_live is missing from the fake, the way a category can be missing from an edition
        # or a future version. Offering it would produce a button that always fails.
        listed = self.run_ok("/digitaldeck/browser/categories")[0].split("\n")

        self.assertIn("instruments", listed)
        self.assertIn("plugins", listed)
        self.assertNotIn("max_for_live", listed)

    def test_an_unknown_category_says_which_ones_exist(self):
        message = self.run_error("/digitaldeck/browser/list", ["synthesizers", ""])

        self.assertIn("synthesizers", message)
        self.assertIn("instruments", message)


class ListTests(BrowserTestCase):
    def test_listing_the_top_of_a_category(self):
        _category, _path, truncated, payload = self.run_ok(
            "/digitaldeck/browser/list", ["instruments", ""])

        self.assertEqual(0, truncated)
        self.assertEqual(["Analog", "Operator", "Drum Rack"], self.names(payload))

    def test_each_entry_says_whether_live_can_load_it(self):
        # The app needs this to grey out folders: offering to load one would always fail.
        payload = self.run_ok("/digitaldeck/browser/list", ["instruments", ""])[3]
        entries = dict((line.split(browser.FIELD)[0], line.split(browser.FIELD)[2])
                       for line in payload.split("\n"))

        self.assertEqual("1", entries["Analog"])
        self.assertEqual("0", entries["Drum Rack"])

    def test_an_entry_says_whether_walking_into_it_leads_anywhere(self):
        # is_folder is not that question, and assuming it was would have cost the picker every
        # user rack on the machine. Measured against Live 12.3: all 47 items at the top of Audio
        # Effects report is_folder False, "Audio Effect Rack" among them, and that one has six
        # folders of racks underneath.
        payload = self.run_ok("/digitaldeck/browser/list", ["audio_effects", ""])[3]
        rows = dict((line.split(browser.FIELD)[0], line.split(browser.FIELD))
                    for line in payload.split("\n") if line)

        rack = rows["Audio Effect Rack"]

        self.assertEqual("1", rack[2], "the rack node is loadable")
        self.assertEqual("0", rack[3], "and Live does not call it a folder")
        self.assertEqual("1", rack[4], "but walking into it leads somewhere")

        self.assertEqual("0", rows["Reverb"][4], "a leaf leads nowhere")

    def test_every_line_carries_all_five_fields(self):
        payload = self.run_ok("/digitaldeck/browser/list", ["instruments", ""])[3]

        for line in payload.split("\n"):
            self.assertEqual(5, len(line.split(browser.FIELD)), line)

    def test_listing_a_folder_further_down(self):
        payload = self.run_ok("/digitaldeck/browser/list", ["instruments", "Drum Rack"])[3]

        self.assertEqual(["808 Core Kit"], self.names(payload))

    def test_a_path_that_does_not_exist_comes_back_as_an_error(self):
        message = self.run_error("/digitaldeck/browser/list", ["instruments", "Nope"])

        self.assertIn("Nope", message)

    def test_a_long_listing_is_capped_and_says_so(self):
        # An unbounded reply would both blow past a sane datagram size and stall Live's thread.
        self.browser.instruments.children = [
            fakes.BrowserItem("Item %d" % index, "query:%d" % index, loadable=True)
            for index in range(browser.MAX_RESULTS + 50)
        ]

        _category, _path, truncated, payload = self.run_ok(
            "/digitaldeck/browser/list", ["instruments", ""])

        self.assertEqual(1, truncated)
        self.assertEqual(browser.MAX_RESULTS, len(self.names(payload)))


class SearchTests(BrowserTestCase):
    def test_searching_goes_through_nested_folders(self):
        payload = self.run_ok("/digitaldeck/browser/search", ["instruments", "808", 10])[3]

        self.assertEqual(["808 Core Kit"], self.names(payload))

    def test_searching_ignores_case(self):
        payload = self.run_ok("/digitaldeck/browser/search", ["instruments", "analog", 10])[3]

        self.assertEqual(["Analog"], self.names(payload))

    def test_folders_are_not_offered_as_results(self):
        payload = self.run_ok("/digitaldeck/browser/search", ["instruments", "rack", 10])[3]

        self.assertEqual([], self.names(payload))

    def test_a_complete_search_says_so(self):
        _category, _query, complete, _payload = self.run_ok(
            "/digitaldeck/browser/search", ["instruments", "a", 50])

        self.assertEqual(1, complete)

    def test_a_search_that_ran_out_of_room_admits_it_is_incomplete(self):
        # Reporting a capped result set as complete would tell the user their plugin is not
        # installed when it is simply further down the list.
        _category, _query, complete, payload = self.run_ok(
            "/digitaldeck/browser/search", ["plugins", "serum", 1])

        self.assertEqual(0, complete)
        self.assertEqual(1, len(self.names(payload)))

    def test_an_empty_query_is_refused(self):
        self.run_error("/digitaldeck/browser/search", ["instruments", "  ", 10])


class LoadTests(BrowserTestCase):
    def test_loading_by_uri(self):
        uri, name = self.run_ok("/digitaldeck/browser/load", ["query:Operator"])

        self.assertEqual("query:Operator", uri)
        self.assertEqual("Operator", name)
        self.assertEqual(["Operator"], [item.name for item in self.browser.loaded])

    def test_loading_by_name_when_it_is_unambiguous(self):
        self.run_ok("/digitaldeck/browser/load", ["", "instruments", "Analog"])

        self.assertEqual(["Analog"], [item.name for item in self.browser.loaded])

    def test_an_ambiguous_name_is_refused_instead_of_loading_the_first_match(self):
        # Two plugins called Serum, VST2 and VST3. Loading whichever came first would be wrong half
        # the time, and wrong silently — the button would appear to work.
        message = self.run_error("/digitaldeck/browser/load", ["", "plugins", "Serum"])

        self.assertIn("2", message)
        self.assertEqual([], self.browser.loaded)

    def test_a_uri_resolves_an_otherwise_ambiguous_name(self):
        # Which is why the app caches the URI the first time the user disambiguates.
        self.run_ok("/digitaldeck/browser/load", ["query:VST2:Serum"])

        loaded = self.browser.loaded[0]
        self.assertEqual("query:VST2:Serum", loaded.uri)

    def test_a_name_nothing_matches_comes_back_as_an_error(self):
        message = self.run_error("/digitaldeck/browser/load", ["", "instruments", "Nonesuch"])

        self.assertIn("Nonesuch", message)
        self.assertEqual([], self.browser.loaded)

    def test_a_folder_cannot_be_loaded(self):
        message = self.run_error("/digitaldeck/browser/load", ["", "instruments", "Drum Rack"])

        self.assertEqual([], self.browser.loaded)
        self.assertTrue(message)

    def test_loading_with_neither_uri_nor_name_is_refused(self):
        self.run_error("/digitaldeck/browser/load", [""])

    def test_a_live_without_a_browser_says_so_rather_than_crashing(self):
        del fakes.APPLICATION.browser

        try:
            message = self.run_error("/digitaldeck/browser/categories")
            self.assertIn("browser", message)
        finally:
            fakes.APPLICATION.browser = fakes.Browser()


if __name__ == "__main__":
    unittest.main()
