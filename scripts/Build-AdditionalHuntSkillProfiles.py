"""Build A/B and requested special hunt skill profiles from reviewed Wiki ownership and CN resources."""
import collections
import json
from pathlib import Path
import re

root = Path(__file__).resolve().parent.parent
research = root / 'dist/research'
data = json.loads((research / 'all-hunt-skill-data.json').read_text(encoding='utf-8'))
facts = {f['nameId']: f for f in json.loads((research / 'additional-hunt-wiki-facts.json').read_text(encoding='utf-8'))}
hunts = [h for h in data['Hunts'] if h['IsSpecial'] or h['Rank'] != 3]
assert len(hunts) == len(facts) == 160
revision = '4fc3798e283432e2bac33130f48769ba778a0eba'
owned, sources = {}, {}
for path in (research / 'BossMod/BossMod.Modules').rglob('*.cs'):
    if '/Hunt/' not in path.as_posix():
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
    sources[key] = f'https://github.com/awgil/ffxiv_bossmod/blob/{revision}/{path.relative_to(research / "BossMod").as_posix()}'

all_names = {a['Name'] for a in data['AllActions'] if a['Name']}
by_name = collections.defaultdict(list)
for action in data['Actions']:
    by_name[action['Name']].append(action)
resources = {r['NameId']: set(r['Keys']) for r in data['Resources']}
aliases = {(10627, '装填环形导弹'): '装填环状导弹', (8916, '石化之眼'): '石化眼'}
expanded = {
    (10615, '前/后/左/右葬'): ['前葬', '后葬', '左葬', '右葬'],
    (10629, '瞑目 / 近侍 / 疏远的敕令'): ['瞑目的敕令', '近侍的敕令', '疏远的敕令'],
    (10631, '右侧 / 左侧锤击'): ['右侧锤击', '左侧锤击'],
}
profiles = []
for hunt in hunts:
    fact = facts[hunt['NameId']]
    assert not fact.get('retry') and fact['requested'] == hunt['Name'], hunt['Name']
    names = []
    for line in fact['skills'].splitlines():
        has_colon = bool(re.search('[：:]', line))
        name = re.split('[：:]', line)[0].strip() if has_colon else line.split()[0] if line.split() else ''
        name = name.lstrip('• ').strip()
        if (hunt['NameId'], name) in expanded:
            for split_name in expanded[hunt['NameId'], name]:
                if split_name not in names:
                    names.append(split_name)
            continue
        if name.startswith(('若', '在', '当', '以下技能', '逆反版')):
            continue
        if not name or name == '攻击' or len(name) > 24:
            continue
        if name in all_names or has_colon and not any(x in name for x in ('，', '。', '注意', '备注', '说明', '提示')):
            if name not in names:
                names.append(name)
    assert names, f'No reviewed skill names: {hunt["Name"]}'
    entries = []
    for name in names:
        action_name = aliases.get((hunt['NameId'], name), name)
        records = [a for a in by_name[action_name] if a['ActionId'] in owned.get(hunt['NameId'], [])
                   and a['TimelineKey'] in resources[hunt['NameId']]]
        candidates = records or [a for a in by_name[action_name] if a['TimelineKey'] in resources[hunt['NameId']]
                                 and not a['TimelineKey'].startswith('mon_sp/gimmick/')]
        exact = [a for a in candidates if a['Family'] == hunt['Family']]
        if not records and exact:
            candidates = exact
        groups = collections.defaultdict(list)
        for action in candidates:
            groups[action['TimelineKey'].replace('[SKL_ID]', hunt['Family'])].append(action)
        representatives = sorted((min(g, key=lambda a: a['ActionId']) for g in groups.values()), key=lambda a: a['ActionId'])
        mode = 'record' if records else 'resource' if len(groups) == 1 else 'variants' if groups else 'unavailable'
        def mapping(a):
            value = {k: a[k] for k in ('ActionId', 'TimelineId', 'TimelineKey')}
            if name != action_name:
                value['ActionName'] = action_name
            return value
        entries.append(dict(Name=name, Mode=mode,
                            Primary=mapping(representatives[0]) if mode in ('record', 'resource') else None,
                            Variants=[mapping(a) for a in representatives] if len(representatives) > 1 else []))
    profiles.append(dict(NameId=hunt['NameId'], Name=hunt['Name'], ModelId=hunt['ModelId'],
                         WikiUrl=fact['url'], RecordSource=sources.get(hunt['NameId']), Skills=entries))
out = root / 'src/HuntModelSwapCN/Catalog/AdditionalHuntSkillProfiles.json'
out.write_text(json.dumps(profiles, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
counts = collections.Counter(s['Mode'] for p in profiles for s in p['Skills'])
print(f'{len(profiles)} profiles, {sum(counts.values())} entries: {dict(counts)}')
for p in profiles:
    if not any(s['Primary'] for s in p['Skills']):
        print('NO PRIMARY:', p['Name'])
    for entry in p['Skills']:
        if entry['Name'] not in all_names:
            print('UNKNOWN NAME:', p['Name'], '/', entry['Name'])
