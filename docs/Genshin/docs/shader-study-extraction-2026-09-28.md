# Extraction findings from the Unity shader study — 2026-09-28

The user supplied a Unity project and authorized ongoing character-shader work.
That work lives in the separate `Packages/com.caladan.shaders` repository.
This Genshin study directory continues to own asset research; production fixes live
in the AnimeStudio projects at the repository root. No competing extractor or Unity
shader implementation is added here. Shader work is currently paused for review
and will resume only when asked.

## Provenance and documentation boundaries

Inspected AnimeStudio base: `9e69ff5c1b59f20003b609f961b3930cad1c6789`,
branch `codex/fbx-tangent-basis`, with uncommitted source and native-DLL changes.
The former study repository was at `47f44fa7504888e9aaa296e1aa856808fac1ab64` before
this documentation update. The old supplied ExportMenuFix build is a separate
artifact and must not be presumed to match this working tree.

Read [the detailed exporter implementation record](../../shader-study-exporter-changes-2026-09-28.md)
for each changed file, coordinate conversion, binary layout, build prerequisites,
tests and limits. Those notes live in AnimeStudio at the user's explicit request.
Existing character/animation/voice/VFX findings remain in their own study documents.

The source client reports 7.1.0. The user explicitly confirmed that export and
captures were from the same exact game version. This does not independently
establish whether every optional modification was absent; do not invent that
provenance. Maps, game installations, raw exports and captures remain local,
uncommitted evidence. Paths below beginning `Studies/`, `Captures/` or
`Validation/` are relative to the Unity project's `Assets/_Games/Genshin/`.

## 1. Higher captured mesh counts were not a lesser Animator export

The original Mona capture had more Body/Hair triangles than the exported FBX,
while face counts agreed. The user reasonably questioned whether a lower-detail
Animator had been selected.

Bounded source inspection found six plain-Mona Animator candidates with
essentially the same base topology. The selected Body source is entry zero of
its LOD group; following entries resolve to Body_LOD1, Body_LOD2, Body_LOD3,
then null. Source Body PathID `7531330786841881056` is not, by itself, globally
unique: retain its source-file/dependency identity from the exported report.

Source/capture geometry evidence:

- 30,775 source edges match the older capture's split-edge table.
- Identical-position welding yields 9,175 groups and 26,939 edges, matching the
  captured runtime topology.
- Compute events CS196/208/242/258/272/287/303 show adaptive Loop subdivision after
  skinning. It changes runtime draw geometry without requiring a different base FBX.
- The source tessellation byte was zero on all six checked renderers, including
  Body. Retaining that byte in AnimeStudio is useful provenance, but it does not
  explain the active compute path.
- Six renderer objects left 24 trailing bytes of unknown meaning. No verified
  static crease/projection table was recovered from stripped LOD/helper objects.

After the user disabled **Dynamic Character Resolution**, restarted the game and
provided new captures, Mona's five main draws matched the exported base mesh:
topology, UVs and colors agree, and all 13,529 Body rest positions, normals and
tangents agree after FBX handedness conversion. Matched-pose Unity skinning has
maximum position error 5.55e-7. **No duplicate idle/off capture is required to
resolve this old blocker.**

The earlier subdivision work is valid research, not the next prerequisite for
this off-setting baseline. General kernels matched all 41,692 checked emitted
triangles and all 26,939 split decisions in diagnostic fixtures. Ordinary runtime
topology preprocessing, constraints and dispatch remain incomplete; captured
tables must not become runtime dependencies.

Evidence: `Studies/MonaDay/ExportDiagnostics/SourceGeometry/lod-groups.json`,
`Captures/Analysis/mona outdoor day/MeshInputs`, and the shader package's main
character study and no-subdivision validation reports.

## 2. Tangent presence in FBX does not prove Unity imported it

The supplied FBX stored direct control-point tangents but lacked matching
binormals. Unity Import mode returned no tangents. Generating a UV tangent basis
would lose the game's authored outline directions.

The exporter now preserves tangent XYZ, reflects tangent W along with X, and
writes the corresponding binormal from cross(normal,tangent) times handedness.
New package recipes opt into importing authored normals/tangents. See the
implementation record for why neither orthogonalization nor normalization belongs
in this repair.

Production Mona regression: all 15,575 imported vertex tangents retained,
maximum XYZ error 3.58e-6; valid bases preserve handedness. Eighty Body vertices
have parallel normal/tangent directions and indeterminate binormal handedness.
This is not a claim that all later outline passes are correct: the user's latest
review still reports missing Escoffier hair outlines.

