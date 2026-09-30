# Manekin Phase 1 source and exporter study

Status: partial, 2026-09-29. This is the canonical AnimeStudio account of
game-data discovery and export behavior. The Unity package's
`Docs/Genshin/Manekin-Implementation-Plan.md` owns the acceptance gate and
runtime/rendering status. The existing `Export/manekin-parts.json` is a name and
container lead list, not a selectable-option registry.

## Reproducible source selection

Source data: Genshin 7.1.0 map `Maps/genshin-7.1.map` and
`Maps/manekin-scene-index/scene-index.json`. The selection helper
`Tools/Genshin/Get-ManekinSetSelection.ps1` groups exact GameObject roots named
`Beyd_Avatar_<Boy|Girl>_<slot>_S####`. It accepts a four-digit group only when
Hair, Top, Bottom and Shoe each occur exactly once. It sorts group IDs, then
uses one shared .NET `Random(20260929)` over Boy followed by Girl, with
`Sort-Object { $rng.Next() }`, taking five per body. The resulting eligible
inventories are 57 Boy and 36 Girl groups. The complete ordered inventories,
index SHA-256 and selected IDs are in the frozen export's
`phase1-selection.json`:

| Body | Selected S groups |
| --- | --- |
| Boy | 0133, 0143, 0131, 0033, 0191 |
| Girl | 0082, 0097, 0084, 0047, 0142 |

Matching S codes establish a reproducible group of source part roots. They do
not establish a native saved outfit preset, visibility/occlusion rules, shared
rig compatibility, or an accepted complete look. Each selected group contains
all of its additional exact GameObject part roots in the batch export: 34 Boy
and 30 Girl roots, 64 total.

## Bounded export and import evidence

`Tools/Genshin/Export-ManekinPhase1.ps1` exports each exact part root. The
frozen batch is
`%TEMP%/manekin-phase1-batch-20260929T2008` with `batch-report.json`,
`phase1-selection.json`, `payload-audit.json` and
`build-provenance.json`. The provenance file records revision
`9e69ff5c1b59f20003b609f961b3930cad1c6789`, a dirty-source
fingerprint, source map/index hashes, and SHA-256 for the CLI apphost, three
managed DLLs and two native libraries. The apphost hash alone does not identify
managed code and must not be used for that claim.

All 64 CLI exports returned success. `Tools/Genshin/Audit-ManekinBatch.ps1`
found 64/64 payload-resolved manifests: each FBX exists; each has meshes and
material bindings; every binding is resolved; its material JSON has a nonempty
original shader name; each property-to-texture payload path exists; and no
manifest lists an unresolved source reference. This is an export audit, not a
visual or recipe acceptance result. The PNG texture output does not preserve a
source-native compressed payload, sampler, or mip chain; their effect on UGC
rendering is unverified. The later native re-export below repairs this payload
gap for ten merged diagnostic oracles, not the frozen 64 individual parts.

The Unity Editor owner copied the frozen batch to
`Assets/_Games/Genshin/Manekins/Source/BatchRuns/20260929T201641670Z`
and imported all 64 FBXs. Its report
`Assets/_Games/Genshin/Validation/Manekins/BatchRuns/20260929T201641670Z/completion.json`
records 110 skinned renderers, 147,856 vertices and no structural import
errors; the scene remained unchanged. This establishes independent geometry
payload validity. It does not test every modular combination or rendered look.

`Tools/Genshin/Stage-ManekinPhase1Catalog.ps1` staged an importer-contract
catalog at `%TEMP%/manekin-phase1-catalog-20260929T2020`: 64 part records, one
source-backed Boy base, and one certified Boy S0017 + Hair S0133 diagnostic
recipe. `status: unsupported` on the options is intentional: their geometry is
exported but the original UGC shaders are not implemented in the package.
`geometryStatus: exported` identifies the narrower usable geometry path.
Payload hashes and exact material/texture bindings are catalog-relative. The
source file/container/path ID, source root path, Unity root-relative renderer
path, ordered bone paths and bind matrices are separate fields. No material or
texture is bound by a first matching filename.

## Exporter repairs and narrow assembly proofs

The Genshin Unity-2017 Shader parser left three source names blank although
their Shader objects were loaded. `GenshinShaderNameReader` now accepts a unique,
aligned serialized `RenderType=Opaque` name envelope with bounded subsequent
header when the ordinary parsed form supplies no name. Fresh material JSON
resolves `miHoYo/Character/Character_Ugc`, `_Skin` and `_Face` as original
identities. The synthetic regression tests reject duplicate candidates,
incorrect tags/headers and malformed lengths, while retaining the legacy
footer case. This recovers metadata; it does not implement shader behavior.

The manifest now emits exact renderer/submesh-to-material JSON bindings and
property-to-texture payload paths. These derive from imported source object
identities and the converter's object-keyed texture output map, including
collision-safe names. A missing or ambiguous link is marked unresolved with a
reason. Representative Boy S0017 export has 25/25 resolved bindings. The
Editor owner found 25 skinned renderers and 12 materials with no missing bones,
bind-pose/material-slot mismatch, missing normals/weights or invalid weighted
indices in its fresh import report under
`Assets/_Games/Genshin/Validation/Manekins/ExportProbes/20260929T195801982Z/`.

The exact Boy Suit S0017 + Hair S0133 source assembly removed three original
hair renderers and passed source rest/bind checks. The Editor owner's modular
assembly control matched a merged source oracle's topology and baked rest/pose
positions to approximately 3.2e-7 m in
`Assets/_Games/Genshin/Validation/Manekins/AssemblyControls/20260929T200350114Z/`.
This is one geometry pair, not a general outfit rule.

