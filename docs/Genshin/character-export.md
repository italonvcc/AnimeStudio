# Character export

The supported GUI workflow is **My tools > Genshin character**. Load a current GI asset map in Asset Browser, select exactly one character Animator, check the desired options, click **Export Character**, and select a parent folder. There are no request files, manual dependency selections, supplementary map pickers, or Unity executable prompts.

The menu is immediately before About. Its checkboxes stay open for multiple selections. Other games retain their normal export behavior. The mannequin submenu is a disabled placeholder by request.

## Output

The character folder contains:

- Character FBX containing the model in its mesh bind pose, rig, material slots, and texture links.
- `Textures/` with model PNG textures, including common textures used by this character.
- `Materials/` with full source material JSON when the existing export-materials setting is enabled, including evidenced shader names.
- `Animations/` with character-specific native Unity `.anim` files when requested, and an index of reused body clips in `Generic/Animations/<body type>/`.
- `VFX/` with effect hierarchies, decoded textures, meshes, raw components/shaders, and dependency manifests when requested. Its material JSON also follows the existing setting.
- `Voices/` with semantic language/path names, original WEM media, decoded WAV, and provenance when requested.
- `Editor/` and a character recipe for automatic Unity prefab/Avatar/controller construction, plus export manifests.

The sibling `Generic/Animations/<body type>/` contains readable animation filenames, such as `Ani_Avatar_Girl_RunCycle__<12-digit hash>.anim`. There are no per-file hash directories. The suffix distinguishes different bytes with the same animation name; the full SHA-256 is verified before reuse, and existing Unity `.meta` files stay intact. Original source/container/path identities remain in manifests. Generic placement follows shared body action names, not numeric IDs.

All model and VFX textures/materials stay in the character tree, even when another character uses identical resources. Unity creates preview materials under `Rig/` and binds character-local textures explicitly. Earlier exports are left untouched; export into a fresh parent folder to use the new layout.

Do not reuse an existing character output folder; select another parent or move your earlier result. Failed partial exports are marked `EXPORT-INCOMPLETE.txt` and never reported as complete.

## Unity use

Copy the character folder **and `Generic/`** into the same parent in `Assets`, preserving their relative layout. Include `Generic/Editor` only once per Unity project; export subsequent characters to the same parent and merge into the existing shared library, retaining `.meta` files. Allow script compilation and import to finish. A generated character prefab and `Rig/Character.controller` are ready to use. The controller has one state per source selection; it defaults to composed standby when available. Drop the prefab into a scene and choose the desired states in Unity.

Humanoid `.anim` files require a matching humanoid Avatar. The included importer reconstructs it from the selected source Animator rather than relying on Unity's automatic FBX bone mapping. Some source actions split body muscles and secondary transform curves between two clips. Small `Rig/*.genshinclip` recipes reference uniquely matching pairs with identical timing/rate; Unity generates their combined clips in its import cache. The controller uses those imported native clips without storing extra merged `.anim` files in the source tree. No frame-by-frame bone baking is involved.

The FBX bind pose is reconstructed from skin matrices without changing weights or inverse binds. Avatar calibration temporarily uses the source reference pose; the importer restores the model pose before saving the prefab.

The importer produces `unity-import-report.json`. It validates the Avatar, human scale, clip durations, and transform curve paths. This has been tested in Unity 6000.6.0f1; other Editor versions are not yet validated.

Materials initially use FBX-compatible preview rendering. Recreating Genshin's shaders, particle simulation, attachment logic and action/audio synchronization remains Phase 2. The VFX folder is an asset/dependency export, not automatically recreated Unity effects.

## Reference refresh and voice tools

Every primary GI map load calculates a fingerprint from map bytes, client version and source-file metadata. Changed fingerprints trigger a bounded scan of current client headers for supplemental references. Unchanged fingerprints reuse the cache beside the map under `.genshin-references/`. No fixed character PathIDs, offsets, game-version map files, or local install paths are compiled into the workflow.

Voice-name provider files are obtained from the current AnimeWwise revision when a cache is created. That revision is recorded and reused within the cache; it is not continuously polled. If offline, model export remains available and voice export retries preparation. The first voice export downloads vgmstream and requires Python on PATH. Provider licenses and versions stay with their external cache files. Missing language packs or absent mapped media remain missing entries, not invented or mislabeled files.

VFX candidate selection uses selected-character prefab names and source event strings. Exact event-string matches and name-only candidates are distinguished in output evidence. This does not prove every shared or dynamically spawned effect belongs to a character. Costume/alternate Animator naming may expose a different subset; missing animation or VFX candidates fail explicitly.

## Reproducible CLI equivalent

```powershell
AnimeStudio.CLI --genshin-character Maps/client.map 'Avatar_Girl_Catalyst_Mona@<current-path-id>' Export/new-character --animations --voices --vfx
```

Use an ID from the currently loaded map only to distinguish same-named Animators. Omit it if the name is unique. `--no-materials` corresponds to clearing the existing GUI materials setting. The CLI calls the same production service as My tools.
