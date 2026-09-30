# Weapon export: offline completion and validation

Date: 2026-09-29 (America/New_York). Branch `codex/weapon-export`.
Continuation of [the initial implementation](Weapon-Export-Implementation.md) and
[the plan](Weapon-Export-Plan.md). Work is restricted to AnimeStudio source,
generated files outside Unity, and offline tests. Another agent owns the Editor.

## Current scope

The export service now groups a child GameObject, Animator or mesh under an
enclosing root only when parsed source references prove the relationship. Older
per-object selector keys remain aliases, including Asset Browser preselection.
Cycles and conflicting ancestry keep records separate with an ambiguity report.
Multiple mesh renderers may share a group only when they resolve to the same
proven enclosing root. This is source hierarchy grouping, not an authoritative
registry of equippable weapon IDs or gameplay appearance variants.

The GUI and CLI share materials/textures, animations, effects, and optional
**offline Unity import setup**. CLI flag `--unity-import` creates recipes and
importer source without invoking Unity. `--verify-source-content` additionally
hashes all selected bundle contents before cache reuse. Bounded scans hash contents
by default; ordinary full-client preparation uses source length/mtime metadata.
Reports name the fingerprint mode and declared-directory version coverage.

Model/animation/effect reports preserve source identities, raw custom components,
available type-tree data, parsed controller layers/states/defaults/overrides and
original controller bytes. These records do not recreate custom runtime behavior.
Animation target paths are derived from source Animator/legacy Animation owners
relative to the exported model root; clips are not silently retargeted to an
outer container. Original state graphs remain separate from generated previews.

The VFX wrapper partitions more than 32 actions, retains every action, rebases
effect/dependency/evidence file links, and accounts for cancellation and failures.
Appearance-disabled VFX omits material, texture and shader payloads. A dependency
index snapshots qualified asset candidates once for a batch; each resolver keeps
its own load/attempt state.

Offline import generation supports source-derived Static, Generic and Legacy
recipes. Root/Motion translation and quaternion channels do not by themselves
imply a Humanoid Avatar. Ambiguous/body/unknown channels remain explicit. Separate
export runs use separate importer namespaces. Recipes constrain paths to the
variant folder, preserve existing prefabs/controllers/materials and FBX remaps,
and report preserved assets that need manual review instead of overwriting edits.
Actual import, binding, reimport and playback remain unvalidated.

## Installed-source audit

Input map remains `docs/Genshin/Maps/genshin-7.1.map`, SHA-256
`A63564A788A006B45ACCFD18B609403A2581F7F246E275ECD942454E578862D0`.
The map contains 3,129,196 entries across 2,099 installed-client source files,
totalling 54,994,332,417 bytes. The catalog report retains the exact list, including
StreamingAssets/Persistent bundles and indexed loose files.

The v3 full scan completed. All 2,099 source paths yielded serialized files; it
recorded zero object parse errors and zero source failures. The cache contains
203,336 supplemental entries, 177,970 topology links and 1,335,254 serialized-file
records. Directory metadata declares client version 7.1.0. Internal loader errors
may still be caught without propagation, so these counters do not certify every
serialized object or a complete equippable registry.

The final grouping pass produced 1,039 source groups, 926 rooted and 113 mesh-only.
Of those mesh-only records, 92 had multiple qualified renderer owners and 21 had
no qualified renderer link. Grouping same-root multi-owner meshes did not change
these counts: the remaining records have distinct or unproven owners. The class
counts are 104 Bow, 83 Catalyst, 185 Claymore, 288 Polearm and 379 Sword.

The 21 meshes without parsed renderer links were Bow `Pledge_Model`,
`Worldbane_Model_Eff_01/02`; Catalyst `MechaPufferfish_Model`,
`Narukami_Model_Eff_01/02/03`, `VaresaTransformer_Model`, `Widsith_Model`;
Claymore `Aniki_Model001`; Polearm `Deshret_Model`, `Muguet_Eff_Model`,
four qualified `Ruby_Model` entries, `Santika_Old_Model`, `Santika_Old_Model_LOD1`,
`Widsith_Model`; and two qualified Sword `Blunt` entries. This does not prove that
these meshes are unused or that their model roots are absent from the client.

## Source-data limitations established by probes