For Girl, fresh `Beyd_Avatar_Girl_Prototype_Manekin` (7 meshes, no unresolved
references) and selected Hair S0084 fail at the source Head local rest
transform. Four other selected hairs also require additional named hair bones
absent from this prototype. A different exact GameObject,
`Beyd_Avatar_Girl_BaseBody_Common` from `12859995.blk`, has 12 meshes and no
unresolved references. It has no hair or face. With an explicit empty removal
list, source-qualified selection, matching target rest paths and the existing
post-merge `GenshinBindPose.Restore`, it accepts selected Hair S0084 additively.
The merged v3 export is
`%TEMP%/manekin-girl-common-s0084-add-assembly-v3-20260929` (14 meshes,
14 resolved bindings, no unresolved references). Four incoming skin bones were
checked; two do not influence base meshes, so they have no base bind for direct
comparison. Their source part binds were retained and verified by Restore;
all incoming matrix values are required finite. This is a structural female
base+hair diagnostic, not a complete female avatar. The Editor owner passed
nine modular geometry controls against this source merged oracle in
`Assets/_Games/Genshin/Validation/Manekins/AssemblyControls/20260929T202857617Z/assembly-controls.json`.
It also reran nine Boy controls after the shared assembly change in
`Assets/_Games/Genshin/Validation/Manekins/AssemblyControls/20260929T202913685Z/assembly-controls.json`.

## Source continuation after the eight supplied captures

Face and pupil are separately selectable geometry leads in the source scene
index. Exact Girl `Face_P0051`, `Pupil_P0057` and `Hair_S0084` can be added to
`BaseBody_Common`; the source merged oracle is
`%TEMP%/manekin-girl-face-pupil-hair-assembly-20260929` (16 meshes, zero
unresolved references). The Editor owner passed nine modular geometry controls
against that merged oracle, with a maximum baked position error of about
1.2e-6 m. The resulting female baseline has face and pupil. `Eyebrow_P0033`
and same-source-block `Eyebrow_P0141` initially failed the strict local rest check at
`Bip001/.../Head/Bone_OgnPRS_Eyebrow_BaseBody001_BaseBrow01_L/
Bone_Eyebrow_BaseBody001_BaseBrow01_L/
Bone_OgnPRS_Eyebrow_BaseBody001_AniBrowA01_L`. The later guarded override
below resolves P0141 for a geometry diagnostic, without certifying eyebrow
animation or native face-selection behavior.

`GenshinPartAssembler` now also admits guarded source bone-frame extensions:
every existing shared ancestor must match its source local rest transform,
incoming bind matrices must be finite, existing base binds must match, and
new frames retain their source local transforms before the merged model's
bind-pose restoration. This produced ten exact S-group structural oracles in
`%TEMP%/manekin-selected-set-assembly-probe-v3-20260929T2115`: five per body,
245 meshes total, with zero unresolved references or material bindings. Boy
S0133 added 41 frames; the Editor owner compared its 27-mesh source oracle
against seven modular parts, passed 11 controls and measured posed position
error below 5.7e-7 m. These S groups are candidate combinations, not native
outfit presets or visibility recipes. The Boy source base still retains
S0017 pieces and underlying body meshes; Girl Common has no native face/eye/
brow choice applied by a source registry.

The later ten-oracle native export is
`%TEMP%/manekin-selected-set-assembly-native-v4-20260929T2140` with
`assembly-probe-report.json` and `native-payload-audit-v2.json`. Its 245
material bindings resolve exactly, with 595 property-keyed native texture
references to 357 distinct ASTEX001 `.astexture` payloads and no source or
native-write failures. The audit hashes all 818 produced files and records
the four present CLI/apphost/managed binary hashes plus relevant exporter source
hashes. The ASTEX001 sidecars preserve the serialized Texture2D compressed
mip chain, `m_ColorSpace`, filter, wrap and aniso data; PNG remains a preview.
The binding carries `nativeTextures` and `textureImport` keyed by the exact
material property name, with source Texture2D CAB/path ID evidence. These
fields come from source identity, not a guessed filename. Unity native-texture
import, material binding and rendered fidelity require separate Editor checks.
The Editor owner then imported those ten native-oracle FBXs in
`Assets/_Games/Genshin/Validation/Manekins/BatchRuns/20260929T212505651Z/completion.json`:
all ten models and 245 skinned renderers passed its structural checks, with
zero import errors. This does not validate the UGC material appearance or
the native sidecar importer for every property.

Those native-v4 FBXs used this checkout's older FBX writer. The previously
validated tangent-basis repair from fork PR #3 was integrated exactly as
documented below, then all source payload families were refreshed into new
immutable runs: 64 individual parts in
`%TEMP%/manekin-phase1-batch-pr3writer-20260929T2153` (64/64 native material
bindings resolved); ten selected S-group merged geometry oracles in
`%TEMP%/manekin-ten-set-oracles-pr3writer-20260929T2158` (10/10, 245 meshes,
595 native property references, 357 sidecars, 818 hashed files); and 20
independent brow/pupil FBXs in
`%TEMP%/manekin-20-faceparts-pr3writer-staged-20260929T2153` (20/20, 60
sidecars, 180 hashed files). The ten-set audit is
`native-payload-audit-v2.json`. The Editor owner imported all ten refreshed
structural FBXs under
`Assets/_Games/Genshin/Validation/Manekins/BatchRuns/20260929T220627643Z`
and passed the ten modular geometry control sets under
`Assets/_Games/Genshin/Validation/Manekins/AssemblyBatch/20260929T221838935Z`.
This replacement run keeps the old byte provenance separate; it does not
upgrade old FBXs by changing only their import settings.

