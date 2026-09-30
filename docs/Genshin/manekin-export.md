> Historical request-driven research. For the current My tools character workflow, see [character export](character-export.md). FBX animation baking was removed, and the new mannequin action is deferred.

# Wonderland Manekin parts and assemblies

Client 7.0.0, Blender 5.2.1 LTS. The original map omitted GameObjects and meshes, so prototype-only discovery missed the clothing prefabs. A bounded scan of 34 evidenced source blocks found 16,273 matching scene/mesh entries, including rig-bearing interchangeable parts.

```powershell
dotnet run --project tools/AssetStudy -- --reindex Export/manekin-parts.json Maps/manekin-scene-index 'Beyd|LittleIdol|Manekin'
```

This research command calls the production map builder with split merging disabled, keeping the client read-only. CLI model/prefab commands accept multiple maps joined by a quoted `|`: original map for dependencies, supplemental scene index for omitted roots.

## Export and assembly

Use the complete rig-bearing prefab, not just a same-named customization anchor. For example, boy Top S0017 has both an anchor (`1006003615594688337`) and a complete rig prefab (`235140464713126197`). The exporter rejects empty geometry instead of reporting a successful model.

```powershell
../../AnimeStudio.CLI/bin/Debug/net10.0-windows/AnimeStudio.CLI.exe --genshin-prefab 'Maps/genshin-asset-map.map|Maps/manekin-scene-index/scene-index.json' 'Beyd_Avatar_Boy_Top_S0017@235140464713126197' Export/manekin-top-new
../../AnimeStudio.CLI/bin/Debug/net10.0-windows/AnimeStudio.CLI.exe --genshin-assemble 'Maps/genshin-asset-map.map|Maps/manekin-scene-index/scene-index.json' Export/assembly-request.json Export/manekin-assembly-new
```

Example request (identifiers verified in this client; re-query after updates):

```json
{
  "base": {"name":"Beyd_Avatar_Boy_Suit_S0017_Store","pathID":"-242358498629974930","type":"Animator"},
  "parts": [{
    "slot":"Top",
    "root":{"name":"Beyd_Avatar_Boy_Top_S0018","pathID":"-7821748467031991677","type":"GameObject"},
    "removeMeshes":["Top_S0017","Top_S0017_Nude"]
  }]
}
```

The GUI provides **Assemble compatible Manekin parts from request…**. Select the request, original/supplemental maps and an output parent. Both GUI and CLI use the same production assembler.

The assembler requires explicit replacements and validates shared bone paths, every required ancestor's local rest transform, and bind matrices before retaining skin weights and substituting meshes. It rejects incompatible/missing bones instead of matching by a bare name. Material/texture names are prefixed by slot to avoid collisions. FBX, textures, material JSON, the original request and an assembly/part manifest are written together.

## Acceptance evidence

Two combinations were exported: S0017 base with independently supplied S0017 top, and the same base with S0018 top. Each top includes clothing (1,739 vertices) and the complementary exposed-body mesh (422 vertices). The original store top had 1,372 vertices; the assembly uses actual standalone part geometry. S0018 is a material/texture variation of that same standalone top geometry. Its diffuse image hash differs from S0017; skin and lightmap dependencies are shared.

Both assembled FBXs import into Blender as one rig with 25 independently selectable meshes. All meshes have skin bindings and weighted vertices. The checker bends spine, head, arms and legs in two opposite **synthetic poses**; all 25 meshes move, all evaluated vertices remain finite, and displacement stays below the conservative 1.5-meter guard. Rendered poses were visually inspected without detached parts or exploding skin.

```powershell
blender --background --python-exit-code 1 --python tools/blender_check_manekin.py -- '<assembly.fbx>' Export/manekin-check-new
```

Local evidence: `manekin-top-s0017-rig/`, `manekin-top-s0018-rig/`, `manekin-assembly-0017/`, `manekin-assembly-0018/`, and matching `*-check/` directories containing images, reports and inspection `.blend` files. These poses establish animatable assemblies; they are not extracted Manekin gameplay animations.

Compatibility is proven for these part/rig combinations, not every customization slot. Facial customization, material runtime overrides, cross-body-type compatibility and the full in-game catalog are not reconstructed. Incompatible selections fail with an explicit reason. Phase 2 Unity scene assembly remains deferred.
