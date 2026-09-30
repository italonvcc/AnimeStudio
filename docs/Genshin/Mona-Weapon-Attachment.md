# Mona catalyst attachment and combat-idle clips

This is the source-data and exporter account for the Mona catalyst setup. The
Unity prefab, animation playback, and visual validation belong to the package
study. The records below establish exported clip contents and a reusable pairing
rule; they do not establish game state transitions or effect spawn timing.

## Source observations

The installed 7.1 asset map contains `AnimationClip
Ani_Avatar_Girl_Catalyst_WeaponStandby`, CAB
`CAB-23e551d114c2f63d2ab471b8e16fb085`, PathID
`-5900740780320310201`, in `blocks/00/04161624.blk` at offset `16668109`.
An exact-selector `--genshin-model` export with Mona's Animator is retained at
`artifacts/weapon-validation/mona-class-weaponstandby/`. Its manifest records a
60 Hz, 1.6666667-second loop with 139 humanoid bindings. The native `.anim`
contains hand/finger and secondary transform curves, including
`Root/CatalystL`, `Root/CatalystR`, and `Root/CameraLook`.

Mona's `Ani_Avatar_Girl_Catalyst_Mona_WeaponStandby` has the same duration and
sample rate, but its exported native clip has seven humanoid bindings and no
`Root/CatalystL` or `Root/CatalystR` curve. Playing it alone leaves the catalyst
mounts at their default pose and does not supply the class body action. The
exporter already has a composition importer which copies the base clip's curves
and overlays the character's curves on matching bindings. The character export
now selects matching weapon-class clips by exact action suffix; the Unity recipe
prefers that class clip when its timing and sample rate match, then falls back
to the more general body clip. Ambiguous equal-name candidates are rejected.

The exported Mona `ExtraAttack` family binds both catalyst mounts. Its observed
endpoint continuity supports a charged-attack presentation of
`ExtraAttack_Charge` followed by `ExtraAttack`, then `ExtraAttack_AS`.
`ExtraAttack_BS` and `ExtraAttack_Attack` form a separate likely quick branch.
This is a clip-order inference, not a verified gameplay controller sequence:
the generated controller contains no transitions and the exported clips have
no animation events. Game action state, blend windows, handoff to weapon motion,
and VFX timing remain unverified.

The separately exported `Equip_Catalyst_Fossil` source controller references
`IdleLoop` (3.3333 seconds), `AttackLoop` (0.25 seconds), `AttackToIdle`
(0.4 seconds), and `Close` (1.6667 seconds). Its `IdleLoop` moves the book's
internal `CatalystControllerBone/CatalystControllerRoot/bone_book` hierarchy,
relative to the weapon Animator child. It can be played as a separate model
clip while the character runs the composed combat idle. These source references
do not prove the original synchronization or that this named equip prefab is
the gameplay ownership record for a particular inventory weapon. The Unity
setup must explicitly select its intended weapon variant and record that choice.

## Exporter validation boundary

`Tests/Genshin/MonaClipSelectionRegression` checks the character, weapon-class,
and generic prefix selection and their order with synthetic map entries. Against
`docs/Genshin/Maps/genshin-7.1.map`, it selected 558 clips including the exact
class PathID above and Mona's character-specific WeaponStandby. Canonical build
`20260930T031901636Z` succeeded. Its CLI exported both exact clips together to
`artifacts/weapon-validation/mona-combatidle-pair-20260930T031901636Z/`:
the manifest records 139 and seven humanoid bindings respectively, both 60 Hz,
both 1.6666667-second loops, with zero unresolved references. A fresh full
character export and Unity import should confirm that the class clip enters
`Generic/Animations/Girl/`, the Mona WeaponStandby composition uses it, both
catalyst paths resolve on Mona's rig, and ordinary animated playback maintains
the book attachment. That live check is separate from source selection and
clip-content coverage.

## Game video reference, offline review