Evidence: `Studies/MonaDay/ExportDiagnostics/NativeTangentExport` and
`Validation/MonaDay/native-tangent-import.json`. The corrected export removes
the need to recover authored directions into an ad hoc study mesh.

## 3. Additional UV layers exposed an FBX material-layer defect

Escoffier Body and Cap lost their distinct material slots on import. Creating
the FBX material element before extra UV layers, together with non-legacy
polygon creation, restored the explicit polygon assignments. Disabling legacy
mode alone was insufficient.

Body now has four slots (59,307 / 10,446 / 18,045 / 1,020 indices); Cap has two
(2,604 / 1,005). Thirteen Mona/Escoffier renderers match their source manifests.
A fresh Mona regression preserves geometry, channels, bind poses and index order.

Evidence: `Studies/Escoffier/ExportDiagnostics/MaterialLayerFirstExport`,
`Studies/MonaDay/ExportDiagnostics/MaterialLayerRegression`, and
`Validation/MonaDay/export-material-slots.json`.

This defect should be fixed in extraction, not hidden by using one approximate
shader for every collapsed slot. Conversely, current skirt backface invisibility
is not yet proved to be another exporter defect; inspect source cull state and
the actual Unity render passes first.

## 4. Source-native texture data is necessary for close shader comparison

PNG previews discard block-compressed payloads and authored mips. RenderDoc PNG
exports can also represent quantized decoded linear values; interpreting those
as original sRGB bytes creates another conversion error.

The new AnimeStudio companion preserves source bytes and metadata:

- `NativeTextures/*.astexture`, with `native-texture-export.json` and
  `NATIVE-TEXTURES.txt` alongside the character.
- ASTEX001 envelope, UTF-8 JSON header, untouched mip payload.
- Schema 1 for single 2D images; schema 2 for six-face cubes.
- Explicit source identity, format, color space, sampler settings, dimensions,
  mip count and payload length.
- Windows-only validated layouts; unsupported cases report a reason. No
  automatic platform transcoding, decompression/recompression or generated mips.

The Unity shader package supplies the optional importer and binder. Native
companions are preferred within each texture search scope, with the character's
own folder searched before a global fallback. Matching uses the preserved
authored name rather than the filename's source-ID suffix. Serialized importer
overrides can select the source shader's color-space view and sampler without
altering the underlying bytes.

All 12 Mona companions and their decoded GPU mips passed independent-upload
comparison. GUID stability and actual binder preference over co-located PNGs
passed. Source-native and captured-native texture inputs produce identical RGBA
under the same matched ordinary-model test conditions.

Vesna's BC6H reflection cube has six 256-square faces, nine mips and 524,448 bytes.
The complete source payload equals the capture in face-major/mip-minor order,
SHA-256 `29D3FB2387FD559809C0842F4AE1922B76D4702306C164F2280055C8CDD6B5DA`.
Unity sampling agrees across 3,538,944 checked face/mip samples. Direct compressed
GPU readback is unavailable on the checked D3D12 backend, so the GPU comparison
reads sampled float output. The raw source/capture hash comparison is separate.

Evidence: MonaDay `ExportDiagnostics/NativeTextureModelExport`, Vesna
`ExportDiagnostics/CubePayloads`, and the shader package's native-texture guide.
The exporter only traverses resolved material texture references for these
companions; the result manifest does not prove all runtime/global textures were
extracted.

## 5. Shader identity and shader-layout support are different claims

A bounded GI 7.1 metadata parser now recognizes type hash
`8EA866E8248B6A3228054C208F671D38`, despite the older serialized version string
`2017.4.30.1.2`. The layout includes separate global/local keyword data,
instanced structured-buffer metadata, additional-blob flags and program hashes.

The parser guards two unknown zero extensions:

- 20 bytes at the end of the recognized subprogram metadata.
- 12 bytes after the known pass stage programs, before the following pass fields.

Character_Glass exposed an incorrect earlier assumption that the 12 bytes were a
pass tail. The current placement is recorded in the exporter implementation.
Unknown nonzero/truncated extensions reject full parsing and preserve the
existing validated identity fallback. Mutation regressions exercise this behavior.

