# Genshin weapon export implementation

Updated 2026-09-30 UTC (2026-09-29 local). Branch: `codex/weapon-export`.
Status: the offline implementation and catalog validation are complete for the
indexed-source workflow. Live Unity acceptance and the documented source-data gaps
in the [weapon export plan](Weapon-Export-Plan.md) remain open.

The user subsequently authorized completing all work that does not require the
Unity Editor. The active offline work and evidence are in
[Weapon-Export-Offline-Validation.md](Weapon-Export-Offline-Validation.md).
The build and test table below records the earlier milestone and is retained for
provenance; consult that continuation for the current source/build and remaining
source-data dependencies. No Unity mutation is authorized.

## What is available

The GUI now has **My tools > Genshin weapons** for a loaded Genshin asset map.
It prepares supplemental source references, then opens a searchable catalog with
**Materials and textures**, **Weapon animations**, and **Weapon effects**
checkboxes. Materials default on; animations and effects default off. Preferences
are saved locally. **Export Selected Weapon** and **Export Whole Indexed Catalog**
use the same export service, with progress, cancellation, reports and output-folder
links. The whole-catalog action ignores the visible search filter.

The list represents indexed source records, not one verified row per equippable
weapon. The UI states this limitation. **Unity import setup** is now enabled as
offline recipe/importer generation with an explicit unvalidated label; it does not
operate the Editor. Source names and matching animation/effect names are discovery
evidence, not proof of ownership or a gameplay variant. The continuation records
the subsequent hidden GUI smoke and offline importer compilation checks.

Individual exports create a fresh directory containing:

```text
<selected output>/
  <source-key>.weapon.json
  Variants/<qualified-root>/
    <model>.fbx
    manifest.json
    Materials/                 # when selected
    ...textures/native clips   # paths recorded in model manifest
  Source/                      # attached controller evidence, when discovered
  VFX/                         # proven embedded effects, when discovered/selected
  ...incomplete marker         # when coverage is partial
```

Batch output adds `catalog.json`, `weapon-export.json`, and `Weapons/<source-key>/`.
All reports preserve source identities and selected options. The family report
records output hashes, discovery, file export and playback separately. Existing
destinations are rejected. A batch accounts for failures and cancellation rather
than silently dropping records.

Exported hierarchy and converted mesh identities allow proven child GameObject,
Animator and mesh records to link to a previously exported root. Such aliases carry
`coveredBy` report links. Equal names alone never establish coverage. A request for
an isolated child may still be missing its enclosing rig; use the outer root when
available. Automatic promotion of all individual selections to verified enclosing
roots remains work to do.

## CLI

Run the canonical launcher `dist/net10.0-windows/AnimeStudio.CLI.exe`:

```text
--genshin-weapon-catalog <map> <new-report.json> [--map-only] [--scan-source <file>]...
--genshin-weapon <map> <catalog-key-or-exact-name> <new-output> [--animations] [--vfx] [--no-materials] [--scan-source <file>]...
--genshin-weapons <map> <new-output> [--animations] [--vfx] [--no-materials] [--scan-source <file>]...
```

Use a catalog `Key` where names are ambiguous. `--scan-source` bounds supplemental
discovery and explicitly reports that scope; all original map entries remain
available for qualified dependency resolution. Without it, preparation scans the
map's source files. `--map-only` is a catalog-inspection shortcut and performs no
supplemental discovery. Ctrl+C requests cancellation.

Exit codes: `0` for catalog generation or Complete export, `3` for an export report
whose status is not Complete, `130` for cancellation, `2` for invalid arguments,
and `1` for an exception. Current real weapon reports are Partial because broader
discovery is unverified, even when their model and animation file stages succeed.

## Implementation and evidence rules

