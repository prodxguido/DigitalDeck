"""
Which UDP ports this copy of the script talks on.

Its own file because the installer rewrites it. The app keeps the chosen pair in its settings and
writes it here when it copies the script in, which is what stops the two halves of the bridge from
drifting onto different ports -- a failure that otherwise looks exactly like Live not being open.

The defaults are deliberately NOT AbletonOSC's 11000/11001. Both scripts can be loaded in Live at
the same time -- there are several Control Surface slots and they do not compete -- but only one
process can bind a given UDP port, so sharing the pair means whichever starts second is silently
deaf. AbletonOSC keeps the ports every tutorial in the community mentions, and DigitalDeck steps
aside, so an installation of each works with nobody configuring anything.
"""

#: Where this script listens for the app.
RECEIVE_PORT = 11010

#: Where this script sends its replies.
SEND_PORT = 11011
