import json, shutil, sqlite3
from pathlib import Path
from datetime import datetime

db = Path.home() / 'AppData/Roaming/DigitalDeck/deck.db'
staged = Path('deck-brand-icons-staged.db')
shutil.copy2(db, staged)

mapping = {
    'Pro-Q 4': 'assets/icons/brand-fabfilter-q4.png',
    'Pro-MB': 'assets/icons/brand-fabfilter-mb.png',
    'Pro-DS': 'assets/icons/brand-fabfilter-ds.png',
    'soothe2': 'assets/icons/brand-oeksound-soothe2.png',
    'Z-Noise': 'assets/icons/brand-waves-znoise.png',
    'NS1': 'assets/icons/brand-waves-ns1.png',
    'VintageVerb': 'assets/icons/brand-valhalla-vintageverb.png',
    'Delay': 'assets/icons/brand-ableton-delay.png',
    'CL 1B': 'assets/icons/brand-softube-cl1b.png',
    'CLA-2A': 'assets/icons/brand-waves-cla2a.png',
    'R-Vox': 'assets/icons/brand-waves-rvox.png',
    'Ableton Live 12': 'assets/icons/brand-ableton-live.png',
    'ZENOLOGY': 'assets/icons/brand-roland-zenology.png',
    'Purity': 'assets/icons/brand-purity.png',
    'Hive': 'assets/icons/brand-uhe-hive.png',
    'Kick 3': 'assets/icons/brand-kick3.png',
    'Splice': 'assets/icons/brand-splice.png',
    'DUNE 3': 'assets/icons/brand-dune3.png',
    'Electra': 'assets/icons/brand-electra.png',
    'Pigments': 'assets/icons/brand-arturia-pigments.png',
    'Analog Lab V': 'assets/icons/brand-arturia-analoglab.png',
}

conn = sqlite3.connect(staged)
cur = conn.cursor()
rows = cur.execute('SELECT id, label FROM items').fetchall()
seen = set()
for item_id, label in rows:
    if label in mapping:
        cur.execute('UPDATE items SET icon_path=? WHERE id=?', (mapping[label], item_id))
        seen.add(label)
conn.commit()
ok = cur.execute('PRAGMA integrity_check').fetchone()[0]
conn.close()
missing = sorted(set(mapping) - seen)
print(json.dumps({'staged': str(staged.resolve()), 'updated': len(seen), 'missing': missing, 'integrity': ok}, ensure_ascii=False))
if missing or ok != 'ok':
    raise SystemExit(1)
