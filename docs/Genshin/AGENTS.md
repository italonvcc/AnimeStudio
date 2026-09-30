# Agent instructions

## Ownership and scope
- This directory in `italonvcc/AnimeStudio` is the canonical home for knowledge about Genshin assets: their observed semantics, formats, source relationships, shader identity metadata, and research evidence. It owns the Genshin asset-research analysis scripts and external-tool integration. Keep concise findings here.
- Production parsing and extraction belong in this repository's application and utility projects. Do not create a competing extractor in this study directory. Analysis tools may call the application libraries. This directory may summarize AnimeStudio changes that matter to Genshin research, but link to the full technical account in the main [docs/](../) directory for the change, rationale, and lessons about Unity import behavior; do not duplicate those narratives here.
- The separate `italonvcc/com.caladan.StylizedGameKit` package owns actionable setup for correct rendering in Unity, Editor tool documentation, detailed shader behavior and reconstruction, implementation, and rendering validation. Link concise summaries here as needed. Each repository follows its own applicable agent instructions.
- Treat the installed Genshin client as read-only input. Keep generated files in this directory's ignored `Export/` and maps in ignored `Maps/`.
- Do not commit game assets, captures, maps, generated exports, local install paths, or large binary samples. Commit reproducible scripts and concise findings instead.

## Project priorities
- Read README.md and docs/phase-1.md before work. Phase 1 covers Wonderland Manekins, full character animation, named voices/combat audio, character VFX, and shader identity in materials.
- Update 2026-09-28: the user has supplied a Unity project and authorized shader reconstruction in its separate `com.caladan.stylizedgamekit` repository. This directory still owns Genshin asset research, while the package owns Unity shader implementation. Read [docs/shader-study-extraction-2026-09-28.md](docs/shader-study-extraction-2026-09-28.md) for discoveries, exporter links and unresolved limits.
- Export native Unity `.anim` with the source Avatar/model package. The latest user direction removes humanoid FBX baking and defers the new mannequin exporter.
- Keep evidence distinct from hypotheses. Never assign a character or shader name from a numeric ID alone. Record game version, tool commit, source/container/path ID, and missing dependencies.

## Workflow
- Inspect Git status from the AnimeStudio repository root and existing work before changes. Use focused `codex/` branches and PRs for meaningful changes; never push assets with a blanket git add.
- AnimeStudio branches and PRs target the user's fork only, never upstream. Preserve upstream compatibility with small, isolated changes.
- Run analysis on bounded asset subsets first. Log commands, results, limitations, and acceptance checks in docs/.
- Test analysis logic with synthetic fixtures where practical. Successful decoding does not prove correct rig motion, audio attribution, or complete VFX reconstruction.