Exact external effect candidates exist in `05785566.blk`:

| Object | Offset | PathID |
| --- | ---: | ---: |
| `Eff_Weapon_Amenoma_Hand` | 30291808 | -1913456645006045516 |
| `Eff_Weapon_Amenoma_Head` | 30297726 | -6667287480817975855 |
| `Eff_Weapon_Kasabouzu` | 30595290 | -752418133347101319 |

Independent structured exports of all three passed with appearance enabled and
disabled, with zero unresolved dependencies or failed output records. Those tests
establish exportable effect objects; they do not prove weapon ownership or trigger
timing. The exact requests and supplemental map are in
`artifacts/weapon-validation/offline-audit/weapon-effects-*.json`.

The actual Amenoma and Kasabouzu model graphs do not establish links to those
external effects or to icon/appearance variants. Qualified probes of their
`MonoVisualEntityTool` components (412 bytes each) and Amenoma's `DynamicBoneArray`
(568 bytes) returned `typed: null`. `Object.ToType` returns null when no serialized
TypeTree exists. Strings such as `RootNode`, `EffectMesh`, `NormalObject` and
`ItemEffect` do not establish field offsets, PPtr layouts, weapon IDs, sockets or
timing. A bounded installed-filename search found no named weapon/item registry
JSON; numeric `.blk` files contain the asset payloads. Probe outputs, raw bytes
and selectors are in `artifacts/weapon-validation/offline-audit/weapon-mono-*`.

**Required source evidence:** a verified field layout or decoded registry/config
linking weapon IDs to models, appearance/icon variants and external effect
references, plus attachment/activation behavior. Source bytes exist; the missing
dependency is their schema/authoritative relationship. Do not infer these links
from names or scan arbitrary byte sequences as pointers. Linked audio, external
effect automation and complete appearance/icon association remain blocked on this
evidence, independently of Unity availability.

Arcdange supplies a proven embedded-effect fixture: seven effect nodes in its
model hierarchy export without failed or unresolved dependencies. Six attached
clips export, but three `Equip_Sword_Vorpal_{Default,Ousia,Pneuma}` clips contain
unresolved GameObject path hash `753088835` for `m_IsActive` (attribute `2086281974`).
`Ousia` and `Pneuma` objects are present; the exported source hierarchy has no
`Default` object. A controller string table maps the hash to `Default`, but also
uses it as a state name. That is not proof of a missing transform path.

The audit also found and fixed an independent `AnimationClipExtensions.AddTOS`
early-completion bug: the seeded zero path and repeated path channels made a
dictionary-size comparison invalid. Completion now requires every requested hash.
This fix does not invent Arcdange's absent binding target. Its three clips remain
partially bound pending source hierarchy/variant evidence.

## Additional exporter defects found by the full sweep

Alkonost references `Monster_Tex_01_Cube` through material `_CubeMap`. Its exact
target, `CAB-63bd32869e5f3fdd0b6cefe150e196be` / PathID
`-5959861441294032356`, is present at `03950401.blk@23113411` but absent from the
object-level map. The dependency index now also accepts the cached serialized-file
locations. It loads a unique exact CAB location and lets the original typed PPtr
verify the object. Conflicting CAB locations remain ambiguous.

`GenshinCubemapPayload` shares the model/VFX preservation path: original serialized
bytes, complete image-data bytes, format/mip/settings metadata, hashes and available
face previews. The native bytes remain authoritative; PNG previews do not preserve
HDR/compression/mips. Model slot records and import recipes retain the Cubemap's
qualified identity and payload paths. Unity Cubemap binding is explicitly unsupported.
Alkonost's real export has all six previews and zero unresolved dependencies.

Iudex's meshes constrain the shared `bone_book` to different rest transforms. The
FBX native writer already stores each mesh's cluster link matrix from that mesh's
inverse bind (`api.cpp`, `SetTransformLinkMatrix`). Weapon export now permits a
typed conflict fallback: restore the complete original converted hierarchy TRS,
retain every source inverse bind and weight, and record both conflicting source
constraints. It does not relax missing-bone, singular-matrix or transform errors.
The default character path remains strict. `iudex-pose` exports four meshes and
nine clips with zero unresolved dependencies; this path remains Partial until live
Unity skin/rig/bind-pose validation.

