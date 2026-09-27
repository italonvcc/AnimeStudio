# Agent instructions for the personal AnimeStudio fork

## Repository policy
- This is `italonvcc/AnimeStudio`. Develop on focused `codex/<topic>` branches and open PRs against this fork's default branch. Never open PRs or push changes to Escartem/AnimeStudio unless the user explicitly changes this policy.
- Preserve the upstream remote and history. Keep fork-specific changes small and isolated so future upstream improvements can be integrated on a separate branch and regression-checked.
- Inspect Git status before editing; preserve unrelated changes. Stage explicit source paths, never exported game data.

## Responsibilities and current scope
- AnimeStudio owns production extraction, file parsing, dependency resolution, model/animation/material/audio/VFX export, and the GUI/CLI controls for them.
- Put research notes, long-form project documentation, external tools, and analysis scripts in sibling `genshin_asset_study`. Consult its README.md and docs/phase-1.md for scope and acceptance criteria.
- Phase 1: Wonderland Manekin parts; complete character animation; named character voices/combat sounds; per-character VFX; resolved shader identity in materials.
- Retain both animation export formats: Unity `.anim` with source humanoid curves and FBX with rig animation. Improvements to either format must not remove the other.
- Surface implemented game-specific export actions based on the selected game (Genshin first), while retaining shared behavior for other games. Do not present unfinished research as a working exporter.
- Phase 2 Unity scenes/shader recreation are deferred until Phase 1 is complete and the user supplies the Unity project.

## Implementation and validation
- Treat the installed game client as read-only. Use the study repo's ignored Export/ and Maps/ for output and local reference data. Never overwrite the supplied Mona regression export.
- Preserve source/container/path IDs, skeleton/bind pose, animation timing/root motion, event/language identity, and material/shader dependencies. Report missing information instead of guessing names or silently claiming full export.
- Keep parsing in AnimeStudio, conversion in AnimeStudio.Utility, and GUI orchestration in AnimeStudio.GUI. Reuse the same conversion logic in CLI and GUI where possible.
- Windows GUI build: `dotnet build AnimeStudio.GUI/AnimeStudio.GUI.csproj -f net10.0-windows`. Build the CLI too if shared export behavior changes. Native libraries are prebuilt; rebuild only for native changes.
- Validate targeted changes with synthetic checks and bounded real asset samples. A successful build does not establish correct humanoid motion, Manekin assembly, semantic audio mapping, or complete VFX behavior.