The supplied `Assets/_Games/Genshin/Captures/Videos/Mona charge atacks.mp4`
is 25.583 seconds at 1920×1080/60 fps. Timestamped frames and 0.2-second
contact sheets are retained under
`artifacts/weapon-validation/mona-video-reference/charge-*`. These are visual
observations, not source transform measurements:

| Video time | Visible observation |
| --- | --- |
| 2.0 s | Mona stands side-on. The catalyst appears as a narrow, upright closed cover/spine behind her upper body on screen-left. |
| 3.8–4.2 s | She lunges/turns into an attack pose; a small blue spell/orb flashes ahead of her. The book is difficult to distinguish during the flash. |
| 5.2–6.4 s | During another attack beat the book is visibly open, floating around shoulder/head height; pages face upward and toward the camera. |
| 14.0 s | Front-biased view shows the open, page-turning book beside her screen-right shoulder, distinct from the yellow stamina arc and blue spell effects. |
| 16.4–17.2 s | A new cast/turn becomes a large star glyph and vertical blue strike with a ground splash. The book remains a small separate object. |
| 18.4 s | Brief combat idle: open book floats beside Mona near the raised hand/shoulder; its spread is roughly one quarter to one third of her visible head-to-foot height in this frame. |
| 20.6–22.0 s | Another star glyph and vertical strike peak near 21.2 s, then Mona recovers while the book stays open and floats near her shoulder. |

The 14.0-second image puts the book spread at roughly 150 screen pixels and
Mona's visible height at roughly 540 pixels. This is only an approximate
same-frame screen ratio; book tilt, character pose, camera orbit, and perspective
all change the projection. It is evidence that the current oversized Unity
appearance needs investigation, **not** a scale factor to apply. Source mesh,
object, FBX, and Unity import transforms must establish the correct scale.

The video contains several separate inputs and a moving camera. It supports
floating, opening/page motion, and a distinct cast/recovery rhythm. It does not
expose clip state names, Animator transitions, playback speed, or VFX event
bindings, so it cannot prove a literal serial playback of the 0.65-second
`ExtraAttack_Charge`, 0.85-second `ExtraAttack`, and 2-second
`ExtraAttack_AS` clips. That sequence remains the endpoint-continuity inference
above until source action data or synchronized Unity/game playback verifies it.

## Native weapon clip tangent serialization

The original `Equip_Catalyst_Fossil` export carried the source controller's
`AttackLoop`, `AttackToIdle`, and `IdleLoop` curves, but the shared YAML scalar
writer serialized positive infinite tangents as the Unicode glyph `∞` on this
.NET runtime. The old files contained 28, 36, and 24 such tokens respectively.
Unity imported at least one `AttackToIdle` paper-bone tangent as `NaN` and
sampling at clip time 0.175 seconds produced a nonfinite transform and invalid
renderer bounds. This was an exporter text defect, not evidence for changing the
source key values or flattening stepped tangents.

`AnimeStudio/YAML/Base/YAMLScalarNode.cs` now emits ASCII `Infinity`,
`-Infinity`, or `NaN` for nonfinite `float` and `double` values and retains the
existing invariant formatting for finite values. `WeaponExportRegression`
checks the actual YAML writer output. Canonical build
`20260930T033136660Z` succeeded and re-exported the exact
`Catalyst-Fossil__e536b85fb2df8a7f3420` family under
`artifacts/weapon-validation/mona-sacfrag-offline/fossil-ascii-infinity-v2/`.
All four new `.anim` files contain zero Unicode infinity glyphs and zero
literal `NaN` tokens; the three clips above contain the same 28, 36, and 24
ASCII `Infinity` tokens, while `Close` has none. The source controller and
qualified clip identities remain in that export's `Source/` graph and variant
manifest. The package Editor owner's reimport evaluated 241 samples and then
1,641 samples across two playback cycles at 120 Hz with finite transforms and
no invalid renderer bounds. That is a playback stability check; it does not
establish game-authentic controller transitions, attachment scale, or visual
fidelity.