| Source | Responsibility |
| --- | --- |
| [GenshinWeaponCatalog.cs](../../AnimeStudio.Utility/GenshinWeaponCatalog.cs) | Five weapon classes, safe stable keys, separate qualified records, candidate links and excluded records. |
| [GenshinWeaponReferences.cs](../../AnimeStudio.Utility/GenshinWeaponReferences.cs), [AssetsHelper.cs](../../AnimeStudio/AssetsHelper.cs) | Cached supplemental roots, Animators, controllers, overrides and clips; no character voice/network preparation. |
| [GenshinWeaponExporter.cs](../../AnimeStudio.Utility/GenshinWeaponExporter.cs) | Shared single/batch pipeline, source graphs, attached animations, embedded effect requests, reports and proven batch aliases. |
| [AssetDependencyResolver.cs](../../AnimeStudio/AssetDependencyResolver.cs) | Opt-in controller and animation object-reference traversal, material inclusion policy. Existing callers retain default traversal policy. |
| [GenshinModelExporter.cs](../../AnimeStudio.Utility/GenshinModelExporter.cs), [ModelConverter.cs](../../AnimeStudio.Utility/ModelConverter.cs) | Material-free geometry path, renderer-slot provenance, converted source-mesh identities and material filename collision detection. |
| [GenshinVfxExporter.cs](../../AnimeStudio.Utility/GenshinVfxExporter.cs) | With materials disabled, omit material, texture and shader appearance payloads. Default material export remains enabled. |
| [MainForm.GenshinWeapons.cs](../../AnimeStudio.GUI/MainForm.GenshinWeapons.cs), [MainForm.Genshin.cs](../../AnimeStudio.GUI/MainForm.Genshin.cs) | Catalog dialog, menu integration and lifecycle/operation handling. |
| [GenshinWeaponCommand.cs](../../AnimeStudio.CLI/GenshinWeaponCommand.cs), [Program.cs](../../AnimeStudio.CLI/Program.cs) | CLI validation and dispatch into the same service. |

Attached Animator/controller/override and legacy Animation references establish
native clip selection. Controller bytes and relationships are retained; a playable
Unity controller/state machine is not reconstructed. Native `.anim` serialization
is checked for unknown path/type markers, but that check does not prove binding or
playback after import. Character wielding animations are not automatically added.

Effect roots require particle/trail components with verified ancestry inside the
model hierarchy. Other graph-linked objects and name candidates remain separate
evidence. A shared name does not prove spawn timing, attachment, cancellation,
ownership or appearance-state semantics. The two tested roots contained no such
embedded effect roots, so their effect stages remain Partial; this test did not
exercise an actual weapon particle export or certify complete effect coverage.

Reference cache schema v2 fingerprints map bytes, scan scope, inferred version,
and source path/length/last-write metadata. It does not hash every source bundle's
contents. The version comes from the first scanned source; mixed-client input
consistency is not established. Some underlying parse failures are caught inside
AssetsManager, so preparation deliberately reports completeness as unverified.

## Build and validation

The branch was created from `9e69ff5c1b59f20003b609f961b3930cad1c6789` with substantial
pre-existing uncommitted work preserved. Changes are uncommitted. HEAD alone is not
the source identity. Initial status and focused pre-edit copies are retained in
ignored `artifacts/weapon-export-start/`.

Canonical `./build.ps1` succeeded for GUI and CLI:

- Build: `20260930T005136835Z`, Release, `net10.0-windows`.
- Distribution: `dist/net10.0-windows`.
- Build/source/payload record: `dist/net10.0-windows/build-manifest.json` and
  `artifacts/build-records/20260930T005136835Z/`.
- Utility DLL SHA-256:
  `AFCA66A951A8F38C9288AD9CA3BFEE3FB0767E7F707F26037B7A97A8AE3C4781`.
- All recorded source hashes matched the working tree after final export testing.

Source map: `docs/Genshin/Maps/genshin-7.1.map`, SHA-256
`A63564A788A006B45ACCFD18B609403A2581F7F246E275ECD942454E578862D0`.
Supplemental discovery was deliberately bounded to four files under
`C:/Program Files/HoYoPlay/games/Genshin Impact game/GenshinImpact_Data/StreamingAssets/AssetBundles/blocks/00/`:
`04803507.blk`, `07845116.blk`, `05785566.blk`, and `11877482.blk`.
The resulting index has 1,425 source records (1,152 roots and 273 mesh records),
which is not a count of weapons or evidence of a complete installed catalog.

