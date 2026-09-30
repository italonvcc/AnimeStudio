# Repeated character loading

Validated on 2026-09-27 against the read-only 7.1.0 client. Baseline production revision: `bff1a35`; fix: `4a5c9d0f4c1f6dfb6939f3b9b3f2c571c19b2a98`. Tests use the same Ryzen 9 9950X3D / 125.6 GiB machine as the [export benchmarks](character-export-performance.md). Client data, logs and reports remain ignored.

## Cause and implementation

Asset Browser called `updateGame`, which synchronously reset the previous view, then called `LoadPathsAsync`, which reset it again. The first reset happened before operation timing/disabled controls were established. Broad block/dependency loading produced a scene tree of about 150,000 nodes even though the asset list showed one selected Animator. Clearing that tree on the UI thread reproduced the reported freeze.

`LoadSelectedPathsAsync` now owns one guarded operation. It detaches the old view with batched TreeView/ListView updates, clears tree-search/dump references, closes asset/resource streams and assemblies on a worker, applies the game selection, then loads the next model. Preview/asset controls are disabled until completion. Explicit cleanup/read stages feed the existing status timer and console. Selection filters are replaced rather than accidentally retained when absent.

Bounded GI Animator/GameObject selections with dependency resolution enabled use the existing object dependency resolver and build the scene tree from its resulting graph. The selected model's rig, meshes, materials and textures are retained; controllers do not pull in the whole animation library. Missing qualified references remain warnings. Ancestors outside the selected graph do not hide the model under an unattached tree node. Existing non-model/non-GI loading uses its usual path.

The core block loader previously used explicit map offsets only for Endfield; GI browser loads could scan every bundle in a block. Browser model loading now opts into `UseSelectedGenshinOffsets`, positioning the stream before header detection. This temporary flag and the dependency-resolution option are restored afterward, including failures. The flag defaults to false for existing export callers. A global-offset experiment changed VFX source selection and was deliberately not shipped; the final mode is restricted to browsing. Export discovery stays at its previously validated setting.

**Export materials** defaults to true in the settings definition, generated default attribute and application configuration. Saved explicit user choices remain respected; resetting options uses the new enabled default.

## Measured GUI sequence

`LoadCycleChecks` instantiates the actual MainForm and native tree/list handles without showing a window, runs a WinForms message pump, and invokes the same loader as Asset Browser. A 25 ms timer records UI scheduling gaps. The test performs three loads in one process, verifies the selected Animator replaces the previous list entry, checks that a hierarchy exists, and checks temporary options are restored. Map deserialization/startup are outside these timings. Filesystem caches are not flushed; these are individual runs, not timing guarantees.

| Load | Baseline total | Fixed total | Baseline synchronous start | Fixed synchronous start |
| --- | ---: | ---: | ---: | ---: |
| Mona first | 4.15 s | 2.04 s | 11 ms | 4 ms |
| Vesna second | 51.94 s | 2.46 s | 49.15 s | 8 ms |
| Mona again | 48.02 s | 1.91 s | 40.93 s | 7 ms |

The longest observed UI timer gap fell from **49.15 seconds to 142 milliseconds** across the sequence. Mona's view goes from 151,072 tree nodes to 178, loading 22 CAB files / 607 objects. Vesna's view goes from 149,455 nodes to 249, loading 57 CAB files / 871 objects. Both model graphs report zero unresolved references in these runs. Qualified selectors and source provenance match [the earlier character fixtures](character-export-fixes.md#evidence).

## Acceptance and reproduction

- 231 synthetic checks and 21 GUI checks pass, including a deliberately blocked stream close that must leave its UI caller free, resource release before reuse, the opt-in loader default and the material default.
- The three-load real-client GUI sequence passes selection, hierarchy and option-restoration checks. GUI .NET 9 and GUI/CLI .NET 10 builds pass.
- The final Vesna model/eight-clip/full-VFX package passes comparison with the prior performance export: 21,271 identical files, two metadata files differing only in tool revision, equivalent generated importer code, all eight source clips identical, and no missing/extra/changed payloads. This protects the exporter from the browser-specific change. Timestamped FBX, timing reports, tool revision and generated importer ID follow the checker's existing exclusions.

```powershell
dotnet run --no-restore --project tools/GuiWorkflowChecks
dotnet run --no-restore --project tools/RegressionChecks
dotnet run --no-restore --project tools/GuiWorkflowChecks -- --load-cycle Maps/genshin-7.1.map Export/<new-report>.json 'Avatar_Girl_Catalyst_Mona@-2810238947810998359' 'Avatar_Girl_Sword_Vesna@-2416894827844816308' 'Avatar_Girl_Catalyst_Mona@-2810238947810998359'
```

Ignored evidence: `Export/load-cycle-baseline.json`, `Export/load-cycle-final.json`, their logs, GUI/synthetic check logs, and `Export/load-cycle-export-final/`. The packaged application is `Export/AnimeStudio-BrowserFix/AnimeStudio.GUI.exe`.

## Export command availability after loading

Validated on 2026-09-27 with the same 7.1.0 Mona source/container/path-ID fixture. Baseline `23f276e` reproduces a disabled **Export Character** command when a GI map is loaded while the current game is Normal: reference preparation incorrectly required both the map and the current game to be GI. Load Selected switched the current game afterward without preparing those references. Its model graph still loaded successfully using the map directly (432 objects, 21 dependency bundles, zero unresolved references), so successful model loading did not imply export readiness. The shader warnings in this sample do not cause the disabled command.

Production fix `9e69ff5` prepares references according to the map's game type, independently of the current game. Closing Asset Browser releases its view while retaining the main window's reference task and map snapshot, allowing the loaded Animator to remain exportable. Explicit map clearing and replacing the map still invalidate the references. The command now supplies a tooltip and logs changed unavailability reasons, including missing map, unfinished/failed preparation, an operation in progress, or an invalid selection.

The real WinForms/message-pump `--export-menu` check fails on the baseline at the initial Normal-to-GI preparation step. All eight checks pass with the fix: reference preparation, Load Selected readiness, browser-close retention, qualified main-window selection, busy disable/recovery, explicit reference clearing, and non-GI map handling. The existing 21 GUI checks and Release .NET 10 GUI publish also pass. This verifies command readiness and reference identity; it does not repeat character extraction or Unity import, whose code is unchanged.

```powershell
dotnet run --no-restore --project tools/GuiWorkflowChecks -- --export-menu Maps/genshin-7.1.map Export/<new-report>.json 'Avatar_Girl_Catalyst_Mona@-2810238947810998359'
dotnet run --no-restore --project tools/GuiWorkflowChecks
```

Ignored evidence: `Export/export-menu-baseline.json`, `Export/export-menu-fixed.json`, their logs, `Export/export-menu-gui-checks.log`, and `Export/export-menu-publish.log`. Updated application: `Export/AnimeStudio-ExportMenuFix/AnimeStudio.GUI.exe`.
