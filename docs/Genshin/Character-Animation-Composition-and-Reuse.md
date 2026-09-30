# Genshin character animations: composition with shared animations and reuse

Prepared September 29, 2026, America/New_York. This guide describes the current
`italonvcc/AnimeStudio` character export and Unity import implementation. It is
intended to travel with characters being reused in another project.

**Current export/import advisory (September 30, 2026):** fresh AnimeStudio
exports contain `anime-studio-import.json` and asset metadata, with no generated
Unity Editor `.cs` files. Install `com.caladan.stylizedgamekit` in the receiving Unity
project and follow the [descriptor contract](Unity-Import-Descriptor.md) and
the package's [current import guide](https://github.com/italonvcc/Project_Caladan/blob/main/Packages/com.caladan.shaders/Docs/AnimeStudio-Import.md).
This guide's generated-script layout, importer version fingerprints and
source-file references below document the September 29 implementation. Apply
its animation composition concepts to current exports, but use the package
guide for current import steps and validation.

**The central rule:** for certain actions, a character-specific clip supplies
secondary channels while a matching shared clip supplies humanoid body motion.
AnimeStudio exports both source clips. During Unity import, a small
`.genshinclip` recipe combines their curve bindings into **one AnimationClip**.
The shared curves are inserted first; the character-specific curves replace
exactly matching bindings. The resulting clip plays through that character's
reconstructed source Avatar.

This is the implemented reconstruction and reuse contract. It does not establish
the original game's complete animation scheduler, controller, blending logic,
physics, or effect timing. Source behavior, historical tests, and recommendations
for the receiving project are distinguished below.

## Contents

