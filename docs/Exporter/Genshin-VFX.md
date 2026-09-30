# Genshin VFX source export and Unity import boundary

The previous VFX path exported component raw `.bin` files and a manifest,
but no structured particle values because the Genshin bundle strips the type
tree. Unity could not turn those files into an authored effect. This change
adds a versioned `.effect.json` for each selected root, preserving stable
source file/path IDs, hierarchy, local transforms, GameObject active flags,
component order and enabled states, renderer material slots, raw SHA-256 and
type hash, and dependency paths relative to the effect JSON. Each Texture2D
gets a native `ASTEX001` companion containing the original texture bytes,
format, dimensions, mips, color space and sampler settings; PNG remains a
preview. The package's native texture importer consumes `.astexture`.

The Mona InFloor decoder is gated to source Unity `2017.4.30f1` and the
observed particle type hash `A854C19E25E0A7F85417CC1807AA87CA` and renderer
hash `EC270FAF17AE20EA92C009F528E47167`. Its compact schema resource is
derived from the pinned vanilla 2017.4.30f1 tree referenced in
[the source-data account](../Genshin/Mona-InFloor-Source-Data.md). It parses
source curves, gradients, bursts and module fields with bounded array counts,
bools, enum checks and exact module byte offsets. Genshin's later Velocity
orbital/offset/radial curves use the same 2017 keyframe width. Strict parsing
passes both 8,116-byte Mona components; a parse ending at byte 8,116 without
intermediate checks was rejected because it falsely placed Emission 68 bytes
early. The unknown ranges remain raw in `modules` and `coverage`.

