"""
Version of the bridge protocol, not of the app.

The C# side compares this against what it was built for and shows a clear "the script is out of
date" state instead of failing in some confusing way further along. Bump the minor when addresses
are added, the major when an existing address changes shape.
"""

SCRIPT_VERSION = "2.6.0"

# Live versions this script has actually been exercised against. Reported in the handshake so a
# mismatch is visible in the app rather than discovered when a button silently does nothing.
TESTED_WITH = "12"
