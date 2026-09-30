# Native Unity animation export

The current character workflow exports source `.anim` clips plus a model and source Avatar recipe. **Humanoid FBX baking has been removed at the user's request.** Earlier Blender/baked-FBX checks in the historical progress report do not describe the current UI.

Use **My tools > Genshin character > Export unity animations**, then **Export Character**. Copy the character folder and its sibling `Generic/` into the same parent in Unity's `Assets`. The generated importer builds a humanoid Avatar, controller and prefab, and pairs body-only/secondary-only clips when action names, durations and sample rates match uniquely.

A standalone `.anim` preserves humanoid curves but needs a valid humanoid Avatar to play correctly. The exported package supplies that setup automatically. The FBX carries the model and rig, not baked animation. Character clips live under `Animations/`; shared body clips live under `Generic/Animations/<body type>/` with readable filenames and short content-hash suffixes. The character recipe and model manifest resolve the actual paths.

Storage optimization uses compact YAML without changing numeric tokens, and reduces only wholly constant, finite, unweighted curves with zero tangents to their original endpoints. Every key of every moving curve is retained, including its flat stretches. Humanoid root/IK scalar vector/quaternion channels retain their sampling topology. There is no resampling, precision rounding, or tolerance-based reduction. Other export paths retain their existing behavior unless explicitly opting into this policy.

For paired actions, `Rig/*.genshinclip` contains small relative-reference recipes. The shared ScriptedImporter creates the combined native AnimationClip in Unity's `Library` import cache and registers both source dependencies. Controllers reference that imported clip. No duplicate `Rig/*__WithBody.anim` is written. This reduces source/export storage; imported runtime compositions still consume cache space and memory.

Direct synchronized Animator layers were tested but differed from the established merged-clip playback, including with untouched source curves. They are not enabled. The cache-based composition keeps the established evaluation behavior, secondary-binding precedence, body clip settings and events. This is an intentional fidelity-driven change from the initial layering proposal; it does not claim to reconstruct the game's native animation controller.

The importer preserves float curves, object-reference curves, events and clip settings when combining layers. It retains source rig hierarchy/local pose, human limits, twist/stretch and scale metadata. It validates clip duration and reports missing transform paths instead of claiming completeness from successful serialization alone.

See [character export](character-export.md), [storage validation](shared-asset-validation.md), and [earlier playback validation](character-workflow-validation.md). Phase 2 shader/effect recreation and any later Unity-to-Blender workflow are separate tasks.