1. [Terms and overall data flow](#1-terms-and-overall-data-flow)
2. [How clips are selected](#2-how-clips-are-selected)
3. [How a body/character pair is recognized](#3-how-a-bodycharacter-pair-is-recognized)
4. [Exactly what composition does](#4-exactly-what-composition-does)
5. [Rig, Avatar, and binding requirements](#5-rig-avatar-and-binding-requirements)
6. [Files and dependency layout](#6-files-and-dependency-layout)
7. [Export and import in another Unity project](#7-export-and-import-in-another-unity-project)
8. [Using the clips in a game controller](#8-using-the-clips-in-a-game-controller)
9. [Root motion, mirroring, facial motion, and other edge cases](#9-root-motion-mirroring-facial-motion-and-other-edge-cases)
10. [Porting the combination algorithm](#10-porting-the-combination-algorithm)
11. [Manekin characters](#11-manekin-characters)
12. [Acceptance checks and troubleshooting](#12-acceptance-checks-and-troubleshooting)
13. [Evidence, limits, and source references](#13-evidence-limits-and-source-references)

## 1. Terms and overall data flow

The word **generic** can mean several different things. They should not be
treated as interchangeable.

| Term | Meaning in this workflow |
| --- | --- |
| Shared/body-type animation | A source clip named, for example, `Ani_Avatar_Girl_RunCycle`, selected as a possible companion to a character action. |
| Shared weapon-class animation | A more specific candidate such as `Ani_Avatar_Girl_Catalyst_<action>`. The current importer tries this before the general body-type candidate. |
| `Generic/` folder | A reusable, content-checked dependency pool. Its location does not guarantee that a clip works on every character. |
| Unity Generic rig | A Unity animation rig mode. It is **not** what the folder name requests. This character importer builds a **Humanoid Avatar** from source metadata. |
| Character-specific source clip | A clip whose name starts with `Ani_<exact selected character Animator name>_`. It may contain full humanoid motion or only secondary channels. |
| Secondary clip | In the pairing algorithm, a character clip classified with `body: false`. This is a technical classification, not proof that it contains only hair or clothing. |
| Composed clip | The imported result of a `.genshinclip` recipe. It contains the union of body and secondary bindings, with secondary precedence on collisions. |
| Avatar | The mapping and calibration that lets Unity interpret humanoid muscle motion on this character's skeleton. It is separate from the mesh's skin bind pose. |

For a representative character:

```text
Selected Animator: Avatar_Girl_Catalyst_Mona
                         |
             character export selection
                         |
          +--------------+-------------------+
          |                                  |
Ani_Avatar_Girl_<action>       Ani_Avatar_Girl_Catalyst_Mona_<action>
shared body-type candidate                 character candidate
          |                                  |
Generic/Animations/Girl/*.anim     Mona/Animations/*.anim
          |                                  |
          +-------- matching pair -----------+
                         |
          Mona/Rig/<character clip>.genshinclip
                         |
               Unity ScriptedImporter
                         |
           one imported AnimationClip asset
                         |
              character Animator state
                         |
        source-calibrated Humanoid Avatar + exact hierarchy
                         |
       animated body, extra bones, and supported renderer curves
```

The merge happens at import time. The runtime Animator evaluates one composed
motion for the paired state. There is no runtime requirement to load the game
client, call AnimeStudio, read the JSON, or merge two clips every frame.

Not every action follows both branches. If a character clip already contains
recognized humanoid muscle bindings, the importer uses that clip directly.
For secondary-only clips, the current source first tries the matching shared
**body type + weapon class** action, then the general **body type** action. The
diagram's shared-body branch shows the general fallback; only the chosen body
candidate is combined with the secondary clip.

## 2. How clips are selected

The character workflow starts with one exact source **Animator**, conventionally
named:

```text
Avatar_<body type>_<weapon category>_<character>
Avatar_Girl_Catalyst_Mona
```

`GenshinCharacterExporter.SelectClips` uses case-sensitive, ordinal string
matching. It does the following:

1. Collect every `AnimationClip` map entry beginning with
   `Ani_<exact Animator name>_`.
2. Remove that prefix to obtain each character clip's action suffix.
3. Form the weapon-class prefix `Ani_Avatar_<body type>_<weapon category>_`,
   taking body type and weapon category from the second and third
   underscore-separated tokens of the selected Animator name.
4. Collect weapon-class clips **only when their complete suffix is present in
   the character set**.
5. Also collect general `Ani_Avatar_<body type>_` clips with a complete suffix
   present in the character set.
6. Deduplicate map entries by source file, bundle offset, path ID, and type.
7. After loading, deduplicate source clips by the combination of their name and
   SHA-256 of raw clip data.

For Mona, the candidate relationship is:

```text
Ani_Avatar_Girl_Catalyst_Mona_RunCycle
                              | action suffix = RunCycle
Ani_Avatar_Girl_Catalyst_RunCycle   [first candidate, if available/compatible]
Ani_Avatar_Girl_RunCycle            [general fallback]
```

The exporter can include both candidates. The Unity importer chooses a body for
each secondary action using the priority and compatibility checks below. A
weapon-class clip is still shared: its name includes the weapon category, not
the individual character name. This name-based choice does not validate an
arbitrary weapon prop's rig or attach it to the character.

### What this selection does not do

- It does not export the entire `Girl`, `Boy`, or other body-type animation library
  automatically. Shared actions with no matching character suffix are excluded.
- It does not use fuzzy matching, translate action names, strip `+Mirror`, or
  collapse similar action phases.
- It does not pair `RunCycle` with `SprintCycle`, or `Standby` with `Idle01`.
- It does not select a different costume's clips solely because the character
  token is similar. Animation selection uses the full Animator name. A separate
  character-token helper used by other export features is not an animation alias
  resolver.
- It does not prove that every clip used by the original game is covered. Clips
  with other naming conventions, standalone facial clips, external action data,
  and runtime-generated motion require their own evidence and handling.

**For another project:** use the exported recipe and actual source identities
as your inventory. A filesystem search for names containing the character's name
is not an equivalent selection procedure.

## 3. How a body/character pair is recognized

Each entry in `<character>.character.json` contains these fields:

| Field | Meaning |
| --- | --- |
| `name` | Original source animation name. Also used for the generated Animator state. |
| `file` | Path to the exported native `.anim`, relative to the character folder. Shared paths normally begin with `../Generic/`. |
| `body` | Whether the source clip has the specific humanoid muscle binding category tested below. |
| `duration` | Source muscle-clip stop time, or zero if that source structure is absent. The importer checks the resulting Unity clip length against it. |
| `rate` | Source sample rate. |

The source classification is deliberately narrow:

```csharp
body = clip.genericBindings.Any(binding =>
    binding.typeID == Animator &&
    binding.customType == 8 &&
    binding.attribute >= 42 &&
    binding.attribute < 137);
```

These source fields identify the muscle range recognized by this exporter.
They are not public Unity `EditorCurveBinding` fields. The conversion step maps
source binding types into Unity animation properties before composition.

Consequences:

- `body: true` means **at least one** binding in that recognized range exists.
  It does not prove that the clip animates every limb or is a complete action.
- A clip containing only root/IK or extra-bone channels can still be
  `body: false`.
- A character-specific clip can be `body: true`; its name alone does not make it
  a secondary clip.
- A file stored in `Generic/Animations` is not accepted as the body of a pair
  solely because of its folder. The candidate's `body` flag must be true.

### Exact automatic pairing conditions

For each source clip, the character importer creates a state. It attempts
composition through this decision process:

1. The current clip has `body: false`.
2. Its name starts with `Ani_<exact character>_`.
3. Try the exact candidate name
   `Ani_Avatar_<body type>_<weapon category>_<same complete suffix>` first.
4. For that name, retain only entries with `body: true`, absolute recipe-duration
   difference **less than 0.0001**, and absolute recipe-rate difference
   **less than 0.0001**.
5. If there is exactly one compatible entry, select it and stop searching.
6. If there is no compatible entry, repeat the same checks for the general name
   `Ani_Avatar_<body type>_<same complete suffix>`.
7. If either examined candidate name has more than one compatible entry, throw
   `Ambiguous body clip: <name>` instead of selecting one.

| Case | Current importer behavior |
| --- | --- |
| Character clip has recognized body muscles | Play the original character clip; do not add a shared body. |
| Secondary character clip has one compatible weapon-class body | Compose with that body; do not also combine the general body. |
| No compatible weapon-class body, but one compatible general body | Compose with the general body. |
| No compatible body in either tier | Leave the state using the original secondary clip. |
| More than one compatible entry for an examined candidate name | Fail import with an ambiguity error. |
| Secondary clip is not under the character prefix | Use it directly; no automatic pair. |

Two different tiers are not automatically ambiguous: one compatible
weapon-class clip takes priority over one compatible general clip. A
weapon-class clip that exists but fails the timing or `body` checks permits the
general fallback. The importer does not continue after an ambiguity error.

The no-compatible-body and non-character-prefix cases do **not** produce a
dedicated “unpaired secondary” field in the current import report. An import can
therefore succeed while a state has only partial motion. Audit expected pairs
explicitly in the receiving project.

**Version distinction:** an earlier importer selected only the general body-type
candidate and left ambiguous pairs uncomposed. Existing exports carry their own
generated importer scripts and do not acquire new selection logic merely because
the AnimeStudio repository changes. The current-source behavior above is tied
to the fingerprints in section 13. Inspect the delivered importer when reusing
an older package.

The importer also checks each source Unity clip against its recipe duration:
a difference **greater than 0.002 seconds** throws an error. This initial check
does not replace the stricter pairing checks.

## 4. Exactly what composition does

### 4.1 The recipe

The generated recipe has only three fields:

```json
{
  "name": "Ani_Avatar_Girl_Catalyst_Mona_RunCycle__WithBody",
  "body": "../../Generic/Animations/Girl/<exact shared filename>.anim",
  "secondary": "../Animations/<exact character filename>.anim"
}
```

This is an explanatory example: replace the bracketed filenames with the
exported paths; do not save it literally as an importable recipe.

The recipe lives under the character's `Rig/` directory. Its dependencies are
relative to the **recipe file**, whereas `clips[].file` in `.character.json` is
relative to the **character folder**. The extra `../` is significant.

`name` is the resulting clip's object name. The generated Animator state retains
the original character action name without `__WithBody`.

The current installed Mona export provides a concrete example. Its character
folder is `Assets/_Games/Genshin/Characters/Avatar_Girl_Catalyst_Mona/` in the
study project. These are existing asset/report observations, not new playback
tests performed for this document. They were already imported with their own
exported scripts; their general-body recipes do not prove that the current
weapon-class-first exporter has regenerated or validated them:

| Character source action | Recipe `body` flag | Duration / rate | Imported motion |
| --- | --- | --- | --- |
| `Ani_Avatar_Girl_Catalyst_Mona_Standby` | `false` | 2 seconds / 60 Hz | Shared Girl Standby + Mona Standby. |
| `Ani_Avatar_Girl_Catalyst_Mona_RunCycle` | `false` | 0.6666667 seconds / 60 Hz | Shared Girl RunCycle + Mona RunCycle. |
| `Ani_Avatar_Girl_Catalyst_Mona_Attack_01` | `true` | 2.8333335 seconds / 60 Hz | Original Mona attack `.anim`, without a composition recipe. |

The Standby sidecar at
`Rig/Ani_Avatar_Girl_Catalyst_Mona_Standby_5640501919462229297.genshinclip`
contains:

```json
{
  "name": "Ani_Avatar_Girl_Catalyst_Mona_Standby__WithBody",
  "body": "../../Generic/Animations/Girl/Ani_Avatar_Girl_Standby__c4ae1ba4b67e.anim",
  "secondary": "../Animations/Ani_Avatar_Girl_Catalyst_Mona_Standby_5640501919462229297.anim"
}
```

The RunCycle sidecar uses shared
`Ani_Avatar_Girl_RunCycle__a66db2c06713.anim` and local
`Ani_Avatar_Girl_Catalyst_Mona_RunCycle_3604737247739174464.anim`.
The direct attack is
`Animations/Ani_Avatar_Girl_Catalyst_Mona_Attack_01_395955848012377913.anim`.
Keep those identifiers as provenance for this export; use your delivered recipe
if exporting another client version.

The source manifest records seven humanoid bindings for the local Standby and
RunCycle clips, versus 139 for Attack_01. That aggregate manifest count is not
the `HasBody` muscle-range test. The composition field `body` above is a **file
path**, whereas `clips[].body` in `.character.json` is a **boolean classification**.
They have different purposes despite the same field name.

### 4.2 Resolve and validate the inputs

`AnimeStudioClipCompositionImport` handles the `genshinclip` extension. For each
dependency it:

1. Resolves the relative path against the recipe directory.
2. Requires the resolved file to remain under the Unity project's `Assets/`.
3. Registers source-asset and imported-artifact dependencies with Unity.
4. Loads the input as an `AnimationClip`.

It rejects the pair if the imported clip lengths differ by more than
**0.0001 seconds**, or if their `frameRate` values are not exactly equal.
The rate comparison here is stricter than the recipe-level tolerance. There is
no resampling, stretching, trimming, frame-count adjustment, or phase offset in
the composition importer.

### 4.3 Merge float curves by binding

The implementation is equivalent to:

```text
floatCurves = dictionary keyed by Unity EditorCurveBinding
objectCurves = dictionary keyed by Unity EditorCurveBinding

for input in [body, secondary]:
    for each numeric animation binding in input:
        floatCurves[binding] = complete curve from input
    for each object-reference binding in input:
        objectCurves[binding] = complete object-keyframe array from input

create output clip
write both dictionaries to output
```

A binding distinguishes the target path, component type, property, and relevant
Unity binding identity. Two curves with the same leaf bone name but different
full paths are not necessarily the same binding.

For a numeric binding `b` at clip time `t`:

```text
combined(b,t) = secondary(b,t), if secondary contains binding b
              body(b,t),      otherwise if body contains binding b
              no authored curve from this composition, otherwise
```

“Contains” refers to the existence of that curve binding, not whether its value
is nonzero at `t`. A constant zero secondary curve still replaces the body
curve for that binding.

| Body input | Secondary input | Result |
| --- | --- | --- |
| Humanoid muscle property | No matching binding | Body muscle curve survives. |
| No matching binding | Extra-bone local rotation | Secondary rotation is added. |
| Root translation property | Same binding | Entire secondary root curve replaces the body curve. |
| Object-reference track | Same binding | Entire secondary object-reference track replaces the body track. |
| No curve for a property | No curve for a property | No new curve is synthesized. |

This is a **curve union with replacement**. It is not numeric addition,
averaging, interpolation between two poses, or an additive delta from a reference
pose. It does not multiply two rotations together. It does not apply an Avatar
Mask or selectively retain only hair/cloth channels from the secondary clip.

The complete source `AnimationCurve` objects are passed through Unity's curve
APIs. The composer does not sample their keys into a new uniform-rate bake.
If two inputs conflict on one binding, it does not splice their keyframes or
fill gaps in one with keys from the other.

### 4.4 Events, settings, and absent properties

| Data | Composition behavior |
| --- | --- |
| Float curves | Body first, secondary overwrites identical bindings. |
| Object-reference curves | Same precedence as float curves. |
| Animation events | Concatenate body events and secondary events, then sort by time. |
| Duplicate events | No deduplication. Duplicate source events can both remain. |
| Equal-time events | LINQ's stable ordering preserves their concatenated order in the stored array; do not use this as a broader gameplay-order guarantee. |
| Output frame rate | From the body. Input rates must match. |
| `AnimationClipSettings` | Copied from the body through `AnimationUtility.Get/SetAnimationClipSettings`. Secondary settings are not merged. |
| Output object name | From the recipe. |
| Unanimated properties | Not populated with newly invented rest/default curves. Runtime defaults and state behavior remain relevant. |

The current importer explicitly copies curves, object tracks, events, and
`AnimationClipSettings`. It does not promise to clone every serialized field or
arbitrary metadata on both source `AnimationClip` objects. For example, there is
no independent assignment of `legacy` or `wrapMode` in the composition code.
Audit any extra clip property your receiving project depends on.

### 4.5 Where the result lives

The importer registers the generated clip as its main imported object with the
subasset identifier `clip`. The generated payload is stored in Unity's import
cache, while the small `.genshinclip` remains the source asset you reference.

This avoids another full merged YAML `.anim` file for every action. It does not
eliminate the imported clip's memory cost. Do not copy `Library/` to transfer
the project: transfer the source recipe, its dependencies, importer, and Unity
metadata, then let the receiving project regenerate the imported result.

### 4.6 Why two Animator layers are not an equivalent replacement

A historical implementation experiment used synchronized Animator layers.
Recorded playback comparisons changed, and that approach was rejected. Layer
weights, masks, defaults, humanoid evaluation, and state timing introduce behavior
beyond the binding replacement defined above.

Use the composed clip as one motion when reproducing this export contract.
Your project may subsequently blend complete actions or add a deliberate look/aim
layer. That is a separate animation design decision and requires its own tests.

## 5. Rig, Avatar, and binding requirements

### 5.1 The model and the Avatar perform different jobs

The FBX supplies the mesh, skeleton, skin weights, bind matrices, additional
transforms, and exported morphs. Native `.anim` clips supply motion. The
character recipe supplies source Avatar calibration and human-bone mappings.

The importer temporarily applies the source Avatar skeleton pose and constructs
a Unity `HumanDescription`. The recipe includes:

- Node paths, names, parent indices, local positions, local rotations, and scales.
- Human-bone and finger mappings.
- Source human limit axes, lengths, and angular limits; limits are converted
  from radians to degrees when building the Unity description.
- Arm/forearm and upper/lower-leg twist settings.
- Arm/leg stretch, feet spacing, and translation-degree-of-freedom setting.
- Source `humanScale`, checked against the reconstructed Animator.

It calls `AvatarBuilder.BuildHumanAvatar`, validates the result as a valid human
Avatar, saves `Rig/CharacterAvatar.asset`, and restores the imported model's
previous transform pose before saving the prefab. This avoids leaving the mesh
in an Avatar-calibration pose that disagrees with its skin bind pose.

Missing recipe nodes can be created while reconstructing the hierarchy, subject
to valid parent ordering. That behavior does not prove an unrelated mesh is
compatible: adding a transform does not repair absent skin weights, missing morphs,
or an incompatible bind matrix.

**Practical rule:** use the generated prefab and its own Avatar first. Do not
replace its Avatar with a newly guessed Humanoid mapping and assume the motion is
unchanged. Do not freeze the model in the calibration pose to “fix” a rest-pose
problem.

### 5.2 Humanoid retargeting does not solve every channel

Humanoid muscle properties are evaluated using the Avatar. Other tracks can
address exact transform or renderer paths relative to the Animator GameObject.
These can include extra garment bones, hair bones, accessories, eyebrows, or face
blend shapes, depending on the actual clip.

For a path such as:

```text
<Animator root>/Offset/Face
```

the native curve stores the part relative to `<Animator root>`. Inserting an
additional wrapper **between the Animator and the bound object** can break it.
Putting the entire prefab beneath a gameplay parent without moving the Animator
or its children preserves the internal relative paths.

Likewise, two characters with compatible humanoid bodies can have different
extra-bone names or face morph names. Sharing one body-type clip does not prove
that their secondary clips can be exchanged.

### 5.3 Facial bindings need both a target and a property

A blend-shape curve requires:

1. The correct renderer path relative to the Animator.
2. A `SkinnedMeshRenderer` at that target.
3. A mesh containing the exact named blend shape.
4. A curve bound to `blendShape.<that name>`.

Standalone native clips can initially carry a numeric CRC instead of the name.
The current `GenshinBlendShapeCurveBindings` resolver uses morph channels from
the model being exported, per renderer path, to recover names. It strips the
exported model-root prefix when producing Animator-relative paths. It rejects
ambiguous CRCs and missing channel names on a matched renderer.

If the curve targets a renderer path absent from the exported model's morph
inventory, the resolver leaves it unresolved because the clip might reference
another model part. That is an explicit reason to inspect remaining numeric
`blendShape.<number>` bindings; successful export alone is insufficient.

The character importer's `missingCurvePaths` report currently checks nonempty
**Transform** curve paths. It does not fully audit renderer existence, blend-shape
names, object-reference targets, or semantic humanoid coverage. An empty array
does not certify facial animation.

### 5.4 Native paths, coordinates, and scale

The native animation converter decodes ACL/streamed/dense/constant curve data
and resolves path hashes through source Avatar/Animator/Animation information.
Unresolved transform path hashes can become `path_<hash>` placeholders. Treat
these as unresolved targets, not as bone names to add blindly to the hierarchy.

For the native `.anim` path, decoded vector/quaternion components, tangents, and
times are written without an extra axis swap or scale multiplier in the curve
writer. The model FBX export separately uses its `scaleFactor = 100` setting and
does not embed these animations. The Avatar recipe retains source local pose
values. These source-code observations explain why the different artifacts must
be kept together; they are not an instruction to multiply imported animation
translations by 100.

Use the generated Avatar's human-scale check and measured model/pose results to
validate units. If porting to another coordinate system, make conversion an
explicit separate step and test it. Do not apply an FBX unit conversion a second
time to native Unity curves merely because the FBX exporter uses that setting.

## 6. Files and dependency layout

For a fresh current export, a useful destination layout is:

```text
Assets/
  ImportedCharacters/
    Generic/
      Editor/
        AnimeStudioClipCompositionImport.cs
      Animations/
        Girl/
          Ani_Avatar_Girl_<action>__<12 hex characters>.anim
        Boy/
          ...
    Mona/
      Avatar_Girl_Catalyst_Mona.fbx
      Avatar_Girl_Catalyst_Mona.character.json
      Avatar_Girl_Catalyst_Mona.prefab          [generated by Unity]
      Animations/
        Ani_Avatar_Girl_Catalyst_Mona_<action>_<pathID>.anim
        shared-index.json
      Editor/
        AnimeStudioCharacterImport_<unique id>.cs
      Rig/                                   [generated by Unity]
        CharacterAvatar.asset
        Character.controller
        <character clip filename>.genshinclip
      Materials/
      Textures/
      manifest.json
      character-export.json
      shared-assets.json
      UNITY-IMPORT.txt
      unity-import-report.json               [generated by Unity]
```

The FBX's exact name is the recipe's `model` value; use that value if a particular
export differs from the illustrative tree.

### Shared storage and identity

The current exporter pools matching shared `.anim` files under
`Generic/Animations/<body type>/`. It removes a trailing numeric path-ID suffix
from the readable filename and appends the first 12 hexadecimal characters of
the file's SHA-256. It verifies the **full** hash when reusing an existing target.
It does not overwrite an existing payload or its `.meta`.

The pool's identity is based on exported file bytes and the naming/category
scheme. It is not an abstract claim that two differently serialized clips are
semantically identical. Names alone are not sufficient identity either.

After pooling, the exporter rewrites the corresponding `sourceClips[].file` in
`manifest.json` and `clips[].file` in `.character.json`, writes sharing indexes,
then removes the newly created duplicate local copies. Character-specific source
clips remain in the character folder.

**Current materials/textures stay character-local.** Older research notes and
older exports used a different pooling policy and hash-directory layout. Do not
move files to imitate a historical screenshot or folder tree; follow the paths
in the actual recipe and inspect the accompanying importer version.

### Why `.meta` files matter when moving an existing Unity import

For an already imported package, preserve `.meta` files with the model, clips,
recipes, Avatar, controller, prefab, textures, and materials. Unity references
use asset GUIDs; copying only filenames can break those references or create
duplicate identities.

For a fresh export that has never been imported, the receiving Unity project
generates the initial metadata. Do not manufacture GUIDs by editing Unity YAML.
Move or remap existing Unity assets through the Editor when reorganizing them.

Keep one compatible `AnimeStudioClipCompositionImport` implementation per
project. The shared class name and extension registration are not per-character.
Each character importer, in contrast, is given a unique generated class name.
Blindly copying a second `Generic/Editor` from another export can create duplicate
classes or conflicting importer versions.

The exporter refuses to write into an output parent whose existing shared
composition-importer source differs from the current build. Use a new export
parent and review the difference; do not silently replace a working project's
importer during asset transfer.

## 7. Export and import in another Unity project

### 7.1 When reusing an already working import

1. Identify the accepted character prefab, its source `.character.json`, and the
   exact shared files referenced by that recipe.
2. Transfer the complete character folder and sibling `Generic` folder beneath
   the same parent in the destination project's `Assets/`, preserving existing
   `.meta` files. Transfer your own runtime/controller assets separately too.
3. Keep one compatible shared composition importer. Check for an existing copy
   before adding another character set.
4. Allow Editor scripts and asset imports to finish. The character importer runs
   in Edit mode and waits while the Editor is compiling or updating.
5. Read the newly written `unity-import-report.json`, not an old report copied
   from the source project. Check Console errors as well.
6. Instantiate the generated prefab and verify motion before replacing its
   controller, Avatar, hierarchy, or mesh.
7. Run the acceptance checks in section 12 in the receiving Unity version.

Copying the complete shared folder is the straightforward transfer route. For a
smaller handoff, the exact dependency closure is sufficient: every shared file
referenced by the selected character recipes/compositions, their metadata, and
the compatible shared importer. Do not prune by action name alone or remove a
pooled file still used by another character.

The current resolver requires dependencies under `Assets/`. Copying these export
folders into a UPM `Packages/` directory without adapting the importer is not an
equivalent supported layout.

### 7.2 When exporting from the current AnimeStudio build

The implemented CLI entry point is:

```powershell
# Example paths; choose a current map and a NEW output folder.
$cli = 'C:/Tools/AnimeStudio/dist/net10.0-windows/AnimeStudio.CLI.exe'
$map = 'D:/GameStudy/Maps/genshin-current.map'
$destination = 'D:/GameStudy/Exports/CharacterSet/Mona'

& $cli --genshin-character $map 'Avatar_Girl_Catalyst_Mona' $destination --animations
```

If the map contains multiple Animator entries with that name, select exactly
one using `AnimatorName@PathID`. Use the actual map's path ID; do not borrow an
ID from another game build.

Supported options on this character entry point are `--animations`, `--voices`,
`--vfx`, and `--no-materials`. Animations are opt-in. The GUI exposes
**Export unity animations** in the character export workflow and uses the same
underlying export logic.

The destination must not already exist. Multiple characters can share one new
export parent so they reuse its sibling `Generic` folder. Preserve the recorded
game version, map/source fingerprint, options, exporter build manifest, and
`character-export.json` with the delivered assets.

Use this fork's matching GUI/CLI distribution and managed dependencies. A
similarly named upstream executable or an older local build may not contain this
character workflow or the latest binding fixes. This guide documents inspected
source; it does not certify an arbitrary executable on disk.

### 7.3 What Unity generates

The character importer creates or updates:

- A source-calibrated Humanoid Avatar at `Rig/CharacterAvatar.asset`.
- A generated `Rig/Character.controller` with one `Base Layer`.
- One state per recipe source clip, with `writeDefaultValues = true`.
- Composition recipes for recognized pairs; the **character state's motion**
  becomes the composed clip. The shared source clip retains its separate state.
- A character prefab using this Avatar and controller.
- An import report with success/error, prefab path, human scale, state/clip count,
  composed-pair count, mesh count, missing Transform paths, and disabled objects.

The preferred default is the human-motion character `Standby` state; otherwise
the first encountered human-motion state is used. A recipe lacking human motion
should not be assumed to have a useful idle default.

The importer sets `Animator.applyRootMotion = false` and
`Animator.cullingMode = AlwaysAnimate`. It can disable auxiliary mesh objects
such as `EffectMesh` and meshes attached beneath armature bones. Inspect
`disabledObjects` before concluding an animated accessory was lost.

It does not rebuild the game's original transition network, blend trees,
parameters, weapon attachment logic, combat system, or action-event dispatcher.

### 7.4 Generated assets versus your own authored assets

The importer rebuilds its controller's layers and subassets when its dependencies
change. It can save the generated prefab again as well. Treat those assets as
generated output.

Keep your gameplay controller, prefab variant/wrapper, scripts, and overrides in
your own project folder. Reference the imported motions there. Changes made
directly to the generated controller can disappear on reimport.

The importer caches a dependency signature under `Library/AnimeStudio/` and
tracks its source recipe, **character importer**, model, clips, and referenced textures. A
successful old report is not proof that a moved/mutated dependency imported
correctly today. Conversely, deleting generated output or changing dependencies
can trigger regeneration. Avoid using the generated controller as a manual
authoring workspace.

That character-import signature does not include the shared composition-importer
source or generated `.genshinclip` text directly. The ScriptedImporter has its
own asset/dependency handling. After upgrading the composition importer,
explicitly reimport and inspect the affected compositions in the Editor and
rerun representative playback checks; do not use an unchanged character report
as proof of a new composition check.

There are three versioned pieces to keep aligned: the exporter that chooses
source clips, each exported character importer that chooses pairs, and the
shared importer that merges the chosen pair. Updating only the third does not
add missing weapon-class source clips or upgrade an older character pairing
policy. Prefer a new, identified export/import when changing that contract.

## 8. Using the clips in a game controller

### 8.1 Use the complete character action

For a paired action, assign the imported `.genshinclip` motion to your custom
controller state. Selecting only the shared body `.anim` can omit the
character's secondary animation. Selecting only the secondary `.anim` can omit
the body motion.

For a character clip classified as body-bearing and left unpaired, assign its
original `.anim`. Do not attach a shared body just because one has a similar
name; that changes the implemented contract.

Build a per-character action map rather than hardcoding one filename layout:

```text
project action   source character state                         selected motion
idle             Ani_<character>_Standby                        composed or own clip
run              Ani_<character>_RunCycle                       composed or own clip
attack           exact action verified in this character recipe original or composed
```

Choose each row from actual recipe/imported-state data. Some characters do not
provide the same named actions, and sparse source clips can require additional
investigation.

### 8.2 Previewing a generated state

This is a small illustrative Unity C# snippet for a prefab that still uses the
generated controller:

```csharp
Animator animator = characterRoot.GetComponent<Animator>();
string statePath =
    "Base Layer.Ani_Avatar_Girl_Catalyst_Mona_RunCycle";
int stateHash = Animator.StringToHash(statePath);

if (animator == null || !animator.HasState(0, stateHash))
    throw new InvalidOperationException("Requested imported state is unavailable.");

animator.Play(stateHash, 0, 0f);
```

Supply `characterRoot`, reference `UnityEngine`, and qualify/import `System` for
`InvalidOperationException`. Use a state verified in your export. The state name
has no `__WithBody` suffix even when its motion is a composed clip.

This does not create movement, transitions, or effect timing. The snippet was
not executed as part of preparing this guide.

### 8.3 Movement and transitions belong to the receiving project

Decide how locomotion moves the gameplay object. With the generated
`applyRootMotion = false` default, your movement controller normally supplies
world translation/rotation while the animation supplies the pose. If you choose
root-motion-driven movement, inspect the actual root curves/settings and test
that policy explicitly.

Similarly, choose your own idle/run thresholds, crossfade durations, attack
interruptions, and grounded/airborne rules. These values are not recovered merely
by exporting clips. A generated state list is not a reconstructed state machine.

When testing transitions, include return-to-idle and interrupted actions. A
property absent from a clip can retain or receive a default value depending on
Animator state configuration. The generated states write defaults; changing that
policy in your own controller can expose persistent accessory or facial values.

### 8.4 Rendering is a separate dependency

The animation package does not require RenderDoc fixtures or this shader-study
project to combine its clips. Correct visual appearance does require suitable
materials/shaders. The ordinary exporter supplies preview material integration;
it does not itself recreate the complete Genshin rendering pipeline.

If you reuse the shader package, install its required runtime support separately
and follow its material/rig setup documentation. Do not treat a missing face,
bad skin bind, or unmapped blend-shape curve as something to compensate for in
the shader.

## 9. Root motion, mirroring, facial motion, and other edge cases

### Root motion and IK

Root/IK channels can exist in either input. They are not filtered out by the
composer. An exact secondary binding collision wins even when the binding is
root motion. The final output's clip settings come from the body.

The source `.anim` exporter preserves the relevant native curves and settings;
the prefab's `applyRootMotion` flag is a separate runtime choice. Setting it
false does not erase root curves from the clip. Do not both apply animation
displacement and independently integrate the same displacement in your movement
controller.

The compositor does not calculate foot placement against a new environment,
reconstruct a procedural IK controller, or resolve contacts. Verify foot sliding,
root rotation, vertical displacement, and loop seams with the actual locomotion
policy used by the destination project.

### Mirrored variants

`+Mirror` remains part of the complete suffix. A character `RunCycle+Mirror`
candidate seeks the corresponding shared `RunCycle+Mirror`, not plain
`RunCycle`. Source clip settings include mirror-related information; the composed
clip uses the body's settings.

The composer does not implement a new left/right bone swap or negate positions
because a filename says `Mirror`. Do not add a second mirroring operation without
inspecting the imported motion.

### Additive-looking names and phase suffixes

Names such as `_Add`, `_Adjust`, `_AS`, `_BS`, `_Loop`, or other action fragments
are useful search leads. This importer treats their complete strings as identity;
it does not decode them into layer mode, phase duration, transition direction,
or blend weights.

Do not put every `_Add` clip into an additive Animator layer by assumption.
Establish the reference pose, channels, settings, and intended timing first.
The composition algorithm itself performs no additive-pose conversion.

### Facial expressions

Facial motion may use bones, blend shapes, or both. A character-named clip is not
automatically the full facial performance for a voiced line. Body/secondary
composition does not synthesize lip sync or expressions absent from the inputs.

When moving a face mesh or assembling modular parts, validate its renderer path
and morph names independently of body animation. A body that moves correctly
with a frozen face often indicates a different binding problem than a frozen
humanoid skeleton.

### Hair, clothing, and accessories

Additional bone curves survive if the targets exist. Their presence is not proof
that all secondary motion is authored in the clips. The workflow does not infer
missing procedural cloth, collision, spring, wind, or hair simulation from the
generic body animation.

An accessory can also be inactive by the importer's default auxiliary-object
policy. Check both activity and binding before changing animation data. Enabling
every exported effect mesh indiscriminately is not a recovered visibility system.

### Events, audio, weapons, and VFX

An event retained on an `AnimationClip` is only data. The receiving game must
provide its intended receiver and behavior. The composer concatenates events,
so audit duplicates before driving damage, footsteps, or sound from them.

Separate exported voice/VFX assets and source action files do not automatically
become timeline hooks. Attachment sockets, weapon visibility, projectile
creation, effect phases, interruption behavior, and audio scheduling require
explicit integration backed by their own source data.

### Clip size reduction

The character export path requests compact animation output. Historical
optimization retained moving curves and humanoid root/IK key topology and reduced
only a conservative class of wholly constant curves. It also compacted YAML
without changing numeric tokens.

The current constant-curve reducer requires at least three keys, strictly
increasing finite key times, identical finite value bits, unweighted keys, and
zero incoming/outgoing tangents. It removes interior keys only when the entire
curve qualifies. It skips scalar property names ending in `.x`, `.y`, `.z`, or
`.w`, retaining component-curve topology used by humanoid root/IK channels.
Moving curves, including flat stretches inside them, keep their keys. The native
conversion also trims Genshin padding samples after the authored stop time;
the composition importer itself performs no trimming.

This is separate from composition. Replacing native curves with a guessed uniform
bake, reducing flat stretches inside moving curves, or rounding numeric values
can change playback. Earlier attempts at broader curve reduction failed runtime
comparisons. Preserve the current exported curves when transferring assets.

## 10. Porting the combination algorithm

### Reimplementing it in another Unity tool

To reproduce the present contract, retain this sequence:

```text
validate source identity and classification
try weapon-class name, then general body-type name, with the full action suffix
within each examined name, filter by timing + body flag
fail on multiple compatible entries; use the first tier with exactly one
resolve source clips and reject invalid/missing dependencies
validate actual clip length and rate
copy body curves into binding dictionaries
copy secondary curves into the same dictionaries, replacing collisions
copy object-reference tracks with the same precedence
concatenate events and order them by time
copy body AnimationClipSettings and frame rate
create one imported clip and preserve stable asset references
play that clip with the matching calibrated Avatar/hierarchy
```

Do not reduce the dictionary key to only the property name or bone's leaf name.
Do not silently choose the first entry when several match one candidate name.
For a new tool, it is useful to emit an explicit unpaired-action report: the
current importer throws on ambiguity but still falls back to the original
secondary motion if neither tier supplies a compatible body.

For independent verification, compare the result's bindings against the expected
union. On collisions, compare against the secondary curve; otherwise compare
against the body. Check object-reference arrays, event arrays, and clip settings
as separate categories. Playback testing is still needed after structural checks.

### Moving to a non-Unity engine

The abstract channel-union rule is portable. Unity's humanoid muscle evaluation
is a separate requirement. A native `.anim` containing muscles is not simply a
list of final bone-local rotations that another engine can copy directly.

Two possible integration approaches are:

1. Implement an equivalent evaluator for the humanoid representation and its
   Avatar calibration, plus the explicit transform/renderer curves.
2. Evaluate the accepted composed clips on the exact character rig in Unity and
   build a separately validated skeletal/morph animation conversion pipeline.

Neither non-Unity approach is supplied or validated by the current character
exporter. Its documented production path preserves native `.anim` plus source
Avatar metadata instead of promising a humanoid FBX bake.

If you add a conversion pipeline, explicitly handle coordinate-system handedness,
units, local versus world transforms, skeleton rest/bind relationships,
quaternion interpolation/sign continuity, root trajectory, morph channels,
sample times, loop boundaries, events, and object references. Compare evaluated
poses/vertices against Unity before claiming equivalent playback. Storage-level
composition success does not prove a correct engine-to-engine conversion.

## 11. Manekin characters

The Manekin work has its own source-selection and Avatar-recovery details. It
should not be described as automatically using the named-character
`Ani_Avatar_<body>_<weapon>_<character>` pairing convention above.

The recorded Phase 1 selected-look runtime uses three native motions per look:
idle, locomotion, and an expressive `UAngry02` action. Source research found:

| Source family/action | Recorded duration/rate | Relevant observation |
| --- | --- | --- |
| Girl Standby | 2 seconds, 60 Hz | Body-bearing selected source. |
| Girl Walk | About 1.1 seconds, 60 Hz | Selected locomotion source. |
| Boy Standby | About 1.8333 seconds, 60 Hz | Exact source selection matters. |
| Boy Run | About 0.6667 seconds, 60 Hz | Used because the indexed Beyd names did not contain Boy `Normal_WalkCycle`. |
| Boy/Girl `UAngry02` | 3 seconds, 60 Hz | Recovered clips include body channels, eight eyebrow Transform paths, and eleven Face blend-shape curves. |

A same-named alternate Standby source had only seven humanoid bindings, while
the selected body-bearing clips had 139 Animator muscle-clip bindings in the
source study's broader count. That broader count is not the same as the narrow
42-through-136 test used by `HasBody`.

The Boy Store Animator had a null Avatar and the Girl Common source lacked an
Animator; the study recovered external Prototype Avatars. Shared humanoid
compatibility did not establish every outfit-specific bone. A selected Girl
modular face hierarchy also needed a path correction, and standalone facial CRC
bindings needed source-backed name recovery.

For another project, transfer the accepted Manekin prefabs, exact source clips,
Avatar data, facial-path fixes, and assembly metadata together. Do not assign a
generic named-character recipe to an arbitrary multipart look because its body
type is also `Boy` or `Girl`.

The canonical details and exact asset identities are in
[Manekin-Phase1-Source-Study.md](Manekin-Phase1-Source-Study.md). The shader package's
`Docs/Genshin/Manekin-Implementation.md` and
`Docs/Genshin/Manekin-Phase1-Coverage.md` own the Unity assembly/runtime acceptance
record. Phase 2 complete-catalog work is separate and was not resumed for this
document.

## 12. Acceptance checks and troubleshooting

### 12.1 Checks for each transferred character

These are recommended receiving-project checks, not new tests claimed by this
documentation session.

| Check | Required evidence |
| --- | --- |
| Import freshness | New successful report and no relevant Console/importer errors. |
| Dependency closure | Every recipe file resolves under Assets; source and composition importers are present once as appropriate. |
| Avatar | Valid human Avatar on the intended Animator; expected human scale; correct bind/rest appearance. |
| Pairing | Expected action maps to the intended body and secondary sources; ambiguous/unpaired cases listed explicitly. |
| Timing | Recipe and actual clip length/rate agree; loop seam and first/last frame checked. |
| Binding coverage | Body muscles, extra Transform paths, renderer paths, morph names, and object-reference tracks audited separately. |
| Rest pose | Unanimated prefab has no garment/body distortion or unexpected offsets. |
| Body motion | Idle, locomotion, a turn/mirror action where present, and a character-specific action visibly work. |
| Face/accessories | A clip that actually contains expression/accessory channels visibly drives those targets. |
| Root policy | Displacement, root rotation, scale, and contacts behave with the project's selected movement policy. |
| State changes | Play, crossfade, interrupt, and return to idle without persistent unwanted values. |
| Reimport | Reimport changes regenerate compositions while your separate gameplay controller stays intact. |
| Runtime build | Referenced imported clips play in an actual target-platform build, with the required assets included. |

For a reusable library, repeat the relevant checks on another character of the
same body type and on a different body type. This distinguishes shared storage
reuse from proven cross-character compatibility.

When investigating a visible difference, keep a baseline and change one input:
full composed clip versus body-only, correct Avatar versus candidate Avatar, or
unchanged hierarchy versus modified hierarchy. Record which state, clip time,
root-motion policy, and model version produced each result.

### 12.2 Common symptoms

| Symptom | Likely checks and next action |
| --- | --- |
| Hair/garment moves but body is static | Confirm the selected motion is the composition, and whether a unique body candidate passed the exact suffix/timing checks. |
| Body moves but character-specific detail is static | Confirm the state is not the raw shared-body state; inspect secondary bindings and target paths. |
| Face is frozen while body works | Inspect renderer path, actual mesh morph names, and numeric/unresolved blend-shape CRC bindings. Empty `missingCurvePaths` is insufficient. |
| Entire mesh is distorted before animation | Inspect bind pose, model export, and restoration after Avatar calibration. Do not first tune animation curves or shaders. |
| Limbs move incorrectly despite valid import | Verify the exact source Avatar, human scale/limits, source clip identity, and rest/calibration distinction. |
| Character moves twice as far | Check whether gameplay displacement and applied root motion are both moving the object. |
| Character stays in place | Generated root motion is disabled; inspect the destination movement policy before concluding root data is missing. |
| `Missing shared dependency` | Copy the sibling Generic folder and preserve relative paths; inspect the recipe's actual file field. |
| `Composition dependency is outside Assets` | Import under Assets or deliberately adapt/validate the importer for another asset root. |
| `Composition clips have different timing or sample rates` | Compare actual imported input lengths/rates and source versions; do not stretch one automatically. |
| Import succeeds but some actions are partial | Audit `body: false` clips without exactly one compatible match; the current importer falls back to the source clip. |
| `Ambiguous body clip` | Multiple compatible entries matched one exact candidate name; resolve their source identity instead of choosing by array order. |
| New exports choose a different body for an action | Compare importer versions and the weapon-class-first versus older general-only policy; audit both candidates and validate the new result. |
| Duplicate class/importer compilation errors | Check for multiple global composition-importer copies or incompatible export versions. |
| Missing accessory despite valid curves | Inspect `disabledObjects`, object activity, renderer visibility, and attachment target. |
| Custom transitions disappear after reimport | Move authored logic out of generated `Rig/Character.controller` into a separate controller. |
| Motion works but sounds/effects never fire | Asset export and clip composition do not supply your event receivers or original action scheduler. |

## 13. Evidence, limits, and source references

### 13.1 What was checked for this guide

This was a read-only implementation/evidence investigation followed by Markdown
authoring. This task changed no exporter source, character asset, scene, or
animation behavior. A concurrent source update introduced weapon-class-first
selection during review; the guide was reconciled to the final fingerprints
below, with existing imported examples kept distinct. No fresh export, curve
comparison, playback capture, or player build was performed for the guide.

The exact-project Unity connection check passed through the approved local CLI
route. The read-only environment query reported Unity **6000.6.3f1**, URP with
`Assets/Settings/PC_RPAsset.asset`, PC quality, Linear color, and Direct3D12. The
Editor was outside Play mode with a clean scene. This establishes access and
environment identity, not animation fidelity in a receiving project.

### 13.2 Historical validation that supports the approach

The preserved shared-assets records report a Mona library with **546 source
clips and 225 imported compositions**, and zero missing Transform curve paths
in the recorded full import. A bounded six-action comparison evaluated
**4,547,835 curve samples with zero difference**, identical sampled bone
positions/rotations, and only sub-0.003 mm vertex rounding differences. The
historical source-library size fell from **1,722.89 MiB to 758.00 MiB**.

The currently installed Mona `unity-import-report.json` also records
`success: true`, `clips: 546`, `composedPairs: 225`, `meshes: 21`, and
`missingCurvePaths: []`. Reading that existing report confirms the recorded
import result; this session did not rerun that full import.

These are historical checks of the recorded export/import and optimization
work. They do not mean every clip was compared frame-for-frame against the game,
that all characters were validated, or that another Unity version will produce
identical results. The historical record limits its real-client fidelity coverage
to Mona. It also records the rejected direct-layering approach.

Later character-fix notes record bind-pose tests for Mona, Vesna, and Ineffa,
with six absent Ineffa thumb-curve paths still reported. Do not turn an idle-pose
pass into a full-motion claim for that rig.

The selected Manekin Phase 1 player run
`20260930T020827892Z-69e87c0fe920493d93cb6f02d05e084a` records runtime checks for
20 looks on D3D12 and D3D11, including native body and expressive face/brow
animation. Its whole-model aligned game fidelity remains unmeasured. This
Manekin coverage does not validate every named-character composition.

Historical reference files, retained at their existing locations:

- [Shared-assets production record](Export/shared-assets-production-pr.md).
- [Shared-assets research record](Export/shared-assets-research-pr.md).
- [Later character-fix record](Export/character-fixes-production-pr.md).
- [Manekin source/Avatar/facial account](Manekin-Phase1-Source-Study.md).

Older records can describe superseded storage layouts. The current source below
governs this guide's implementation details.

### 13.3 Current source map

Paths are relative to the AnimeStudio repository. Function names are supplied so
the references remain useful as line numbers change.

| Source | Relevant responsibility |
| --- | --- |
| [GenshinCharacterCommand.cs](../../AnimeStudio.CLI/GenshinCharacterCommand.cs) | CLI flags and exact Animator selection, including `@PathID`. |
| [GenshinCharacterExporter.cs](../../AnimeStudio.Utility/GenshinCharacterExporter.cs) | `SelectClips`, loaded-clip deduplication, native export orchestration, export manifest. |
| [GenshinModelExporter.cs](../../AnimeStudio.Utility/GenshinModelExporter.cs) | Model/native `.anim` output and source provenance. |
| [GenshinUnityPackage.cs](../../AnimeStudio.Utility/GenshinUnityPackage.cs) | `DescribeAvatar`, `HasBody`, recipe generation and importer deployment. |
| [GenshinSharedAssets.cs](../../AnimeStudio.Utility/GenshinSharedAssets.cs) | `Store`/`Package`, shared-byte identity and relative-path rewriting. |
| [GenshinUnityImport.cs.txt](../../AnimeStudio.Utility/GenshinUnityImport.cs.txt) | Avatar construction, pair recognition, generated controller/prefab/report. |
| [GenshinClipCompositionImport.cs.txt](../../AnimeStudio.Utility/GenshinClipCompositionImport.cs.txt) | Complete binding merge, event/settings policy, dependency tracking. |
| [AnimationClipConverter.cs](../../AnimeStudio.Utility/YAML/AnimationClipConverter.cs) | Native curve decoding and conversion to Unity animation properties. |
| [AnimationClipExtensions.cs](../../AnimeStudio.Utility/YAML/AnimationClipExtensions.cs) | Native animation YAML serialization and compact output. |
| [GenshinConstantCurveReduction.cs](../../AnimeStudio.Utility/GenshinConstantCurveReduction.cs) | Conservative constant-curve reduction; verify the implementation when modifying compression. |
| [GenshinBlendShapeCurveBindings.cs](../../AnimeStudio.Utility/GenshinBlendShapeCurveBindings.cs) | Resolve model-specific standalone blend-shape CRC bindings. |

The inspected checkout's HEAD was
`9e69ff5c1b59f20003b609f961b3930cad1c6789`, on `codex/weapon-export`, with existing
uncommitted changes. **HEAD alone does not identify the inspected implementation.**
For the core composition contract, these are SHA-256 fingerprints of the files
read for this guide:

```text
GenshinCharacterExporter.cs
60ACD13F0F1BBC0C3D02F526B1A16D86BCBA67E83FA455E83EE8EC63E28CC4FB
GenshinSharedAssets.cs
6443C66197000A9E8818EADB77E03A6BA02C4DD055DD3A5ECB0B16F285CFFBC0
GenshinUnityPackage.cs
9456BB451AABBB9580CC17517AC5C0653D320A84DD434EEB999D770CFC7C9D88
GenshinUnityImport.cs.txt
D3E5B341B4AC0FA49FE2DE5D1E35A5647D0F2B32EE23F9348136AA92D7E59DAE
GenshinClipCompositionImport.cs.txt
03363C216FC696F9829AD9D17E4401AA8A5F8E2B95894D15ADAFE7AC687C91DD
```

For a handoff to another project, deliver this guide alongside the exported
source recipe/manifests, matching importers, exact asset dependencies and metadata,
the exporter build identity, and that project's fresh acceptance results. That
keeps clip storage, pairing behavior, and demonstrated playback independently
traceable.
