# Complete rest hierarchy and simultaneous character import

Validated on 2026-09-27 (local date), using Genshin **7.1.0**, production commit **4df89e3**, and **Unity 6000.6.3f1**. Fixtures were generated from the working changes before that commit. The installed client and the user's Unity project were read-only. Assets and detailed reports remain ignored under `Export/`.

## Findings

The previous rest test verified skin matrices and baked skinned vertices, but not the entire armature or rigid meshes. Unweighted bones retained serialized posed transforms. Constraining every mesh to its old world transform also left rigid attachments behind when their parents changed. The exporter now restores Avatar **default-pose** locals, then applies skin inverse-bind constraints. Only skinned renderer frames retain their original world placement. Rigid attachments follow corrected parents. Humanoid calibration remains separate; weights and inverse binds are unchanged.

Vesna's import report contained `Unexpected clip duration: Ani_Avatar_Girl_Sword_Vesna_Sit02Loop_Adjust`. Its identity is `CAB-6b70e1346c95a3457fb618898bad1f5a`, PathID `7014691780643675480`. The source interval is 0–1.6666667 s, with **241 curves and 6,840 bytes of ACL tracks**, but no separate database. The old `IsSet` check required both and silently exported no curves. The native decoder already supports standalone tracks. The managed caller now passes a null database for them, checking both track headers to reject a genuinely missing required database before native decoding. Duration validation remains strict.

The user's log also contained a compile error when a character importer referred to the shared importer class before the sibling script was available. Character scripts now serialize their own composition recipe without that compile-time dependency. They defer generation during Play-mode entry/playback and reschedule on return to Edit mode. Generated preview Animators use `AlwaysAnimate`, including without a visible camera.

Logs establish the missing-prefab and script-reference failures. They do **not** establish a unique cause of the user's stopped Mona playback; corrected playback is tested below.

## Acceptance

Qualified character identities are in [earlier fixture provenance](character-export-fixes.md#evidence). Both complete packages and Generic were copied before the first successful import. Tests do not manually invoke character importers.

| Check | Mona | Vesna |
| --- | ---: | ---: |
| Source clips, all nonempty and matching source duration | 546 | 578 |
| Cached body/secondary compositions | 225 | 226 |
| Missing transform curve paths | 0 | 0 |
| Rest vertices, in both FBX and prefab | 16,181 | 46,995 |
| Distinct skin bones | 109 | 179 |
| Other default-pose bones, FBX / prefab | 22 / 31 | 22 / 47 |
| Rigid mesh locals, FBX and prefab | 15 | 15 |

Both prefabs generate automatically. Skin matrix tolerance is 0.001; baked rest vertex tolerance is 0.0001 m. Other bones and attachments agree with independent source default-pose/transform fixtures within 0.001 m, 0.1 degrees, and 0.001 scale units. Rest checks disable Animators and sample no animation.

The test enters **real Play mode with domain reload**, checks the default Standby state, and observes motion through more than one loop. It then plays Attack_01 through its full duration. Standby changed over 17 sampled intervals for each character, reaching normalized time 1.270. Attack changed over 20 intervals for Mona and 27 for Vesna, reaching 1.472 and 1.088 respectively. Controller files remain unchanged across Play-mode entry/exit. Unity reports **one passing combined integration test**, with no unexpected animation errors. Runtime checks cover these two actions; all 1,124 source clips receive curve-presence and duration checks.

GUI and CLI .NET 10 builds pass; **236 synthetic checks** pass. New synthetic coverage includes rigid placement, unweighted default-pose bones, standalone ACL detection, and required-database rejection in both transform/scalar blocks.

## Reproduction

From `docs/Genshin/`, using fresh ignored output directories:

```powershell
dotnet run --no-restore --project tools/RegressionChecks
$env:DOTNET_gcServer='1'
dotnet run --no-restore --project tools/RegressionChecks -- --shared-character Maps/genshin-7.1.map 'Avatar_Girl_Catalyst_Mona@-2810238947810998359' Export/<packages>/Mona --full
dotnet run --no-restore --project tools/RegressionChecks -- --shared-character Maps/genshin-7.1.map 'Avatar_Girl_Sword_Vesna@-2416894827844816308' Export/<packages>/Vesna --full
dotnet run --no-restore --project tools/RegressionChecks -- --character-source Maps/genshin-7.1.map 'Avatar_Girl_Catalyst_Mona@-2810238947810998359' Export/<evidence>/mona.json
dotnet run --no-restore --project tools/RegressionChecks -- --character-source Maps/genshin-7.1.map 'Avatar_Girl_Sword_Vesna@-2416894827844816308' Export/<evidence>/vesna.json
unity projects new <test-project> --path Export --editor-version 6000.6.3f1 --template com.unity.template.3d
python tools/prepare_character_batch_check.py Export/<packages> Export/<test-project> Export/<evidence>/mona.json Export/<evidence>/vesna.json
unity test Export/<test-project> --mode EditMode --filter UnityCharacterBatchCheck --output Export/<results>.xml --timeout 600 -- -nographics
```

Ignored evidence: `Export/batch-final/`, `Export/batch-validated/Vesna/` (also exercises standalone-header validation), `Export/batch-diagnostics/`, `Export/character-batch-validation/`, and `Export/batch-unity-results.xml`. Packaged application: `Export/AnimeStudio-UnityFix/AnimeStudio.GUI.exe`.

Existing exports are not automatically rewritten. Re-export affected characters and replace generated Editor scripts as well as model/animation payloads. Copy character folders and Generic under the same Unity Assets parent, retaining one shared composition importer. VFX simulation, shader reconstruction, voices, and all-action runtime validation are outside this change.

## Auxiliary visibility and local Unity materials

Production commit **23f276e** disables EffectMesh and mesh GameObjects below the armature in both the imported FBX and generated prefab. It preserves their components, meshes, transforms and hierarchy, and keeps the armature active. Embedded FBX materials are copied into readable character-local `Materials/*.mat` assets, with source texture assignments, and remapped on the ModelImporter. Prefabs inherit the same references. Existing local materials are reused without overwriting shader, color or texture edits. Unity's built-in fallback for meshes with no source material is not treated as an embedded material. Previously generated Rig/PreviewMaterial files are not deleted.

`UnityCharacterDefaultsCheck` verifies against the export manifest that all 21 Mona and 26 Vesna meshes remain present; 16 objects per model/prefab (EffectMesh plus 15 bone-attached meshes) are inactive. It checks all actual FBX material remaps target the local Materials folder: five assets for Mona and six for Vesna. Forced model reimport plus another character import preserves material GUIDs, user-edited colors (1e-6 serialization tolerance) and material counts. This targeted test passes in Unity 6000.6.3. The existing complete rest/clip and real idle/attack playback test also passed with these importer changes. GUI/CLI builds pass.

The project-preparation helper copies both test classes. Use `--filter UnityCharacterDefaultsCheck` for the targeted test or `--filter UnityCharacter` for both. Updated importer templates were installed only in the ignored disposable project. Evidence: `Export/defaults-unity-results.xml`, `Export/defaults-unity.log`; application: `Export/AnimeStudio-MaterialsFix/AnimeStudio.GUI.exe`. Existing packages need the new generated Editor script to get these Unity import defaults.
