# Mona Idle particle day color and emission: runtime evidence boundary

This note records the native source behavior behind the decoded Genshin particle
extensions. The installed source is the
Genshin 7.1 `GenshinImpact.exe` identified in
[Mona-Idle-Source-Data.md](Mona-Idle-Source-Data.md), SHA-256
`08A3086D5F3FE695F01DAB61EFA42E442006B18E5E475B2520DF356F6A073B7D`.
File offsets below refer to that executable, not serialized asset offsets.
The full static values are in Unity-project-relative
`Assets/_Games/Genshin/Effects/Exports/particle-extension-evidence-20260929T1825Z/idle-color-over-day.json`
(SHA-256 `6AA07D3CC06AC486FAA4D35811425D9DC7DEA06E96A08692D71816BEC9AE1126`).

Update 2026-09-29: paired noon/night captures now establish the per-emitter shader
day-color binding and byte-quantized plateaus for the integrated Idle export
`mona-idle-action-integrated-20260929T1805Z`, SHA-256
`91CC96C34F676BE1CFDF88296535F64A9AF39B2BB20CAEA06762B641564FB0F5`.
The full capture/Unity account belongs in the package at
[`Docs/Genshin/Mona-Effect-Day-Captures.md`](<I:/_Archive/ToNas/Unity Projects/Projects/Game_Asset_Study/Packages/com.caladan.shaders/Docs/Genshin/Mona-Effect-Day-Captures.md>).
Its implementation plan records the reusable day/emission adapters and validation.
The static search limitations below are historical, not evidence that the shader
binding remains unknown. Native clock mapping, transition interpolation and the
emission metric's provider remain unresolved.

## Confirmed static source fields

The named ParticleSystem transfer at executable file `21166643–21166704`
identifies a `ColorOverDayModule` pointer at particle `+0x3D8`. The module's
enabled byte is at `+8`; its gradient begins at `+0x10`. The native named and
fast transfers use the same ColorModule layout, and the exported raw tail ends
exactly after the gradient. See the bounded disassemblies
`particle-modules-named-transfer.txt`, `color-named-transfer.txt`, and
`color-fast-read.txt` alongside the JSON above. The module is **enabled on all
five Idle emitters**, with `minMaxState=1` and nonwhite RGB keys. The white
`minColor` and `maxColor` fields therefore do not establish an inert module.

`00_Bubble` has four RGB keys at 16-bit times 15026, 19538, 49151, 52612.
Its endpoint RGB values are approximately `(0.486,0.402,0.854)`, while the
middle pair is `(0.719,0.608,1)`. Drops, LittleSpark, Splash, and SplashCenter
share seven RGB keys at 15026, 17282, 19538, 39321, 49151, 50432, 52612;
these progress from `(0.617,0.657,0.744)` through white to warmer dusk keys
and the same endpoint. Both serialized alpha keys are 1 for all five. These
are **serialized gradient values**, not measured particle colors or a proven
mapping from game clock to gradient position.

The post-Emission extension is also named by native transfer. In the five Idle
systems, its recovered fields are `placingGround=false`, `keepVisible=true`,
`detectLen=6`, `YDelta=3`, `layerMask=0`, `emissionLevel=(.4,.6,.8,1)`,
`enableFallOff=true`, `falloffStart=50`, `falloffEnd=150`, and
`enableSpecifyPostion=false`. The source field offsets relative to its module
are `+0x1D8` through `+0x20C`; see the peer's named/fast transfer records in
the same evidence directory. A native setter at executable file
`20687088–20687949` clamps the four emission-level entries to `[0,1]` and
sets `falloffEnd = max(falloffEnd, falloffStart + 10.0)`. The `10.0` constant
is at VA `1421D8038`.

## Recovered emission update equation

The runtime builder receives the native Emission module at particle `+0x328`
and passes `module+0x10` as its source pointer. Its `source+0x1E0` is thus
native `module+0x1F0`, not another serialized layout. Builders at file
`20685473–20685631` and `20685974–20686134` copy into a compact update
structure:

| Native module field | Compact update field | Meaning |
| --- | --- | --- |
| `+0x1F0..+0x1FC` | `+0x30..+0x3C` | Four quality level floats. |
| `+0x200` | `+0x40` | Enable falloff. |
| `+0x204` | `+0x44` | Falloff start. |
| `+0x208` | `+0x48` | Falloff end. |
| `+0x20C` | `+0x5C` | Enable specified position. |
| `+0x20E` | `+0x5D` | `SkipEmissionDecrease`, a separate dynamic property initialized false at file `20869463`; it is not in the decoded post-Emission tail. |

The quality predicate at file `20688000–20688057` selects one of the four
native level floats and returns true only when it is **greater than 0.01**.
The selector at file `21024496–21024557` calculates
`qualityIndex = clamp(configRecord[+0xA8] - qualityAdjustment, 0, 3)`.
The adjustment is at process VA `145664DFC`, with getter file `21024560`.
Initialization at file `21098640–21098810` writes adjustment 0, 1 or 2
using a graphics/configuration value at record `+0xB0`, and sets a separate
global scalar of 1, 0.75 or 0.5 at VA `1454C5E64`. The exact game-facing
quality option at `+0xA8` and the input to the `+0xB0` comparison remain
untyped.

