"""Reconcile manually browsed Wiki dialogue ownership with installed CN NpcYell rows.

Inputs are private research snapshots; only matching IDs, text fingerprints and source URLs ship.
"""
import json
from pathlib import Path

root = Path(__file__).resolve().parent.parent
audit = json.loads((root / 'dist/research/s-rank-dialogue-wiki.json').read_text(encoding='utf-8'))
game = json.loads((root / 'dist/research/hunt-dialogue-data.json').read_text(encoding='utf-8'))
rows = {r['RowId']: r for r in game['Rows']}
hunts = {h['Name']: h for h in game['Hunts']}
assert len(audit) == len(hunts) == 47
profiles = []
for page in audit:
    assert not page.get('retry')
    hunt = hunts[page['requested']]
    expected = {}
    for entry in page['dialogues']:
        uid = entry.get('id') or ''
        if not uid.startswith('YE-'):
            continue
        assert entry['speaker'].startswith(hunt['Name'])
        row_id = int(uid[3:])
        text = entry['text'].replace('\r\n', '\n').strip()
        assert rows[row_id]['Text'].replace('\r\n', '\n').strip() == text, (hunt['Name'], row_id)
        expected[row_id] = text
    if hunt['NameId'] == 13156:
        # BossMod pinned in THIRD_PARTY_NOTICES confirms all 30 directional yell variants.
        for first in (16874, 16879, 16884, 16889, 16894, 16899):
            for offset in (3, 4):
                assert rows[first + offset]['Text'] == expected[first + offset - 2]
                expected[first + offset] = rows[first + offset]['Text']
    profiles.append(dict(NameId=hunt['NameId'], Name=hunt['Name'], ModelId=hunt['ModelId'],
                         WikiUrl=page['url'], Rows=[dict(RowId=k, ExpectedText=v) for k, v in sorted(expected.items())]))
target = root / 'src/HuntModelSwapCN/Catalog/SRankDialogueProfiles.json'
target.write_text(json.dumps(profiles, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(f'{len(profiles)} hunts checked; {sum(bool(p["Rows"]) for p in profiles)} with verified dialogue; '
      f'{sum(len(p["Rows"]) for p in profiles)} native rows; '
      f'{sum(len(set(r["ExpectedText"] for r in p["Rows"])) for p in profiles)} distinct lines.')
