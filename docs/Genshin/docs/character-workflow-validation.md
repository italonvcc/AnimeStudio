# My tools character workflow validation

This records the earlier self-contained package workflow. See [shared storage validation](shared-asset-validation.md) for the current smaller export, shared dependency pool and cached clip compositions.

Validated on 2026-09-27 against the installed **7.1.0** client and a freshly rebuilt map. Production commit: `3aedd9b` on the fork's `codex/genshin-phase1-foundation` branch. Export manifests retain the preceding compiled informational revision plus qualified source identities. All client data, binaries, maps and generated projects are ignored local artifacts.

## Checks

| Check | Result |
| --- | --- |
| .NET 10 Windows GUI and CLI | Build/run pass; existing dependency and compiler warnings remain |
| Synthetic regressions | 195 pass, including map-content/client-metadata cache invalidation, character clip selection and particle shader footer validation |
| GUI construction checks | 10 pass: exact My tools placement/labels, three checkboxes per submenu, no materials checkbox, mannequin disabled, other games disabled |
| Final all-options character export | Mona model, 12 model PNGs, 5 model material JSONs, 546 native animation files |
| VFX file/dependency check | 59 roots, 95 decoded PNGs, 95 materials with shader names; no missing selected roots, known-pointer unresolved references or export failures |
| Voice decoding | 464 semantic WAV files, each with positive frame count/rate; 3,784 provider entries absent from installed packages remain recorded |
| Materials disabled/model only | No Materials, Animations, VFX or Voices directories; model, textures and Unity setup files export successfully |
| Clean Unity import | Unity 6000.6.0f1: valid humanoid Avatar, seven representative source clips, 21 renderers, zero missing curve paths |
| Actual native playback | Five action categories move body/fingers and produce finite posed vertices across six skinned renderers |

The final animation files used for playback are byte-identical to the corresponding files in the final all-options export. Seven source clips cover five actions because idle and run have separate shared-body and character-secondary clips. The generated importer combines those as native curves.

| Native clip | Moving local bones | Moving finger bones | Sampled posed height (m) |
| --- | ---: | ---: | ---: |
| Standby, body + secondary | 100 | 30 | 1.724 |
| RunCycle, body + secondary | 104 | 30 | 1.593 |
| Attack_01 | 105 | 30 | 1.735 |
| ElementalArt | 107 | 30 | 1.708 |
| ElementalBurst | 104 | 30 | 1.669 |

Playback compares poses at 20% and 65% of each clip. It proves changing transforms and bounded skin geometry, not frame-perfect fidelity to the game. Source human scale is 0.9272022. No animated FBX is generated.

## Reproduction

From `docs/Genshin/`, with the AnimeStudio projects built:

```powershell
dotnet run --project tools/RegressionChecks
dotnet run --project tools/GuiWorkflowChecks
dotnet run --project tools/AssetStudy -- --prepare-character Maps/client.map
dotnet run --project ../../AnimeStudio.CLI -f net10.0-windows -- --genshin-character Maps/client.map 'Avatar_Girl_Catalyst_Mona@<current-path-id>' Export/new-character --animations --voices --vfx
python tools/check_vfx_export.py Export/new-character/VFX Export/new-vfx-check.json
```

For native playback, import a bounded package containing the five named Mona actions plus shared Girl Standby/RunCycle into a disposable Unity project at `Assets/Mona`. Copy `tools/UnityCharacterPlaybackCheck.cs` into its `Assets/Editor`. After automatic package import, run:

```powershell
unity run Export/scratch-project --timeout 180 -- -nographics -executeMethod UnityCharacterPlaybackCheck.Run
```

Read `unity-import-report.json` in the package and `native-playback-check.json` at the scratch project root. Do not treat a successful Editor exit code alone as import success. A full-library rebuild hit the 300-second test timeout during development; the clean final playback check is deliberately bounded. Large full-library imports can take several minutes.

## Local evidence index

- `Export/character-my-tools-71/`: final all-options output and manifests.
- `Export/character-my-tools-71-vfx-check.json`: all final VFX image/material/dependency checks.
- `Export/character-workflow-71-no-materials/`: settings-off output.
- `Export/character-unity-bounded/`: clean native Unity import/playback project and reports.
- `Export/my-tools-regressions-final.log`, `Export/my-tools-menu-check-final.log`, `Export/my-tools-gui-build-final.log`: final checks/build.
- `Export/shader-common-71-fixed.json`: current particle shader identity recovered from a validated footer. This shader is 73,595,804 bytes, exceeding the old 64 MiB bound; the GI-only fallback now permits up to 128 MiB.
- `Export/AnimeStudio-MyTools/`: runnable GUI build.

Reference preparation scanned 2,099 current source files and added 99,739 supplemental headers. The cache was reused on repeated exports. No character-specific ID or source offset is embedded in the production workflow.

## Limits

Only the Mona reference character has end-to-end real-client validation for this new workflow. Discovery is generic but other character/costume naming and newer client formats may need additional evidence. Name-only VFX candidates are labeled separately from source event strings. Zero unresolved known pointers does not cover opaque script internals or prove complete effect ownership.

The voice checkbox exports provider-mapped voice clips, not automatically synchronized combat events or Wwise mixes. Missing languages/media remain explicit. Initial provider acquisition needs network access; voice-name reading requires Python. A cache pins the acquired provider revision until the map/client fingerprint changes or that cache is removed.

Unity imports preview materials and editable native animation. Game shaders, complete particle behavior and runtime event timing remain Phase 2. The new mannequin export action remains disabled by request; old assembly investigations are historical, not a hidden input to this character flow.