The compact evaluator at file `20684896–20685102` performs the following
ordered arithmetic, with `C` the compact update structure:

```text
qualityIndex = clamp(configuredQuality - qualityAdjustment, 0, 3)
scalar       = min(C[+0x54], SkipEmissionDecrease ? 1 : globalScalar)
rate         = C.level[qualityIndex] * scalar
falloff      = enableFalloff
             ? 1 - clamp((C[+0x4C] - falloffStart) / (falloffEnd - falloffStart), 0, 1)
             : 1
```

`C[+0x54]` starts at 1 in caller-built fallback structures at file
`20309450–20309741` and `20872816–20873124`, then receives the scalar
minimum. `SkipEmissionDecrease` bypasses the global *float scalar* clamp;
the quality selector still uses the adjustment. `C[+0x4C]` starts at 0 in
those fallback structures. The builder does not assign it. Its source,
update timing and units remain unknown; camera distance is an inference
from the falloff field names, not a proven metric.

The emission count branch at file `20874932–20875456` multiplies its
positive count by `rate * falloff`, truncates toward zero, then keeps at
least one event when the original count was positive. Both rate and falloff
must be at least `0.01`. A second count path at file
`20688276–20688607` uses the same factors and integer conversion. The
calculation affects future emission counts. Other quality-predicate callers
participate in particle update/visibility work, so these paths do not prove
that a game quality change leaves already-live particles unaffected.

The full bounded disassemblies are saved in Unity-project-relative
`Assets/_Games/Genshin/Effects/Exports/emission-runtime-evidence-20260929b/`.
`ParticleDayColorProbe` supports direct-field and clustered scans, PE value
reads and RIP references. A matching displacement alone does not identify
an object's type; the source-to-update mapping above follows the pointer
from particle `+0x328` through the builder call.

## Native consumer trace and unresolved behavior

`ParticleDayColorProbe` under `Tools/Genshin/` scans decoded executable
instructions for a field displacement or constant and reports containing PE
function ranges. The direct `+0x3D8` references closest to consumption are:

| Executable file offset | Proven role |
| --- | --- |
| `19752219–19752488`, `19855753–19855896` | Managed module enabled/property binding; setting enabled updates a `0x400000` particle feature bit. |
| `20954800–20954949` | Allocates the optional 0x50-byte module and initializes its gradient. |
| `20986968–20986998` | Mirrors the module enabled byte into the particle feature bit. |
| `21118054–21118153`, `21185434–21185549` | Serialized module transfer. |

The native string `ColorOverDaytimeModule` at file `61140761` has one direct
reference at `4840258`, an IL2CPP marshaling wrapper. It is not a simulation
routine. The feature-cache function at `20986320–20987001` writes every
module's enabled state to the particle's `+0x110` bitfield; ColorOverDay is
bit 22 (`0x400000`, or byte `+0x112` mask `0x40`). Its direct callers at
`20955338` and `20955801` are module setup/copy. A bounded scan for direct
`test`/`and`/`cmp` consumers of `+0x110` and `+0x112` in the adjacent particle
engine functions found other flags but not a verified bit-22 evaluation path.
The `ColorOverDayModuleEnable` string at file `61606016` is referenced by the
transfer/property binding at `21182492`. The executable's `TimeOfDay` strings
refer to shadow and light names; none supplied a day-phase setter for this
module. The direct `+0x3D8` and `0x400000` scans found setup, property and
transfer paths; the latter constant also appears in unrelated string code.
These scans do **not** establish that the game ignores the gradient. Its
runtime kernel may consume copied module data or compiled particle job data.

Dividing the seven key times by 65535 and scaling to 24 hours places the
transitions near 05:30, 06:20, 07:10, 14:24, 18:00, 18:28, and 19:16. The
dawn/dusk pattern strongly suggests a normalized game-day clock input, but
that mapping is an **inference**. It does not prove the clock's exact range,
offset, color conversion, or when particles receive the evaluated color.

No recovered native consumer yet proves the normalized day-phase source, its units
or wrap, or gradient transition evaluation/interpolation. Subsequent captures
resolve the per-draw material factor and measured plateau conversion; see the
package study linked above. The emission quality selector and linear falloff arithmetic
are recovered, but the game-facing quality setting, the `C[+0x4C]` metric,
its update timing, interaction with prewarm/visibility, and impact on
existing particles remain unproved. The current first-review camera distance under 20 m
and a nominal quality index 3 may make some branches inactive, but neither
value proves that these modules have no effect.

The useful next day control samples a dawn/dusk transition to distinguish gradient
evaluation and byte-quantization order. Separate source controls should vary
quality index and candidate falloff
metrics across 50 and 150, measuring spawn counts and existing particle
state. A recovered native update/job consumer with the gradient and scene-day
parameter would also resolve the remaining clock semantics. The Unity importer
now retains source values and supplies the evidenced shader binding; emission
requires explicit scene inputs while native providers are unidentified.