The source's `BydCostumeRenderAsset` MonoBehaviours lack a type tree, but named
aligned fields can be decoded in a bounded way. The 14 Girl SkinTone raw records
in `%TEMP%/manekin-girl-skintone-values-20260929.json` expose exact
`_SkinTintColorOutlineLerp`, `_SkinTintColor`, `_SkinTintSecondColor`,
`_FirstShadowMultColor` and `_CoolShadowMultColor` values with raw byte hashes.
After standard sRGB-to-linear conversion, source P0156 matches capture 43096
FacePS10 color-buffer pair within 1.33e-7 per channel; P0161 matches 48104
within 8.68e-8; P0151 matches 48306 within 1.72e-7. The next-best source
candidate is more than 0.09 away in each case. This identifies those active
skin parameters, not the runtime mechanism producing the cached UGC skin
textures or a menu application registry.
Two Boy face draws also match Girl-named source presets: frame 49477 matches
P0154 within 1.48e-7 (next candidate 0.0734), and frame 51482 matches
P0279 within 6.72e-9 (next 0.1123). The full 7.1 map contains no
Boy-named SkinTone records. This is strong evidence that those source palette
values can be active for Boy, but it does not reveal the exact runtime
reference or menu binding that selected them.

The full compressed 7.1 map must be queried for late source blocks: the older
`Export/manekin-parts.json` name inventory omits `Lipstick_P0475` in
`16636590.blk`. `--genshin-map-query` found both Girl and Boy P0475
MonoBehaviours and distinct Texture2Ds. A fresh raw source probe found
`_UseLipMakeup=1` and exact fileID 1 texture PPtrs: Girl Mono path ID
`-9144073006740317716` references texture `-1473269716887100364`, and
Boy Mono `188857788560603552` references texture
`5345415812535386646`. The Girl texture name is the exact t3 resource bound
in captures 48104/48306. Likewise Girl EyeShadow P0045 and FacialMakeup P0074
have enabled flags and exact PPtrs to their named source textures. The compact
candidate increment `%TEMP%/manekin-cosmetic-candidates-v3-20260929.json`
records five distinct Girl SkinTone records, those three Girl makeup sources
and Boy Lipstick P0475 with source CAB/path ID/container/raw hashes. All nine
remain `status: unsupported`: source application rules and production UGC
material/shader binding have not been proved. The five selected SkinTone IDs
are P0151/P0154/P0156/P0161/P0279; all five have unique captured color
matches across Girl and Boy draws.

The first live Unity batch of all ten multipart oracles passed Boy structural
controls but the five Girl checks stopped at a shared unweighted ancestor
*local* transform comparison. `GenshinBindPose.Restore` keeps source local
transforms for unweighted ancestors while restoring weighted bone *world*
transforms from inverse binds; compensating local transforms can differ between
the independent part FBX and Common-root merged FBX. The Editor owner's
`CompareRigs` found weighted world/bind agreement around 1e-6 for Girl ArmAcc
S0082, while Common `Bip001` local X is -0.566 and the part's is about zero.
The exporter checks shared local rest *before* Restore. The Editor owner
updated its diagnostic to compare exact source-certified pre-Restore rig paths
and post-Restore weighted world/self-bind transforms, reporting compensated
unweighted-local differences separately. All ten oracles then passed 11 Unity
modular controls each in
`Assets/_Games/Genshin/Validation/Manekins/AssemblyBatch/20260929T211655492Z/completion.json`.
This establishes structural geometry compatibility for those exact selected
parts, not animation pivot equivalence or native outfit state.

For the female brow, a source hierarchy probe
`%TEMP%/manekin-girl-common-brow-hierarchy-20260929.json` found 16 brow
GameObjects, each with exactly one Transform component. The retained Common+
Face+Pupil+Hair avatar has no mesh, morph or weighted bone under that branch.
`GenshinPartAssembler` now permits a local rest override only for a target
subtree with those same certified conditions; it refuses synthetic/unresolved
source GameObjects or any extra component/retained geometry. The fresh
`%TEMP%/manekin-girl-brow-p0141-exclusive-v1-20260929` merged oracle has
17 meshes, 17 resolved bindings, 14 ASTEX001 sidecars and six recorded
old/new local TRS overrides with source GameObject identities. A standalone
native Eyebrow P0141 payload is in
`%TEMP%/manekin-girl-eyebrow-p0141-native-v1-20260929`. The negative control
Prototype_Manekin+Hair S0084 still fails at its weighted Head rest transform
with exit code 1 and no output. The Editor owner passed 11 modular geometry
controls against this 17-mesh brow oracle in
`Assets/_Games/Genshin/Validation/Manekins/AssemblyControls/20260929T213151170Z/assembly-controls.json`:
rest and two posed comparisons stayed below about 1.21e-6 m. Eyebrow
animation and menu selection still require separate proof.

The corrected-writer selected head/look run is
`%TEMP%/manekin-ten-head-looks-guarded-pr3writer-20260929T2203`.
`Tools/Genshin/Export-ManekinSelectedLooks.ps1` pairs the five exact brow and
pupil IDs per body with the five selected S groups; Girl also receives source
Face P0051. Boy replaces the one retained Store brow/pupil mesh by exact path.
The original assembler guard counted those *planned-to-be-removed* meshes as
retained, falsely preventing a source-certified Boy brow branch replacement.
`GenshinPartAssembler` now computes the retained mesh/bone set after checking
the exact remove list; all exclusive subtree, finite bind and rest checks
remain. The same Boy S0133/Pupil P0057/Brow P0141 request passes when
`Eyebrow_P0132` is removed and fails when it remains, the intended negative
control. All ten full-head source oracles assembled with 260 resolved mesh/
material bindings, 640 property-keyed native texture references, 432 ASTEX001
sidecars, and 973 hashed files; 60 exclusive brow-rest overrides are recorded
by source path. `look-assembly-report.json` freezes eight binary hashes, and
`look-payload-audit.json` checks source IDs and native payloads. The look
arrangement is a deterministic source-compatible candidate, not a native menu
preset. The Editor owner imported all ten full-head FBXs under
`Assets/_Games/Genshin/Validation/Manekins/BatchRuns/20260929T221520262Z`.
Modular source-to-merged equivalence, eyebrow animation and rendered appearance
remain separate checks.

