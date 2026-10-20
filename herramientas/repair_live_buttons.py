import datetime
import json
import os
import sqlite3
from pathlib import Path

root = Path(__file__).resolve().parents[1]
catalog = json.loads((root / "live-browser-diagnostic.json").read_text(encoding="utf-8"))
entries = []
for address, args in catalog.values():
    if address == "/digitaldeck/browser/search":
        entries.extend((args[0], *line.split("\t")[:2]) for line in args[3].splitlines())
names = {"Pro-Q 4": "Pro-Q 4", "Pro-MB": "Pro-MB", "Pro-DS": "Pro-DS",
         "R-Vox": "RVox Stereo", "Purity": "Purity_x64"}
app = Path(os.environ["APPDATA"]) / "DigitalDeck"
db = sqlite3.connect(app / "deck.db")
backup = app / "backups" / ("deck-before-live-repair-" + datetime.datetime.now().strftime("%Y%m%d-%H%M%S") + ".db")
with sqlite3.connect(backup) as out:
    db.backup(out)
updates = []
for item_id, label, raw in db.execute("select id,label,data from items where data is not null").fetchall():
    data = json.loads(raw)
    kind = data.pop("kind")
    data = {"kind": kind, **data}
    if kind == "ableton_load":
        name = names.get(label, data["name"])
        matches = {(cat, n, uri) for cat, n, uri in entries
                   if n == name and ("#VST3:" in uri or label in ("Purity", "Delay"))}
        assert len(matches) == 1, (label, matches)
        category, name, uri = matches.pop()
        data.update(name=name, uri=uri, category=category)
    updates.append((json.dumps(data, ensure_ascii=False, separators=(",", ":")), item_id))
with db:
    db.executemany("update items set data=? where id=?", updates)
assert db.execute("pragma integrity_check").fetchone()[0] == "ok"
rows = db.execute("select label,data from items where data is not null").fetchall()
(root / "live-actions-validation.json").write_text(json.dumps(rows, ensure_ascii=False), encoding="utf-8")
print("Backup:", backup)
print("Updated actions:", len(updates))
db.close()
