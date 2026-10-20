import sqlite3
from pathlib import Path

db = Path.home() / 'AppData/Roaming/DigitalDeck/deck.db'
root = db.parent
conn = sqlite3.connect(db)
rows = conn.execute("SELECT label, icon_path FROM items WHERE icon_path LIKE '%brand-%' ORDER BY label").fetchall()
print('items', len(rows))
missing = []
for label, rel in rows:
    p = root / rel
    print(label, rel, p.exists())
    if not p.exists():
        missing.append(label)
print('integrity', conn.execute('PRAGMA integrity_check').fetchone()[0])
conn.close()
if len(rows) != 21 or missing:
    raise SystemExit(1)