The 10 alternate custom multipart candidates are
`%TEMP%/manekin-ten-multipart-hair-rotation-pr3writer-20260929T2230`.
For each body, the exporter rotates only Hair to the next of its five selected
S groups (wrapping the fifth to first), retaining the row's other S-group
parts, exact brow/pupil sources and Girl Face P0051. Each request names the
source Hair GameObject and exact target renderer removals; the audit checks
the Hair, brow and pupil source IDs against the result. All ten assembled
under the strict source rest/bind checks: 260 resolved meshes/materials, 640
native texture references, 973 hashed files, zero unresolved bindings. These
are deliberately custom combinations. Native visibility, saved outfit slots,
animation pivots, Unity import and rendering remain unverified for this run.

The bounded 90-candidate grid
`%TEMP%/manekin-90-source-candidates-v2-20260929.json` has five distinct
source identities in each of 18 body/category cells. The generator
`Tools/Genshin/Stage-Manekin90SourceCandidates.ps1` uses exact source-map and
scene-index IDs, the frozen five S groups/body, and only GameObject entries
whose source block actually exists. It records 90 *candidates* and zero
accepted selections. Full-map inventories have 94 distinct brow and 52 pupil
GameObject IDs per body; EyeShadow MonoBehaviour counts are 12 Boy/16 Girl,
Lipstick 7/10, and FacialMakeup 23/26. The Boy SkinTone cell points to five
Girl-named palette records; only P0154/P0279 have Boy capture matches, so
cross-body selection of the other three is unsupported. The outfit and
multipart cells reuse the selected five S groups, with separate candidate
roles; neither role proves native visibility or complete saved-preset state.

The 20 selected brow/pupil GameObjects were exported independently as 20
one-mesh FBXs with source materials and native texture sidecars. Their unified
staged run is `%TEMP%/manekin-20-faceparts-native-staged-v2-20260929` with
`face-part-payload-audit.json`, exact root source IDs and SHA-256 for every
payload: 20 meshes, 20 resolved material bindings, no unresolved source or
native-write failures. An initial Boy Pupil P0061 index record pointed to
missing old block `02702837.blk`; a second exact P0061 GameObject in available
`13368452.blk` was re-exported and the candidate grid corrected. The
independent FBXs do not establish that all 20 choices can be swapped onto
the same avatar rig or that they are the game's selectable menu entries.

For the selected five EyeShadow, Lipstick and FacialMakeup candidates per
body, the source probe loaded all 30 `BydCostumeRenderAsset` MonoBehaviours.
Each selected record's raw bytes are preserved under
`%TEMP%/manekin-30-makeup-native-export-v1-20260929/MonoBehaviour`; 22
distinct PPtr-target Texture2Ds are preserved there as ASTEX001/PNG. The
VFX exporter's evidence-only mode resolved all 52 explicit source assets with
zero missing/failing references. The audited contract
`%TEMP%/manekin-30-makeup-source-inputs-v1-20260929.json` matches each raw
payload to the source probe byte-for-byte, decodes the exact aligned `_Use*`
flag and relevant scalar/color words, resolves its texture PPtr fileID 1
through the full map by path ID, verifies the native header/source identity,
and records per-property import intent and SHA-256. All 30 `_Use*` flags are
1. The 30 bindings resolve to 22 distinct Texture2Ds, so several choices
share payloads. Name matching would misbind them, while each recorded PPtr
resolves to one exact payload.
These are reusable source material inputs, not a demonstrated face-layering
composition or a working Unity appearance picker.

The five capture-aligned SkinTone MonoBehaviours are also preserved as raw
payloads in `%TEMP%/manekin-five-skin-presets-raw-v1-20260929`, with decoded
values and byte hashes checked in
`%TEMP%/manekin-five-skin-source-inputs-v1-20260929.json`. This allows a
future runtime binder to receive exact preset parameters. It does not create
the generated skin diffuse/lightmap textures: all five captures that bind
those BC7 render targets show only reads, with no producer write in their
recorded frame history. Full-map name queries for `UgcSkin` and `SkinBlit`
found no named producer. Capturing before generation or finding runtime
generator source is the concrete missing input for native texture
composition; consumer color parameters alone cannot define its equations.

