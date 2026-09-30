# Character loading, storage and rest-pose fixes

Follow-up: [complete armature/rigid attachment restoration and simultaneous character import](character-batch-import.md) extends the earlier skin-only checks below and fixes standalone ACL clips that prevented Vesna prefab generation.

Validated on 2026-09-27 with the installed **7.1.0** client and **Unity 6000.6.0f1**. Production changes are committed as `0665cdc` in the user's AnimeStudio fork. Initial fixtures were generated from the working changes over `4422486`; the packaged GUI/CLI are rebuilt from the committed revision. Client data was read-only. All assets, maps, temporary Unity projects and detailed reports remain ignored under `Export/` and `Maps/`.

## Findings and changes

- GUI status/progress callbacks waited for the UI thread. They now coalesce into a 100 ms UI timer, with elapsed operation time and current stage. Loading is awaitable through construction of the asset views. Conflicting menu/browser actions are disabled while work runs; the main window stays enabled. Worker errors are collected for an end-of-operation dialog. The GUI logger now respects global log flags and updates status even with the console enabled. Legacy console Quick Edit is disabled so selecting console text cannot suspend worker writes.
- Asset Browser selections with explicit bundle offsets skip split-file directory scans and whole-container RAM estimates. Older selections without offsets retain those safeguards. Core parsing remains mostly sequential; these changes do not promise full CPU/GPU utilization or a measured speedup factor.
- VFX export rebuilt the 3.1-million-entry dependency index for every effect. It now builds one resolver and inspects the already-resolved subsets. Selection lookup is indexed once by name/type. Progress includes dependency passes, animation counts and VFX object counts. Character-local VFX manifests no longer undergo a redundant shared-resource rewrite.
- Vesna selected **258 effect roots plus five event assets**, exceeding the old 128-root guard. Explicit selections are no longer rejected by that count; dependency-resolution pass/bundle safeguards remain. A second failure involved identical CAB identities in installed and patched blocks. The loader now records each observed source/offset-to-CAB association, so a reused CAB resolves through qualified identity rather than path ID alone. Requested selections and resolved source identities remain in manifests.
- Model and VFX textures/materials stay in each character's folder. Only shared body animations are pooled, directly under `Generic/Animations/<body type>/Ani_...__<12-digit SHA-256>.anim`. Full hashes are checked before reuse; existing payloads and Unity `.meta` files are never replaced. Source IDs remain in manifests. There are no per-file hash directories.
- Serialized model transforms were not necessarily the mesh bind pose. The generated prefab also saved the temporary Avatar calibration pose. The exporter now reconstructs bone transforms from mesh inverse-bind matrices while retaining mesh placement, skin weights and inverse binds. Conflicting or unrepresentable constraints fail explicitly. The importer snapshots that model pose, builds the humanoid Avatar using source calibration, then restores the model pose before saving the prefab. This follows the relationship described by [Unity's Mesh.bindposes documentation](https://docs.unity.com/en-us/engine/6000.5/script-reference/unityengine/mesh/bindposes).

## Evidence

Names come from selected map entries; the following identities document those selections rather than inferring ownership from numeric IDs.

| Character | Animator PathID | CAB | Source block / offset | Map container |
| --- | --- | --- | --- | --- |
| Avatar_Girl_Sword_Vesna | -2416894827844816308 | CAB-cd6b5190afe31ba514cb4bc24b483f4b | 10453615.blk / 21784037 | 2134759154 |
| Avatar_Girl_Catalyst_Mona | -2810238947810998359 | CAB-10de5bcb3c6f39b943b3115ec9f3cccb | 00630208.blk / 20041775 | -68619104 |
| Avatar_Girl_Pole_Ineffa | -6887380846428120661 | CAB-e07329cef8170158c4ba17ea48d23acb | 00630208.blk / 50201709 | -320667469 |

Before correction, the prior Mona fixture had maximum unanimated vertex displacement of **0.17831 m in the FBX** and **1.43527 m in the prefab**, relative to mesh bind vertices. The revised FBX and prefab are tested with Animator disabled and no sampled animation:

| Character | Skin bone slots | Vertices | Maximum FBX/prefab rest-vertex error |
| --- | ---: | ---: | ---: |
| Mona | 129 | 16,181 | 3.949e-7 m |
| Vesna | 228 | 46,995 | 4.271e-7 m |
| Ineffa | 155 | 32,273 | 5.966e-7 m |

- GUI and CLI builds pass. **222 synthetic checks** and **15 GUI workflow/logging checks** pass, including 10,000 worker updates without a UI message pump, deferred error reporting, local texture/material retention, shared-file reuse and conflicting bind constraints.
- Mona: eight source clips, six actions, 5,315 bindings and **4,547,835 curve evaluations with zero error**. Thirty-six native playback poses have identical bone positions/rotations; maximum vertex rounding is 1.996e-6 m and scale difference 9.701e-6. Original clips and new exports use the corrected rig in this comparison. It does not independently prove every action on every character.
- All three packages import valid humanoid Avatars and two cached body/secondary compositions each. Mona and Vesna have zero missing transform curve paths. Ineffa reports six thumb-transform paths absent from its selected hierarchy; these remain explicit warnings, not invented bones or a claim of complete Ineffa motion. Its idle skinning passes. Character-local texture assignments are verified. Earlier full-library size/import validation remains in [shared asset validation](shared-asset-validation.md); the new exports intentionally limit animation samples to avoid repeating unchanged full-library decoding.
- Vesna: **258 VFX selections, five event assets, 328 dependency bundles, 10,953 exported objects**, zero failed/missing selections and zero unresolved graph references. Model + eight animation samples + VFX took **185.35 seconds** on this machine; cached-reference preparation is outside that timing. Mona and Ineffa's bounded model/animation exports took 17.10 and 21.17 seconds respectively. These are absolute timings, not controlled before/after performance comparisons.
- Vesna file/image validation decodes **253 PNGs** and finds **421 material JSONs**. All linked files are present and nonempty. Five materials reference loaded shaders whose names remain undecoded; the strict VFX checker reports those five errors. Their qualified identities/raw shader data are retained. Particle simulation, runtime attachment/timing and source event associations beyond exact strings remain unsupported or explicitly unverified. A successful export does not establish game-equivalent effects.

## Reproduction and outputs

From the research repository:

```powershell
dotnet run --no-restore --project tools/RegressionChecks
dotnet run --no-restore --project tools/GuiWorkflowChecks
dotnet run --no-restore --project tools/RegressionChecks -- --shared-character Maps/genshin-7.1.map 'Avatar_Girl_Sword_Vesna@-2416894827844816308' Export/<new-parent>/Vesna --vfx
python tools/check_vfx_export.py Export/<new-parent>/Vesna/VFX Export/<new-report>.json
```

The bounded harness selects Standby, RunCycle, Attack_01, ElementalArt, ElementalBurst and Show_01 when present, plus matching body clips. `--vfx` retains the entire character-scoped effect selection.

Copy finished character packages and their shared `Generic` folder into the same parent in a disposable Unity project's Assets. Copy `tools/UnityCharacterRestCheck.cs` into Assets/Editor, then run:

```powershell
unity run Export/<test-project> --timeout 240 -- -nographics -executeMethod UnityCharacterRestCheck.Run
```

For native playback, the project additionally contains Assets/Mona, corresponding untouched baseline clips in Assets/Original, and `tools/UnitySharedAnimationCheck.cs` in Assets/Editor. Execute `UnitySharedAnimationCheck.Run` and inspect the JSON results, not just the process exit status.

Current ignored evidence:

- `Export/character-fixes-v2/{Mona,Vesna,Ineffa}` and sibling `Generic` — bounded exports; Vesna includes the complete discovered VFX graph.
- `Export/character-fixes-unity-final/character-rest-check.json` and `shared-animation-check.json` — all three rest poses and Mona playback.
- `Export/character-fixes-vesna-vfx-check.json` — file/image checks and five undecoded shader names.
- `Export/AnimeStudio-CharacterFixes/AnimeStudio.GUI.exe` — rebuilt GUI.

Use a fresh export parent when switching from the old hashed resource layout. Existing exports and Unity projects were not migrated or overwritten. Copy the new character and Generic folders together; the character's generated importer handles its local materials/textures and idle prefab pose.
