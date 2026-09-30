# Genshin Unity import descriptor

AnimeStudio exports source-backed assets and metadata. The Unity Editor code that
reads them belongs to `com.caladan.stylizedgamekit` (its `Editor/` import implementation),
not to each export. New exports contain no generated `.cs` files or `Generic/Editor`
folder. Legacy importer templates remain in the source tree for comparison, but
are no longer embedded in the utility assembly or written to output.
The package's [AnimeStudio import guide](https://github.com/italonvcc/Project_Caladan/blob/main/Packages/com.caladan.shaders/Docs/AnimeStudio-Import.md)
owns current Unity setup and live validation; this page owns the exporter file
contract and offline evidence.

Every model folder has `anime-studio-import.json` schema version 1:

```json
{
  "schemaVersion": 1,
  "kind": "genshin-model",
  "recipe": "manifest.json",
  "model": "Example.fbx"
}
```

`recipe` and `model` are filenames in the descriptor's own folder. They must
exist when AnimeStudio writes the descriptor. The importer should reject unknown
versions/kinds and paths that escape the export and Unity project. The descriptor
selects an existing recipe; it does not duplicate source fields or assert that
Unity import/playback has succeeded.

| `kind` | Recipe | Produced by | Scope |
| --- | --- | --- | --- |
| `genshin-model` | `manifest.json` | `GenshinModelExporter`, including `--genshin-model`, `--genshin-prefab`, and `--genshin-assemble` | One FBX and its source-backed streams, materials and selected clips. For Manekin part/assembled model exports this does **not** perform look assembly; the package's dedicated catalog/assembler retains that contract. |
| `genshin-character` | `<character>.character.json` | `GenshinCharacterExporter` / Avatar source probe | Humanoid Avatar mapping, clips, material/texture metadata. `GenshinSharedAssets.Package` may rewrite clip paths to sibling `Generic/` data after descriptor creation. |
| `genshin-weapon` | `weapon-import.json` | `GenshinWeaponUnityPackage` for each exported variant | Static/Generic/Legacy rig decision and retained model, clip, material, cubemap, bind-pose and source-link evidence. |

The model `manifest.json` records the selected source object (serialized file,
offset and PathID), mesh topology and vertex-channel availability, skin/bind
data, material bindings and source clips. The parser and conversion path is
`AssetsManager` → `GenshinModelExporter` / `ModelConverter` → FBX plus manifest.
Character exports add the source Avatar skeleton and humanoid calibration in
`GenshinUnityPackage.DescribeAvatar`. Weapon exports derive the variant recipe
from the family report, model manifest and exported `.anim` files in
`GenshinWeaponUnityPackage.Write`. Unity import must use these same exported
resources; an imported asset missing a stream or binding must be traced through
the source, conversion, payload and Editor import stages before assigning cause.

Install the package in the target Unity project, copy the export under
`Assets/_Games/Genshin/`, preserve existing `.meta` files, and let the package
processor read the descriptor. Character exports also need their sibling
`Generic/` data folder. Reimport reports and Unity playback remain live Editor
checks. A successful AnimeStudio build and offline descriptor test establish
the file contract only; they do not prove the model's visual fidelity or
Manekin look assembly.

## 2026-09-30 offline validation

Release GUI/CLI build `artifacts/build-records/20260930T054154864Z/` passed
from Git HEAD `9e69ff5` and the recorded working-tree source hashes. The
`Tests/Genshin/WeaponExportRegression` run passed 59/59 checks, including
same-folder descriptor paths, no emitted `.cs`, and traversal rejection.

Fresh exports from that build and its preceding successful build
`20260930T053155522Z`, using `docs/Genshin/Maps/genshin-7.1.map` and installed
Genshin 7.1 source bundles, are under ignored
`artifacts/import-descriptor-regression/`:

| Run | Exact source selection and result | File contract |
| --- | --- | --- |
| `20260930T053327097Z/BoyStoreS0017` | Initial build `053155522Z`; `--genshin-model Beyd_Avatar_Boy_Suit_S0017_Store@-242358498629974930`; source CAB `CAB-8b50668c3517d3cfc5e4f784c204abb7` in `00850201.blk` offset 17339808. | `genshin-model`; 25 meshes with normals and tangents, 25 material binding slots, zero unresolved, 65 files, zero `.cs`. FBX SHA-256 `A1727FA04DAB645B6EE04025582A2FA59971FCE7A04EA553A45EA5D61630BF07`. |
| `20260930T054241569Z/Mona` | `--genshin-character Avatar_Girl_Catalyst_Mona@-2810238947810998359`, with no optional animation/audio/VFX flags. | `genshin-character`; 147 Avatar nodes, 21 meshes, 22 material binding slots, zero unresolved, 36 files, zero `.cs` or `Editor` folders. All 23 preview PNG and 23 native texture references resolve. |
| `20260930T053703997Z/Kasabouzu` | Initial build `053155522Z`; `--genshin-weapon Sword-Kasabouzu__ba0b9b3fbb40e94117e3 --animations --unity-import` with bounded `04803507.blk`, `07845116.blk`, `11877482.blk` scans. Family report is `Partial`/CLI exit 3 because source discovery is bounded. | One `genshin-weapon` variant; Generic rig, two clips, two materials, 29 files, zero `.cs`. Recipe status is `GeneratedNotValidated`. |

The model manifest retains `materialBindings[]` by `rendererPath` and
`submeshIndex`, with `sourceMaterial.SerializedFile`/`PathID` and a JSON path
whose filename includes the PathID. In BoyStoreS0017, 12 BaseBody slots and
three Hair slots repeat display names, each group referring to one source
material. A package importer must verify uniqueness before mapping FBX
subassets by display name; distinct source materials can share a name in other
exports. Unity import, material remapping, animation playback and appearance
checks remain unverified for these fresh exports.

The first Mona export under `20260930T053426614Z/Mona` exposed a packaging
defect during live import: `GenshinModelExporter` wrote root-relative preview PNG
paths, then `GenshinCharacterExporter` moved the PNGs to `Textures/` while
`GenshinSharedAssets.Package` retained the old manifest paths. The source
material and native `.astexture` data were present. The current packaging step
rewrites `materialBindings[].textures` to the shipped `Textures/` path and keeps
native texture paths intact. The old export remains diagnostic evidence; use
the fresh `054241569Z` export for further Unity checks.
