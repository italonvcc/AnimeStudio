# Shader-study exporter changes — 2026-09-28

This document records the exporter changes made while studying Mona, Jahoda,
Escoffier and Vesna in the user's Unity project. It is intentionally kept beside
the implementation at the user's request. Cross-project research is indexed in
[the Genshin asset-study notes](Genshin/docs/shader-study-extraction-2026-09-28.md).

## Revision and status

The inspected checkout is `codex/fbx-tangent-basis`, based on
`9e69ff5c1b59f20003b609f961b3930cad1c6789`. **The changes described here are
uncommitted working-tree changes, not a released exporter version.** Preserve
them when switching branches or rebuilding. No commit, staging or push was
performed for this documentation request.

The current native DLL is 4,657,152 bytes, SHA-256
`1C3CF66913FF1BFE35422BE72D9E0914F816D5BDF394CAA0ED6A14967032BCE4`.
This identifies the local native binary only; it does not identify a complete GUI
or CLI distribution. The older supplied ExportMenuFix executable must not be
assumed to contain these changes.

The source game sample reports 7.1.0. The Unity validation environment was
6000.6.3f1, URP 17.6, D3D12, Linear color. These are dated observations. This
documentation-only follow-up did not rebuild or rerun Unity.

## Changed-file inventory

| File | Responsibility |
|---|---|
| `AnimeStudio.FBXNative/api.cpp` | Complete tangent/binormal basis and non-legacy polygon creation |
| `AnimeStudio.FBXWrapper/FbxExporterContext.cs` | Create polygon material layer before extra UV layers |
| `AnimeStudio.Utility/ModelConverter.cs` | Reflect tangent handedness together with tangent X |
| `AnimeStudio.Utility/GenshinUnityPackage.cs` | Mark new recipes as containing an authored tangent basis |
| `AnimeStudio.Utility/GenshinUnityImport.cs.txt` | Import authored normals/tangents for marked model recipes |
| `AnimeStudio/Classes/Renderer.cs` | Retain the source tessellation byte instead of discarding it |
| `AnimeStudio/Classes/Shader.cs` | Bounded recognition of a GI 7.1 shader layout and guarded unknown extensions |
| `AnimeStudio/Classes/Texture2D.cs` | Retain color-space and per-axis wrap metadata |
| `AnimeStudio.Utility/UnityTexturePayload.cs` | New lossless 2D/cubemap companion writer |
| `AnimeStudio.Utility/GenshinModelExporter.cs` | Emit native companions, result manifest and consumer instructions |
| `AnimeStudio.Libraries/AnimeStudio.FBXNative.dll` | Rebuilt native library for the FBX changes |

The existing `Cubemap.ImageLayout` and `LayoutError` implementation is consumed
by the companion writer; `Cubemap.cs` is not a new working-tree change in this
set. Existing animation, skin rest-pose, voice and VFX fixes are separate work;
do not attribute all current fork behavior to this patch.

## 1. Preserve authored tangent directions through FBX and Unity

### Failure and evidence

The supplied FBX contained tangent arrays but no matching binormal layer.
Unity's Import-tangents mode returned no tangents, while calculating UV tangents
would replace the source data. These game tangents can be smooth outline
directions, often nearly parallel to normals; ordinary MikkTSpace tangents are
not equivalent. The exporter must preserve the authored vectors, not repair the
shader around their absence.

### Coordinate conversion

`ModelConverter` reflects the X coordinate when converting the mesh. A
reflection changes handedness. Tangent conversion is now:

```text
T_export = (-T_source.x, T_source.y, T_source.z)
W_export = -W_source
```

Previously X changed but W did not. For an orthogonal reflection R,
`cross(R*N, R*T) = det(R) * R*cross(N,T)`; changing W as well preserves the
binormal represented by `cross(N,T)*W`.

### Native FBX layers

`AsFbxMeshCreateElementTangent` still creates direct, control-point tangent
data. When a normal layer exists, it also creates a direct, control-point
binormal layer. As each tangent is appended, the native exporter uses the normal
at the same control-point index and writes:

