# Manekin weapon attachment source trace

Status: exact installed Genshin 7.1 source bindings decoded offline on 2026-09-30. This account covers the Boy/Girl Prototype visual components and the two renderable roots used by the current Unity study. It does not establish a complete gameplay equip sequence, source-to-FBX stow conversion, or a rendered weapon match. The package's Manekin study owns Unity setup and visual validation.

## Source selection and extraction

The Boy and Girl `Beyd_Avatar_*_Prototype_Manekin` Animators are distinct CABs in `blocks/00/16636590.blk`, at offsets `42148453` and `43630425`. Their Animator PathIDs are `5166640995251034702` and `-904139397338801904`, respectively. Those are the same source Animators recorded in the accepted packages' `avatar-source-provenance.json` under `Assets/_Games/Genshin/Manekins/Source/BatchRuns/20260929T225804771Z/{Boy,Girl}/`. The exact installed 7.1 map SHA-256 is `A63564A788A006B45ACCFD18B609403A2581F7F246E275ECD942454E578862D0`.

`artifacts/weapon-validation/manekin-attachment-source/AttachmentProbe/` loads each selected CAB using the Genshin map and exact source offset. `DecodeBindings.ps1` reads the serialized `MonoVisualEntityTool` dictionary: count at byte 32, each aligned UTF-8 key followed by a local `Transform` PPtr. The Boy visual component is PathID `-5493182100707643307`, CAB `CAB-1448006e25ebe0643280596df8b08570`, 2,428 bytes, raw SHA-256 `7757AB582D404693ECE3D4FC73F43B80445AA0813BD0C05E245B4C5D58E55BD3`. The Girl component is PathID `5818105940600808779`, CAB `CAB-7757c039bc175f2e05727e1497341470`, 2,428 bytes, raw SHA-256 `651CE7B6028F1C438D6ADFBA5716428E1EC941191D0BA1663A6255F2FCCC514C`. Each has 59 entries; 48 point to loaded transforms, while 11 collider/trigger labels hold null PathID 0. All non-null PPtrs have fileID 0 and resolve within their selected CAB. The remainder after dictionary byte 1768 has not been assigned semantics here.

The full keyed binding, exact Transform/GameObject PathIDs, root-relative hierarchy, and raw local position/rotation/scale are in `artifacts/weapon-validation/manekin-attachment-source/{boy,girl}-prototype-bindings.json`. The companion `*-prototype-exact-cab*.json` records all loaded GameObjects, local transforms, component identities, and Mono raw hashes. This resolves names against source Transform identities, instead of matching a similarly named Unity child by text alone.

## Proven attachment bindings

The paths below are relative to each **Prototype** root. `WeaponR/L` bind to the hand bones with the same names. `CatalystR/L` bind to the separate children of `Root`. `HandItemR/L` alias `WeaponR/L`, and `WeaponR2` aliases `WeaponR`; these are repeated keys to the same Transform PathIDs, not additional sockets. Neither Prototype visual table has a separate Sword, Claymore, Pole, or Bow stow key. Both have one `WeaponBackPut` key, which points to `PRIVATE_WeaponRootPole` under `Bip001 Spine1`. The name of that transform alone does not prove that every weapon class uses its authored pose unchanged.

The separately extracted [equip configuration findings](../../artifacts/weapon-validation/manekin-equip-config/findings.md) trace published `ConfigAvatar_Manekin.json` at commit `34658855d518b2c504428a77d4676d3cb9af38c1`: `equip01=WeaponR`, `equip02=WeaponL`, and the stow label is `WeaponBackPut`. The UGC Avatar config also maps `equip01/02` to those hand labels. Its size-shaped fields are `-1`; their sentinel meaning is unknown. This published config version is separate from the installed 7.1 source CABs decoded here. Together they provide a named equip-to-Transform path, without proving final runtime weapon pose or visibility.

