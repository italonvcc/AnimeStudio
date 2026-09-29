# Character export performance

Measured on 2026-09-27 against the read-only **7.1.0** client, on a Ryzen 9 9950X3D (16 cores / 32 logical processors) with 125.6 GiB RAM. Production baseline: `0665cdc7afc2d0d20bfb3c99baf0e8c098c65139`; optimized implementation: `bff1a35a95430178935463296b68e05685209511`. Benchmarks used the working implementation before the final commit; subsequent edits clarify timing field names, complete progress when materials are disabled, and remove a redundant logging guard. The distributable is rebuilt from the commit.

## Findings

The previous exporter used approximately one logical processor across the measured process. Mona's 546-clip export took 102.96 seconds and 103.88 CPU-seconds; animation conversion/writing alone took 81.82 seconds. Parallel writers initially reduced the total only to 83.14 seconds because curve preparation remained serialized.

The final implementation:

- Builds per-clip curves and YAML concurrently. Graph/pointer resolution and native ACL calls retain a shared gate; each clip has a single worker. Cache binding lookup, humanoid curve objects and resolved custom attributes within each converter. Filter irrelevant objects before sorting rig candidates.
- Parses independent GI serialized files concurrently, keeping each file's cursor on one worker. Resource seek/read sequences and resource-cache lookup are synchronized. Dependency linking remains sequential; other loaders retain the default single parser worker.
- Encodes VFX textures from private byte snapshots and parses aligned object type trees with private readers. Raw source reads and pointer traversal remain protected. Output/manifest ordering remains deterministic; failures remain reported per object.
- Skips interpolated verbose-log arguments entirely when verbose logging is disabled. Previously, large diagnostic type-tree dumps were constructed and discarded. Enabled logging retains formatting and caller prefixes.
- Enables server garbage collection for GUI and CLI. On the final Mona workload, server GC took 35.59 seconds versus 53.53 seconds with workstation GC, with lower observed peak working set (9.59 versus 11.41 GiB). See Microsoft's [GC performance guidance](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/performance) and [runtime GC settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector).

Worker count defaults to half the logical processors, bounded to 1–16 and by one worker per 2 GiB of available runtime memory budget. This is a concurrency heuristic, not a total-memory cap; loaded bundles dominate Vesna's memory use. `ANIMESTUDIO_EXPORT_WORKERS` can request a lower count and is clamped to the same bounds. This machine selects 16 automatically. No GPU computation was added.

## Matched workload results

The harness calls the same `GenshinCharacterExporter.Export` entry point as the GUI. Each run used a fresh destination, cached current-client references, normal Info/Warning/Error logging, materials enabled, and voices disabled. Runs were sequential, without other benchmarks in parallel. Reference preparation and Unity import are outside the timed export; filesystem caches were not flushed. These are observed development runs on one machine, not statistical or cold-start guarantees.

| Workload | Before | After | Reduction | Speedup |
| --- | ---: | ---: | ---: | ---: |
| Mona model + all 546 selected native clips | 102.96 s | 35.59 s | 65.4% | 2.89× |
| Vesna model + 8 sampled clips + full discovered VFX | 108.59 s | 58.08 s | 46.5% | 1.87× |

| Resource measure | Mona before / after | Vesna before / after |
| --- | ---: | ---: |
| Process CPU-seconds during export | 103.88 / 140.95 | 111.17 / 274.25 |
| Process peak working set | 10.22 / 9.59 GiB | 23.38 / 24.27 GiB |
| Managed bytes allocated during export | 53.30 / 44.47 GiB | 91.67 / 76.06 GiB |

Peak working set is the process lifetime high-water mark, including reference preparation. Allocated bytes are cumulative allocation, not simultaneously occupied memory. The exporter now uses more CPU in parallel but still has sequential bundle/dependency work and native decoder calls; 100% overall CPU utilization is not expected throughout an export. Animation size and fidelity are unchanged by these performance changes.