```text
B_export = cross(N_export, T_export) * W_export
B_export.w = 0
```

Normal and tangent are already in export coordinates. The stored tangent is
neither orthogonalized nor normalized. The code checks for both layers and a
matching normal index. It does not invent a basis when normal data is absent.

Parallel normal/tangent pairs produce a zero binormal and cannot independently
encode a meaningful handedness. This is an explicit data limit, not permission
to replace their tangent direction.

### Automatic Unity import

New `*.character.json` recipes contain `authoredTangentBasis=true`.
The generated importer recipe type includes this flag and its importer version
advances from 2 to 3. `OnPreprocessModel` finds the adjacent recipe, requires
the flag and matching model filename, then selects Import for normals and
tangents. Older recipes retain their previous import policy until re-exported.

The generated importer source compiled during this work. A complete fresh
automatic model/animation/prefab import with every changed exporter feature
enabled has not been established by this shader-study validation. Do not
confuse compilation with that end-to-end acceptance test.

### Checked result

A fresh production Mona export imports tangent XYZ on all 15,575 vertices,
maximum direction-component error 3.58e-6. Nondegenerate bases preserve
handedness. Eighty Body vertices have parallel normal/tangent vectors.
Bones, bind poses and submesh counts match the checked source. The original
export was preserved; a separate native-tangent export was adopted.

## 2. Preserve material slots on meshes with several UV layers

Escoffier Body and Cap originally imported with one material slot each. Creating
the material element after extra UV elements could place it on a later FBX
layer, while polygon assignment used layer zero.

The managed wrapper now calls `AsFbxMeshCreateElementMaterial` immediately
after control-point creation, before normals/tangents/extra UV layers. The
native polygon call now uses
`BeginPolygon(materialIndex, -1, -1, false)`, disabling the legacy per-texture
mode. Turning off legacy mode alone did not solve the observed defect; both
ordering and polygon mode matter.

The corrected Escoffier export imports:

| Renderer | Indices per material slot |
|---|---|
| Body | 59,307 / 10,446 / 18,045 / 1,020 |
| Cap | 2,604 / 1,005 |

All 13 checked Mona/Escoffier renderers match their source material manifest.
A fresh Mona regression preserves positions, normals, tangents, colors, UVs,
bind poses and index order exactly. This is shared FBX code, so other games and
FBX consumers still deserve bounded regression coverage before release.

## 3. Retain source renderer metadata without inventing subdivision rules

`Renderer.m_UseTessellation` is now a public byte populated at the same read
position where it was previously a discarded local. Stream consumption does
not change. The nearby terrain-tessellation/vertex-light/combine fields were
not reinterpreted by this change.

This byte is **not sufficient to reconstruct dynamic character subdivision**.
It was zero on all six checked Mona renderers, including Body, while the older
capture visibly used runtime subdivision. Six SkinnedMeshRenderer samples also
left 24 trailing bytes whose meaning remains unknown.

No static crease table, projection constraint table or complete runtime
subdivision exporter was added. See the sibling research note for the separate
capture evidence and the user's newer Dynamic Character Resolution Off capture.

## 4. GI 7.1 shader metadata parsing

### Layout discrimination

The source serialized version string `2017.4.30.1.2` is insufficient to select
this game's custom shader layout. The change explicitly recognizes type hash
`8EA866E8248B6A3228054C208F671D38` in the existing predicates for:

- Separate global/local keyword indices.
- Instanced structured-buffer metadata.
- Additional-blob flags.
- Program hashes.

For this type, unknown GPU program kinds, the Unknown enum value, or hardware
tiers outside 0–3 reject parsing. This catches desynchronization before plausible
but wrong metadata is accepted.

### Unknown extensions and exact placement

The parser preserves 20 bytes following the subprogram's known parameter/buffer
metadata in `m_UnresolvedGenshin71ProgramExtension`. It also preserves 12 bytes
after the pass's known vertex/fragment/geometry/hull/domain programs, before
the subsequent pass fields. For the checked GI layout this is before the
instancing flag and strings. The exact placement is in `SerializedPass`;
do not move the bytes to a generic pass tail.

