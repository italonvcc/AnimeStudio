# Mona ParticleSystem native extension evidence

The 2026-09-29 native transfer trace identifies the previously unnamed header,
Shape, post-Emission and tail bytes. It also corrects a material parsing defect:
all five Idle and both InFloor sources have `m_UseMeshScale=true` and
`alignToDirection=false`. The earlier decoder read the former as the latter.
This supersedes the alignment statement in the earlier
[Idle source account](Mona-Idle-Source-Data.md), and the tentative attribution of
Shape's last 56 bytes to CurveSpawn. Those bytes are `m_MeshSpawn`.

This is source-layout proof, not a complete playback implementation. Enabled,
varying ColorOverDay gradients and enabled emission falloff remain real runtime
obligations. See the separate [day/runtime investigation](Mona-Particle-Day-Behavior.md)
for the distinction between proven data and unresolved evaluation semantics.

## Provenance and method

Source root: `CAB-9f2563ac8ccd85196764846f5b861cbc`, PathID
`451401874049533967`, block `06860440.blk`, offset `11021908`; the linked source
account retains its full block provenance. Five Idle components and two InFloor
controls share particle type hash `A854C19E25E0A7F85417CC1807AA87CA`, Unity
`2017.4.30f1`, and raw lengths 7868, 8060 or 8116. No live game or executable
was modified.

Read-only executable: `C:/Program Files/HoYoPlay/games/Genshin Impact game/GenshinImpact.exe`,
SHA-256 `08A3086D5F3FE695F01DAB61EFA42E442006B18E5E475B2520DF356F6A073B7D`.
All executable offsets below are **decimal file offsets**, not serialized byte
offsets. Its image base is `0x140000000`; `.text` RVA is file offset plus `0xC00`.
The probe prints virtual addresses as a second coordinate.

Property bindings led to module lookup and constructors, whose callers led to
named transfers and fast binary readers. Field names are joined to target memory
offsets in the named transfer, then to sequential binary reads of those same
offsets. The strict parser checks every module boundary and end of stream.
Plausible float values or default-looking data alone were not used to assign names.

Raw disassemblies, executable hash and independently decoded gradient audit live
under the Unity project at
`Assets/_Games/Genshin/Effects/Exports/particle-extension-evidence-20260929T1825Z/`.
`evidence-hashes.json` covers the transfer and Shape-activation logs plus
`idle-color-over-day.json`, the separate raw-gradient audit.

| Transfer | Named file start | Fast-read file start | Saved evidence |
| --- | ---: | ---: | --- |
| Header | 20540800 | 20542832 | `header-*.txt` |
| Shape | 20395728 | 20400768 | `shape-*.txt` |
| Emission | 20718512 | 20721040 | `emission-*.txt` |
| Parent modules | 21182848 | — | `particle-modules-named-transfer.txt` |
| Text | 19934880 | 19935456 | `text-*.txt` |
| Color | 20820800 | 20820944 | `color-*.txt` |

## Header and corrected packed Shape flags

Serialized offsets 32, 104 and 108 are respectively `useOptPrewarm` (bool,
align to four), `useCullingUpdate` (bool, align to four), and
`cullingUpdateDistance` (float). All seven fixtures store true, true, 20.
These are enabled source controls; their exact optimization/runtime effects
have not been recovered and must not be called inactive.

Four packed Shape booleans occupy bytes 1016–1019:

| Raw byte | Key | Native Shape memory offset | All seven values |
| ---: | --- | --- | --- |
| 1016 | `m_UseMeshMaterialIndex` | `0x13C` | false |
| 1017 | `m_UseMeshColors` | `0x13D` | true |
| 1018 | `m_UseMeshScale` | `0x13E` | true |
| 1019 | `alignToDirection` | `0x374` | false |

Fast read instructions at 20402648, 20402695, 20402742 and 20402789 target
these distinct offsets. The vanilla tree had only three booleans and treated
byte 1019 as padding. Inserting the extra boolean fixes the meaning without
changing the downstream boundary.