The shared model exporter now records up to 16 spread source and converted
vertex-channel samples per mesh, with source position/normal/tangent/color/
UV0-UV2 and converted FBX input values. Fresh representative manifests are
`%TEMP%/manekin-hair-s0133-vertex-probe-20260929/manifest.json` and
`%TEMP%/manekin-girl-brow-vertex-probe-20260929/manifest.json` (Face, Bottom
skin and brows). `ModelConverter` carries UV0-UV7, authored tangents and
vertex colors. Tangent X is negated
for handedness, while color RGBA and UV xy are copied. Face has tangent,
color and UV0/UV1; Bottom skin has tangent, color and UV0/UV1/UV2. The small
24-vertex Girl `Top_BaseBody_Upper01` mesh has no source tangent or color,
which the manifest marks explicitly. The read-only source component probe
`%TEMP%/manekin-girl-tiny-top-source-flags-20260929.json` identifies its
GameObject `8711163553606756631` as active and its SkinnedMeshRenderer
`-8069051979464625338` as enabled; the sole Common-root ancestor is active
in `%TEMP%/manekin-girl-tiny-top-ancestor-flags-20260929.json`. Its source
material is `Beyd_Avatar_Girl_BaseBody_Common_02_Mat`, shader
`miHoYo/Character/Character_Ugc_Skin`, with exact native `_MainTex`/`_MTMap`
payloads. Absence of a matching draw in the eight supplied captures therefore
does not certify that this renderer is always invisible. The source lacks
authored tangent/color/extra UV channels on this mesh, so ordinary shader
behavior for it needs an explicit fallback or native visibility proof.
A Unity import with Tangents=Import
reported zero tangents on the old-writer Hair S0133 FBX despite source arrays
being present. This checkout was at `9e69ff5`, predating the already validated
FBX tangent-basis fix in fork commit `f902829` / PR #3. Its exact managed layer
order, native binormal/material changes, and native DLL blob
`382ee1e8e51d0cf09f5f68c81324c7ad4f7ed860` (SHA-256
`1C3CF66913FF1BFE35422BE72D9E0914F816D5BDF394CAA0ED6A14967032BCE4`)
were brought into this checkout; see the package's
`Docs/Genshin/Baseline-Fixes-2026-09-28.md` for the original four-character
Unity validation. Fresh source probes are
`%TEMP%/manekin-tangent-fixed-hair-s0133-20260929` and
`%TEMP%/manekin-tangent-fixed-girl-common-20260929`, with eight-binary and
payload hashes in `%TEMP%/manekin-tangent-fixed-provenance-20260929.json`.
CLI and GUI Release net10 builds passed; the native DLL was explicitly copied
into both run directories because incremental PreserveNewest did not replace
their older output. The Editor owner then imported authored tangents: Hair
S0133 passed 32/32 source-spread vertex-channel comparisons, and Girl Common
passed 192/192, including normals, tangents, colors and UV0-UV2. Reports are
`Assets/_Games/Genshin/Validation/Manekins/VertexStreams/20260929T215120972Z`
and `20260929T215254113Z`. The older 64-part batch, ten native-v4
assembled oracles, and 20 facepart FBXs retain their original writer identity.
Their corrected-writer replacements are separate immutable runs:
`%TEMP%/manekin-phase1-batch-pr3writer-20260929T2153` (64/64 source parts,
64/64 native payload audits),
`%TEMP%/manekin-ten-set-oracles-pr3writer-20260929T2158` (10/10 structural
assemblies, 595 native texture references and 818 hashed files), and
`%TEMP%/manekin-20-faceparts-pr3writer-staged-20260929T2153` (20/20 independent
brow/pupil exports, 60 native texture sidecars). Each run records the eight
runtime binary hashes, including the corrected native DLL; the old apphost
hash alone is insufficient provenance. The Editor owner imported the ten
corrected structural oracles under
`Assets/_Games/Genshin/Validation/Manekins/BatchRuns/20260929T220627643Z`
and passed all 11 geometry controls per look under
`Assets/_Games/Genshin/Validation/Manekins/AssemblyBatch/20260929T221838935Z`.
The 20 refreshed independent facepart FBXs were imported under
`Assets/_Games/Genshin/Validation/Manekins/BatchRuns/20260929T215747733Z`.

The read-only `--genshin-animation-index` scan of the full 7.1 map found
2,246 Beyd AnimationClip entries (2,159 distinct names) in
`%TEMP%/manekin-beyd-animation-map-index-20260929.json`. Exact source clips
were exported by the existing `.anim` path, not FBX baking:
`%TEMP%/manekin-girl-common-standby-walk-source-20260929` includes looping
Girl `Ani_Beyd_Avatar_Girl_Normal_Standby` (2 s, 60 Hz) and
`...WalkCycle` (1.1 s, 60 Hz), and
`%TEMP%/manekin-boy-store-standby-run-source-20260929` includes looping
Boy `...Normal_Standby` (1.8333 s, 60 Hz) and `...RunCycle` (0.6667 s,
60 Hz). Both looping source pairs have 139 Animator muscle bindings; the
same-named Standby clips in `07199115.blk` have only seven humanoid bindings
and do not prove full-body motion. There is no Boy `Normal_WalkCycle` in this
Beyd name index. `%TEMP%/manekin-animation-source-audit-20260929.json`
records every clip CAB/path ID, SHA-256, duration, binding/path counts, maps
and eight exporter binary hashes. The Editor owner imported Girl/Boy probes
under `Assets/_Games/Genshin/Validation/Manekins/ExportProbes/20260929T222122747Z`
and `20260929T222141957Z`; playback and source Avatar calibration were not
certified by those imports.