| Binding / Prototype path | Boy Transform PathID | Girl Transform PathID |
| --- | ---: | ---: |
| `WeaponR`: `Bip001/Bip001 Pelvis/Bip001 Spine/Bip001 Spine1/Bip001 Spine2/Bip001 R Clavicle/Bip001 R UpperArm/Bip001 R Forearm/Bip001 R Hand/WeaponR` | `4394700494777520849` | `-4963050157499264091` |
| `WeaponL`: matching left-hand chain ending `/WeaponL` | `70930483165008895` | `-8617155891984938731` |
| `CatalystR`: `Root/CatalystR` | `3983153371461212948` | `6375439605316362487` |
| `CatalystL`: `Root/CatalystL` | `-3143433550659988103` | `-9145348455928411528` |
| `WeaponBackPut`: `Bip001/Bip001 Pelvis/Bip001 Spine/Bip001 Spine1/PRIVATE_WeaponRootPole` | `-5985091320696932612` | `8510818690232001041` |

For `WeaponBackPut`, Boy's raw Unity source local position is `[-0.378,-0.317,-0.089]`, quaternion `[0.08294652,0.60665804,-0.07642596,0.78692126]`, and scale `[1,1.0000001,1.0000001]`. Girl's values are `[-0.349,-0.276,-0.114]`, `[0.07561813,0.62509125,-0.04340939,0.77566653]`, and `[1.0000004,1.0000008,1.0000008]`. These numbers are serialized source Unity local TRS, before any AnimeStudio FBX transform conversion. They should not be copied into an imported FBX prefab until the Editor owner validates that prefab's parent basis and local coordinate convention.

The nonidentity hand-socket rest rotations are also source data. Boy Prototype `WeaponR` local position/quaternion are `[-0.043924704,0.0059265923,0.0033165219]` and `[0.21987604,0.0213158,-0.073518075,0.9725201]`; its `WeaponL` values are `[-0.04308938,0.007252121,-0.008127007]` and `[0.9845686,-0.09299396,0.08151424,0.12382342]`. Girl Prototype `WeaponR/L` local positions are zero, with quaternions `[-0.05154359,0.025614234,-0.31119263,0.9486023]` and `[-0.94861907,0.31114984,-0.02565307,0.05147381]`. The full tables retain source precision and both catalyst TRS records.

## Renderable source roots and Unity import boundary

The currently accepted Boy FBX comes from `Beyd_Avatar_Boy_Suit_S0017_Store`, Animator PathID `-242358498629974930`, `blocks/00/00850201.blk` offset `17339808`, CAB `CAB-8b50668c3517d3cfc5e4f784c204abb7`. The accepted Girl FBX comes from `Beyd_Avatar_Girl_BaseBody_Common`, GameObject PathID `-6714641167054581609`, `blocks/00/12859995.blk` offset `39695979`, CAB `CAB-78800a305e9274b03be5709055d4f9de`. The latter exact GameObject and offset are in `Maps/manekin-scene-index/scene-index.json`; the general 7.1 map does not index that GameObject.

An exact CAB inspection found one each of `WeaponR`, `WeaponL`, `CatalystR`, and `CatalystL` in both renderable roots. Neither root contains `PRIVATE_WeaponRootPole`, `WeaponBackPut`, or `MonoVisualEntityTool`. Boy Store has `MonoBeyondGachaAvatar` but no visual tool; Girl Common has `MonoCostumeProxy` but no visual tool. This explains the observable data split between a renderable wardrobe root and the Prototype's attachment contract. A runtime composition between these source families is an inference; this probe has not found the game code that instantiates or reconciles them. The base model export did not promise gameplay visual components, so this absence alone is not an exporter defect.

The roots' rest poses differ from the Prototype. For example, Boy Store source `WeaponR` has local position `[0,0,0]` and quaternion `[-0.03441006,0.0018861117,-0.29062015,0.95621777]`, whereas Boy Prototype has the values above. Girl Common `WeaponR` is near zero position and quaternion `[-0.051513374,0.025619594,-0.31131393,0.94856405]`. Native Prototype rest TRS cannot be applied wholesale to the current FBXs. Their exported/imported transform paths and animation evaluation are Unity validation questions, reported by the package study.