The checked Base_Uber sample parses 316 properties, 10 passes and 3,980 stage
subprograms. Matching source DXBC hashes to capture programs recovers named
constant bindings; shader features should not be inferred from arbitrary buffer
offsets or keyword names alone. A six-shader Jahoda sample, including Glass and
built-in metadata, passes bounded parsing. Twenty-eight trailing Shader bytes
remain uninterpreted.

Earlier documentation stating that only shader-name fallback exists is historical
for those earlier layouts/fixtures. This update is **bounded new layout support**,
not general proof that every game shader can be decoded or rendered.

## 6. Sampling, channels and geometry roles matter to interpretation

Findings for future extraction/consumer audits:

- Unity ForceEnable anisotropy can promote anisoLevel 1. Explicit level 0 is needed
  for non-anisotropic eye/SDF/pattern roles. In one Vesna close-up the projected UV
  was exact, but the wrong footprint changed an SDF sample across its cutoff,
  creating extra highlights. Preserve sampler evidence as well as image bytes.
- `_UseCoolShadowColorOrTex`, not the unrelated legacy `_UseShadowRamp`, controls
  the checked day/cool shadow interpolation. Incorrect mapping caused a facial
  color bias even with correct exported textures.
- Base_Uber's hair-map option can use normal-map red as selected-region AO while
  replacing normal RG with neutral values. It is not exclusive to crystal
  materials. Vesna's checked hair MRT1 improved to 5,293/5,293 exact after fixing
  the shared runtime branch.
- FacialUVExpression needs multiple UV roles and expression-geometry selection.
  Treating it as an ordinary diffuse-only material previously hid newer faces.
  Preserve UV channels, group data and separate Face_Eye/Pupil mesh roles.
- Front hair can blend lit/dark material responses over a face/eye stencil before
  lighting, using a head-relative camera fade. It is not conventional final-color
  transparency. Jahoda and Vesna have a separate Bang mesh; the inspected Mona
  and Escoffier exports do not.
- Source normal targets use R10G10B10A2. Their bytes are not RGBA8.
  Lit/dark responses, HDR intensity, emission and material class have independent
  target semantics. A PNG alone is insufficient evidence for packed-buffer layout.
- Native outline tangents and the derivative normal-map frame are different data.
  Do not regenerate one to compensate for a problem in the other.

These are observed source/consumer behaviors. They do not establish a universal
interpretation for an unobserved channel or a later game version.

## 7. Current runtime status and outstanding review

The saved Unity review scene contains Jahoda, Mona, Escoffier and Vesna.
Compilation succeeded and several isolated material/deferred tests are exact,
but the user reports the following unresolved live defects:

1. Invisible skirt interiors/backfaces.
2. Non-Mona skin responding incorrectly to light; faces too bright relative to bodies.
3. Face toon-shading artifacts during light rotation.
4. Incorrect shadows, particularly near fully shaded/backlit orientations.
5. Skirt occlusion brightening underlying Vesna/Escoffier meshes.
6. Incomplete facial visibility and camera-dependent fading through hair.
7. Missing Escoffier hair outline.
8. Whiteout toward the top of Vesna/Escoffier socks as the light turns.

The authoritative reproduction checklist is the shader package's
`Docs/Genshin/Baseline-Review.md`; its `Docs/Session-Handoff.md` is the resume
entry point. Do not mark any item fixed because an intermediate buffer matched.
Character switching and hit effects remain deferred behind this baseline.

## Reproduction and future work

Use the existing production exporter and current-client map. Select the qualified
Animator from the dependency graph, export to a fresh directory, and preserve
native companions/material JSON with the model. Never replace the supplied
regression exports in place.

The shader package's `Tools/Genshin/SourceGeometry~/` uses AnimeStudio libraries
for bounded source evidence. Its arguments are map, selection and output directory.
Selections include `list:<name>`, an Animator PathID, and the inspected
`shaders:`, `textures:`, `payloads:` and `cubemaps:` modes. Inspect the current
program before running; source IDs and local paths are fixture-specific.

Use live Unity Editor APIs for import and serialized changes. Validate rest
geometry, skinning, every relevant vertex channel, material slots, shader identity,
native texture mips/samplers and render-state behavior separately. Keep raw
captures and generated reports outside source control.

Shared exporter builds, tangent/slot/native-payload checks and bounded shader
parsing passed during implementation. The documentation update did not rerun them.
A fresh all-features automatic character package export/import and wider
non-Genshin FBX regression remain open. Current shader defects may require more
exporter work if traced to source parsing/export/import, but their causes are
not established by the user's visual report alone.
