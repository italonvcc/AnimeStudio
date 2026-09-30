"""Run the fork-backed scene indexer in bounded batches after targeted discovery proves insufficient."""
import argparse, json, subprocess
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('blocks', type=Path)
p.add_argument('output', type=Path)
p.add_argument('pattern')
p.add_argument('--batch-size', type=int, default=48)
a = p.parse_args()
if not 1 <= a.batch_size <= 64:
    raise ValueError('Batch size must be 1–64')
a.output = a.output.resolve()
files = sorted(a.blocks.resolve().rglob('*.blk'))
tool = Path(__file__).parent / 'AssetStudy/bin/Debug/net10.0/win-x64/AssetStudy.dll'
if not tool.is_file():
    raise FileNotFoundError(tool)
a.output.mkdir(parents=True, exist_ok=False)
merged = []
for start in range(0, len(files), a.batch_size):
    batch = a.output / f'batch-{start // a.batch_size:03}'
    seed = a.output / f'seed-{start // a.batch_size:03}.json'
    seed.write_text(json.dumps({'entries':[{'Source':str(f)} for f in files[start:start+a.batch_size]]}))
    with (a.output / f'batch-{start // a.batch_size:03}.log').open('w') as log:
        subprocess.run(['dotnet', str(tool.resolve()), '--reindex', str(seed), str(batch), a.pattern], stdout=log, stderr=subprocess.STDOUT, check=True)
    merged.extend(json.loads((batch/'scene-index.json').read_text())['AssetEntries'])
    print(f'{min(start+a.batch_size,len(files))}/{len(files)} source files; {len(merged)} matching entries', flush=True)
(a.output/'scene-index.json').write_text(json.dumps({'GameType':'GI', 'AssetEntries':merged}))
print('Complete:', a.output/'scene-index.json', flush=True)