The existing `GenshinUnityPackage` character workflow serializes a source
Humanoid Avatar's human/skeleton mapping and reconstructs it in Unity via
`AvatarBuilder.BuildHumanAvatar` before native muscle-clip playback. The
Manekin model exporter only writes native `.anim` after
`PrepareForExport`/`ConvertSerializedAnimationClip`; it does not turn muscle
curves into generic Transform curves. `ModelConverter` explicitly warns that
FBX export does not bake humanoid Animator curves. A bounded
`--genshin-avatar-recipe` probe now shares that existing Avatar description
code but refuses unresolved source pointers. The selected Boy Store Animator
has null `m_Avatar` (fileID/pathID 0/0). Girl Common has Transform plus
MonoBehaviour, no Animator. Girl Prototype points to external Avatar
`cab-9c8a30208a07d78ade1c176aa041378f` pathID
`2373564413490261357`; Boy Prototype points to
`cab-846a03ea1f8853c0308c1c0cdf2c0d55` pathID
`-568279900326963701`. The existing full map has zero Avatar entries. An
existing CAB-map build over the 1,985 installed blocks resolved Boy CAB to
`00630208.blk` offset 14,280,703 and Girl CAB to `10453615.blk` offset 538,988;
its binary map is `%TEMP%/Maps/manekin_avatar_20260929.bin` (SHA-256
`B9254E5C4BB0C90CFF3CA499A79C64A99F145EF7D2AA88CA0056E512EB3AE7AD`).
The companion minimal asset map has zero Avatar entries, so the probe now
loads the exact external CAB and path ID through that source map. Resolved
source recipes are `%TEMP%/manekin-girl-prototype-avatar-recipe-v2-20260929.json`
and `%TEMP%/manekin-boy-prototype-avatar-recipe-v2-20260929.json`, with raw
Avatar SHA-256 and source-pointer provenance. Girl's source Avatar has all
52/52 Common weighted bone paths; Boy's has 66/87 Store weighted paths. The
other 21 are S0017-specific top/backwear/headwear bones outside the Avatar
calibration skeleton. Path coverage does not prove rest-pose equivalence or
Unity Humanoid validity.

`%TEMP%/manekin-source-avatar-packages-20260929/{Girl,Boy}` contains isolated
diagnostic Unity packages produced by the existing
`GenshinUnityPackage.Write`: corrected-writer base FBX, two exact looping
native `.anim`, `<root>.character.json`, a unique generated importer per body,
and `avatar-source-provenance.json`. The shared sibling `Generic/Editor` contains
the existing clip-composition importer. The root `rootpackage-audit.json` hashes
all 17 payload files, records both exact source Avatars/clips, the source and
CAB maps, and the eight runtime binary hashes. Each package keeps its
`source-request.json`; rerun it with `AnimeStudio.CLI.exe
--genshin-avatar-package <genshin-7.1.map> <source-request.json>
<new-output-directory>` after pointing its local source paths at the same
client. Root names and first Avatar node names
match; both clips per body are marked `body: true` by the existing source
binding test, with 46 human nodes per Avatar. These packages use the resolved
Prototype source Avatar against Common/Store models for a **diagnostic** Unity
test. Source selection semantics and complete body/cloth behavior remain
unverified; the bounded eyebrow playback result is recorded below.

## Eyebrow rest and native clip ownership

The selected eyebrow has its own source rest frame. The primary Boy S0133
source oracle's `Assets/_Games/Genshin/Manekins/Source/BatchRuns/20260929T221520262Z/Boy/S0133/manifest.json`
records `Eyebrow_P0141` from `12859995.blk` replacing the Store base's
`Eyebrow_P0132`. Its six `exclusiveRestOverrides` retain the incoming brow
pivot transforms from P0141, with the original Store transform and exact source
GameObject IDs alongside each replacement. The guarded assembler permits this
only after proving the replaced subtree has no retained renderer, morph,
weighted bone or non-Transform component. That proves a geometry-safe source
replacement; the rest difference is not introduced by the Unity import or by
an invented exporter transform. It does not establish a native menu pairing.

The Humanoid Avatar recipe comes from the Prototype's external Avatar, while
the Boy Store Animator has a null Avatar pointer. `GenshinUnityPackage.DescribeAvatar`
serializes Avatar calibration; the Unity importer temporarily applies that
calibration to build a Humanoid Avatar, then restores the exported FBX model
pose before saving its prefab. `GenshinBindPose.Restore` separately uses the
source default pose for unweighted nodes and mesh inverse binds for weighted
bones. Thus the Avatar recipe pose is not the selected eyebrow's mesh rest
pose. The selected native Boy clips and Girl Standby contain explicit Transform
curves on brow descendants; Girl WalkCycle has no brow Transform channels.
They cannot be treated as generically unanimated branches.