| Check | Result and local evidence |
| --- | --- |
| Pure weapon regression | **25/25 passed**. Identity/order, candidate boundaries, all five classes, path safety, cache invalidation/scope, missing roots, cancellation, failed batches and existing-directory preservation. `artifacts/weapon-validation/catalog-regression-reviewed.log`. |
| Existing shader-name regression | **7/7 passed**. `artifacts/weapon-validation/shader-regression.log`. |
| Shared character model/material path | Final canonical CLI exported the established `Avatar_Girl_Catalyst_Mona@-2810238947810998359` baseline: **21 meshes, 22 resolved material slots**, zero unresolved dependencies/material slots/native texture failures, one FBX. `artifacts/weapon-validation/character-model-regression/` and `character-model-regression-checks.json`. No animation selection or Unity import. The initial unqualified-name attempt was rejected as ambiguous before export. |
| Final canonical CLI: Kasabouzu Animator, materials + animations | **51/51 source/file checks passed**, two native controller-linked clips, no unknown binding markers. Aggregate Partial, CLI exit 3 as designed. `artifacts/weapon-validation/kasabouzu-release/` and `kasabouzu-release-validation.log`. |
| Amenoma, geometry only | **14/14 checks passed**, no texture payloads. Earlier milestone build; `artifacts/weapon-validation/amenoma-geometry-v1/`. No Unity import performed. |
| Production batch service on a bounded two-weapon fixture | **9/9 records accounted**, two root exports, seven linked aliases, zero Failed entries. Aggregate Partial. `artifacts/weapon-validation/real-batch-reviewed/` and `real-batch-reviewed.log`. The test harness restricts the catalog to Amenoma and Kasabouzu while retaining dependency entries. It is not a whole-installed-catalog run. |
| Whitespace / distribution-source consistency | `git diff --check` passed; build manifest source mismatches: zero. |
| Unity | Exact-project read-only connection check succeeded (Unity 6000.6.3f1, port 7800). No scene edits, imports, asset refresh, playback or rendering checks. User explicitly reserved Editor use for another agent. |

Regression entry points:
[WeaponExportRegression](../../Tests/Genshin/WeaponExportRegression/Program.cs),
[ValidateWeaponExport.ps1](../../Tests/Genshin/ValidateWeaponExport.ps1), and
[ShaderNameRegression](../../Tests/Genshin/ShaderNameRegression/Program.cs).
All generated exports stayed under AnimeStudio's ignored `artifacts/weapon-validation/`,
outside Unity's watched asset directories. Game files were read-only.

Kasabouzu was validated using key `Sword-Kasabouzu__ba0b9b3fbb40e94117e3`,
Animator PathID `3209486893970526571`. Its controller is in `11877482.blk` at offset
`21842814`, CAB `CAB-e80cce08e114f5a8bedef5174cc204d9`, PathID
`-1228506144758669554`. Controller links identify
`Ani_Equip_Sword_KasabouzuLoop` (2.6666667 seconds, 60 Hz) and
`Ani_Equip_Sword_KasabouzuMove` (4 seconds, 60 Hz). Both export as native clips.
Exported root translation/rotation channels do not establish a humanoid weapon rig.

Earlier failures are preserved rather than replaced: `kasabouzu-v1` used a
three-file discovery scope missing the controller and correctly failed the
required-animation check. `real-batch-final` exposed null component tokens during
alias consolidation; the corrected run is `real-batch-reviewed`. Build
`20260930T004734607Z` compiled but failed cleanup while an older CLI process held
a previous DLL open; the later canonical build above completed successfully.

## Remaining work

1. Prove enclosing roots and authoritative family/variant relationships, improve
   individual child/mesh selection, and reconcile coverage with a weapon registry.
   Run the whole indexed catalog with per-record accounting before claiming batch
   coverage beyond the two tested weapons.
2. Resolve external effects through actual configuration or object references.
   Validate at least one embedded and one external weapon effect, including the
   material-off option. Add icons, appearance variants, linked audio and other
   relevant items only with verified associations; these are not complete today.
3. Retain/reconstruct controller state behavior where needed and test overrides,
   legacy rigs, animation events and object-reference bindings. DynamicBone/custom
   script behavior is not recreated merely because original bytes are preserved.
4. Add a static/Generic/legacy weapon Unity import recipe. Keep this deferred while
   the user reserves the Editor. Later validate skeleton/bind poses, materials,
   animation paths and motion, then effect behavior and visual fidelity through
   the live Editor. File export checks do not satisfy this acceptance criterion.
5. Exercise the GUI interactively and broaden real samples across the five weapon
   classes. Extend the passed Mona model/material regression to native character
   animation and other character export options; current checks do not establish
   complete character compatibility.
6. Strengthen source scan failure accounting, cache content validation and
   mixed-version checks before treating preparation as a completeness proof.

Next useful independent work: source-backed enclosing-root/family grouping and
external effect ownership. No Unity mutation is authorized by this milestone.
