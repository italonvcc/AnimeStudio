# Humanoid animation reference checks

Phase 1 investigation, client 7.0.0. Source inspection/export baseline: AnimeStudio `5cf1d0f`; tested rotation primitive: `b3ed653`. Both `.anim` and FBX remain required export options. The production rotation primitive is in AnimeStudio; these scripts only prepare and sample an independent Unity reference from existing exports. No Phase 2 scene, shader, or user Unity project is involved.

## Results

The five Mona `.anim` exports generated after the ACL database fix import in Unity 6000.6.0f1. Attack_01, ElementalArt, and ElementalBurst report `isHumanMotion = true`; five sampled poses show motion in 46 mapped humanoid bones, including hips. RunCycle and Standby report false and contain only MotionT/MotionQ Animator bindings plus secondary-bone Transform bindings. All 58 distinct Transform paths in those two clips resolve in the Mona Avatar; none target a mapped body joint.

The map also contains actual AnimationClip objects named `Ani_Avatar_Girl_RunCycle` (PathID `-2545555828280571326`) and `Ani_Avatar_Girl_Standby` (`-2563298973179742343`). Each has 139 Animator bindings and 17 compressed Transform tracks. Their sample counts are 42 and 122 at 60 Hz, matching the corresponding Mona secondary clips. This supports a shared-body/character-secondary layering hypothesis. The runtime controller link, combination order, root settings, and composition remain unverified; the exporter must not automatically associate them based only on matching names or duration. The same names also identify TextAssets, so always retain object type and serialized-file identity. Local `girl-body-locomotion.json` records qualified identities and source paths.

The source Avatar's `m_AvatarSkeletonPose` reconstructs the humanoid reference correctly: source and Unity-rebuilt human scale are both approximately 0.927202165. Using `m_DefaultPose` instead gives approximately 0.875076890 and different joint axes. The latter is the bind/default pose, not the humanoid reference pose. Hips maps to `Bip001`, with `Bip001 Pelvis` as an intermediate bone. The four non-thumb fingers on each hand use humanoid muscles; thumbs in the tested clips use explicit Transform tracks.

`HumanoidRotationMath` in AnimeStudio now implements a standalone joint-rotation primitive using source Avatar pre/post quaternions, signs, radian limits, and twist weight. For signed axis angles `(x,y,z)`, swing is the normalized quaternion `(0, tan(y/2), tan(z/2), 1)`; twist is the X-axis rotation. Pre/post basis changes surround their product. Remaining twist is expressed in the following joint's parent space. Intermediate bones still require a basis conversion by the eventual caller. Muscle overshoot is preserved, not clamped. This formula was measured against Unity; an exponential-map swing candidate produced errors of several degrees and was rejected.

Validation: 675 comparisons (45 non-hips mapped joints × 5 poses × 3 humanoid clips) use **source** Avatar axes and imported source curve values. Maximum angular error: **0.000043 degrees**, with a 0.01-degree failure threshold. All 168 synthetic checks also pass. The sample is one explicitly selected Mona Avatar variant and three clips, not a general retargeter acceptance result.

The primitive is deliberately not connected to FBX yet. Hips orientation/translation depend on body orientation and center of mass, not a single muscle rotation. Root-motion extraction settings, intermediate bones, shared clip composition, and optional IK remain work. The current FBX exporter still reports skipped humanoid curves; `.anim` remains the working preservation path. Moving local joints alone does not satisfy complete-animation acceptance.

## Reproduce

Use an ignored scratch project and already-exported clips. Requires Python, .NET 10, an installed/licensed Unity Editor, and Unity CLI. The production primitive itself has no Unity dependency. The scratch builder currently uses Unity's default muscle limits; source/custom-limit incompatibility is detected by the source-axis comparison, not silently accepted.

```powershell
# Query bounded shared-body candidates through AnimeStudio's parser.
dotnet run --project tools/AssetStudy -- Maps/genshin-asset-map.map '^Ani_Avatar_Girl_(RunCycle|Standby)$' Export/girl-body-locomotion.json --inspect

# Create a local scratch project; no cloud project or interactive editor is needed.
unity projects new HumanoidReference --path Export --editor-version 6000.6.0f1 --template com.unity.template.3d --format json
python tools/unity_reference/prepare_reference.py Export/mona-resolved.json Avatar_Girl_Catalyst_Mona_ModelAvatar Export/mona-phase1-decoded/Animations Export/HumanoidReference --avatar-index 0
unity run Export/HumanoidReference --editor-version 6000.6.0f1 --timeout 180 --format json -- -nographics -executeMethod HumanoidReference.Run

dotnet run --project tools/RegressionChecks -- --humanoid-rotations Export/mona-resolved.json Avatar_Girl_Catalyst_Mona_ModelAvatar Export/HumanoidReference/Assets/ReferenceInput/rig.json Export/HumanoidReference/reference-result.json
```

`mona-resolved.json` is the existing dependency-resolution report; regenerated reports now include each Avatar's source, CAB and PathID. The legacy report has two different Avatars with the same name, so the preparation command requires an explicit occurrence index. It records that selection and a SHA-256 of the report; the Unity result records the rig input hash. The checker rejects mismatched inputs. Index 0 is the variant measured above; it must not be assumed to match another Animator without checking its PPtr.

Preparation refuses an existing input directory unless `--replace-input` is supplied, and refuses a changed clip set with leftover `.anim` files. Use a new project for a different set. Unity samples at source-frame-aligned times with clip looping and foot/playable IK disabled, and writes `reference-result.json`. The report includes body transforms for research, but the current regression assertion covers **only local joint rotations**: accumulated Animator root motion and scene placement are not a root-motion oracle. Source game data and original exports are not modified.

## References

[Unity's Mecanim explanation](https://unity.com/blog/engine-platform/mecanim-humanoids) describes muscle references, twist distribution, body center of mass, average body orientation, and optional IK. [ShaderMotion's HumanAxes](https://github.com/CuteWaterBeary/ShaderMotion/blob/github/Script/Common/HumanAxes.cs) was consulted for the pre/post-axis relation; its exponential-map swing is not the formula that matched these Unity samples. No implementation was copied from either source.