Raw `[1144,1156)` is `m_AABBSlice`, a Vector3 `(1,1,1)`; named instruction
20400404 and fast reader 20403171 join it to Shape memory `+0xD0`.
Raw `[1156,1212)` is `m_MeshSpawn`, the same MultiModeParameter structure as
`radius`: value 0, mode 0, spread 0, speed scalar/minScalar 1 and empty curves.
Named instruction 20400653 and fast reader 20403217 join it to `+0x140`.

CurveSpawn exists in the native property table, but its value/spread/speed
fields are at `+0xE8/+0xF0/+0x104`, with minimum speed at `+0x100`.
Those are distinct from MeshSpawn. The native Shape constructor starts at
20279024; the Shape module is reached through ParticleSystem `+0x320`.

### Shape activation evidence

The sampling dispatcher starts at 20286672 (`0x1413598D0`) and reads shape type
at 20286983. Types 6, 13 and 14 branch into the mesh path at 20293209.
The jump table at 20293436 sends type 2 to 20287538 and type 4 to 20287809;
both select sampling routines using the ordinary arc mode at `+0x8C`.
Idle Bubble/Drops/LittleSpark use type 2; Splash/SplashCenter use type 4.

Reads of AABBSlice `+0xD0/+0xD4/+0xD8` occur in box-type branches 5, 15 and
16, at 20288911, 20289529 and 20290862. These are not the Idle type 2/4
branches. Mesh acquisition at 20283840 likewise explicitly accepts only
6/13/14 and returns for other types at 20285123. The mesh sampling path calls
20323728 or 20329680 after requiring cached mesh data.

**Observation:** the traced box-slicing and mesh-acquisition branches are not
selected by Idle. **Remaining gap:** the specific MeshSpawn/UseMeshScale
consumer and complete downstream sampling routines have not yet been joined.
Do not generalize this bounded branch proof into a claim that every custom
Shape field is inert in every shape, or equate the renderer's mesh mode with
the emission Shape type.

## Post-Emission 52-byte contract

This is part of `EmissionModule`, not an unnamed color. It starts at raw 1312
without a burst and 1368 with one burst. Relative offsets follow:

| Relative offset | Exact exported key | Type | All seven values |
| ---: | --- | --- | --- |
| 0 | `m_PlacingOnGround` | bool | false |
| 1 | `m_KeepVisible` | bool; align to four | true |
| 4 | `m_DetectLen` | float | 6 |
| 8 | `m_YDelta` | float | 3 |
| 12 | `m_LayerMask` | UInt64, exported decimal string | `"0"` |
| 20 | `m_EmissionLevel` | Vector4 x/y/z/w | (.4,.6,.8,1) |
| 36 | `m_EmissionFalloffStart` | float | 50 |
| 40 | `m_EmissionFalloffEnd` | float | 150 |
| 44 | `m_EnableFallOff` | bool; align to four | true |
| 48 | `m_EnableSpecifyPostion` | bool; align to four | false |

The last name preserves the native misspelling. Named transfer instructions
20720269–20720922 and fast-reader instructions 20721717–20722447 establish
the order. Emission is reached through ParticleSystem `+0x328`; target memory
offsets are ground `0x1D8`, keep-visible `0x1D9`, detect `0x1DC`, Y `0x1E0`,
mask `0x1E8`, level `0x1F0`, enable-falloff `0x200`, start `0x204`, end `0x208`,
specify-position `0x20C`. The serialized order differs from that memory order.

Enabled falloff and nonuniform quality levels cannot be dismissed from a
near-camera fixture. The exact quality index, distance definition, attenuation
equation and relation to existing particles still require a runtime consumer.

## Tail: disabled Text and enabled ColorOverDay

The last 440 bytes are two named modules. The parent names `TextModule` at
21185342 and `ColorOverDayModule` at 21185441 call the transfer functions above.

`[rawLength-440,rawLength-376)` is TextModule (64 bytes): enabled bool/alignment,
three 12-byte PPtrs (`sceneCamera`, `canvas`, `font`), int `fontSize`, int
`fontStyle`, bool/alignment `outlineEnable`, Vector2 `outlineDistance`, and
bool/alignment `emitWithWorldPosition`. All fixtures disable this module.

