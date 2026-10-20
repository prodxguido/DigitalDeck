r"""
DigitalDeck's Remote Script for Ableton Live.

Copy this folder into:
    %USERPROFILE%\Documents\Ableton\User Library\Remote Scripts\DigitalDeck

then restart Live and select "DigitalDeck" as a Control Surface in
Preferences > Link, Tempo & MIDI. See README.md next to this folder for the full walkthrough.

Nothing Live-specific is imported at module level: create_instance does the real import when Live
asks for an instance. That is what lets the codec and the dispatcher be unit tested from a normal
Python interpreter, where the Live module does not exist.
"""

from .version import SCRIPT_VERSION  # noqa: F401 - part of the public surface of this package.


def create_instance(c_instance):
    """Entry point Live calls once the script is selected as a Control Surface."""
    from .control_surface import DigitalDeckSurface

    return DigitalDeckSurface(c_instance)
