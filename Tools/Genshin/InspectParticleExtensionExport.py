"""Read-only audit of the native-transfer-proven extension fields in an effect export."""
import argparse
import hashlib
import json
import struct
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('effect_json', type=Path)
a = p.parse_args()
schema = json.loads((Path(__file__).parents[2] / 'AnimeStudio.Utility/GenshinParticleSystem2017Schema.json').read_text())
color = next(n for n in schema['children'] if n['name'] == 'ColorModule')

def read(node, data, at):
    children = node['children']
    if children:
        value = {}
        for child in children:
            value[child['name']], at = read(child, data, at)
    else:
        fmt = {'float':'f', 'int':'i', 'UInt16':'H', 'UInt8':'B', 'bool':'?'}[node['type']]
        value, = struct.unpack_from('<' + fmt, data, at)
        at += struct.calcsize(fmt)
    if node['flags'] & 16384:
        assert not any(data[at:(at+3)&~3]), 'Nonzero color-module alignment'
        at = (at+3)&~3
    return value, at

effect = json.loads(a.effect_json.read_text(encoding='utf-8-sig'))
rows = []
for obj in effect['objects']:
    for component in obj['components']:
        if component['type'] != 'ParticleSystem':
            continue
        data = (a.effect_json.parent / component['raw']['assetPath']).read_bytes()
        assert hashlib.sha256(data).hexdigest().upper() == component['raw']['sha256']
        tail = len(data)-440
        gradient, end = read(color, data, tail+64)
        assert end == len(data)
        rows.append(dict(name=obj['Name'], pathId=component['id']['pathID'], sha256=component['raw']['sha256'],
                         shapeBooleans=list(data[1016:1020]), textModuleEnabled=bool(data[tail]),
                         colorOverDaySourceOffset=tail+64, colorOverDayModule=gradient))
print(json.dumps(rows, indent=2))