An earlier attempted tail placement happened to parse non-instanced passes with
empty strings. Character_Glass exposed the wrong offset. The corrected position
also permits the checked built-in shaders to parse.

Both extensions must have exactly the expected length and all-zero bytes.
Nonzero or truncated extensions throw `InvalidDataException`; their semantics
remain unknown. The broader Shader loader retains its validated identity
fallback when parsed-form construction fails. Mutation tests check that the
parsed form is rejected and the original shader name is retained.

Do not convert these guarded zeros into invented arrays, skip arbitrary nonzero
values, or declare complete 7.1 format support. The inspected Shader object still
has 28 uninterpreted trailing bytes.

### Checked metadata

The bounded Character_Base_Uber sample yields 316 properties, 10 passes and
3,980 stage subprogram records. Source DXBC identity can then be matched to
RenderDoc programs to recover named material/buffer offsets. Stockings_New and
FacialUVExpression also parse in bounded Jahoda sampling. A later six-shader
Jahoda regression includes Character_Glass and built-in metadata; unknown
extension mutation checks pass.

Shader layout recognition is not executable shader translation and does not
prove all material features are implemented in Unity.

## 5. Lossless native texture companions

### Why PNG is insufficient

A PNG preview does not preserve original BC7/BC6H blocks, authored mip levels or
source format/color-space metadata. Recompressing or generating new mips changes
threshold samples and glitter/reflection detail. Source/capture comparisons
showed the native payloads matched while PNG-based shader comparisons retained
avoidable differences.

`Texture2D.m_ColorSpace` is now retained, initialized to -1 when unavailable.
`GLTextureSettings` now retains V and W wrap modes; older one-wrap layouts copy
U to V/W. Reads remain at their existing offsets.

### Integration and discovery

`GenshinModelExporter` resolves textures referenced by the selected materials'
texture environments, keeps distinct source texture objects and calls
`UnityTexturePayload.Export`. It writes:

- `NativeTextures/<safe-name>_<PathID>.astexture`.
- `native-texture-export.json`: exported/unsupported results and reasons.
- `NATIVE-TEXTURES.txt`: consumer and compatibility instructions.

This traversal is material-reference based. It is not a complete export of every
scene, particle, shader-global or runtime-generated texture. Unresolved references
that do not yield a Texture are filtered before this writer; the companion report
alone must not be presented as proof of dependency completeness.

Preview PNG behavior remains available. The custom Unity importer and binder
live in the user's `com.caladan.stylizedgamekit` package, not AnimeStudio. Without that
package, consumers can continue using PNG previews. The production exporter
must not depend on RenderDoc, Unity execution or the shader-study fixtures.

### Container layout

All integer fields in the binary envelope use BinaryWriter's little-endian
encoding:

| Field | Contents |
|---|---|
| Magic | Eight ASCII bytes `ASTEX001` |
| Header length | Signed 32-bit byte count |
| Header | UTF-8 JSON, no terminator |
| Payload | Original native image bytes, unchanged |

2D companions use schemaVersion 1, dimension `2D`, imageCount 1. Cubes use
schemaVersion 2, dimension `Cube`, imageCount 6. The magic remains ASTEX001
for both versions. Older schema-1 2D consumers remain supported by the package.

Header fields include name, sourceId (decimal string preserving 64 bits),
sourceFile, sourcePlatform, width, height, mipCount, format (Unity TextureFormat
name), colorSpace (0 linear/raw, 1 sRGB), filterMode, anisoLevel, mipBias,
wrapU/V/W and byteCount. A filename contains an ID to avoid ordinary name
collisions; it is not a substitute for source-file-qualified provenance.

The writer uses FileMode.CreateNew, so existing companions are not silently
overwritten. Use a fresh output directory for a repeat export. No decoder,
row flip, face rotation, mip generation or recompression participates.

### Supported/guarded layouts

The current writer accepts StandaloneWindows/StandaloneWindows64 only. 2D input
must declare dimension 2 and one image. Cubemap input must have an accepted
`Cubemap.ImageLayout`, six square faces, and an exactly sized payload.
Unknown color-space values are unsupported.

