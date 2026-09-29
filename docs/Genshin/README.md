# Genshin asset study

Genshin asset research and validation within [italonvcc/AnimeStudio](https://github.com/italonvcc/AnimeStudio). Production extraction lives in the application projects; this directory holds asset findings, analysis tools, external-tool integration, and reproducible checks. Run the relative commands below from `docs/Genshin/`.

This is an ordinary folder in the AnimeStudio repository, migrated from the former `genshin_asset_study` repository. The former checkout retains only Git metadata and configuration for decommissioning; `docs/Genshin/` is not a nested checkout. Local maps, exports, captures, and generated reports moved to the ignored `Maps/` and `Export/` folders here. Genshin asset findings live here; full exporter change records and Unity import lessons live in the parent [docs/](../) directory.

## Export a character

1. In the updated AnimeStudio build, select **GI**.
2. Open **Asset Browser** and load a fresh asset map for the installed client. Wait for Genshin reference preparation to finish.
3. Search for the character and select **one Animator** row. You do not need **Load Selected**.
4. Open **My tools > Genshin character** and choose **Export Voice clips**, **Export VFX**, and/or **Export unity animations**.
5. Click **Export Character**, then select an output parent folder.

The exporter creates a folder named after the selected character and a sibling `Generic/` shared animation library. Model and VFX textures/materials stay inside each character folder. Named shared body animations sit directly in `Generic/Animations/<body type>/`. Native animation text is compacted and wholly constant curves retain only their endpoints; moving curves keep every key. Material JSON follows the existing **Export materials** export setting. There is no separate material option in My tools.

Copy the character folder **and its sibling `Generic/` folder** into the same parent under Unity's `Assets`. Use one shared `Generic/` library per project and keep existing `.meta` files when adding characters. The included Editor scripts create a humanoid Avatar, controller, and character prefab automatically. Paired actions use small `.genshinclip` recipes; Unity builds their compositions in its import cache, without extra merged `.anim` source files. Native animations remain editable; there is no FBX animation bake or Unity executable picker. Shader recreation and game-equivalent VFX playback remain Phase 2.

**My tools > Genshin manequin** contains the requested three checkboxes and a disabled **Export all manequins** action. Its implementation is deferred until the character workflow is settled.

On Unity import, EffectMesh and mesh GameObjects attached beneath armature bones (such as AO_Bip001 Spine) start inactive in both the FBX model and generated prefab. They remain present and can be enabled in the hierarchy. Embedded FBX materials become named `.mat` files in the character's `Materials/` folder; the FBX and prefab reference these editable local assets. Existing local materials and their edits are preserved on reimport.

See [character export details](docs/character-export.md), [native animations](docs/animation-export.md), [workflow validation](docs/character-workflow-validation.md), and [loading, Vesna and bind-pose fixes](docs/character-export-fixes.md). Earlier 7.0 request-driven experiments are retained as historical research, not the current GUI instructions.

Character export uses bounded parallel workers automatically. On the measured 16-core machine, Mona's complete 546-clip export fell from 103 to 36 seconds; Vesna's sampled-animation plus full-VFX export fell from 109 to 58 seconds, with unchanged payloads. See [performance measurements and reproduction](docs/character-export-performance.md).

Asset Browser **Load Selected** now loads the selected GI model's dependency graph and releases the previous view without blocking file cleanup. The measured second-character load fell from 52 to 2.5 seconds; see [repeated-loading validation](docs/repeated-character-loading.md). **Export materials** is enabled by default, while saved explicit choices remain respected.

Character export references are prepared when a GI map is loaded even if the app starts in Normal mode, and remain available when Asset Browser closes. Disabled export commands explain the missing requirement in the console; see [export availability checks](docs/repeated-character-loading.md#export-command-availability-after-loading).

## References and updates

The [2026-09-28 shader-study extraction findings](docs/shader-study-extraction-2026-09-28.md) cover Dynamic Character Resolution versus exported topology, authored tangents, material slots, bounded GI 7.1 shader parsing, native 2D/cubemap companions, and remaining limitations. This directory is the canonical home for Genshin asset research; it may summarize relevant exporter changes, while full exporter implementation details and Unity import lessons live in the main AnimeStudio [docs/](../) directory. The separate `com.caladan.shaders` package owns shader implementation, actionable Unity setup, Editor tool docs, and shader validation. Current rendering defects remain open; shader-specific reasoning and validation stay with that package.

The latest [batch-import validation](docs/character-batch-import.md) covers Mona and Vesna together: complete armature and rigid attachment rest poses, all 1,124 source clips, automatic prefabs, and actual idle/attack playback in Unity 6000.6.3. Re-export existing packages to obtain these fixes, including their generated Editor scripts.

Rebuild the asset map after a game update: old bundle offsets may no longer identify the same objects. Loading a GI map prepares current-client model, mesh, shader, event, and VFX references automatically. A cache keyed by map content, source file sizes/timestamps, and installed version is reused when unchanged.

Voice names come from the external AnimeWwise provider; WAV decoding uses vgmstream. Naming data is refreshed for a new map/client cache, and the decoder is acquired when voices are requested. First preparation needs internet access. Voice export also needs `python` on PATH. Tool versions, provenance, and provider licenses remain in the ignored map cache. Missing installed language/media files are recorded in the voice manifest.

## RenderDoc captures

The [custom RenderDoc 1.45 package and instructions](tools/renderdoc/README.md) provide the separate Windows x64 build that the user confirmed can launch Genshin and take frame captures. Download the [ZIP](tools/renderdoc/renderdoc-1.45-genshin-win64.zip), extract it completely, and follow the guide. The package includes the original licenses and a file checksum manifest; the guide documents the exact tool modification and validation limits. Save captures under this directory's ignored `Export/`.

## Workspace

| Location | Purpose |
| --- | --- |
| `../../` | AnimeStudio repository root: parsers, dependency resolution, GUI/CLI exports |
| `tools/` | Analysis and validation, calling fork libraries |
| `docs/` | Scope, evidence, reproduction steps and limitations |
| `Maps/` | Ignored asset maps and derived reference caches |
| `Export/` | Ignored samples, test projects, reports and output |
| Installed client | Read-only input |

Never commit game assets, maps, captures, generated exports, or installation paths. Preserve the original Mona sample. The current validation client is 7.1.0; earlier 7.0 evidence is explicitly historical.

## Development

Use focused `codex/` branches and PRs targeting **italonvcc/AnimeStudio**, never upstream. Keep upstream history intact and fork-specific changes isolated. Extraction belongs in AnimeStudio; do not build a second extractor here.

```powershell
# From the AnimeStudio repository
 dotnet build AnimeStudio.GUI/AnimeStudio.GUI.csproj -f net10.0-windows
 dotnet build AnimeStudio.CLI/AnimeStudio.CLI.csproj -f net10.0-windows
 Set-Location docs/Genshin
 dotnet run --project tools/RegressionChecks
 dotnet run --project tools/GuiWorkflowChecks
 dotnet run --project tools/AssetStudy -- Maps/client.map 'Mona|Manekin' Export/discovery.json
```

AssetStudy accepts `--inspect` or `--resolve` after the report path for bounded source inspection. Queries match names, containers and decimal PathIDs. Names alone are discovery evidence, not proof of runtime ownership. Existing reports are not overwritten.

## Scope

[Phase 1 requirements](docs/phase-1.md) cover character models, native Unity animations, named voices/combat audio, VFX dependencies, shader identity, and later mannequin assembly. The latest user direction removes humanoid FBX baking and postpones the new mannequin exporter. The user supplied the separate Unity project for character-shader reconstruction; that work now lives in its `com.caladan.shaders` package. This directory continues to document extraction and asset research. A Unity-to-Blender workflow and other unrequested Phase 2 work are not implied by that shader task.