An unsupported animation decode no longer discards an otherwise exported weapon
model. Weapon-only tolerance preserves successful clips and per-clip failure records,
raw serialized clip bytes, parsed ACL track/database payloads and streamed-resource
provenance. The default shared model API still throws unless tolerance is requested.
Ayus's `ayus-decode` fixture retains three meshes and three clips, with two explicit
decode failures rather than a missing model manifest. Precise decode diagnosis is
recorded below when established.

The standalone test DLL was initially launched through `dotnet`, whose executable
directory does not contain AnimeStudio's native libraries. The test apphost fixes
that launch condition. This also exposed a production finalizer defect: a failed
FBX native initialization left an incompletely constructed context whose finalizer
threw. Cleanup now tolerates absent collections/context. An isolated forced-failure
test verifies the original error remains recoverable without a finalizer crash.

## Earlier validation milestones

All evidence is under ignored `artifacts/weapon-validation/`; no generated assets
were copied into Unity's watched folders. The initial full-catalog run
`catalog-export-all-v3` was deliberately interrupted after nine ledger entries to
integrate shared dependency indexing and stronger failure diagnostics. Its ledger
retains Running status as interrupted evidence; it is not a completed catalog run.

The old resolver rebuilt the 3.13-million-entry candidate index in 1,415 ms in a
measured constructor check. Repeating that twice per 1,039 records alone approaches
49 minutes, before file conversion. The shared index removes that repeated work.

The 33-action real-effect test crossed the batch boundary into two batches, retained
33 effect records and both rebased animation-evidence records, with zero unresolved
dependencies. `effects-many-reviewed/manifest.json` and its log preserve the result.
Pure tests also cover 65-action partitioning, cancellation accounting, graph cycles,
shared meshes, old selectors, path traversal and same-size/same-timestamp source
changes under content verification.

Build `20260930T015952278Z` passed canonical GUI/CLI publication. Its source hashes
matched the working source at publication. The subsequent source closure work and
its final build are recorded separately below; a historical build is not evidence
for later edits.

- Pure weapon regressions: 48/48, including qualified container lookup, rejected
  ambiguous CABs, conflicting bind-pose provenance and detached-bone rejection.
- Forced native-loader failure: passed without a finalizer crash.
- Shared shader-name regressions: 7/7; blend-shape binding regression: passed all
  eleven real UAngry02 CRCs, exact paths, named preservation and missing-channel rejection.
- Fresh canonical Mona export: 21 meshes, 22 material slots, no unresolved dependency
  or native texture failure (`character-model-cubemap`).
- Kasabouzu grouped-root fixture: 69/69 file/hash/mesh/clip checks; both clips target
  `Equip_Sword_Kasabouzu_Model` relative to the exported enclosing root.
- Real option policy: geometry succeeds without appearance dependencies; requested
  missing appearance fails, and a missing mesh fails even with appearance disabled.
- Hidden GUI smoke and synthetic Cubemap recipe round-trip: passed. Importer source
  compiles against installed Unity managed assemblies without connecting to Editor.

The full catalog validation uses eight independent test processes with disjoint
keys/output roots and two internal workers each. Each calls the production sequential
batch service. They use the frozen Cubemap-capable source from build
`20260930T015110723Z`; later fixes receive targeted reruns. Initial incorrectly hosted
test runs and interrupted sequential runs remain historical evidence, never completed
catalog results. Final accounting and remaining source limitations follow below.

## Final offline result

The canonical build is `20260930T022331687Z`, in `dist/net10.0-windows`.
`dist/net10.0-windows/build-manifest.json` records compiler logs, source hashes and
payload hashes. No Unity project/package files were changed, and no Editor import,
refresh, compilation, scene operation or playback was invoked.

The full sweep completed all **1,039/1,039** source keys across eight disjoint
production-service batches. `catalog-full-audit.json` verifies the exact key set,
source fingerprints, all **22,115** listed output files and their SHA-256 hashes.
Its frozen-source outcomes were 896 Partial and 143 Failed: 47 rooted failures and
96 mesh-only failures. Shards cannot share cross-record aliases; a separate exact
source-identity audit found 69 of those mesh-only records already exported in other
shards (`catalog-full-audit-with-coverage.json`). This is dependency coverage, not
proof of an equippable family relationship. The full sequential production batch
can link those dependencies when their owning roots precede the mesh record.

