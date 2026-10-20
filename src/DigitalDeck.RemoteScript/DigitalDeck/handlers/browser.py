"""
Live's Browser: listing what it has indexed, and loading one of those items.

This is the part AbletonOSC does not cover, and the reason DigitalDeck needs a script of its own
rather than a dependency. It is also the riskiest part of the bridge, for two reasons worth stating
plainly:

* **load_item only loads what Live already indexed.** A plugin, preset or rack that is not in the
  Browser cannot be reached by any path -- pointing at a .fxp or .vst3 file on disk is impossible.
  Anything the app offers the user has to come from a listing here, not from a file dialog.
* **Walking the tree happens on Live's own thread.** A user with a large library has tens of
  thousands of nodes, and an unbounded walk would freeze Live while it ran. Every traversal below
  is bounded, and says so when it hit the ceiling rather than pretending the results are complete.
"""

from ..dispatch import as_int, as_text, ArgumentError

#: Top-level categories of Live's browser, in the order the app should offer them.
CATEGORIES = (
    "instruments",
    "sounds",
    "drums",
    "audio_effects",
    "midi_effects",
    "plugins",
    "max_for_live",
    "packs",
    "user_library",
    "clips",
    "samples",
    "current_project",
)

#: Ceiling on nodes visited in one traversal. Live stays responsive; results say if it was hit.
MAX_NODES = 20000

#: Ceiling on results returned in one reply, so a datagram stays a sane size.
MAX_RESULTS = 500

#: How deep a search will go. Live's own tree is shallow; runaway depth means a cycle.
MAX_DEPTH = 8

#: Separator between fields of one result line. Tabs cannot appear in a Live item name.
FIELD = "\t"


def register(dispatcher, ctx):
    add = dispatcher.register

    add("/digitaldeck/browser/categories", lambda args: _categories(ctx))
    add("/digitaldeck/browser/list", lambda args: _list(ctx, args))
    add("/digitaldeck/browser/search", lambda args: _search(ctx, args))
    add("/digitaldeck/browser/load", lambda args: _load(ctx, args))


# ---------- Handlers ----------


def _categories(ctx):
    """Which categories this Live actually exposes -- they differ between versions and editions."""
    browser = _browser(ctx)
    available = [name for name in CATEGORIES if getattr(browser, name, None) is not None]

    return ("/digitaldeck/browser/categories", ["\n".join(available)])


def _list(ctx, args):
    """
    One level of the tree.

    Listing a level at a time rather than dumping everything is what keeps this usable on a large
    library: the app asks for what the user is looking at, not for the whole Browser.
    """
    category = as_text(args, 0)
    path = as_text(args, 1, "")

    node = _navigate(_category(ctx, category), path)
    children = list(getattr(node, "children", []))

    lines = [_describe(child) for child in children[:MAX_RESULTS]]
    truncated = 1 if len(children) > MAX_RESULTS else 0

    return ("/digitaldeck/browser/list", [category, path, truncated, "\n".join(lines)])


def _search(ctx, args):
    """Everything under a category whose name contains the query, case-insensitively."""
    category = as_text(args, 0)
    query = as_text(args, 1).strip().lower()
    limit = min(as_int(args, 2, 100), MAX_RESULTS)

    if not query:
        raise ArgumentError("the search needs something to look for")

    root = _category(ctx, category)
    found = []
    visited = [0]

    def walk(node, depth):
        if len(found) >= limit or visited[0] >= MAX_NODES or depth > MAX_DEPTH:
            return

        for child in getattr(node, "children", []):
            visited[0] += 1

            if visited[0] >= MAX_NODES or len(found) >= limit:
                return

            name = getattr(child, "name", "") or ""

            if query in name.lower() and getattr(child, "is_loadable", False):
                found.append(child)

            walk(child, depth + 1)

    walk(root, 0)

    complete = 0 if (visited[0] >= MAX_NODES or len(found) >= limit) else 1
    lines = [_describe(item) for item in found]

    return ("/digitaldeck/browser/search", [category, query, complete, "\n".join(lines)])