The source-base and selected-look playback control now passes for these exact
diagnostic inputs. The [package Unity validation record](<I:/_Archive/ToNas/Unity Projects/Projects/Game_Asset_Study/Packages/com.caladan.shaders/Docs/Genshin/Manekin-Implementation.md#eyebrow-avatar-compatibility-repair--2026-09-29>) owns its sampled clips,
rest checks, geometry comparison, limits and reproduction details. These
Standby/RunCycle/WalkCycle samples have effectively neutral brow motion; they
verify preservation of the source-selected offset and binding behavior, not
expressive eyebrow pivots or facial-motion fidelity. No exporter source change
was needed for this measured rest difference.

### Expressive facial clip lead — 2026-09-30

The full 7.1 Beyd animation index contains exact Boy and Girl
`Ani_Beyd_Avatar_<Boy|Girl>_Outgame_UAngry02` clips. Both were exported through
the current canonical `dist/net10.0-windows/AnimeStudio.CLI.exe` using a single
anchored clip-name regex and a fresh output directory. The Boy command used
`--genshin-model` with exact Store Animator path ID
`-242358498629974930`; the Girl command used `--genshin-model` with exact
Prototype Animator path ID `-904139397338801904`. The Girl Common GameObject
is absent from the full and companion maps as a direct CLI root; the Girl
Prototype FBX from this probe is not a replacement for the selected Common
model. Only its exact native clip is a candidate for the selected Girl rig.

| Body | Source clip block / path ID | Fresh output root under `%TEMP%` | Native `.anim` SHA-256 | Export manifest SHA-256 |
| --- | --- | --- | --- | --- |
| Boy | `05040389.blk` / `8158385883319150667` | `manekin-facial-boy-uangry02-20260930` | `6D6BCA346220C83067BA6EE470748C521337F446776CE693A8F98318FBD60069` | `542FC22BC07ECD0411AE91158A698A9B8E27A5B79EDCE01B58AFF9CDC3E12618` |
| Girl | `05790511.blk` / `2153900131883871362` | `manekin-facial-girl-uangry02-prototype-20260930` | `427EC7615A2C9CB1041CDF9D95D411F76C6728978491252794D06F46D1B53444` | `C4F113CA46C03FDDF2C3FEBA3536300C91FE9E1CE0A3108018558A676D012019` |

Each manifest records the exact source object and exporter revision. The map
SHA-256 is `A63564A788A006B45ACCFD18B609403A2581F7F246E275ECD942454E578862D0`;
the canonical distribution's build-manifest SHA-256 is
`441306BF7231E96498F716769107DFCC87FA773CB793BD6BEDFD30833FDFDBC4`
(build UTC `20260929T231559226Z`, CLI executable SHA-256
`79912B1637FC36ECFBB5762FD15D0F72111C39538E67389F384913455D1A0ABD`).
No exporter rebuild or source edit occurred.

Both native clips are 3 seconds at 60 Hz, with 139 humanoid bindings, eight
eyebrow Transform paths matching the corresponding source Avatar recipe and
eleven `Face` blend-shape curves. Their 181-key brow tracks vary: the largest
rotation-component span is about 0.168 for each body, and the largest local
position span is about 0.00976 m for Boy and 0.01152 m for Girl. Seven Boy and
five Girl `Face` curves also vary, with a 0–100 span on at least one curve per
body. This establishes available source facial motion and a concrete Unity
probe input. Selected-look playback, actual blend-shape application and facial
rendering still require the package-side diagnostic; a matching path alone is
not native selection or visual-fidelity proof.

#### Native Face curve binding repair — 2026-09-30

The first Unity preview sampled the source UAngry02 clips on twenty attached
looks. Brow transforms followed their imported curves, while every Face weight
delta remained zero (`Assets/_Games/Genshin/Validation/Manekins/AvatarCompatibility/20260930T005442010Z-f46f7189/facial-availability.json`).
The Boy source and selected prefab import 11 named Face blend shapes. The clip's
11 Face bindings instead said `blendShape.<decimal CRC>`, so a varying curve
existed in the `.anim` but did not name an imported shape. For example,
`blendShape.1821054144` is the CRC32 of the exported FBX channel
`Eye_WinkA_L`; all eleven hashes match the eleven Face channel names. The
source files also carry the same CRC/name pairs. The import audit
`Assets/_Games/Genshin/Validation/Manekins/AvatarCompatibility/20260930T011035818Z-3d540786/facial-binding-audit.json`
confirmed zero exact Boy bindings and eleven CRC matches on the selected Boy
mesh. This is a native export binding defect, rather than evidence that the
source animation has no facial keys.

Standalone source clips need not have a controller link that lets
`CustomCurveResolver` find a source mesh. `GenshinBlendShapeCurveBindings` now
resolves remaining numeric `SkinnedMeshRenderer` attributes against the morphs
of the exact model being exported. It strips only that model's root from an
FBX mesh path before comparing it with the relative `.anim` binding path, and
uses exact CRC32 channel matches. A mismatched hash at an existing renderer
path fails the export; a curve targeting an absent renderer stays explicit.
Already named curves remain unchanged. The focused regression checks all
eleven real CRC/name pairs, exact path matching, idempotence and rejection of a
missing channel. This fix does not rename meshes, fabricate keys or alter a
model's blend-shape geometry.

Canonical `./build.ps1` run `20260930T011156525Z` passed for GUI and CLI.
Its saved build manifest is
`artifacts/build-records/20260930T011156525Z/build-manifest.json`
(SHA-256 `B730C4B07FD77D2DE9ED0DD1A65F385F5B8F584DFD846E1E5127DE2095F06310`,
Utility DLL SHA-256 `8A9C6EA7A58C428818F136D6CD01DDB26B64178FE19D9863EC14A94981A882F8`).
Using map `docs/Genshin/Maps/genshin-7.1.map` and the exact Boy Store / Girl
Prototype roots above, that build re-exported both UAngry02 clips into fresh
ignored `artifacts/export-validation/facial-bindings-20260930T011156525Z/`
directories. Each manifest records 11 resolved bindings and each `.anim` has
11 named, zero numeric Face bindings. Boy clip SHA-256 is
`67D4C5F674F46BBE4B525A87151D9AA3AEB2DDA31B043AD638856B8FE873F166`;
Girl is `FBA3BE036CF47893B90A711BDCB59F5EDA214D3FDD412A1EE3B9362C1EA7A378`.
Reversing only those eleven attribute names yields byte-identical text to
each prior clip. An established Boy Standby sample also re-exported there
with 11 already named Face bindings and zero resolver changes (clip SHA-256
`0F7218160AF6A147B899A34F8015E5D85465A2B5738026519D87DF4D69286535`).
Build `20260930T011049665Z` had packaged binaries but failed temporary backup
cleanup under a Windows file lock; its staging was removed after verifying
the prior distribution zip, and the later successful build is the accepted one.

Unity import and actual playback of the corrected clips remain for the Editor
owner. The selected Girl Common root has a Transform-only `Face` placeholder.
The detailed audit
`Assets/_Games/Genshin/Validation/Manekins/AvatarCompatibility/20260930T011715175Z-199a132f/facial-binding-audit.json`
finds the selected Girl's 11-channel Face renderer at
`DiagnosticPart_0_Beyd_Avatar_Girl_Face_P0051/Face`, while the native clip binds
`Face`. The separate Girl Face P0051 source FBX and assembled S0082 FBX both
contain eleven channels. This is a selected modular prefab hierarchy mismatch,
separate from the numeric-name export defect. The package's prefab assembler or
an explicit native clip path adapter must bring the selected renderer and clip
onto the same relative path, then prove animated weight and geometry response.
The corrected clips alone do not establish finished expressive animation or
visual fidelity.

## Requested category coverage

The user requested five distinct accepted choices in each of 18 body/category
cells, 90 selections, and ten looks with both complete and multipart recipes,
20 states. The table distinguishes source leads from certified choices. Counts
in the cosmetics columns are *name-matched source records*, not proven menu
options. For eyebrow/pupil the Girl counts refer to materials, and the many
Boy/Girl GameObject children in the scene index are not a selection registry.

| Category | Boy leads | Girl leads | Exported/validated status | Missing source proof |
| --- | ---: | ---: | --- | --- |
| Hair | 5 selected roots | 5 selected roots | 10 independent geometry payloads; used in 10 same-group and 10 custom rotated source oracles | Per-choice appearance and animation compatibility |
| Eye Brow | 94 distinct GameObject IDs | 94 distinct GameObject IDs | Five/body independent native FBXs; all five/body appear in full-head source oracles; 0 accepted choices | Modular/eyebrow animation and native selection binding |
| Eye / Pupil | 52 distinct GameObject IDs | 52 distinct GameObject IDs | Five/body independent native FBXs; all five/body appear in full-head source oracles; 0 accepted choices | Modular/eye animation and native selection binding |
| Eye Makeup | 12 full-map MonoBehaviours | 16 full-map MonoBehaviours | Five/body exact raw configs/PPtrs/native textures; 0 accepted choices | Face composition/application registry |
| Lip Stick | 7 full-map MonoBehaviours | 10 full-map MonoBehaviours | Five/body exact raw configs/PPtrs/native textures; 0 accepted choices | Face composition/application registry |
| Facial Makeup | 23 full-map MonoBehaviours | 26 full-map MonoBehaviours | Five/body exact raw configs/PPtrs/native textures; 0 accepted choices | Layer/mask composition and application registry |
| Skin Tone | 0 Boy-named records in full map | 14 Girl MonoBehaviours | Five raw source payloads/decoded values match Girl/Boy captures; 0 accepted choices | Explicit cross-body registry and generated skin composition |
| Outfit sets | 5 S groups | 5 S groups | 64 individual FBXs; 10 merged structural and 10 full-head source oracles; 0 accepted complete recipes | Native preset and visibility/occlusion rules |
| Multi part outfits | 5 candidate groups | 5 candidate groups | 10 custom Hair-rotated full-head source oracles; older same-group sets passed 11 Unity modular controls each; 0 accepted native recipes | Alternate modular Unity controls, required/excluded slots, animation pivots, native outfit state |

The game-file `BydCostumeRenderAsset` MonoBehaviours are not entirely opaque:
the five exact raw examples in `%TEMP%/manekin-source-probe-v4-20260929.json`
show property names and scalar/vector words. Boy Lipstick P0025 has
`_UseLipMakeup=1` and `_LipMakeupTex` PPtr file ID 1/path ID
`1197119004939234200`; that path ID matches the named Girl Lipstick P0025
Texture2D in `Export/manekin-parts.json`, direct evidence of a shared payload
reference. Girl Lipstick adds `_LipMakeupLinearBlend`; Girl SkinTone P0151
contains tint/shadow color keys; Boy EyeShadow P0025 has `_UseEyeMakeup` and
`_EyeMakeupTex`; Boy FacialMakeup P0025 has decal placement/color/ramp/texture
keys. `MonoBehaviour.ToType()` returns null because these records have no
serialized type tree. The custom packed field layout has not been fully
decoded, and the source registry applying a chosen render asset to a face/body
material is not identified. A property string or matching name alone cannot
certify one of the 90 selections. The next source investigation must resolve
that registry/layout and prove same-body or shared-body compatibility with
exact references; a shader capture alone cannot establish these links.

The three UGC original shader families are absent from the package. The local
shader investigation recovered 151 D3D11 programs for those families under
`docs/Genshin/Export/manekin-shader-probe-20260929`. The user supplied eight
captures, which identify several active properties/resources and source colors;
they do not by themselves establish a working Unity shader, cached UGC skin
texture producer, or rendered fidelity.
Runtime shader coverage and reference visual fidelity are both incomplete.
No phase-one acceptance or 1:1 rendering claim follows from the geometry
checks above.

## Checks and version boundary

CLI and GUI Release `net10.0-windows` builds passed after the shader-name and
binding-manifest changes. The later guarded add-only/source-qualified assembly
change passed a CLI Release rebuild with zero errors and a fresh Girl merged
re-export. `dotnet run --project
Tests/Genshin/ShaderNameRegression/ShaderNameRegression.csproj -c Release`
passed seven tagged-name/legacy-footer cases. Existing SDK/dependency warnings
remain. The frozen 64-part batch was produced *before* the later assembly
source change; its `build-provenance.json` identifies that binary. The Girl v3
assembly uses the rebuilt CLI and is not covered by the earlier batch binary
fingerprint. The phase-one catalog at `manekin-phase1-catalog-20260929T2020`
likewise describes the frozen part batch and earlier Boy diagnostic, not the
later Girl assembly. Do not combine their identities into one purported run.
The later native exporter and map-query code passed CLI Release with zero errors.
The additional hierarchy probe and exclusive-rest guard passed CLI/GUI Release;
the fresh brow merged export succeeded, the incompatible Prototype Hair case
still failed, and seven shader-name regressions passed.
The native ten-set audit records its **pre-map-query** binary hashes; the
map-query rebuild afterward is a different binary, so any later exports need
their own hashes. The source cosmetic increment and ten native oracles have not
been promoted into the older importer-contract catalog or accepted as the
requested 90 selections/20 looks.
