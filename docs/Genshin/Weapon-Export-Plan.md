# Genshin weapon catalog export plan

Date: 2026-09-29. Updated 2026-09-30 UTC. Branch: `codex/weapon-export`.
Status: indexed-source exporter, GUI/CLI options and offline import generation implemented;
offline catalog/source validation completed. Live Unity acceptance remains deferred.

The first source-export milestone is recorded in [Weapon-Export-Implementation.md](Weapon-Export-Implementation.md).
Current implementation coverage, builds, full-catalog accounting, fixed export defects
and remaining source-data dependencies are in [Weapon-Export-Offline-Validation.md](Weapon-Export-Offline-Validation.md).
The research baseline below describes the state before implementation. The full
plan remains open: authoritative catalog grouping, external effect ownership and
Unity import/playback are not yet validated. Unity operations are read-only at the
user's request while another agent uses the Editor.

## Requested outcome

Add a Genshin weapon workflow to this AnimeStudio fork, comparable to character
export: checkbox options, **Export Selected Weapon** and **Export Whole Weapon
Catalog** buttons, and an organized folder for each weapon containing its model
and selected associated assets. Both buttons must use the same export pipeline.
Animated weapons and weapon effects are part of the scope, including an honest
record of source features that cannot yet be reconstructed.

This document belongs in AnimeStudio `docs/Genshin` as explicitly requested.
Exporter discovery, parsing, GUI/CLI, packaging and import lessons stay here.
Game shaders and reusable runtime effect implementations belong in
[`com.caladan.shaders`](https://github.com/italonvcc/com.caladan.shaders).
This plan does not resume the paused Mona study or change the Manekin workstream.

## Research baseline and existing implementation

Inspected the working source of `italonvcc/AnimeStudio` at
`I:/_Archive/LocalOnly/repos/AnimeStudio`, branch `codex/mona-vfx-structured`,
HEAD `9e69ff5c1b59f20003b609f961b3930cad1c6789`, with substantial pre-existing
uncommitted changes. HEAD alone does not identify this implementation; neither
does the existence of an executable in `dist`. No source files were changed,
exporter rebuilt, weapon exported, or weapon imported for this planning task.

| Observed implementation | Reuse and required change |
| --- | --- |
| [`MainForm.Genshin.cs`](../../AnimeStudio.GUI/MainForm.Genshin.cs), `UpdateGameExportMenu`, `UpdateCharacterExportAvailability`, `ExportCharacter`: GI-only character action, Voices/VFX/Unity animations checkboxes, one selected Animator, asynchronous export into a named child folder. Optional checkboxes initially off; materials follow the global export setting. | Reuse menu placement, progress, operation locking and map/browser lifecycle handling. Add explicit weapon selection and catalog actions; do not gate catalog export on selecting an Animator. |
| [`GenshinCharacterCommand.cs`](../../AnimeStudio.CLI/GenshinCharacterCommand.cs): one character selection and matching optional CLI flags. | Expose the same weapon service through GUI and CLI for repeatable batch validation. There is no dedicated weapon catalog command in the inspected source. |
| [`GenshinCharacterReferences.cs`](../../AnimeStudio.Utility/GenshinCharacterReferences.cs), `Prepare`, and [`AssetsHelper.cs`](../../AnimeStudio/AssetsHelper.cs): cached source/map references; supplemental GameObject discovery currently targets `Eff_` and `SkillObj_`. | Reuse cache mechanics, but explicitly index weapon root GameObjects and invalidate on source/map/schema changes. Character preparation is not proof that every weapon root is indexed. |
| [`GenshinCharacterExporter.cs`](../../AnimeStudio.Utility/GenshinCharacterExporter.cs): character naming, own/shared body clips, humanoid Avatar, character VFX prefixes, voices and character package recipe. | Reuse orchestration concepts, not character naming or humanoid assumptions. A weapon without an Avatar or animation is a valid asset. |
| [`GenshinModelExporter.cs`](../../AnimeStudio.Utility/GenshinModelExporter.cs), `Export`: accepts Animator **or GameObject**, resolves dependencies, exports FBX hierarchy/skin/blend shapes, material JSON, PNG/native texture payloads and explicitly selected native `.anim` clips. | Primary model export path. Audit weapon binding/path requirements and texture placement; no new FBX converter is needed unless a measured defect requires one. |
| [`AssetDependencyResolver.cs`](../../AnimeStudio/AssetDependencyResolver.cs): qualified references, bounded bundle loads, GameObject children/components, mesh/skin/material/texture/shader and particle renderer mesh references; legacy Animation clips are followed. Animator traversal intentionally excludes controllers. | Add a bounded weapon animation discovery policy instead of globally following all controllers. General custom-script references and particle module dependencies are not exhaustively traversed by this switch. |
| [`AnimatorController.cs`](../../AnimeStudio/Classes/AnimatorController.cs) retains clip pointers and string table; [`AnimatorOverrideController.cs`](../../AnimeStudio/Classes/AnimatorOverrideController.cs) retains base/override references. The controller constructor reads its state data into a local `ControllerConstant`. | Follow qualified clip/override links, preserve original controller bytes and decoded relationships. Full controller reconstruction needs additional retained data and validation; a generated preview controller is a separate artifact. |
| [`GenshinVfxExporter.cs`](../../AnimeStudio.Utility/GenshinVfxExporter.cs) and [`GenshinStructuredVfxExporter.cs`](../../AnimeStudio.Utility/GenshinStructuredVfxExporter.cs): exact root requests, dependency outputs, `.effect.json`, native texture payloads, original component bytes and coverage. | Reuse structured export and strict layout decoders. Requests currently require 1–32 actions and retain a character field; add a compatible subject descriptor and bounded batching without discarding effects. |
| [`GenshinUnityPackage.cs`](../../AnimeStudio.Utility/GenshinUnityPackage.cs) and [`GenshinUnityImport.cs.txt`](../../AnimeStudio.Utility/GenshinUnityImport.cs.txt): character Humanoid Avatar/controller/prefab recipe. [`GenshinSharedAssets.cs`](../../AnimeStudio.Utility/GenshinSharedAssets.cs), `Package`, assumes character/body names and `.character.json`. | Add a weapon import recipe supporting static, Generic and legacy rigs. Reuse safe storage mechanisms where useful; do not pass weapons through the character package wrappers. |

Current VFX support is substantial but partial. Later
[particle extension evidence](Mona-Particle-Extension-Evidence.md) supersedes some
earlier unknown-field descriptions in [the VFX account](../Exporter/Genshin-VFX.md).
[Day/emission behavior](Mona-Particle-Day-Behavior.md) still requires runtime inputs;
[action data](Mona-Effect-Action-Data.md) does not establish spawn, attachment or
interruption semantics from script names or strings. Weapon effects must pass
their own layout and behavior checks; Mona coverage does not certify them.

The current success signal also needs strengthening for this workflow. The model
exporter records some unresolved references, unsupported `materialBindings` and
`nativeTextureFailures` without throwing. The character wrapper's incomplete marker
is exception-based. Animation serialization can retain unknown path/type markers;
the dependency resolver does not currently traverse AnimationClip object-reference
curves or event object references. Explicit binding and referenced-object checks
are therefore required before declaring a weapon animation complete. Relevant
code: [`AnimationClipConverter.cs`](../../AnimeStudio.Utility/YAML/AnimationClipConverter.cs),
binding conversion and path resolution, plus
[`AnimationClipExtensions.cs`](../../AnimeStudio.Utility/YAML/AnimationClipExtensions.cs),
`PrepareForExport` and serialized curve export.

### Bounded weapon inventory

Read-only queries against `docs/Genshin/Maps/genshin-7.1.map` and an existing
supplemental reference cache found the following. These are map objects and
name-based candidates, not confirmed prefab dependencies or a complete catalog.
The queries used the existing local CLI; its correspondence to the working source
was not verified. No weapon bundle export or playback was performed.

| Candidate | Observed inventory | Next proof needed |
| --- | --- | --- |
| Kasabouzu | 19 base-map entries matching the name, including `Equip_Sword_Kasabouzu_Model` Animator (PathID `3209486893970526571`) and Mesh (`-4596272258777294818`), `Ani_Equip_Sword_KasabouzuLoop`, `Ani_Equip_Sword_KasabouzuMove`, separate `Eff_Ani_Weapon_Kasabouzu`, 01/02 textures and base/Awaken UI icons. | Resolve root, controller and clip bindings. This is a strong independently animated weapon candidate; effect ownership and appearance-state meaning remain unverified. |
| Amenoma | 16 base-map entries matching the name: model Mesh, material, 01 diffuse/lightmap and 02 diffuse textures, `Eff_Ani_Weapon_Amenoma_Hand` clip and UI icon variants. No matching Animator or GameObject in that base-map query. Supplemental references contain `Eff_Weapon_Amenoma_Hand` and `Eff_Weapon_Amenoma_Head` GameObjects. | Find the actual model root; prove effect links. Missing Animator in this index does not prove the source weapon is static. |
| Indexed weapon-like meshes | Supplemental cache contains 273 Mesh entries / 261 distinct names matching `Equip_(Sword|Claymore|Pole|Bow|Catalyst)_`: Sword 66, Catalyst 60, Pole 54, Bow 48, Claymore 45 entries. Also 251 `Eff_Weapon_` GameObject entries. | Audit root coverage, duplicates, props and family/variant grouping. These counts must never be presented as 261 or 273 fully identified weapons. |
| Character wielding control | `Mona_WeaponStandby` query returns three Avatar animation entries. | Keep these separate from native weapon animation unless an explicit cross-rig action contract is requested and verified. |

Inventory provenance:

- Map SHA-256: `A63564A788A006B45ACCFD18B609403A2581F7F246E275ECD942454E578862D0`.
- Supplemental cache:
  `Maps/.genshin-references/6A57C9E2810DB5177F53BD5E398EB606D0ABA53A50949F12BB25BF7BE6A3B5E7/references.json`
  relative to this document's directory. The directory name is a cache fingerprint,
  not a claim that every entry was revalidated against the current client.
- Queried executable: `dist/net10.0-windows/AnimeStudio.CLI.exe`, SHA-256
  `79912B1637FC36ECFBB5762FD15D0F72111C39538E67389F384913455D1A0ABD`;
  build manifest identity `20260929T231559226Z`. Source equivalence unverified.
- From the repository root, reproduce a bounded query with
  `dist/net10.0-windows/AnimeStudio.CLI.exe --genshin-map-query docs/Genshin/Maps/genshin-7.1.map Kasabouzu <new-report.json>`.
  Replace the final placeholder with a new local evidence path. Planning queries
  used temporary reports, which were removed after inspection.

| Map/cache selector | PathID | Source block | Bundle offset |
| --- | --- | --- | ---: |
| Kasabouzu Animator | `3209486893970526571` | `04803507.blk` | 33235580 |
| Kasabouzu Mesh | `-4596272258777294818` | `07845116.blk` | 43278484 |
| `Ani_Equip_Sword_KasabouzuLoop` | `2048446843576605512` | `07845116.blk` | 43276912 |
| `Ani_Equip_Sword_KasabouzuMove` | `-694257161685084298` | `07845116.blk` | 43339903 |
| `Eff_Ani_Weapon_Kasabouzu` | `-4002737897236652858` | `04172885.blk` | 4951090 |
| Amenoma Mesh | `-3384825064289393044` | `07845116.blk` | 36902029 |
| `Eff_Weapon_Amenoma_Hand` GameObject | `-1913456645006045516` | `05785566.blk` | 30291808 |
| `Eff_Weapon_Amenoma_Head` GameObject | `-6667287480817975855` | `05785566.blk` | 30297726 |

These selectors seed the source investigation; they do not establish CAB-qualified
runtime links until the corresponding bundles are inspected.

## Catalog and association contract

The default catalog is all identifiable weapon families in the selected installed
Genshin client/map, across sword, claymore, polearm, bow and catalyst assets.
Completeness is version-specific. Distinguish equippable weapon families from
NPC/monster weapons, props, projectiles and character skill objects; list ambiguous
records explicitly rather than silently including or discarding them. Related
projectiles/skill objects may be dependencies of a verified weapon entry.

Create one family record with all proven model/appearance variants. Preserve
normal/alternate/ascended forms when the source proves those relationships;
do not equate a filename suffix with a progression state. Until game IDs or
localized names are resolved, use readable source names and stable source IDs.
Never fabricate catalog IDs or merge distinct objects solely because names match.

Discovery order:

1. Read the map and relevant serialized root/config/controller records; record
   source version, map hash, source file/offset and CAB/PathID identities. Discover
   GameObject roots as well as Animators. Check source availability and stale maps.
2. Resolve model variants and components through explicit source references.
   Preserve the reason for each family/variant grouping and unresolved alternatives.
3. Find animations from attached legacy Animation components, Animator controller
   and override references, and verified configuration links. Bound traversal by
   the selected weapon and report any shared controller that escapes that scope.
4. Resolve embedded effects and externally spawned effects separately. Trace
   renderer materials, textures, effect geometry, trails, particle shape/sub-emitter
   references, effect animations and script/config references where their layouts
   are known. Export unread script/controller data with provenance for later work.
5. Use exact-token naming only to propose additional candidates. Store these under
   candidate evidence with an association reason; do not attach them automatically
   or count them as confirmed coverage. A typed link or verified source hierarchy
   promotes a candidate to a dependency.

Each animation needs an owner/binding target: the weapon, one of its effect roots,
or a character wielding it. `Ani_Avatar_*_Weapon*` and UI weapon-show clips are not
automatically weapon animation. Keep character attack/showcase motion outside the
default weapon bundle; report known external character requirements instead.
Retain native curves, timing, looping, object references and events. Material or
visibility animation counts as animation even when no bone moves.

## User interface and export options

Add **My tools > Genshin weapons**, backed by a searchable catalog showing source
name, verified display name if available, class, variants, model readiness and
animation/effect discovery status. Show the installed version and indexed family
count. Asset Browser selection can select its corresponding catalog entry when
unambiguous; a duplicate name must remain disambiguated.

| Control | Proposed default and behavior |
| --- | --- |
| Model and source identity | Always included. Preserve hierarchy and rig without requiring a Humanoid Avatar. |
| **Materials and textures** | Checked. Material JSON, shader identity/keywords, texture slot mapping, native payloads and previews. Unchecking produces a clearly labeled geometry package; it cannot be reported as appearance-ready. Implement per-request policy rather than temporarily changing global settings. |
| **Weapon animations** | Unchecked, matching the character workflow's opt-in animation behavior. Includes verified weapon and selected effect animation dependencies. Preserve native `.anim`; no humanoid baking. |
| **Effects and related assets** | Unchecked, matching current character behavior. Exports linked embedded/external effect roots, meshes, materials, textures, structured data and unknown raw components. Include required effect-local animations even if standalone weapon animations are off, and state that dependency in the option description. |
| **All model/appearance variants** | Checked and required for whole-catalog export. For individual export only, unchecking requires an explicit selected variant and records omitted variants. The catalog button explains and requires this checkbox rather than silently choosing variants for other families. |
| **Unity import setup** | Checked. Produces the weapon recipe and shared importer needed for prefab and supported animation setup. Enables material binding only when its required exports exist. Unsupported effect behavior stays explicit. |
| **Linked audio** | Optional later control, disabled with a reason until weapon event-to-bank/media links are verified. Do not reuse character voice-name heuristics. Preserve observed audio event/config evidence with effects in the meantime. |

With Effects checked and Materials/textures unchecked, allow data-only effect
export but mark its appearance dependencies intentionally omitted. The UI must
explain this combination before starting. Required source metadata, dependency
manifest and coverage report cannot be disabled. No option may silently switch
itself or turn an unknown category into “none.”

**Export Selected Weapon** creates the selected family's folder and chosen
variants. **Export Whole Weapon Catalog** exports the full indexed catalog,
independent of the current search filter. Display the fixed catalog count and
options before starting; do not treat a filtered screen as the full catalog.
Disable conflicting operations while exporting. Provide progress by weapon and
stage, a Cancel button, and a completion view listing complete/partial/failed/
cancelled entries with **Open Output Folder** and **Open Report** actions.

## Output layout and manifests

Proposed layout under a user-selected new destination:

```text
Genshin-Weapons-<version>-<run>/
  catalog.json
  export-report.json
  UNITY-IMPORT.txt
  Generic/
    Editor/AnimeStudioWeaponImport.cs
  Weapons/
    <readable-family>__<stable-key>/
      weapon-export.json
      <stable-key>.weapon.json
      Variants/
        <variant-key>/
          <source-root>.fbx
          manifest.json
          Materials/
          Textures/                 # PNG previews and native .astexture
          Animations/               # verified native .anim clips
      VFX/                          # existing structured exporter layout
        manifest.json
        Effects/*.effect.json
        <source-type>/...
      Source/                       # controller/config/raw provenance
      Reports/                      # bindings, candidates, unsupported data
      EXPORT-INCOMPLETE.txt          # present for failed/interrupted export
```

Empty optional directories need not exist. Preserve the VFX exporter's internal
relative paths rather than flattening its files into attractive but broken folders.
Model texture normalization must move both PNG and native payloads and update every
manifest, material and importer reference; do not blindly copy the character
wrapper's PNG-only move. Validate the resulting paths after normalization.

Keep weapon-specific assets within the weapon folder; use the top-level shared
Editor helper once per import. Individual export uses this same parent layout and
contains everything needed to import it. Include precise copy instructions for the
weapon folder and `Generic/Editor`. Initially keep shared payload copies local to
each family; any later content-addressed deduplication must preserve portable paths,
provenance and existing Unity `.meta` identities.

Catalog IDs must survive discovery ordering and duplicate/sanitized names. Combine
an authoritative game ID when available with explicit source/variant identities;
otherwise derive a deterministic key from qualified source identity. Bound path
lengths and handle Windows reserved names, case-insensitive collisions and Unicode.
Names are labels, not the only identity.

The family manifest records requested/effective options, roots/variants, verified
and candidate associations, all output paths and hashes, source version and map
fingerprint, exact exporter revision/build hashes, dependencies and omissions.
Use separate category fields for discovery, export and playback readiness:

- Discovery: `Verified`, `Candidate`, `NotPresentAfterVerifiedSearch`, `Unknown`.
- Export: `Complete`, `Partial`, `Failed`, `SkippedByOption`, `Cancelled`.
- Playback: `NotApplicable`, `NotTested`, `Supported`, `Unsupported`, `Partial`.

“No clips found” is normal for a verified static weapon; it is not proof that an
incomplete map contains no animation. Successful file writing is not full effect
playback. A partial decoder can preserve complete raw evidence while runtime
coverage remains partial. Propagate unresolved dependencies, native texture
failures, unsupported bindings and VFX coverage into the family and batch reports.

## Implementation sequence and ownership

### 1. Establish the weapon catalog and representative evidence

Add a source-aware catalog/reference layer, proposed
`AnimeStudio.Utility/GenshinWeaponReferences.cs` and `GenshinWeaponCatalog.cs`.
Parsing improvements belong in `AnimeStudio`; extend `AssetsHelper` only where
needed for missing indexed classes/roots. Cache keys include map/source/version
and discovery schema. Scan the available client without writing to its files.

Deliver a reproducible inventory with family/variant counts, exclusions, ambiguous
groups and verified animation/effect associations. Select fixtures across all five
classes: static, independently animated, skinned/articulated, alternate appearance,
embedded effect, external effect, and shared dependency cases. Each special case
must actually exist in source; unavailable cases remain explicit coverage gaps.
**Gate:** exact root identities and binding targets established for representative
weapons, with a coverage denominator for the selected client. Resolve missing roots
or config sources here before advertising complete catalog discovery.

### 2. Implement one complete weapon export service

Add `GenshinWeaponExporter` and `GenshinWeaponOptions` in `AnimeStudio.Utility`.
Use `GenshinModelExporter`, material/native-texture exporters and dependency
resolution, supplying explicitly verified clips. Preserve all mesh streams,
submeshes, bind poses, local transforms, inactive nodes and renderer slots.
Make dependency validation follow the requested option: the current model exporter
rejects unresolved Material/Texture references before applying `exportMaterials`.
A geometry-only request must preserve geometry/rig checks while reporting omitted
appearance dependencies without failing solely because an unrequested texture is
unavailable. Test this with missing material/texture sources and, separately,
missing mesh/bone sources that must still fail. Keep full appearance validation
strict when materials/textures are requested.
Add narrowly scoped controller/override/custom-reference parsing as proven by
fixtures; preserve undecoded records instead of inventing behavior.

Export effects through the existing structured VFX service with subject-neutral
metadata compatible with old character manifests. Resolve source-root ownership,
dependency policies, controller limits and the existing action-count bound.
Write the family report even when one optional stage fails; model success must
not conceal missing requested animations or effects.
**Gate:** representative source-to-file comparisons pass; no guessed links or
silent omissions; option combinations and failure statuses are correct.
Include synthetic requests with more than 32 action groups and many effect roots
to verify batching, reassembled subject manifests and no lost dependency links.
The current limit is on action groups, not the number of effects inside one group.

### 3. Supply a weapon Unity import contract

Add `GenshinWeaponUnityPackage.cs` and a weapon-specific importer template.
Create a prefab preserving weapon hierarchy and materials. Select static, Generic
or legacy animation behavior from source evidence; never invent a Humanoid Avatar.
Validate clip paths, hashed bindings, transform/material/visibility curves and
object-reference remapping against the imported hierarchy. Keep original controller
data separate from a clearly labeled generated preview controller.

Store source-supported attachment/socket transforms and external character
requirements in the recipe. Do not hardcode a character hand or auto-trigger an
unverified effect. Reuse the package's structured effect importer where compatible;
record its required version and missing adapters. Unsupported scripts/trails/
particle layouts must stay visible in the import report. Preview materials and
successful parsing do not establish game shader or effect fidelity.
**Gate:** live Unity import/reimport preserves GUIDs and user material edits;
static and animated weapon fixtures work, and partial effects are reported honestly.

### 4. Add the GUI controls and matching CLI

Implement the controls above, preferably in `MainForm.GenshinWeapons.cs`, using the
existing operation lifecycle. Add a shared CLI adapter, proposed
`GenshinWeaponCommand.cs`, and dispatch in `Program.cs`. Suggested command forms
are `--genshin-weapon <map> <qualified-key> <new-output>` and
`--genshin-weapons <map> <new-output>` with corresponding option flags; these are
proposals, not commands available today. Ensure map reload, game switching,
browser closure, reference refresh and stale selection cannot export the wrong
weapon. Persist option preferences without borrowing mutable character settings.
**Gate:** selected-button and CLI exports have equivalent contents and reports;
the catalog button uses all catalog entries, not the active search result.

### 5. Make whole-catalog export reliable

Snapshot catalog/options/source fingerprints at run start. Preflight the output
root, source access, path collisions and available disk space (label size estimates
as estimates). Export families sequentially initially, with bounded internal
workers and fresh/cleared managers; do not share mutable readers across weapons.
Continue past per-weapon failures while recording them. Cancel between safe work
units, retain useful partial output and record entries not started.

Write the run ledger incrementally and finalize reports atomically. Never overwrite
an existing export or Unity edits. A retry/resume can skip an entry only after its
source/options/schema and output hashes match; otherwise use a new output folder.
A catastrophic preflight failure stops the run; one malformed weapon does not.
**Gate:** every catalog entry is accounted for; duplicate names, interrupted runs,
missing sources, stale maps and mixed success/failure runs behave predictably.

### 6. Validate, document and release

Build GUI and CLI through [`build.ps1`](../../build.ps1), retaining the canonical
`dist/net10.0-windows` distribution and build reports. Add focused regressions under
`Tests/Genshin` for catalog grouping, qualified identity, cache invalidation,
option semantics, binding failures, deterministic paths and batch accounting.
Run the existing material/shader-name, native-texture, animation and VFX controls
affected by changes; re-export a character as a shared-code regression.

Use a designated live Editor owner for the exact target project. Put generated
weapon validation assets under `Assets/_Games/Genshin/Weapons/`, outside the package
and AnimeStudio tracked source. Check imported topology, mesh streams, materials,
native texture settings, animation start/mid/end/loop, rig motion and effect-local
bindings. Save/reload/reimport, attach a verified weapon to a representative
character socket, animate/move it, and inspect effects in Editor and a fresh player
when runtime adapters change. Capture before/after evidence with source/run IDs.

Game-reference visual checks need aligned source captures/recordings for each
claimed effect/state; static imports cannot certify animated trails, event timing,
visibility transitions or material animation. Record export coverage, runnable
Unity coverage and game-reference coverage separately. Publish documentation and
enabled controls only for implemented capabilities.

## Acceptance and remaining dependencies

The feature is accepted when both requested buttons work, checkbox choices are
honored, every selected family/variant has an organized traceable export, verified
weapon animations bind and play, and all linked assets are exported or explicitly
reported with their missing/unsupported reason. Whole-catalog export must account
for every entry in its frozen installed-client catalog. A run containing partial
entries is useful output, but must not be labeled “everything exported completely.”

Before implementation completion, resolve and record:

- The authoritative equippable weapon registry, family/variant relationships and
  localized naming availability for this client; map names alone are insufficient.
- Weapon-root coverage in cached/base maps, plus exact root/controller/config links
  for animated and externally spawned-effect fixtures.
- Binding/path recovery and object-reference animation support on actual weapons.
- Active weapon trail/custom-script/particle layouts, and the source of event
  timing, sockets, follow rules, activation, stopping and interruption behavior.
- Linked audio event-to-media mapping if the optional audio control is enabled.

Missing source metadata is an explicit discovery gap; request exact config/CAB or
schema evidence after the bounded audit identifies it. Request recordings/captures
for the particular weapon state needed to validate playback, not a blanket data
dump. No additional user input is required to begin catalog research in phase 1.

## Planning checks

Repository roots, fork remote, working changes and relevant source/docs were
inspected. The package's exact-project handshake passed through approved elevated
CLI access: Unity `6000.6.3f1`, ready, port `7800`, project
`I:/_Archive/ToNas/Unity Projects/Projects/Game_Asset_Study`. The sandbox-only
handshake could not see the instance; the approved retry resolved that access
boundary. No live rendering settings, weapon import, playback, build or visual
comparison were checked. Existing unrelated changes were preserved.
Independent review identified the geometry-only dependency policy and catalog
variant-selection ambiguity; both are addressed above. Documentation link and
content checks are the validation for this planning-only deliverable.
