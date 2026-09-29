# Shared character storage and fidelity validation

This records the first shared-resource layout. See [character export fixes](character-export-fixes.md) for the current character-local materials/textures, readable animation paths, loading improvements and bind-pose validation.

Validated against the installed 7.1.0 client and Unity 6000.6.0f1 on 2026-09-27. Production implementation: `italonvcc/AnimeStudio` commit `4422486` on `codex/shared-character-assets`. Generated data, projects, source identities and logs remain ignored under `Export/`; the installed client was read-only.

## Final behavior

- Only wholly constant, finite, unweighted curves with zero tangents are reduced to their original endpoints. Moving curves retain every key, including flat stretches. Humanoid root/IK vector and quaternion scalar components retain their original topology. Compact YAML changes layout without changing numeric tokens.
- Shared body clips are stored under `Generic/Animations/<body type>/<SHA-256>/`. Character clips remain in the character's `Animations/` folder. Model/VFX texture PNGs and full material JSON live in reusable `Generic/Textures/` and `Generic/Materials/` pools. Per-character indices and manifests retain assignments and source identities.
- Reuse requires identical exported bytes and filename. Different content with the same name is separate; edited pooled files fail rather than being overwritten. Existing Unity `.meta` files are left intact. Generic placement means reusable storage, not proven universal character ownership.
- Paired actions use small `Rig/*.genshinclip` recipes. A single importer in `Generic/Editor/` combines the referenced curves in Unity's import cache, preserving the previous binding precedence, events, object curves and body settings. No additional merged `.anim` source files are produced. Runtime/cache memory still includes the composed clips.
- The prefab's preview materials bind shared textures explicitly rather than relying on Unity's global name search. Shader reconstruction remains deferred.

## Measured checks

The bounded package contains Standby and RunCycle body/secondary pairs, Attack_01, ElementalArt, ElementalBurst and Show_01: eight source clips spanning six actions.

| Check | Result |
| --- | --- |
| Bounded native animation bytes | 101,510,835 before; 45,804,230 after (54.9% smaller) |
| Full native animation library | 546 clips: 1,722.89 MiB before; 758.00 MiB after (56.0% smaller) |
| Full storage split | 301 character clips: 449.45 MiB; 245 reusable shared clips: 308.55 MiB |
| Final VFX dependencies | 59 roots, 95 decoded PNGs, 95 materials with shader identities, zero missing/empty/outside dependencies |
| Unity curve comparison | 5,315 bindings; 4,547,835 evaluations at original keys and quarter/three-quarter-frame offsets; maximum error zero |
| Source timing/settings | Duration, frame rate, clip settings, event payloads and object-reference keys retained |
| Runtime comparison | 36 sampled poses over six actions; zero bone position and quaternion-component error |
| Skinned geometry | 582,516 vertex comparisons; maximum position difference 0.000002193 m; finite bounded geometry |
| Scale comparison | Maximum component-vector difference 0.000009545; same small evaluation-path rounding also present in the original-source control |
| Import integrity | Valid humanoid Avatar; eight source selections; two compositions; zero missing transform paths; six renderer material slots use pooled textures |
| Clean full Unity import | 546 source selections, 225 cached compositions, valid Avatar, zero missing curve paths, no merged `.anim` source copies |
| Composition fixtures | Weighted keys, tangent values/weights, secondary binding precedence, object references, loop setting, event ordering and parameters pass |
| Repeated real export | Both packages reference the same 19 pooled payloads; pool remains 19 payloads |
| .NET regressions | 213 checks pass, including constant/moving curves, signed zero, nonfinite/weighted data, compact YAML and shared-resource collision protection |
| GUI checks/builds | GUI workflow checks and GUI/CLI .NET 10 builds pass; pre-existing warnings remain |
| Materials disabled | Model-only export succeeds without character Materials/Animations/VFX/Voices directories |

## Why the implementation differs from the initial proposal

The first experiment reduced constant stretches inside moving curves and used synchronized Animator layers. Unity editor curve evaluations agreed, but runtime interpolation did not. Untouched source clips reproduced the layering discrepancy (RunCycle vertex error up to about 8.9 mm in that test). Moving-curve key reduction also changed subframe motion even with equal editor curves. Neither experiment is enabled in the final exporter.

The final policy preserves moving-curve topology and uses import-cache compositions. It passes the stricter original-motion comparison while removing source-file duplication. This does not establish frame-perfect playback against the game itself; it establishes preservation relative to the prior working export. Only Mona has full real-client validation.

## Reproduction and evidence

```powershell
dotnet run --project tools/RegressionChecks
dotnet run --project tools/GuiWorkflowChecks
dotnet run --project tools/RegressionChecks -- --shared-character Maps/client.map 'Avatar_Girl_Catalyst_Mona@<current-path-id>' Export/new-bounded/Mona
```

Create a disposable Unity project using the installed editor. Copy the new bounded `Mona/` and `Generic/` into its `Assets`. Put the corresponding untouched source clips from the previous export in `Assets/Original`, and copy `tools/UnitySharedAnimationCheck.cs` into `Assets/Editor`. Run:

```powershell
unity run Export/scratch-project --timeout 240 -- -nographics -executeMethod UnitySharedAnimationCheck.Run
```

Read `shared-animation-check.json` and the character's `unity-import-report.json`; an Editor exit code alone is insufficient. For the full library, `tools/UnitySharedPackageImportCheck.cs` verifies import, shared texture assignments, composition creation and zero missing curve paths. `tools/check_vfx_export.py` follows shared VFX references only within the character tree or its sibling Generic pool.

Final local evidence: `Export/shared-assets-bounded-final/`, `Export/shared-assets-unity-final/`, `Export/shared-assets-unity-full/`, `Export/shared-assets-release/`, `Export/shared-assets-release-vfx-check.json`, `Export/shared-assets-model-only/`, and the matching `shared-assets-*.log` files. `Export/AnimeStudio-SharedAssets/` is the runnable GUI build. Earlier `shared-assets-*` experiment folders retain diagnostic evidence and are not the final outputs.
