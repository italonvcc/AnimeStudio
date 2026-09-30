"""Coordinate AnimeStudio extraction with a separately supplied wwiser parser.

AnimeStudio performs package extraction and semantic event traversal. This tool
only runs external tools and collects explicit bank links from their XML output.
"""
import argparse, hashlib, json, subprocess, sys, xml.etree.ElementTree as ET
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('anime_studio', type=Path)
p.add_argument('wwiser', type=Path)
p.add_argument('audio_folder', type=Path)
p.add_argument('event_names', type=Path)
p.add_argument('output', type=Path)
p.add_argument('--decoder', type=Path)
a = p.parse_args()
for key in ['anime_studio','wwiser','audio_folder','event_names','output']:
    setattr(a,key,getattr(a,key).resolve())
a.output.mkdir(parents=True, exist_ok=False)

def run(label, command):
    with (a.output/f'{label}.log').open('w', encoding='utf-8') as log:
        subprocess.run([str(x) for x in command], stdout=log, stderr=subprocess.STDOUT, check=True)
    print(label, 'complete', flush=True)

events, content = a.output/'events', a.output/'content'
run('extract-events',[a.anime_studio,'--genshin-banks',a.audio_folder,a.event_names,events])
run('parse-events',[sys.executable,a.wwiser,'-d','xml','-dn',events/'parsed',str(events/'*.bnk')])
xmls=[events/'parsed.xml']
root=ET.fromstring('<banks>'+xmls[0].read_text(encoding='utf-8')+'</banks>')
if list(root.iter('error')):
    raise RuntimeError('wwiser reported parser errors; inspect the dump before proceeding')
ids=sorted({int(f.get('value')) for f in root.iter('field') if f.get('name')=='bankID'})
if ids:
    ids_file=a.output/'referenced-bank-ids.json'
    ids_file.write_text(json.dumps(ids))
    run('extract-content',[a.anime_studio,'--genshin-bank-ids',a.audio_folder,ids_file,content])
    run('parse-content',[sys.executable,a.wwiser,'-d','xml','-dn',content/'parsed',str(content/'*.bnk')])
    xmls.append(content/'parsed.xml')
request=a.output/'event-media.json'
run('event-graph',[a.anime_studio,'--genshin-event-graph',a.event_names,request,*xmls])
provenance={'animeStudioSha256':hashlib.sha256(a.anime_studio.read_bytes()).hexdigest(),
            'wwiserEntrySha256':hashlib.sha256(a.wwiser.read_bytes()).hexdigest(),
            'eventNamesSha256':hashlib.sha256(a.event_names.read_bytes()).hexdigest(),
            'parserSource':'https://github.com/bnnm/wwiser',
            'note':'Preserve external parser checkout/version alongside the generated XML for exact reproduction.'}
(a.output/'tools.json').write_text(json.dumps(provenance,indent=2))
if a.decoder:
    run('export-audio',[a.anime_studio,'--genshin-audio',a.audio_folder,request,a.output/'audio','--decoder',a.decoder.resolve()])
print('Named request:',request,flush=True)
