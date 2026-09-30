"""Select semantic voice names through a user-supplied AnimeWwise Mapper checkout.

No third-party implementation or mapping data is bundled. The upstream map and
reader retain their own CC BY-NC-SA license; outputs are local research data.
"""
import argparse
import hashlib
import importlib.util
import json
import re
import sys
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('reader_directory', type=Path)
p.add_argument('map', type=Path)
p.add_argument('pattern')
p.add_argument('output', type=Path)
p.add_argument('--revision', required=True)
args = p.parse_args()
if args.output.exists():
    raise FileExistsError(args.output)
sys.path.insert(0, str(args.reader_directory.resolve()))
from mapper import Mapper
mapping = Mapper(str(args.map))
pattern = re.compile(args.pattern, re.I)
entries = []
for key in mapping.keys:
    name, language = mapping.get_key(key, addLang=True)
    if pattern.search(name):
        entries.append(dict(kind='External', id=str(int(key, 16)), name=name, language=language))
report = dict(schemaVersion=1, source=dict(
    url='https://github.com/Escartem/AnimeWwise', revision=args.revision,
    license='CC-BY-NC-SA-4.0', sha256=hashlib.sha256(args.map.read_bytes()).hexdigest(),
    readerSha256=hashlib.sha256((args.reader_directory / 'mapper.py').read_bytes()).hexdigest(),
    evidence='External semantic path map; validate installed package presence and playback.'), entries=entries)
args.output.parent.mkdir(parents=True, exist_ok=True)
with args.output.open('x', encoding='utf-8') as out:
    json.dump(report, out, indent=2)
print(f'{len(entries)} mapped entries written')