## Source mesh and exported size

The bounded source/FBX measurements are retained in
`artifacts/weapon-validation/mona-scale-mesh/scale-audit.md`, with parsed
source transforms and independent binary FBX geometry records alongside it.
The installed `Equip_Catalyst_Fossil` root, Animator/model branch, and book
bone ancestors all have unit scale. Its 3,655-vertex main mesh has source
local bounds of `(0.43201193, 0.16919623, 0.71517956)` metres; the FBX
Vertices array and live Unity `sharedMesh.bounds` agree within float
precision. The only source `TransformCopy` scale of `0.8` belongs to a sibling
of the model branch; its possible runtime use is not established by ancestry.
The reviewed complete catalog contains one Catalyst-Fossil source family/root,
so there is no alternate exported variant with a different model scale.

The source material's `_Scale=0.01` is shared by all 115 reviewed exports using
`miHoYo/Weapon/Weapon_Effects_Uber`; `_ParentScale=1` and
`_ToonPerspectiveEnable=0` on Fossil. No original shader vertex-stage evidence
was available to assign `_Scale` a geometric meaning. The exact
source/FBX/Unity mesh agreement excludes a lost exporter unit conversion or
Unity import scale factor, but does not resolve active attachment behavior or
the game's rendered-size comparison. No manual scale correction is justified
by these records.

## Attachment and scale source audit

An exact-CAB probe of Mona's `MonoVisualEntityTool` (PathID
`-4362291498376708275`, `CAB-10de5bcb3c6f39b943b3115ec9f3cccb`,
`blocks/00/00630208.blk` offset `20041775`) decoded its named Transform
references. Both its `CatalystL` and `WeaponL` labels point to Transform
`-3580093714925977169`, the GameObject `Root/CatalystL`. The separate
hand-skeleton GameObject named `WeaponL` has Transform `-937910871332478450`.
Thus the current active attachment at `Root/CatalystL` follows the source visual
binding; the `WeaponL` label does not identify the hand-skeleton GameObject.
Mona's `WeaponBackPut` binding instead points to Transform `1543668402117227`,
the `PRIVATE_WeaponRootCatalyst` GameObject under `Bip001 Spine1`. Its authored
local scale is `0.6`, but this is a distinct stow binding, not evidence for an
active catalyst scale.

The exact Fossil CAB (`CAB-bcc3951f811a2ecc4d05f8f80ae1ebc6`,
`blocks/00/04803507.blk` offset `31076781`) preserves scale `1` from
`Equip_Catalyst_Fossil` through the Animator/model and visible mesh ancestry.
Its visual component (PathID `-1876197164874673970`) binds `RootNode`,
`NormalObject`, and `ItemEffect` to the mesh GameObject's Transform
`-7399964353397901241`. The `TransformCopy` GameObject
(`-1508292045010137738`, Transform `-6397755543087262483`) has scale `0.8`,
but is a leaf sibling outside that ancestry and has no serialized reference in
the visual component or exported weapon clips. The Unity import retained unit
FBX/file scale and these source hierarchy scales. Neither authored transform
alone establishes the active multiplier. The independent game capture and
equip-size table below establish the active rendered factor, although the
exact runtime consumer of the table remains unidentified. Exact
selectors, decoded binding tables, source TRS, and the offline probe are in the
[scale trace](../../artifacts/weapon-validation/mona-scale-data/findings.md).

## Current extracted equip-size table

