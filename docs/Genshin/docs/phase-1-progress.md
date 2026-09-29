> Historical request-driven research. For the current My tools character workflow, see [character export](character-export.md). FBX animation baking was removed, and the new mannequin action is deferred.

# Phase 1 implementation and validation

Phase 1 acceptance is complete for the documented 7.0.0 reference cases. Production extraction was recorded in the [AnimeStudio fork PR](https://github.com/italonvcc/AnimeStudio/pull/1); the original study recorded its research, validation and instructions in `italonvcc/genshin_asset_study#1` (historical reference to the retired repository). This page now lives with the [Genshin study](../README.md) in AnimeStudio. Phase 2 has not started. Generated evidence stays in ignored `Export/` and `Maps/` under `docs/Genshin/`.

Production commits: `5e4f3da` (complete animation bake) and `c458969` (assembly/audio/VFX and final shader/cubemap fixes). Validation ran against these source changes before commit; generated assembly informational versions may therefore retain the preceding commit hash. The logs and final artifact paths below identify the tested results.

| Requirement | Validated result | Workflow and limits |
| --- | --- | --- |
| Wonderland Manekins | Two assemblies with independently supplied S0017/S0018 tops; one rig, 25 weighted meshes each; two opposite synthetic poses and rendered inspection pass | [Manekin export](manekin-export.md). Compatibility is checked; the full customization catalog is not reconstructed. |
| Character animation | Both source/combined `.anim` and FBX retained. Idle, run, attack, skill and burst pass Blender inspection with 100–105 locally moving bones, including body, limbs and fingers | [Animation export](animation-export.md). Complete humanoid FBX uses an optional installed, licensed Unity Editor; ordinary standalone FBX remains available with explicit omissions. |
| Named audio | 168 English Mona voice lines and 13 combat media variants exported as WEM and playable PCM WAV, with traceable path/event/bank/media identities | [Named audio](named-audio.md). Missing languages and five absent mapped English IDs remain explicit; no runtime Wwise mixing simulation. |
| Character VFX | 17 action-associated roots; 62 texture PNGs, six cubemap previews plus original HDR data, 61 materials, four meshes and four effect clips; zero selected-root, known-pointer or export failures | [VFX export](vfx-export.md). Raw unsupported components and undecoded event timing are preserved; particle playback/reconstruction is Phase 2. |
| Shader identity | Six real Mona material exports pass; all 61 VFX material references have verified shader names; file/path IDs and name provenance retained | Referenced Shader metadata only; no numeric-ID naming guesses or shader-program recreation. |

## Final validation

- GUI and CLI .NET 10 Windows builds pass. Existing compiler and NuGet audit warnings remain; GUI dialogs were compiled, while end-to-end validation used their shared services through CLI.
- 185 synthetic checks pass, covering shader footer bounds/ambiguity/editor variants, pointer resolution, animation codec and humanoid math, audio package/media/event graph boundaries, and assembly compatibility rejection.
- Five real Mona clips pass deterministic finite-curve/unit-quaternion checks. Six material exports pass through the existing CLI material export path.
- The Unity-assisted export checks authored 60 Hz timing, source stop times, humanoid body center/orientation and explicit secondary curves. Blender verifies actual changing local bones and coherent rendered poses. Meter scale yields approximately 1.722 m idle character height. Weapon/camera tracks outside the selected rig are reported.
- Both Manekin combinations retain weighted skinning under two opposite synthetic poses. S0018 is a texture/material variant of the standalone S0017-shaped top; exported diffuse hashes differ.
- Audio WAV headers, durations, sample rates and nonzero waveforms were checked. The event-to-bank-to-sound-media workflow was reproduced end to end with external wwiser and vgmstream.
- VFX validation decodes every PNG, checks each referenced output is nonempty, and checks material shader names. Zero unresolved pointers means closure of the implemented dependency traversal, not proof that raw unsupported particle/script fields contain no additional references.
- The original Mona regression export and installed client remain unchanged. No game assets, generated maps, machine paths or external-tool binaries are committed.

## Local acceptance artifacts

All paths below are relative to `docs/Genshin/` and intentionally ignored:

| Artifact | Purpose |
| --- | --- |
| `Export/mona-phase1-meters/` | Final Mona FBX, `.anim`, materials/textures, manifest and Unity bake evidence |
| `Export/mona-phase1-meters-check.json`, `Export/mona-phase1-meter-poses/` | Blender checks and five inspected poses |
| `Export/manekin-assembly-0017/`, `Export/manekin-assembly-0018/` | Two compatible assembled variations |
| Matching `Export/manekin-assembly-0017-check/`, `Export/manekin-assembly-0018-check/` | Skinning reports, posed images and inspection scenes |
| `Export/mona-voices/`, `Export/mona-combat-audio-v3/` | Named voice and combat WEM/WAV with provenance |
| `Export/mona-audio-reproduction/` | Reproduced typed event-to-media export pipeline |
| `Export/mona-vfx-accepted/`, `Export/mona-vfx-accepted-check.json` | Final per-action VFX export and decoded-file validation |
| `Export/material-phase1-verified/`, `Export/material-phase1-verified.log` | Final six-material shader check |
| `Export/animation-phase1-final.log`, `Export/phase1-final-regressions.log` | Real clip and synthetic acceptance logs |

Earlier investigation outputs are historical and are not substitutes for these final artifacts. See the workflow pages for exact commands, source identities, external-tool versions and limitations. Original decoding and rotation investigations remain in [initial findings](initial-findings.md) and [humanoid reference](humanoid-reference.md).