All 47 rooted failures plus both Ayus variants were rerun after the fixes in
`catalog-retry-0` through `catalog-retry-3`. All 49 were accounted for; **36 of the
47 rooted failures now export**, leaving 11 source-limited rooted failures:

- Four Darker records request Mesh `7754218542786632672` from
  `CAB-47e7f9bbe52ccda19f6306db39190c40`. Its actual metadata contains Mesh
  `-885322651828743213` and AssetBundle `1`, not the requested ID. Exact source:
  `07845116.blk@40581213`.
- Oyaji requests Mesh `3927692894465953714` from
  `CAB-886386b06ac3aa4305a9eb0243c3a5da`. Its actual metadata contains Mesh
  `3090342962265824858` and AssetBundle `1`. Exact source: `15871610.blk@27590299`.
  Both CABs load successfully through the exact-offset loader; substituting the
  other mesh would violate the qualified source pointer. Evidence:
  `artifacts/weapon-importer-compile/CabProbe/missing-mesh-cab-probe.log`.
- Four Crowfeather records contain an explicitly null skinned mesh pointer.
  Blunt and BloodMoon_Dissolve produce no model geometry. Their renderer/source
  records remain in `catalog-remaining-rooted.json`; these are not missing bind poses.

The detached-rig fix accounts for 31 recovered records. It requires one resolved
source bone pointer per bind pose, valid weighted indices and acyclic same-container
parent chains. The export contains only the selected weapon subtree plus the
referenced bones and their paths to the source-proven common ancestor. Unrelated
renderers stay excluded. Clip target paths are rebased by the proven selected-root
path relative to the FBX root; runtime binding remains unvalidated. YeLan's real
source and FBX agree on one 2,956-vertex mesh and four bones. Independent binary FBX
inspection found 2,970 faces, four skin clusters and 4,814 weighted influences.
The source recipe records eight included transforms and one excluded sibling.

Iudex and SakuraFan recover through preserved per-mesh bind poses. FairyGarden and
two BloodMoon records recover because duplicate names on unreferenced effect
transforms no longer invalidate the skin path table; duplicate names on actually
referenced bone/renderer paths still fail. Independent FBX parsing also checks
Iudex's four meshes, seven skin clusters and 6,668 weighted influences. These file
contracts do not prove Unity deformation or visual fidelity.

Final focused checks include 50/50 pure regressions, a three-record real batch
(Amenoma, Kasabouzu and an exact mesh-alias fixture) with 9 old selector aliases,
one linked mesh alias and no failed entries, 69/69 Kasabouzu file/source checks,
and the 33-action VFX test across two batches with no unresolved dependency.
The shared character regression exports Mona's 21 meshes, 22 material slots and
one native Standby clip with no unresolved dependencies or animation failure.
Hidden GUI smoke, Cubemap/bind-pose/rig recipe round-trips and offline compilation
of the generated importer passed. All real checks write under ignored `artifacts/`.

Ayus uses legacy `AC10AC10` compressed clips rather than `AC11AC11` tracks. Format
dispatch now validates the legacy v3 header, dimensions and offsets, aligns native
input, and checks output dimensions. The legacy MHY DLL is loaded from the same
application directory where the canonical build packages it. Captured Appear/Loop
headers describe 14/42 samples and 560/1,680 values; corrupted tag/version/offset
controls are rejected without a native decode. The fresh `ayus-native-release`
canonical export contains **all five native clips with zero animation failures**,
including both previously failing legacy clips. It also retains three meshes and
has zero unresolved dependencies. This verifies decoding/export, not playback.

## Remaining acceptance work

Unity import/reimport, material and Cubemap binding, rig deformation, Generic/legacy
animation playback/root motion, attachment/socket behavior and effect runtime/visual
comparison remain untested. The generated importer explicitly reports unsupported
or unvalidated cases. Source registry/variant identity, external effect ownership,
runtime script layouts/timing and the mismatched/null mesh references above require
additional source evidence; similar names are not a replacement for it. The available
feature is an indexed source exporter with traceable partial coverage, not a claim
that every gameplay weapon/effect is fully reconstructed.
