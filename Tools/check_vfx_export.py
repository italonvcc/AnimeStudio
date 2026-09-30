"""Verify a completed VFX export's files, image decoding and material identities (requires Pillow)."""
import argparse
import json
from collections import Counter
from pathlib import Path
from PIL import Image

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('export', type=Path)
p.add_argument('report', type=Path)
a = p.parse_args()
root = a.export.resolve()
manifest = json.loads((root / 'manifest.json').read_text(encoding='utf-8-sig'))
effects = [e for action in manifest['actions'] for e in action['effects']]
dependencies = [d for effect in effects for d in effect.get('dependencies', [])]
shared_root = root.parent.parent / 'Generic'
shared_package = (root.parent / 'shared-assets.json').is_file()
def allowed(path):
    return path.is_relative_to(root) or (shared_package and path.is_relative_to(shared_root))
def label(path):
    return str(path.relative_to(root)) if path.is_relative_to(root) else str(path.relative_to(shared_root.parent))
linked = {(root / d['file']).resolve() for d in dependencies if d.get('file')}
errors = []
for key in ('missingSelections', 'unresolved', 'failures'):
    if manifest[key]:
        errors.append(f'{key}: {len(manifest[key])}')
images = []
for path in sorted(set(root.rglob('*.png')) | {p for p in linked if p.suffix == '.png' and allowed(p)}):
    try:
        with Image.open(path) as im:
            im.load()
            images.append({'file': label(path), 'size': im.size,
                           'extrema': im.getextrema()})
    except Exception as exc:
        errors.append(f'{label(path)}: {exc}')
shaders = Counter()
materials = {p for p in (root / 'Material').glob('*.json') if p.name != 'shared-index.json'}
materials.update((root / d['file']).resolve() for d in dependencies if d.get('file') and d.get('identity', {}).get('type') == 'Material')
for path in sorted(materials):
    if not allowed(path) or not path.is_file():
        errors.append(f'Missing/outside material: {path}')
        continue
    material = json.loads(path.read_text(encoding='utf-8-sig'))
    shader = material['m_Shader']
    shaders[shader['Name']] += 1
    if not shader['IsNull'] and not shader['Name']:
        errors.append(f'{path.name}: unnamed shader')
for effect in effects:
    for dep in effect.get('dependencies', []):
        name = dep.get('file')
        path = (root / name).resolve() if name else None
        if path is None or not allowed(path) or not path.is_file() or path.stat().st_size == 0:
            errors.append(f'Missing/empty/outside dependency: {name}')
report = {'gameVersion': manifest['gameVersion'], 'roots': len(effects),
          'images': images, 'shaders': dict(shaders), 'errors': sorted(set(errors)),
          'scope': 'File integrity and decoding only; does not validate runtime particle behavior or character ownership.'}
with a.report.open('x', encoding='utf-8') as output:
    json.dump(report, output, indent=2)
print(f'{len(effects)} roots; {len(images)} decoded PNGs; {sum(shaders.values())} materials; {len(report["errors"])} errors')
raise SystemExit(bool(errors))
