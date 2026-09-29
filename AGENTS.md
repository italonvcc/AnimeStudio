# Agent instructions for the personal AnimeStudio fork

## Repository policy
- This is `italonvcc/AnimeStudio`. Develop on focused `codex/<topic>` branches and open PRs against this fork's default branch. Never open PRs or push changes to Escartem/AnimeStudio unless the user explicitly changes this policy.
- Preserve the upstream remote and history. Keep fork-specific changes small and isolated so future upstream improvements can be integrated on a separate branch and regression-checked.
- Inspect Git status before editing; preserve unrelated changes. Stage explicit source paths, never exported game data.

## Responsibilities and current scope
- AnimeStudio owns production extraction, file parsing, dependency resolution, model/animation/material/audio/VFX export, and the GUI/CLI controls for them.
- Keep canonical technical documentation for AnimeStudio changes in this repository: what changed, why it changed, source-to-export-to-Unity diagnosis, provenance, validation, and lessons about importing exported assets into Unity. General Genshin asset knowledge, analysis scripts, and external research tools live under [docs/Genshin/](docs/Genshin/); they may summarize an AnimeStudio change and link to its full technical account here.
- Phase 1: Wonderland Manekin parts; complete character animation; named character voices/combat sounds; per-character VFX; resolved shader identity in materials.
- Export native Unity `.anim` with source Avatar metadata and a model FBX. The latest user direction removes humanoid FBX baking and defers the new mannequin exporter.
- Surface implemented game-specific export actions based on the selected game (Genshin first), while retaining shared behavior for other games. Do not present unfinished research as a working exporter.
- Update 2026-09-28: the user has supplied a Unity project and authorized shader work in the separate `italonvcc/com.caladan.shaders` repository. That repository owns the usable Unity setup, Editor tools, shader documentation, and shader agent rules; it is distinct from the local Unity project. Keep exporter fixes and their technical rationale here. Genshin-specific asset findings belong under `docs/Genshin/`, with concise cross-links where useful. Do not duplicate full technical documents.
- Detailed exporter patch documentation is kept here in [docs/shader-study-exporter-changes-2026-09-28.md](docs/shader-study-exporter-changes-2026-09-28.md). Preserve the uncommitted implementation and read its validation limits before continuing.

## Implementation and validation
- Treat the installed game client as read-only. Use ignored `docs/Genshin/Export/` and `docs/Genshin/Maps/` for study output and local reference data. Never overwrite the supplied Mona regression export.
- Preserve source/container/path IDs, skeleton/bind pose, animation timing/root motion, event/language identity, and material/shader dependencies. Report missing information instead of guessing names or silently claiming full export.
- Keep parsing in AnimeStudio, conversion in AnimeStudio.Utility, and GUI orchestration in AnimeStudio.GUI. Reuse the same conversion logic in CLI and GUI where possible.
- Windows GUI build: `dotnet build AnimeStudio.GUI/AnimeStudio.GUI.csproj -f net10.0-windows`. Build the CLI too if shared export behavior changes. Native libraries are prebuilt; rebuild only for native changes.
- Validate targeted changes with synthetic checks and bounded real asset samples. A successful build does not establish correct humanoid motion, Manekin assembly, semantic audio mapping, or complete VFX behavior.
