"""Build factual skill-name/action mappings from reviewed Wiki sections and local CN data.

The inputs live under dist/research and are not redistributed. No guide prose or module
implementation is copied into the generated resource; each profile retains its sources.
"""
import collections
import json
import pathlib
import re

root = pathlib.Path(__file__).resolve().parents[1]
research = root / 'dist/research'
data = json.loads((research / 'hunt-skill-data.json').read_text(encoding='utf-8'))
facts = json.loads((research / 's-rank-wiki-facts.json').read_text(encoding='utf-8'))
revision = '4fc3798e283432e2bac33130f48769ba778a0eba'
owned, sources = {}, {}
for path in (research / 'BossMod/BossMod.Modules').rglob('*.cs'):
    if '/Hunt/RankS/' not in path.as_posix():
        continue
    source = path.read_text(encoding='utf-8-sig')
    name_id = re.search(r'NameID\s*=\s*(\d+)', source)
    actions = re.search(r'enum AID\s*:\s*uint\s*\{(.*?)\n\}', source, re.S)
    boss = re.search(r'Boss\s*=\s*0x([\da-fA-F]+)', source)
    if not name_id or not actions:
        continue
    ids = []
    for line in actions.group(1).splitlines():
        value = re.search(r'^\s*\w+\s*=\s*(0x[\da-fA-F]+|\d+)\s*,', line)
        if value and ('Boss->' in line or boss and re.search(r'(?i)\b' + boss.group(1) + '->', line)):
            ids.append(int(value.group(1), 0))
    key = int(name_id.group(1))
    owned[key] = ids
    relative = path.relative_to(research / 'BossMod').as_posix()
    sources[key] = f'https://github.com/awgil/ffxiv_bossmod/blob/{revision}/{relative}'

all_names = {a['Name'] for a in data['AllActions'] if a['Name']}
# The reviewed Wiki uses these older spellings; keep its label and validate the
# current CN Action name independently instead of dropping the skill.
aliases = {(8900, '迅速回旋'): '迅猛回旋', (8895, '轮转枪'): '转轮枪',
           (2969, '等离子立场'): '等离子力场'}
by_name = collections.defaultdict(list)
for action in data['Actions']:
    by_name[action['Name']].append(action)
resources = {r['NameId']: set(r['Keys']) for r in data['Resources']}
profiles = []
for hunt, fact in zip(data['Hunts'], facts, strict=True):
    assert hunt['Name'] == fact['requested'] and fact['skills']
    names = []
    for line in fact['skills'].splitlines():
        name = re.split('[：:]', line)[0].strip() if re.search('[：:]', line) else line.split(' ')[0].strip()
        if name and name != '攻击' and (name in all_names or (hunt['NameId'], name) in aliases
                or name in ('预报实现', '预报修正')) and name not in names:
            names.append(name)
    entries = []
    for name in names:
        action_name = aliases.get((hunt['NameId'], name), name)
        records = [a for a in by_name[action_name] if a['ActionId'] in owned.get(hunt['NameId'], [])
                   and a['TimelineKey'] in resources[hunt['NameId']]]
        candidates = records or [a for a in by_name[action_name] if a['TimelineKey'] in resources[hunt['NameId']]]
        exact = [a for a in candidates if a['Family'] == hunt['Family']]
        if not records and exact:
            candidates = exact
        groups = collections.defaultdict(list)
        for action in candidates:
            groups[action['TimelineKey'].replace('[SKL_ID]', hunt['Family'])].append(action)
        representatives = [min(group, key=lambda a: a['ActionId']) for group in groups.values()]
        representatives.sort(key=lambda a: a['ActionId'])
        mode = 'record' if records else 'resource' if len(groups) == 1 else 'variants' if groups else 'unavailable'
        def mapping(action):
            result = {key: action[key] for key in ('ActionId', 'TimelineId', 'TimelineKey')}
            if action_name != name:
                result['ActionName'] = action_name
            return result
        entries.append({'Name': name, 'Mode': mode,
                        'Primary': mapping(representatives[0]) if mode in ('record', 'resource') else None,
                        'Variants': [mapping(a) for a in representatives] if len(representatives) > 1 else []})
    assert entries
    profiles.append({'NameId': hunt['NameId'], 'Name': hunt['Name'], 'ModelId': hunt['ModelId'],
                     'WikiUrl': fact['url'], 'RecordSource': sources.get(hunt['NameId']), 'Skills': entries})

assert len(profiles) == 47 and len({p['NameId'] for p in profiles}) == 47
output = root / 'src/HuntModelSwapCN/Catalog/SRankSkillProfiles.json'
output.write_text(json.dumps(profiles, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
counts = collections.Counter(entry['Mode'] for profile in profiles for entry in profile['Skills'])
print('Profiles:', len(profiles), 'entries:', sum(counts.values()), dict(counts))
for profile in profiles:
    if not any(entry['Primary'] for entry in profile['Skills']):
        print('NO PRIMARY:', profile['Name'])