Block formats: DXT1/BC4 use 8-byte 4x4 blocks; DXT5/BC5/BC6H/BC7 use 16-byte
4x4 blocks. Uncompressed formats accepted by the current size validator:

| Bytes/pixel | Formats |
|---|---|
| 1 | Alpha8, R8 |
| 2 | RGB565, ARGB4444, RGBA4444, RG16, RHalf |
| 3 | RGB24 |
| 4 | RGBA32, ARGB32, BGRA32, RFloat, RGHalf |
| 8 | RGFloat, RGBAHalf |
| 16 | RGBAFloat |

For each mip, dimensions are clamped to one; compressed dimensions round up to
whole 4x4 blocks. The per-face mip sum is multiplied by six for cubes. The
payload must equal this size exactly. Invalid dimensions/mip counts throw;
unsupported platform, format, dimension, color-space or byte size is reported
with a reason. Arrays, 3D textures, Crunch/mobile layouts and arbitrary platform
transcoding are not implemented by this writer. Listing a format in the code
does not mean every such format has been tested on real assets.

### Checked payloads and Unity integration

All 12 Mona 2D companions imported successfully, including BC7 with 1/9/10/11
mips and one-level RGB24 ramps. GPU-decoded mip comparisons, actual binder
preference over PNG and GUID stability after reimport passed. Ordinary Mona
rendering with source companions was RGBA-identical to the capture-texture
control under matched test conditions.

Vesna's Avatar_Tex_Crystal_Cube is 256 square, BC6H, nine mips, 524,448 bytes.
All six faces match captured bytes in face-major/mip-minor order:
SHA-256 `29D3FB2387FD559809C0842F4AE1922B76D4702306C164F2280055C8CDD6B5DA`.
The Unity RGB_BC6H_UFloat import matched an independent reference over 3,538,944
samples spanning all faces and mips. Direct compressed GPU readback is not
supported on the checked backend; validation samples float output instead.

The binder can override a texture's sampling/color-space view for a known
shader role without modifying its payload. In particular, Unity ForceEnable
anisotropy promotes level 1; level 0 is required for a captured non-anisotropic
role. That is a Unity consumer correction, not an exporter byte transformation.

## Build and reproduction

Native prerequisites used successfully: MSVC v143 14.44.35207, FBX SDK 2020.3.7,
Windows SDK 10.0.28000.0. Build Tools 2026 selected missing v180 defaults on this
machine; invoking MSBuild with VCTargetsPath set to the installed v170 targets
resolved that issue. Initial MSB4278/missing SDK failures are historical, not
current blockers. The native project already specifies its FBX SDK include/lib
locations; verify those against the local installation.

From this repository, with the installed MSBuild and VC targets resolved:

```powershell
# Substitute the discovered MSBuild executable and v170 target directory.
& $studyMsbuild AnimeStudio.FBXNative/AnimeStudio.FBXNative.vcxproj /t:Build /p:Configuration=Release /p:Platform=x64 "/p:VCTargetsPath=$studyVcTargets" "/p:SolutionDir=$($PWD.Path)\"
dotnet build AnimeStudio.GUI/AnimeStudio.GUI.csproj -c Release -f net10.0-windows
dotnet build AnimeStudio.CLI/AnimeStudio.CLI.csproj -c Release -f net10.0-windows
```

Native, CLI and GUI Release builds passed during implementation. One combined
stage reported 48 managed warnings and zero errors; the later metadata/cubemap
stage recorded GUI 26 warnings and CLI 2 warnings, zero errors. These are dated
run results, not a guarantee for an arbitrary later checkout. The latest
documentation request did not rerun builds or the broader extraction suite.

Re-export representative models into a fresh output directory. Copy the complete
package, including NativeTextures and Materials, into a disposable game asset
folder or use the live Editor's AssetDatabase APIs. Preserve existing GUIDs and
user edits when adopting a tested export.

Shader-package helpers under `Tools/AgentScripts~/`:

