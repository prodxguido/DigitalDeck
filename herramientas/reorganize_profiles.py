import sqlite3, json, shutil
from pathlib import Path

db = Path.home() / 'AppData/Roaming/DigitalDeck/deck.db'
staged = Path('deck-profile-reorg-staged.db')
shutil.copy2(db, staged)
conn = sqlite3.connect(staged)
conn.row_factory = sqlite3.Row
cur = conn.cursor()

profiles = [dict(r) for r in cur.execute('SELECT * FROM profiles ORDER BY id').fetchall()]
pages = [dict(r) for r in cur.execute('SELECT * FROM pages ORDER BY id').fetchall()]
print('profiles_before', profiles)
print('pages_before', pages)

estudio = cur.execute('SELECT id FROM profiles WHERE name=?', ('Estudio',)).fetchone()
produccion = cur.execute('SELECT id FROM profiles WHERE name=?', ('Producción',)).fetchone()
if not estudio:
    raise SystemExit('Missing Estudio profile')
estudio_id = estudio[0]

if produccion:
    prod_id = produccion[0]
    prod_pages = cur.execute('SELECT id, name FROM pages WHERE profile_id=? ORDER BY id', (prod_id,)).fetchall()
    target = cur.execute('SELECT id FROM pages WHERE profile_id=? AND name=?', (estudio_id, 'Producción')).fetchone()
    if target:
        target_id = target[0]
        for row in prod_pages:
            cur.execute('UPDATE items SET page_id=? WHERE page_id=?', (target_id, row[0]))
            cur.execute('DELETE FROM pages WHERE id=?', (row[0],))
    elif prod_pages:
        target_id = prod_pages[0][0]
        cur.execute('UPDATE pages SET profile_id=?, name=? WHERE id=?', (estudio_id, 'Producción', target_id))
        for row in prod_pages[1:]:
            cur.execute('UPDATE items SET page_id=? WHERE page_id=?', (target_id, row[0]))
            cur.execute('DELETE FROM pages WHERE id=?', (row[0],))
    cur.execute('DELETE FROM profiles WHERE id=?', (prod_id,))

conn.commit()
integrity = cur.execute('PRAGMA integrity_check').fetchone()[0]
after_profiles = [dict(r) for r in cur.execute('SELECT * FROM profiles ORDER BY id').fetchall()]
after_pages = [dict(r) for r in cur.execute('SELECT * FROM pages ORDER BY id').fetchall()]
summary = {}
for row in cur.execute('SELECT p.name AS profile, pg.name AS page, COUNT(i.id) AS items FROM profiles p JOIN pages pg ON pg.profile_id=p.id LEFT JOIN items i ON i.page_id=pg.id GROUP BY p.id, pg.id ORDER BY p.id, pg.id'):
    summary[f"{row['profile']} / {row['page']}"] = row['items']
print(json.dumps({'profiles_after': after_profiles, 'pages_after': after_pages, 'pages_items': summary, 'integrity': integrity}, ensure_ascii=False))
conn.close()
if integrity != 'ok':
    raise SystemExit(1)
