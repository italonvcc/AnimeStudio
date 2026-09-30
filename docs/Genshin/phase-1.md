# Phase 1 requirements and acceptance

Current direction: simplify character extraction into **My tools**, remove FBX animation baking, and defer implementation of **Export all manequins** until character export is settled. This supersedes the earlier requirement to retain both animation formats. Historical 7.0 reference-case acceptance is retained in [phase-1-progress.md](phase-1-progress.md); current 7.1 workflow evidence lives in [character-workflow-validation.md](character-workflow-validation.md).

## Character workflow

Load a current GI map, select one character Animator in Asset Browser, select voice/VFX/native-animation checkboxes, click Export Character and choose a parent folder. No request files or additional dependency pickers. Material JSON obeys the existing export-materials setting.

Generate a Unity-ready model/rig package with textures and optional animation, materials, VFX and voice subfolders. Build the Avatar/controller/prefab automatically on Unity import. Prove native playback with idle, locomotion, attack, skill and burst samples, including changing body/finger bones, clip timing and finite posed geometry. Do not require a Unity executable to export.

## References and provenance

Refresh current-client reference data when GI maps load if map/client metadata changed. Never embed version-specific character IDs, offsets or reference-map paths. Retain source/container/qualified PathID, game version, tool revision, dependency links and missing data. Semantic audio names need evidence; numeric IDs alone are not names.

## Audio, VFX and shaders

Export evidenced voice paths in playable form with language, media ID and provider provenance. Report absent language/media files. Named combat-event extraction remains a research/CLI capability; the new checkbox requests voice clips only.

Export character VFX asset graphs, raw event/component data, textures, meshes, materials and shaders. Distinguish source event-string evidence from name-based candidates and unresolved references. Asset extraction does not establish faithful Unity particle/trail behavior or action timing; runtime reconstruction is Phase 2.

Resolve shader names from qualified Shader references and validated metadata. Keep original IDs and report unresolved/unnamed objects; never fabricate identity.

## Mannequins and Phase 2

Show the requested mannequin submenu and checkboxes with its export action disabled. Do not implement the new mannequin export yet. Earlier assembly research remains available as historical evidence.

Update 2026-09-28: the user has now provided the separate Unity project and authorized shader reconstruction there. Keep that implementation in its `com.caladan.stylizedgamekit` package; extraction research and production-exporter responsibilities remain unchanged. See [shader-study extraction findings](shader-study-extraction-2026-09-28.md). Disposable import/playback verification projects remain extraction acceptance tests.