## UGC spawned weapon effects

The separately traced UGC ability configuration names five spawned weapon effects. The extracted config records are in `artifacts/weapon-validation/manekin-equip-config/weapon-effect-modifiers.json`; the Beyond effect registry rows are in `beyond-weapon-effect-rows.json`. That producer-to-effect link is a different mechanism from an inventory weapon's default equip slot. The config extraction is from a published source revision, while this section reads the installed 7.1 effect prefabs. The literal effect names connect them; source revision equivalence is not assumed.

The older `Maps/manekin-scene-index/scene-index.json` (SHA-256 `EAFBB0F7DDE243DB7A1CD612AEE4032B60F99A950CB1DCFF9CE885317CB249EC`) contains exact GameObject PathIDs for these prefabs, but its `05267696.blk` offsets no longer decode in the installed block. `AttachmentProbe/Program.cs` rescanned only that current 41,694,424-byte block (SHA-256 `83FF9B30E2446403313BBCC364057D6350DCDAA42EB58686866D71F3FB494BEF`) with `UseSelectedGenshinOffsets=false`: 1,416 CABs, all five exact GameObjects found with their original PathIDs at shifted offsets. The current mapping is `artifacts/weapon-validation/manekin-attachment-source/current-weapon-effect-block-scan.json`; the failed old-offset reports remain separately named `effect-source-*.json` and contain zero objects. Current exact CAB component and transform records are `effect-source-*-current.json`.

| Spawned effect | Current block offset | `MonoEffectPluginFollow` PathID | Literal follow-name field |
| --- | ---: | ---: | --- |
| `Beyd_Eff_Weapon_Sword_01` | `39543945` | `2015897693594994957` | `WeaponR` |
| `Beyd_Eff_Weapon_Sword_02` | `39545767` | `-4516763524565650132` | empty |
| `Beyd_Eff_Weapon_Sword_03` | `39547519` | `-6496556173383653151` | empty |
| `Beyd_Eff_Weapon_Claymore_02` | `39521832` | `-2936266341677615232` | empty |
| `Beyd_Eff_Weapon_Pole_01` | `39532921` | `-6193913556638445591` | empty |

Each prefab has `MonoEffect` and `MonoEffectPluginFollow` on its root, an identity-local-TRS `Root` and visible `Equip_*_Model` child, and a `DurationForMesh` particle child. In the Sword 01 Follow raw payload, byte 32 holds string length 7 and bytes 36–42 spell `WeaponR`. In each other Follow payload, byte 32 holds length 0. After the aligned name field, the remaining 276 bytes are identical across all five, SHA-256 `12C3305CEFFD7E37F79213961D9808FF30BBEF973D3B26E55FBDE7464289DE6D`. `effect-follow-summary.json` records each exact root/CAB, component PathID, raw hash, model name, name offset, and normalized trailing hash. `WeaponR` is a proven serialized label for Sword 01; the component's game behavior and the empty-name default for the other four still require runtime schema or capture evidence. The identity child transforms do not supply an attachment offset themselves.

## Diagnostic combat clip exports

Four exact installed 7.1 `Ingame` clips were exported as native Unity `.anim` for the Editor owner's socket playback test. The first isolated `ClipProbe/` conversion under `combat-clips/` loaded only the clip. Its transform paths remained `path_<hash>` placeholders, and the Editor owner's import found those paths unresolved. Those files are a recorded failed diagnostic and must not be used for socket playback.