`coverage.verifiedNativeFieldsReady` means the known source particle fields
passed these checks. `coverage.verifiedRendererFieldsReady` means the renderer
mode, controls, outline and streams passed their bounded checks. Both may be
true while `status` remains `Partial` and `nativePlaybackReady` is false.
They authorize only an explicitly partial native review profile in the Unity
package. Strict import must continue to reject active partial components.
Neither flag proves the unknown extensions or game script behavior are
visually inert, and neither proves a match to a capture. The root
`MonoEffect` component and event spawn/attachment timing remain unsupported.
The [Unity 2017.4 generated bindings](https://raw.githubusercontent.com/Unity-Technologies/UnityCsReference/2017.4/artifacts/generated/common/modules/ParticleSystem/ParticleSystemBindings.gen.cs)
confirm source Shape values 2 = Hemisphere and 4 = Cone for this sample.

The representative re-export used the current AnimeStudio checkout on branch
`codex/mona-vfx-structured`, starting commit
`9e69ff5c1b59f20003b609f961b3930cad1c6789` plus uncommitted source
changes. The CLI invocation selected one source GameObject from the 7.1 map,
using a supplemental root map entry at source block offset `1064554` and a
single-action request. Output:
`Assets/_Games/Genshin/Effects/Exports/mona-infloor-reviewed-20260929T131546Z/`.
Input hashes were: `docs/Genshin/Maps/genshin-7.1.map`
`A63564A788A006B45ACCFD18B609403A2581F7F246E275ECD942454E578862D0`,
temporary supplemental root map
`905624C784332C109630E9F0FB18DC6F4244B7E3A58AD00FF27DECF46ABB7F06`,
and one-action request
`44A7B3BE6D633BB4442DC545EF28EDC357079A0896E7A7316C200E110349D106`.
The supplemental entry selects GameObject type 1, path ID
`592123511860584661` from the source block at offset `1064554`; the action
is `dash-in-floor` and does not assert timing or attachment.
The original 28,836,226-byte block SHA-256 is
`90AA119D91305EC7566B63E5D1F95741995ABB55EB75EEE917B919F2DC114756`.
The final `AnimeStudio.CLI.dll` SHA-256 is
`1D8C775F527F3A5AE743EA3ED26C4017432C59FA19FEF74BBF921E993CEA03D6`;
its copied `AnimeStudio.Utility.dll` SHA-256 is
`58C72A2C32D2FDB93FE6539A1B541A3F0045E6A0BE75F4EFBADDC17D54CA5074`.
The manifest's `toolRevision` reflects the starting commit; this document
records the additional uncommitted decoder revision. Do not infer that an
older supplied executable contains these changes.

Validation: `dotnet build AnimeStudio.GUI/AnimeStudio.GUI.csproj -f
net10.0-windows --no-restore` and the corresponding CLI build both passed
with zero errors. The bounded export completed, and
`Tests/Genshin/ValidateMonaVfxExport.ps1 -ExportRoot <path>` passed source
raw hashes, byte accounting, native texture envelopes, source burst/lifetime
values, material slots, outlines and explicit partial coverage. The export
was then imported in Unity's explicit partial native-review profile. The
package importer reported source SHA-256
`A936B5C90D084642CD2196697DB5D63AAF4385B185CCAF35D3F709F787423951`,
created two particle systems and two renderers, and retained a stable prefab
GUID across import. Strict import rejected the remaining unknown coverage as
intended. The package's actual-player checks passed on D3D12 and D3D11 after
a fresh build succeeded with zero errors, run
`20260929T132729363Z-845c7cb57d284b6abe7eded71caf762d`. These are
runtime checks, not a claim of full game-reference visual fidelity.

The generated particle polygon mesh in the package needed a `Color32`
vertex channel for native particle playback. That was a package-owned mesh
construction fix; the AnimeStudio export already retained the original
source particle bytes and outline. The full Unity import, runtime and visual
account is in the package's
[effect implementation plan](<I:/_Archive/ToNas/Unity Projects/Projects/Game_Asset_Study/Packages/com.caladan.shaders/Docs/Genshin/Effects-Implementation-Plan.md>)
and [liquid shader study](<I:/_Archive/ToNas/Unity Projects/Projects/Game_Asset_Study/Packages/com.caladan.shaders/Docs/Genshin/Liquid_Common_New.md>).
The next source investigation is to identify the seven Genshin additions,
especially the gradient-like tail, and `MonoEffect` payload before claiming
full fidelity.

## Source keyword and renderer-base follow-up (2026-09-29)

The Material reader previously consumed Unity 2017 `m_ShaderKeywords` but
discarded it. The Drops material `Eff_Water_059_00` has a nonzero
`_NoiseToggle` float while its serialized keyword string is empty. Splash
`Eff_Water_084_00` has exactly `EFFECTED_BY_FOG`. A float cannot stand in
for the selected shader variant. The shared reader now retains the raw
keyword string, with legacy/modern keyword arrays for other Unity versions.
Material JSON exposes `ShaderKeywordState.Status`, `Raw`, `Enabled` and
`Invalid`: empty `Raw` with `Status=Serialized` is verified keyword-off;
`Unavailable` means the source field was not read. VFX export also saves
each Material's original `.bin`, byte count and SHA-256 in `SourceRaw`.

The particle renderer decoder now exports inherited Genshin Renderer fields
from bytes `[12,164)` in `sourceRendererBase`. Both Mona renderers have cast
and receive shadows off, light/reflection probes off, lightmap indices
`65535`, sorting layer/order zero, motion-vector raw value `1`, and dynamic
occlusion on. Unestablished enum meanings remain raw. The eight-point UV
outlines and stream ID lists are unchanged.

Partial particle and renderer components now list
`coverage.unresolvedRanges` with source offsets and known module-enabled
state. Shape `[1144,1212)` and Emission `[1368,1420)` are enabled, so their
extensions cannot be called inert. Collision `[5056,5060)` and Trail
`[6180,6560)` sit inside disabled modules in both InFloor particles. The
header fields `[32,36)` and `[104,112)`, gradient-like tail `[7676,8116)`,
and three renderer extensions still lack proven roles. No source field was
renamed from byte appearance alone. `nativePlaybackReady` remains false;
strict Unity import should keep rejecting complete-native coverage.
Additional serialization metadata, controlled source variants, or an
instance-aligned capture are concrete dependencies. `MonoEffect` is a
separate action/attachment dependency for plan stage 6; its unread script
data does not redefine the standalone particle system's verified bytes.

The latest bounded output is
`Assets/_Games/Genshin/Effects/Exports/mona-infloor-source-final-20260929T1700Z/`.
Effect JSON SHA-256:
`07CC891B33B1B0327F43EA7CB878FAF9F4DCDFD4D3ACB7065BCADC7436A9E921`.
It uses the same source GameObject/block/request described above. The
supplemental root map SHA-256 is
`4BA07BBCB28C3D0F5692EAC1604FA470D7EE05B851A24FB541D45A252129CA47`.
The CLI SHA-256 is
`38499F0F63E0870BD9A3E90459EC5AF458D960949F4109FCBF5F3A4EC7A4A877`;
its copied Utility SHA-256 is
`CC41C63DC79E2FF01469F0957777A10ACE0084F3D2F5500300A20BB51963F25F`.
CLI and GUI `net10.0-windows --no-restore` builds passed with zero errors;
`Tests/Genshin/ValidateMonaVfxExport.ps1` passed the final output's raw
particle/material hashes, keyword state, source bursts, renderer
controls/outlines/streams, and texture envelopes. Live Unity import and
image comparisons belong to the Editor owner; these exporter checks do not
establish them.

A bounded comparison with the pinned
[Unity 2018.4.30f1 type tree](https://raw.githubusercontent.com/AssetRipper/TypeTreeDumps/20ba06e00e0625de0e49a502c6868d6cc86d0f0b/InfoJson/2018.4.30f1.json)
(SHA-256 `3345AFC4C929E7924920EEFBFA00751A4D17756FA59F81022A50A436FF49251F`)
found later Shape texture/mesh-spawn fields and Trail fields. Their order
does not prove the Genshin extension names: the active 52-byte Emission
addition has no established vanilla counterpart, and the current decoder
reaches plausible 2017 Shape values before its 68-byte addition.

## Stationary submerged Mona Idle first review (2026-09-29)

The user identified the target state as staying still while submerged, so the
first bounded native review uses `LiquidStrike_Idle` with five active child
ParticleSystems. Full source provenance, component/material inventory, native
Mesh signature, unresolved field audit, and tests are in
[Mona Idle source data](../Genshin/Mona-Idle-Source-Data.md). The final fresh
export is `Assets/_Games/Genshin/Effects/Exports/mona-idle-renderer-typed-20260929T1612Z/`;
the prior InFloor export remains a regression, not the target prefab.

The shared VFX Mesh detail export now includes parsed source tangent, color,
and UV1–7 channels instead of truncating the Mesh to positions, normals and
UV0. The Idle Bubble Mesh has 181 tangent float4 values; these were present
in AnimeStudio's parsed `Mesh` but previously absent from VFX JSON. The first
renderer Mesh pointer is read from the 396-byte Genshin renderer at offset
332, included in dependency traversal, and emitted as both the original
FileID/PathID and a resolved Mesh asset path. Three remaining mesh slots are
preserved as null. The captured Bubble topology and UV signature matches the
resolved source mesh.

The strict particle decoder now checks the 7868-, 8060-, and 8116-byte
profiles used by Idle/InFloor, including zero/one burst and short/long
rotation curves. Every named module boundary and final byte is validated.
Using the pinned 2017.4.30f1 renderer type tree and the installed player's
property-binding names, the exporter additionally decodes
`m_UseCustomVertexStreams` at byte 212, GPU-instancing and octagon booleans at
216–218, the eight octagon coordinates at 220–287, parent transform controls
at 288–319, streams/mesh pointers, `m_MaskInteraction` at 380 and `m_Flip`
at 384–395. Strict padding/bool checks leave no unresolved renderer-specific
span. The full offset and native-binary evidence is in the source-data account.
Neither this parse nor the successful dependency graph changes
`nativePlaybackReady=false`: enabled Shape and Emission additions and other
possibly active source bytes still have no proven runtime interpretation.

CLI and GUI `net10.0-windows --no-restore` builds succeeded with zero errors.
`Tests/Genshin/ValidateMonaIdleVfxExport.ps1` passed on the final Idle output,
and `Tests/Genshin/ValidateMonaVfxExport.ps1` passed on the fresh two-system
InFloor regression output at
`Assets/_Games/Genshin/Effects/Exports/mona-infloor-renderer-typed-20260929T1612Z/`.
The Editor owner is responsible for live import and image validation.
