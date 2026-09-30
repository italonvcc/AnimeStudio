> Historical request-driven research. For the current My tools character workflow, see [character export](character-export.md). FBX animation baking was removed, and the new mannequin action is deferred.

# Initial findings and validation

This is the historical foundation investigation. See [current progress](phase-1-progress.md) for subsequent dependency exports, the ACL database fix, Blender validation, and audio/VFX evidence.

Baseline: client 7.0.0; AnimeStudio upstream-derived commit `db860e1`; local map contains 2,809,115 entries. Research was performed with the checked-in AssetStudy utility using AnimeStudio's own readers. Raw reports and game exports remain under ignored `Export/`.

Implementation: [AnimeStudio PR #1](https://github.com/italonvcc/AnimeStudio/pull/1), commit `44819e6`.

## Status

| Phase 1 work | Evidence / implemented result | Still required |
| --- | --- | --- |
| Manekins | Located and read boy/girl prototype rigs and part-related metadata | Load complete dependencies; export and validate two interchangeable assemblies |
| Animations | Confirmed humanoid bindings skipped by FBX; added warning and .anim menu entry | Implement and validate humanoid-to-bone baking, including root motion and fingers |
| Voices and combat audio | Local AudioAssets contains bank packages, English(US), and BeyondUGC directories | Recover event/media/character/language mapping and add named audio export |
| Character VFX | Mona effect candidates exist in map | Resolve action references and dependency closure; export supported components with timing |
| Material shader identity | Recover Mona shader name from validated metadata footer; export provenance in GUI/CLI JSON | Broader shader fixtures; full shader program parsing remains unsupported for this fixture |

These results do not complete Phase 1. Phase 2 has not started.

## Shader identity: actual failure and fix

The supplied material JSON references path ID `-5030116878936476770`. Source inspection resolves the external serialized file to `CAB-6e58d05f096c660999930176fa94e59c`. The map identifies the shader candidate in `blocks/00/00030609.blk` at bundle offset 20222358. The loaded object's serialized-file identity matches the material reference; the numeric path ID alone was not used as proof.

The 15,286,348-byte shader has an empty NamedObject name. Full SerializedShader parsing raises EndOfStreamException before assigning the parsed form. Its raw data includes a length-prefixed metadata record for `miHoYo/Character/Character_Base_Uber` and `MoleMole.CharacterUberShaderEditor`.

The fork adds a narrow recovery path for GI/Unity 2017 shaders with this parse failure (up to 64 MiB). It requires a unique aligned name/editor/fallback record, bounded dependency strings, valid platform IDs, matching platform/blob table lengths, and blob ranges within the object. It also handles this client's external-blob sentinel. It does not claim that subshader/program parsing succeeded, and it deliberately leaves unrecognized or ambiguous layouts unresolved.

Genshin material JSON retains `m_Shader.m_FileID`, `m_PathID`, `Name`, and `IsNull`, and adds `ShaderReference` with status, serialized-file identity, a lossless textual PathID, name source, and `ParsedShader`. Recovered names use `NameSource: ValidatedGenshinFooter` and `ParsedShader: false`.

A missing dependency can be loaded later and retried; PPtr no longer permanently caches the initial miss. Repeated misses against an unchanged loaded-file list remain cached.

### Reproduce

```powershell
dotnet run --project tools/AssetStudy -- Maps/genshin-asset-map.map '^(Avatar_Girl_Catalyst_Mona_Mat_(Body|Face|Hair)|-5030116878936476770)$' Export/shader-check.json --inspect
dotnet run --project tools/RegressionChecks -- --materials Export/shader-check.json Export/material-check
```

Use new output paths each time. The smoke harness delegates writing to AnimeStudio.CLI.Exporter.ExportJSONFile; it does not implement another extractor. All six located Mona body/face/hair materials passed with the recovered name and explicit incomplete-parser provenance.

### GUI workflow

Build the fork, select GI, and load the desired materials/model **and their shader dependencies**. In Asset Browser, the shader may still be listed as `Shader #-5030116878936476770` because map creation deliberately avoids full shader parsing. Loading the shader object enables identity recovery. Use Export > Genshin Impact > Export selected materials (JSON), or the existing model material export.

The new menu follows changes from the game selector and asset-map game selection. It provides material JSON, .anim clips, and the existing selected-hierarchy FBX workflow. It does not expose unimplemented named-audio or assembled-Manekin exporters. .anim output preserves the existing humanoid curve export route; it is not a baked FBX solution.

## Animation: source evidence

`Ani_Avatar_Girl_Catalyst_Mona_Attack_01` contains **222 Transform bindings and 139 Animator bindings**. All 139 Animator bindings use custom type 8, the converter's humanoid-muscle category. `ModelConverter.ReadCurveData` handles Transform and blendshape channels and skips other bindings; the YAML AnimationClipConverter has a separate muscle-curve path.

This directly establishes a missing conversion path consistent with the reported static main bones. It does not prove that every observed Blender issue has this cause. No new FBX has been visually checked in Blender, and no humanoid baking fix is claimed.

```powershell
dotnet run --project tools/AssetStudy -- Maps/genshin-asset-map.map '^Ani_Avatar_Girl_Catalyst_Mona_Attack_01$' Export/attack-bindings.json --inspect
```

## Manekins: promising real rigs

Both `Beyd_Avatar_Boy_Prototype_Manekin` and `Beyd_Avatar_Girl_Prototype_Manekin` are Animator objects in `blocks/00/15607500.blk`, at bundle offsets 27155448 and 27513970. They have 137 and 141 hierarchy nodes respectively. Each contains seven skinned renderers: BaseBody_EffectMesh0001, Body, Cloth, ClothSpecial, Face, Item, and ItemSpecial. Boy Body references 54 bones; girl Body references 56.

The bounded prototype-only load leaves all seven meshes and the avatar external/unresolved. This is a dependency-loading task, not evidence that the rigs are absent.

The `^Beyd_Avatar_(Boy|Girl)` query finds 8,162 entries, including 1,948 materials and 3,731 textures. Material names expose parts such as ArmAcc, Backwear, and Bottom. This prefix query returns no Mesh entries: related meshes must be located through object references rather than assuming they share the same prefix. Character-named `_Manekin` variants elsewhere in the map should not be confused with these customizable prototypes.

```powershell
dotnet run --project tools/AssetStudy -- Maps/genshin-asset-map.map '^Beyd_Avatar_(Boy|Girl)_Prototype_Manekin$' Export/manekin-check.json --inspect
```

## Audio and VFX

The installed AudioAssets folder contains Banks*.pck, English(US), and BeyondUGC inputs. Their filenames do not establish event/character mappings. No decoding or semantic attribution is claimed yet.

The map includes Mona effect candidates such as Eff_Mona_Show and Eff_Ani_Model_Mona_Phantom_Rush_Skin, but also GCG, scene, and other unrelated name matches. Correct per-action VFX export needs dependency and action-reference evidence; name filtering alone would over-include assets and miss shared effects.

## Checks performed

- .NET 10 Windows GUI and CLI builds: passed. Existing compiler and NuGet audit warnings remain; dependency upgrades were outside this change.
- 149 synthetic checks: valid/sentinel footer recovery, every truncated prefix, false positives, ambiguity, invalid blob bounds, null/missing/late-loaded references, and material JSON identity/status.
- Six map-query checks passed: lossless IDs, no overwrite, zero matches, invalid regex, input protection, and extension validation.
- Six real material exports through the production CLI exporter: passed.
- Two real Manekin hierarchies and the Mona attack clip inspected through the core reader.
- Git ignore checks: original map and Mona sample are excluded; client files and original sample were not modified.
- GUI menu interaction and Blender playback were not exercised. Full shader program parsing, general humanoid baking, named audio, and complete VFX/Manekin dependency export remain open.

## Later shader-study evidence (2026-09-28)

The [new extraction findings](shader-study-extraction-2026-09-28.md) extend the historical shader-identity fallback with bounded GI 7.1 metadata parsing and document native texture companions, FBX repairs and the resolved Dynamic Character Resolution mesh discrepancy. Earlier limitations above refer to their dated fixtures; unknown extensions and broader parser support remain explicitly limited.