`[rawLength-376,rawLength)` is ColorOverDayModule, an enabled bool/alignment
followed by the standard 372-byte MinMaxGradient. Its native module is at
ParticleSystem `+0x3D8`, enabled at module `+8`. **All seven fixtures enable
it.** The five Idle gradients use `minMaxState=1` and varying RGB keys;
white `minColor`/`maxColor` values are not evidence of a white active gradient.

Bubble has four color-key times 15026, 19538, 49151 and 52612. Its night/day
colors include approximately (.486213,.402004,.854197) and (.718729,.607843,1).
The other four share seven times 15026, 17282, 19538, 39321, 49151, 50432 and
52612: blue-gray night, warm dawn, white daylight, pink dusk, then night again.
Both alpha keys are 1 at times 0 and 65535. Full exact values are retained in
the audit JSON and fresh typed export. Unused key slots are not active keys.
The day-phase source, color space, update timing and multiplication order are
unresolved; the linked runtime note records that investigation.

## Implementation and checks

`AnimeStudio.Utility/GenshinParticleSystemDecoder.cs` adds the native fields
to a cloned pinned schema. The vanilla schema resource remains unchanged.
Shape ends at 1212; Emission ends at 1420 in the 8116-byte profile, adjusted
for the existing 56-byte burst and 192-byte rotation-curve differences.
Text and ColorOverDay end exactly at stream end in every fixture.

`GenshinStructuredVfxExporter.cs` now leaves only Collision's 4-byte unknown
and Trail's 380-byte extension in unresolved byte ranges. Both parent modules
are disabled in these fixtures. Runtime obligations remain explicit under
unsupported fields; `nativePlaybackReady=false` and `Partial` remain correct.
Naming the data does not implement its runtime behavior.

The standalone `Tests/Genshin/ParticleExtensionDecoderRegression` links the
production decoder and pinned schema. Seven real components passed exact
raw SHA, module-boundary and new-field checks, plus 84 mutation controls:
each Shape boolean was varied independently and malformed bool value 2 was
rejected at ten native positions per particle. Its build passed zero errors
and warnings. Existing Idle/InFloor export regressions were updated for the
correct flags, typed extensions and remaining unknown ranges.

The source-action worker subsequently reported CLI and GUI builds with zero
errors, fresh exports with zero missing/unresolved dependencies or failures,
and passing Idle, InFloor and action regressions. Integrated output directories:
`mona-idle-action-integrated-20260929T1805Z` and
`mona-infloor-action-integrated-20260929T1806Z` under the same project export root.
Effect JSON SHA-256 values are respectively
`91CC96C34F676BE1CFDF88296535F64A9AF39B2BB20CAEA06762B641564FB0F5` and
`C22453FF22CC630A60D0097728FEC426B44025BC922436EEC85275FF79004829`.
These build/export results are separate from Unity rendering or fidelity.
This worker performed no live Editor operations and changed no shader equations.

## Reproduction tools and next evidence

Build `Tools/Genshin/ParticleExtensionProbe/ParticleExtensionProbe.csproj` and
run `Tools/Genshin/Capture-ParticleExtensionEvidence.ps1` with the executable
path and a fresh output directory. The probe accepts decimal file offset/count,
or `--refs` with a hexadecimal virtual address. PE runtime-function entries may
describe only fragments of a larger function; do not infer function ends from
one entry. Bounded linear instruction scans can encounter inline data and are
not proof that a field has no consumers.

`Tools/Genshin/InspectParticleExtensionExport.py` accepts an existing effect JSON,
validates raw hashes and lengths and reports the independent tail interpretation.
The standalone regression executable accepts one or more effect JSON paths.
Retain actual game assets and raw disassembly under the project evidence path,
not in this repository.

Remaining source evidence: Collision/Trail field identities, the exact
MeshSpawn/UseMeshScale consumers and header runtime controls. Remaining active
playback evidence: ColorOverDay evaluation/application and emission quality/
falloff semantics. The correct next integration is the proven alignment fix
and explicit unsupported controls; guessed day tint or distance equations
would exceed the evidence.
