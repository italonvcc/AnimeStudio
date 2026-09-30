# Mona InFloor effect source data (Genshin 7.1)

`Eff_Avatar_Girl_Catalyst_Mona_LiquidStrike_InFloor` is GameObject path ID
`592123511860584661` in `CAB-a873be2b850e3d26dfdbd0b37a359a8e`, inside
`StreamingAssets/AssetBundles/blocks/00/16076595.blk` at offset `1064554`.
The 28,836,226-byte source block's SHA-256 is
`90AA119D91305EC7566B63E5D1F95741995ABB55EB75EEE917B919F2DC114756`.
The exact source event name associates this effect with Mona, but the event's
spawn timing, attachment and `MonoEffect` behavior are not decoded. The source
serialized file says Unity `2017.4.30f1\n2`, format `RefactorTypeData`, and
omits the particle and renderer type trees.

The root has two active child GameObjects. `00_Drops` has local position
`(0, 0.25001526, 0.6)` and ParticleSystem path ID
`-2575742631642871789`; `00_Splash` has local position
`(0, 0.05000305, 0.6)` and ParticleSystem path ID
`8747702471478872686`. Both have one active particle renderer and a
two-slot material list with a null second slot. Drops uses
`Eff_Water_059_00` and Splash uses `Eff_Water_084_00`, both using
`miHoYo/Particles/Liquid_Common_New`. Their six texture dependencies are
exported as native `.astexture` payloads with source sampler metadata.

The particle binaries are 8,116 bytes each, with SHA-256 respectively
`8AED12A0DF097E05DCCC62B357E6D6BA5A564F082C87CE675AD3805D139C1ACC`
and `4FAC1F9DF8757EDAEB665A0C5020A422CDB467C0140840BA7797528D93947003`.
The standard-shaped fields decode against the pinned vanilla Unity 2017.4.30f1
release tree with strict source byte checkpoints. Both systems burst once at
time zero: Drops chooses 11–12 particles, Splash 28–30. Lifetime modes carry
1/0.85 seconds for Drops and 1/0.6 for Splash. Drops starts at 0.65–0.85
speed; Splash at 1.2–3. Shape source type values are 2 and 4 respectively;
the [Unity 2017.4 generated bindings](https://raw.githubusercontent.com/Unity-Technologies/UnityCsReference/2017.4/artifacts/generated/common/modules/ParticleSystem/ParticleSystemBindings.gen.cs)
identify these as Hemisphere (Drops) and Cone (Splash).
The material renderer stores eight UV outline points for each particle. Their
source V coordinate is flipped in the observed D3D12 capture. The Drops
polygon is a near octagon; Splash is irregular. This is not a four-vertex
quad.

Seven Genshin particle additions remain opaque: byte ranges `[32,36)`,
`[104,112)`, `[1144,1212)`, `[1368,1420)`, `[5056,5060)`, `[6180,6560)`,
and `[7676,8116)`, totaling 956 bytes. The last block has a gradient-like
structure, so it cannot be presumed visually inert. Renderer blocks
`[212,224)`, `[288,320)`, and `[380,396)` also remain opaque. Both original
raw files and all unknown bytes are preserved in the export. `MonoEffect`
is an enabled 184-byte component with no decoded payload.

The reference type tree is the vanilla Unity 2017.4.30f1 JSON from
[AssetRipper TypeTreeDumps at pinned commit 20ba06e](https://raw.githubusercontent.com/AssetRipper/TypeTreeDumps/20ba06e00e0625de0e49a502c6868d6cc86d0f0b/InfoJson/2017.4.30f1.json),
SHA-256 `38D77013DE60C22F7883EA1822B94666C40EEF4F83C444D1E40CAD8AE67394BE`.
It is a parsing baseline, not proof that all Genshin-specific semantics match
vanilla Unity. The local reference is under
`Assets/_Games/Genshin/Captures/Analysis/MonaEffects/schema-reference-20260929/`.

The bounded source export is
`Assets/_Games/Genshin/Effects/Exports/mona-infloor-reviewed-20260929T131546Z/`.
See [the exporter account](../Exporter/Genshin-VFX.md) for the JSON contract,
build, checks and current Unity import limit. The package's
[effect implementation plan](<I:/_Archive/ToNas/Unity Projects/Projects/Game_Asset_Study/Packages/com.caladan.shaders/Docs/Genshin/Effects-Implementation-Plan.md>)
and [liquid shader study](<I:/_Archive/ToNas/Unity Projects/Projects/Game_Asset_Study/Packages/com.caladan.shaders/Docs/Genshin/Liquid_Common_New.md>)
hold the Unity runtime and visual-validation account.