| Entry | Purpose |
|---|---|
| `ValidateNativeTangents.Inspect` | Imported tangent direction/basis comparison |
| `ValidateExportMaterialSlots.Run` | Renderer/submesh slots versus source manifest |
| `ValidateNativeTextures.Run` | All checked 2D mips, native lookup and reimport stability |
| `ValidateNativeCubemap.Run` | Six-face/nine-mip GPU sampling comparison |
| `ValidateAssetStudioBinding.RunNativeExport` | Actual binder and source JSON compatibility |
| `ValidateMonaCapture.RunSourceNativeWithoutSubdivision` | Source-native textures in the matched ordinary-model comparison |

These helpers contain local fixture paths; read them before running. The package's
`Tools/Genshin/SourceGeometry~/Program.cs` performs bounded source inspection
through AnimeStudio libraries, including shaders/textures/payloads/cubemaps modes.
It is an analysis tool, not a replacement production extractor.

Evidence is local to the Unity game's asset folder: MonaDay ExportDiagnostics
(NativeTangentExport, MaterialLayerRegression, NativeTextureModelExport),
Escoffier ExportDiagnostics/MaterialLayerFirstExport, Jahoda
ExportDiagnostics/GlassShaderBindings, Vesna ExportDiagnostics/CubePayloads,
and Validation/MonaDay. Do not commit these generated assets or raw captures.

## Remaining acceptance limits

- Full automatic character package re-export/import after all these changes,
  especially mixed characters and animations, needs a fresh end-to-end check.
- Shared FBX modifications need broader non-Genshin and alternate-consumer tests.
- Unknown shader extensions/trailing data remain unknown; retain guarded failures.
- Native payload support is bounded, Windows-specific and not a transcoder.
- Dynamic subdivision reconstruction is incomplete and not solved by exporting
  the tessellation byte.
- The user's latest Unity review still reports backface, skin/face lighting,
  shadow, hair visibility/outline and stocking-whiteout defects. The exporter
  fixes above do not establish a 1:1 character renderer. Investigate exporter
  data again if evidence points there, rather than compensating in the shader.

## Resumed baseline work: multi-UV channel binding repair

During the resumed shader review, several FBXs contained valid tangent/color
arrays but Unity selected empty secondary elements. Creating only the material
layer first was insufficient. `AnimeStudio.FBXWrapper/FbxExporterContext.cs` now
creates material, normal, tangent/binormal and color elements before extra UV
layers. No vertex payload conversion is changed by this fix.

CLI and GUI Debug net10.0-windows builds passed with --no-restore. This resumed
work did not modify/rebuild the native exporter. Build provenance: base
9e69ff5c1b59f20003b609f961b3930cad1c6789 plus pre-existing/current uncommitted
changes on codex/fbx-tangent-basis. CLI app-host SHA256:
2996B119CFDF8C23EBC9A6644FE73287A4EBF7426BDFF283755A581154286DF4.
This hash alone does not identify its managed/native dependencies.

Using --genshin-model with Maps/genshin-7.1.map, fresh Jahoda, Vesna, Escoffier
and Mona exports went into the Unity project's
Assets/_Games/Genshin/Characters/ChannelFixExports. Exact FBX channel-array
comparisons passed against ReviewExports for all four. Live Unity comparison
checked 26 renderers: triangle position/skin correspondence, bone order and bind
poses preserved; authored channels now import. Unity can weld/reorder vertices
differently after the declaration fix. Repaired non-Mona meshes were adopted
into the existing scene; Mona retained its already valid mesh.

Shader-package evidence: Validation/BaselineReview/Resume/channel-import-regression.json
and export-channel-regression.json beneath the game's local assets. Package
Tools/Genshin/CompareExportChannels.py and Tools/AgentScripts~/AdoptChannelFixMeshes.cs
record the checks. Detailed input IDs and reproduction are in the shader package's
Docs/Genshin/Baseline-Fixes-2026-09-28.md. Wider non-Genshin/alternate-consumer
regressions remain unperformed. User requested a safe pause; source and scene
work are saved, without committing or pushing.