Selectors and qualified CAB/source/container identities are recorded in [the prior validation](character-export-fixes.md#evidence). Mona uses `Avatar_Girl_Catalyst_Mona@-2810238947810998359`; Vesna uses `Avatar_Girl_Sword_Vesna@-2416894827844816308`. Vesna retains 258 effect selections plus five event assets, 328 dependency bundles, 10,953 exported objects, and zero missing selections, unresolved references or failed outputs. The eight-clip workload is explicitly not Vesna's complete animation library. Voices were not benchmarked.

## Correctness checks

- **231 synthetic checks** and **15 GUI checks** pass. New checks cover worker bounds, opt-in parallel parsing, lazy verbose arguments/formatting, and 2,000 competing resource reads with deliberately interleaved cursor scheduling. GUI/CLI .NET 10 builds and GUI .NET 9 build pass.
- All **546 Mona source clips** are byte-identical by SHA-256, including 245 shared clips. Its 324 character payload/metadata files match exactly. Vesna's **21,273 payload/metadata files** and eight source clips also match exactly. Manifest graph identities, material JSON, texture PNGs, raw VFX data, parsed JSON, recipes and source animation bytes are covered.
- The comparison excludes timing reports and timestamped FBX binaries, permits only `toolRevision` differences in JSON, and compares generated importer code after normalizing its random class ID. No payload mismatch was ignored. Shared composition importer code is unchanged by this optimization.
- A fresh isolated **Unity 6000.6.0f1** import of the optimized Vesna model plus eight clips passes: 11 skinned meshes, 228 bone slots, 46,995 vertices; FBX and unanimated prefab rest-vertex error at most **4.271e-7 m**. Eight clips and two cached compositions import successfully, with zero missing curve paths. The first validation invocation lacked the shared importer in its manually assembled fixture; adding the exported `Generic/Editor` folder corrected that fixture error.
- Earlier Mona motion and all three character rest-pose checks remain documented in [character export fixes](character-export-fixes.md). This turn does not claim new full-library playback validation. Five Vesna shader names remain undecoded, and runtime particle reconstruction remains deferred; faster extraction does not change those limitations.

## Reproduction

From `docs/Genshin/`, with the AnimeStudio projects built and the current-client reference cache prepared:

```powershell
dotnet run --no-restore --project tools/RegressionChecks
dotnet run --no-restore --project tools/GuiWorkflowChecks
$env:DOTNET_gcServer = '1' # the harness is its own executable; GUI/CLI configure this themselves
dotnet run --no-restore --project tools/RegressionChecks -- --shared-character Maps/genshin-7.1.map 'Avatar_Girl_Catalyst_Mona@-2810238947810998359' Export/<fresh-parent>/Mona --full
dotnet run --no-restore --project tools/RegressionChecks -- --shared-character Maps/genshin-7.1.map 'Avatar_Girl_Sword_Vesna@-2416894827844816308' Export/<fresh-parent>/Vesna --vfx
python tools/compare_character_exports.py Export/<baseline>/Mona Export/<fresh-parent>/Mona Export/<comparison>.json
```

`export-profile.json` records elapsed time, CPU time, allocation and phase boundaries. The production `animation-export-performance.json` and `VFX/export-performance.json` record worker counts and phase duration. Animation `prepareWorkerSeconds`/`writeWorkerSeconds` are sums across workers, including gate waits; they are not CPU time or additive elapsed phases. Earlier benchmark snapshots used the field names `decodeSeconds`/`writerSeconds` for those counters.

Ignored evidence remains under `Export/performance-baseline/`, `Export/performance-final-server/`, `Export/performance-final-workstation/`, the two `performance-*-byte-check.json` reports, and `Export/character-performance-unity/character-rest-check.json`. The packaged GUI is `Export/AnimeStudio-Performance/AnimeStudio.GUI.exe`. No client assets, generated reports, binaries, maps or local installation paths are committed.