The [current AvatarExcelConfigData](https://gitlab.com/GuraFoundation/YuanShenResources/-/raw/34658855d518b2c504428a77d4676d3cb9af38c1/ExcelBinOutput/AvatarExcelConfigData.json)
(commit `34658855d518b2c504428a77d4676d3cb9af38c1`, SHA256
`38133EF05AD0D15608F94E168FCD15CF70DD63AA00F4FA97FF250E0E2FC3725A`)
identifies Mona, avatar `10000041`, as `BODY_GIRL` using a Catalyst. The
[same-commit ConfigGlobalCombat](https://gitlab.com/GuraFoundation/YuanShenResources/-/raw/34658855d518b2c504428a77d4676d3cb9af38c1/BinOutput/Common/ConfigGlobalCombat.json)
(SHA256 `058FE28326AF26F5AA1480F9A06896D25890CB368EACA6DA684B8BDD395E83CD`)
has an avatar equip-size table with five body keys. Catalyst take-out /
put-away pairs are `1: 0.65/0.60`, `2: 0.60/0.55`, `3: 0.75/0.60`,
`4: 0.75/0.60`, and `5: 0.55/0.50`. The obfuscated current fields
`PBKPELJEPGN`, `BKIFJBCJFFG`, `LFEGPJFDHJN`, and `PJNFCFNCGKH` map by
structure and values to the readable historical fields `avatarEquipSizeDatas`,
`equipType`, `takeOutSize`, and `putAwaySize` in [commit
`c2214701`](https://gitlab.com/YuukiPS/GC-Resources/-/raw/c2214701f05bc0299f17535a5c0388bf7b262013/Resources/BinOutput/Common/ConfigGlobalCombat.json).
Only the current table supplies the scale values for this study; older rows
have changed.

The extracted records do **not** explicitly map numeric body key `2` to
`BODY_GIRL`. That association follows historical client enum-name order and
remains an inference. The key-`2` active value `0.60` is independently
confirmed by the exact Fossil game capture: the captured draw has the same
3,655 source vertices and all 10,638 triangle indices match, while four
rigid same-bone vertex-pair length ratios to the source mesh are
`0.600000035`, `0.600000053`, `0.600000058`, and `0.600000020`.
This measures the total active rendered scale, not which runtime code applied
it. The active application check is saved in the Unity project's
`Assets/_Games/Genshin/Validation/MonaWeapon/ScaleCapture/active-scale-check.json`:
draw 5746, fitted scale `0.6000000413324141`, maximum pair-ratio deviation
`2.15e-8`, and all 10,638 indices matched.

The [holstered capture report](../../artifacts/weapon-validation/mona-scale-capture/holstered-mesh/holstered-scale-report.json)
independently confirms a total rendered scale of `0.550000201223974` on
draw 3529. It matched the same 3,655-vertex Fossil source mesh and all
10,638 indices; four rigid-bone pair ratios lie between `0.55000018` and
`0.55000022`, with maximum distance residual `1.64e-7` metres from `0.55`.
Thus both key-`2` table values are corroborated by separate exact captures,
even though the numeric body-key mapping itself remains inferred. The
holstered measurement does not establish where in the game hierarchy the
factor is applied, and holstering is not implemented in the current demo.
Neither value should be confused with Mona's authored
`PRIVATE_WeaponRootCatalyst` scale `0.6` or Fossil's leaf `TransformCopy`
scale `0.8`; no runtime composition between these values is proven.
Exact current source paths, all rows, hashes, and the application gate are
retained in the
[normalized candidate](../../artifacts/weapon-validation/mona-scale-public/mona-catalyst-scale-candidate.json)
and [source audit](../../artifacts/weapon-validation/mona-scale-public/findings.md).

The [exact Fossil controller audit](../../artifacts/weapon-validation/mona-weapon-controller/controller-audit.md)
also limits animation timing claims. Mona's extracted equip config requests
`PlaySpeed=2` and gives `playTime=0.8` for attack triggers, but the attached
source `Equip_Catalyst_Apprentice` controller has no `PlaySpeed` parameter or
speed binding; its four states use speed 1. The config request does not prove
that the weapon controller runs at 2× speed or how runtime triggers are
scheduled. The audit records its states, clips, and transitions.