The corrected `combat-clips-v2/` exports use the existing `--genshin-model` CLI with each exact Prototype Animator and one exact clip-name regex. This loads the source rig/Avatar context for `FindTOS` before native conversion. Each v2 directory contains a source manifest, a diagnostic FBX, and one `Animations/*.anim`. All four v2 clips have named `WeaponR/L` and `CatalystR/L` transform paths, zero `path_<hash>` placeholders, and zero recorded animation failures. None contains a `PRIVATE_WeaponRootPole` curve. The assigned Unity Editor owner imported the corrected clips at `Assets/_Games/Genshin/Manekins/Source/WeaponProbes/20260930T051724727Z` and reported moving canonical hand sockets with static `Root/CatalystR/L` paths in its playback check. Its package study owns the actual sample conditions and metrics. Weapon parent selection and rendered fit remain separate checks.

| Artifact under `artifacts/weapon-validation/manekin-attachment-source/combat-clips-v2/` | Source PathID | v2 `.anim` SHA-256 |
| --- | ---: | --- |
| `Boy-Sword-Attack01/` | `5096733456520565297` | `89402735F24098292616E88E11CCA1EF2ED0586FFC6F31FB566A812E489D5535` |
| `Boy-Catalyst-Attack01/` | `364005534194796014` | `5AA578B52781B36C3BC12A110941D5DC2905D2C530145486F86812C3AA62AF22` |
| `Girl-Sword-Attack01/` | `7647767202552722420` | `98F2247FAB3AFF192B8CC3D697245DD2FFF65E56DCE91AAB5FB5B98807748B43` |
| `Girl-Catalyst-Attack01/` | `5671604911575751338` | `349C8AE01FEE0CEB63BB5F6229B2B8E61231C0CED31E126586FB6B9C4F45FA3A` |

All four clips are from `blocks/00/15832467.blk`, but different CABs and offsets, recorded in the individual provenance files. This installed asset version must remain distinct from any published equip config revision used to interpret slot names. The local diagnostic toolchain used `dist/net10.0-windows/bin/AnimeStudio.dll` SHA-256 `EFF94B077BF9CE2D9DBEFF040DE2341645184F581218F76BCA191D6F253D3B9D`, `AnimeStudio.Utility.dll` SHA-256 `3C786D4F9014D0C3A051B8C5E0B2F846928B827F6D6619D4B31B800C795D6A00`, and `AnimeStudio.Ooz.dll` SHA-256 `C4BDB9DF0EF484E5E17EE1D32359769662055DA7460569CB10AA9F303D706D27`. The supplied executable is identified by these hashes, not assumed to match the current dirty checkout.

Reproduce binding extraction from the AnimeStudio root with `dotnet run --project artifacts/weapon-validation/manekin-attachment-source/AttachmentProbe/AttachmentProbe.csproj -c Release -- <installed-block-path> <offset> <prototype-Animator-PathID> <new-report.json>`, then run `DecodeBindings.ps1 <report.json> <new-bindings.json>`. Reproduce a playable clip with `dist/net10.0-windows/AnimeStudio.CLI.exe --genshin-model docs/Genshin/Maps/genshin-7.1.map 'Beyd_Avatar_<Boy|Girl>_Prototype_Manekin@<Animator-PathID>' <new-output-directory> '^Ani_Beyd_Avatar_<Boy|Girl>_Ingame_<Sword|Catalyst>_Attack_01$'`, substituting one exact body/class/name. The diagnostic FBXs, v1 failed clips, and scratch binaries are evidence artifacts, not game assets or Unity package code.

## Remaining discriminating checks

- Trace the game's consumer of the equip config and `MonoVisualEntityTool` binding for the active and stowed weapon, including any class-specific pose or scale applied after parenting. The one visual `WeaponBackPut` binding is a source pointer, not proof of the final stowed weapon transform.
- Compare source-to-FBX local basis and parent world transform before reconstructing the absent `PRIVATE_WeaponRootPole` on a disposable Unity preview. Keep duplicate `DiagnosticPart_*` child rigs out of socket lookup. The Editor owner has already imported and sampled ordinary and the four corrected combat clips; its package report owns those measured playback results.
- Verify a representative imported weapon at the chosen active/stow attachment during animation against an aligned game reference. This offline probe did not run an Editor import or visual comparison.
