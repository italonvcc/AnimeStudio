> Historical request-driven research. For the current My tools character workflow, see [character export](character-export.md). FBX animation baking was removed, and the new mannequin action is deferred.

# Per-action VFX export

The fork exports selected prefab hierarchies and their reachable dependencies, grouped by character action. Mona is the validated 7.0.0 reference. Particle simulation and Unity scene reconstruction remain Phase 2 work.

## Discovery and export

The original export-oriented map omits many GameObjects. First reindex source blocks selected by a bounded discovery report; if needed, scan the client in bounded batches using the fork's map builder:

```powershell
dotnet run --project tools/AssetStudy -- --reindex Export/discovery.json Maps/targeted-scenes 'Mona'
python tools/index_scene_batches.py '<client blocks folder>' Maps/mona-scenes '(Eff|SkillObj).*Mona'
../../AnimeStudio.CLI/bin/Debug/net10.0-windows/AnimeStudio.CLI.exe --genshin-vfx 'Maps/genshin-asset-map.map|Maps/mona-scenes/scene-index.json' Export/mona-vfx-request.json Export/mona-vfx-new
```

Requests use exact selectors, with PathIDs when names repeat:

```json
{
  "character": "Mona",
  "gameVersion": "7.0.0",
  "actions": [{
    "name": "attack",
    "evidence": "Exact effect string in the source animator event object",
    "effects": [{
      "name": "Eff_Avatar_Girl_Catalyst_Mona_NormalAttack_01",
      "pathID": "2655929179074427106",
      "evidence": "Exact animator-event effect reference"
    }]
  }],
  "evidenceAssets": [{
    "name": "EventPattern_Girl_Catalyst_Mona",
    "pathID": "638274166518115007",
    "type": "MonoBehaviour"
  }]
}
```

The GUI provides the equivalent Genshin VFX request export. Select the request, multiple maps, and an output parent. Existing destinations are refused. GUI compilation and CLI end-to-end exports use the same service.

An unresolved qualified reference does not prove an asset is absent. The study helper searches serialized-file metadata through the production reader, without decoding unrelated objects or merging files in the client:

```powershell
dotnet run --project tools/AssetStudy -- --find-dependencies Export/mona-vfx-new/manifest.json '<client blocks folder>' Maps/vfx-dependencies.json
# Repeat the export to a fresh destination, appending this map to the quoted | list.
```

## Evidence and limits

The complete discovery scan examined 1,925 blocks and retained 304 matching scene entries. The request selects 17 roots: four attack, two dash, five burst, and six skill. Exact animator-event references support attack, dash and StarChart area associations. Additional named Phantom/StarChart roots are explicitly labeled discovery associations; their runtime ability links have not been decoded.

Outputs preserve local transforms, parent/child/component pointers, meshes in source Unity coordinates, material shader identity, texture previews, effect `.anim` clips, raw components, event strings with byte offsets, and qualified source identities. Each action records unresolved references and unsupported components. Unknown particle modules, trail settings, script logic, event timing, runtime attachments and conditions are retained as raw evidence; these are not ready-to-play Unity particle prefabs.

The six initially missing dependencies were found by exact CAB and PathID: five shaders and `Eff_Universe_Cube`. Shader identities come from uniquely validated length-prefixed metadata and bounded platform/blob tables, including the observed `MoleMole.RevertableEditor` , `MiHoYoASEMaterialInspector` and `MoleMole.ASECharacterShaderEditorBase` editor forms. Shader program reconstruction is not claimed.

The cubemap layout is guarded by its observed type fingerprint `285F64E452C966AD1FFF6626784F3FA6`. It contains six 512Ãƒâ€”512 BC6H faces with one mip. Export retains the original compressed HDR image data and six PNG previews identified by serialized face index; preview conversion does not preserve HDR precision or establish Unity face-axis orientation.

Local acceptance artifact: `Export/mona-vfx-accepted/`. Earlier intermediate exports remain separate evidence and should not be substituted for the validated output.

Validation command (Pillow required):

```powershell
python tools/check_vfx_export.py Export/mona-vfx-accepted Export/mona-vfx-accepted-check.json
```

Acceptance: 17 roots, 68 decoded PNGs, 61 verified material shader names, and zero file/dependency errors. Raw unsupported component fields can contain additional references outside the implemented traversal; closure is not a claim of complete runtime simulation.