def _load(ctx, args):
    """
    Loads one Browser item into the selected track.

    Resolution is by URI first and by name only as a fallback, because names are not unique --
    two versions of the same plugin, or a user preset named like a factory one, both happen. When
    the name is ambiguous this refuses and reports how many matched, rather than loading whichever
    came first: silently loading the wrong plugin is worse than not loading one.
    """
    uri = as_text(args, 0, "").strip()
    category = as_text(args, 1, "").strip()
    name = as_text(args, 2, "").strip()

    if not uri and not name:
        raise ArgumentError("give a URI or a name to load")

    roots = [_category(ctx, category)] if category else _all_categories(ctx)
    matches = _find(roots, uri, name)

    if not matches:
        raise ArgumentError(
            "Live has nothing indexed matching %r" % (uri or name))

    if len(matches) > 1:
        raise ArgumentError(
            "%d items match %r -- pick one by URI" % (len(matches), name))

    item = matches[0]

    if not getattr(item, "is_loadable", False):
        raise ArgumentError("%r is not something Live can load" % getattr(item, "name", "that item"))

    _browser(ctx).load_item(item)

    return ("/digitaldeck/browser/loaded",
            [getattr(item, "uri", "") or "", getattr(item, "name", "") or ""])


# ---------- Tree walking ----------


def _browser(ctx):
    browser = getattr(ctx.application, "browser", None)

    if browser is None:
        raise ArgumentError("this version of Live does not expose the browser")

    return browser


def _category(ctx, name):
    if name not in CATEGORIES:
        raise ArgumentError("unknown category %r (try one of: %s)" % (name, ", ".join(CATEGORIES)))

    root = getattr(_browser(ctx), name, None)

    if root is None:
        raise ArgumentError("this version of Live has no %r in the browser" % name)

    return root


def _all_categories(ctx):
    browser = _browser(ctx)
    roots = []

    for name in CATEGORIES:
        root = getattr(browser, name, None)
        if root is not None:
            roots.append(root)

    return roots


def _navigate(node, path):
    """Walks down a path of names like "Audio Effects/Reverb". An empty path stays at the root."""
    if not path:
        return node

    for step in path.split("/"):
        step = step.strip()
        if not step:
            continue

        for child in getattr(node, "children", []):
            if (getattr(child, "name", "") or "") == step:
                node = child
                break
        else:
            raise ArgumentError("there is nothing called %r at that level" % step)

    return node


def _find(roots, uri, name):
    """
    Collects every item matching a URI or a name.

    A URI match short-circuits: it identifies exactly one item, so there is no reason to keep
    walking once it is found. A name match cannot, because the whole point is to find out whether
    it is ambiguous.
    """
    matches = []
    visited = [0]
    wanted_name = name.lower()

    def walk(node, depth):
        if visited[0] >= MAX_NODES or depth > MAX_DEPTH:
            return False

        for child in getattr(node, "children", []):
            visited[0] += 1

            if visited[0] >= MAX_NODES:
                return False

            child_uri = getattr(child, "uri", "") or ""

            if uri and child_uri == uri:
                matches.append(child)
                return True

            if not uri and wanted_name:
                child_name = (getattr(child, "name", "") or "").lower()

                if child_name == wanted_name and getattr(child, "is_loadable", False):
                    matches.append(child)

            if walk(child, depth + 1):
                return True

        return False

    for root in roots:
        if walk(root, 0):
            break

    return matches


def _describe(item):
    """
    One result line: name, URI, loadable, folder, and whether it goes anywhere.

    The last field is not a restatement of the fourth. Measured against Live 12.3, ``is_folder`` is
    False for every one of the 47 items at the top of Audio Effects -- including "Audio Effect
    Rack", which has six folders of user racks underneath it. A picker that decided what to open by
    ``is_folder`` would therefore be unable to reach a single one of them, which is why the app is
    told separately whether walking in leads somewhere.
    """
    return FIELD.join((
        (getattr(item, "name", "") or "").replace(FIELD, " "),
        getattr(item, "uri", "") or "",
        "1" if getattr(item, "is_loadable", False) else "0",
        "1" if getattr(item, "is_folder", False) else "0",
        "1" if _has_children(item) else "0",
    ))


def _has_children(item):
    """
    Whether this item has anything under it.

    Wrapped because it touches the Live Object Model one level deeper than the listing itself does,
    and an attribute that is missing or throws on some node type would otherwise take down a whole
    listing over one bad row.
    """
    try:
        return bool(len(getattr(item, "children", ()) or ()))
    except Exception:  # noqa: BLE001 - one odd node must not cost the whole listing.
        return False
