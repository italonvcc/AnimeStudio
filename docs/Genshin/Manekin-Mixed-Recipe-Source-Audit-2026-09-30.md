# Manekin Phase 1 garment recipe source audit — 2026-09-30

This is the source/export account for the twenty selected garment states. The
package's `Docs/Genshin/Manekin-Implementation-Plan.md` owns their Unity and
rendering acceptance. A recipe here is an explicit, source-qualified assembly;
no native saved-outfit registry is claimed or required for these custom looks.

The frozen 7.1.0 source map has SHA-256
`A63564A788A006B45ACCFD18B609403A2581F7F246E275ECD942454E578862D0`.
The five selected S groups per sex, their seed `20260929`, source path IDs and
all 64 individual part exports are in
`%TEMP%/manekin-phase1-batch-pr3writer-20260929T2153`. The ten existing
full-head primary requests/oracles are in
`%TEMP%/manekin-ten-head-looks-guarded-pr3writer-20260929T2203`.
Their earlier alternate requests changed only Hair. The new alternate requests
change an actual garment: Top comes from the next selected S group while
Bottom, Shoe, accessories, Hair, brow and pupil stay with the row's primary
selection. Girl Face remains P0051. All source selections are exact
GameObject CAB/path-ID references, not names alone.

| Body | Primary Bottom/Shoe and other parts | Multipart Top |
| --- | --- | --- |
| Boy | S0133 | S0143 |
| Boy | S0143 | S0131 |
| Boy | S0131 | S0033 |
| Boy | S0033 | S0191 |
| Boy | S0191 | S0133 |
| Girl | S0082 | S0097 |
| Girl | S0097 | S0084 |
| Girl | S0084 | S0047 |
| Girl | S0047 | S0142 |
| Girl | S0142 | S0082 |

The Boy base `Beyd_Avatar_Boy_Suit_S0017_Store` is an assembled Store look,
not a bare body. Its exact source component probe at
`%TEMP%/manekin-boy-store-s0017-component-flags-20260930.json` reports
Headwear_S0017 and Backwear_S0017 GameObjects active with enabled
SkinnedMeshRenderers, along with its other S0017 garment renderers. The older
assembly requests removed S0017 meshes only where an incoming selected group
had that same slot. Thus the prior Boy S0133 oracle, for example, retained
Headwear_S0017 and Backwear_S0017. The source manifest makes those retained
meshes explicit. Their eventual native visibility/occlusion behavior remains
unproven; carrying unrelated Store geometry into a constructed selected outfit
is nevertheless an avoidable recipe error.

The corrected Boy requests explicitly remove every remaining S0017 garment
renderer, including the absent selected slots, through the assembler's exact
`removeMeshes` paths. This is a recipe correction, not an FBX or shader change.
The source assembler still checks rest frames, finite binds, mesh collisions,
and the guarded brow subtree. The five corrected Boy complete requests and
oracles are in
`%TEMP%/manekin-five-boy-primary-exclusions-source-audit-20260930`.
The ten mixed requests and oracles are in
`%TEMP%/manekin-ten-mixed-top-recipes-source-audit-20260930`. Copy each whole
run, including `request.json`, `manifest.json`, FBX, material JSON and native
texture sidecars, into a new local Unity `Assets/_Games/Genshin/Manekins/Source/`
run before importing. The primary Girl oracles stay at their existing source
run; their Common base contains body meshes but no retained S-group garment.

`primary-exclusion-audit.json` reports 5/5 successful Boy complete assemblies
with 121 resolved material bindings and 298/298 present native texture
property sidecars. `mixed-recipe-audit.json` reports 10/10
successful distinct garment combinations with 250 resolved bindings, zero
unresolved source references, zero native texture write failures, no remaining
S0017 garment mesh in Boy, and exact rotated Top source IDs and meshes. All
610 native texture property references in the mixed oracles resolve to existing
sidecars. The Boy complete audit SHA-256 is
`CE21F9D4AA6E44BBBBF941AF5FBD9F63F3A6EAFE174AB83D90BBFF0E2EAFAF5F`;
the mixed audit SHA-256 is
`EA435D40F656D06B2E0862AABC217BBB3B5F5376E63E49F70CD54A43E7ADBE1D`.
The current canonical distribution's build-manifest SHA-256 was
`F5AD4C97C18545165B6E498CFFFA15A5011B0C5B2B87728A2642C29B5325372C`.
The working checkout was `9e69ff5c1b59f20003b609f961b3930cad1c6789`
with unrelated dirty files; the build manifest, not that commit alone,
identifies the executable. No exporter source edit or rebuild was made here.

The independent P0057–P0061 pupil exports in
`%TEMP%/manekin-20-faceparts-pr3writer-staged-20260929T2153` resolve five
distinct source material path IDs and five different `_MainTex` ASTEX001
hashes. Boy and Girl roots have different GameObject IDs but reference the
same five Girl-named material IDs/textures. The renderer child is named
`Pupil_P0147` in each source root, so the child name is not the selectable
choice ID. The exact source root plus material/texture dependency is the
choice identity. This supports five distinguishable source-backed pupil
appearances per body without inventing Boy-only pupil textures.

The five selected SkinTone records are Girl-named P0151, P0154, P0156, P0161
and P0279. Exact captured color-buffer values match three Girl and two Boy
draws as recorded in `Manekin-Phase1-Source-Study.md`; only P0154/P0279 have
direct Boy capture matches. A custom Boy selection of the other three can
carry those exact source parameter values, but native Boy menu availability
and generated skin texture composition are unverified. Likewise the existing
18-cell/90-candidate grid is a source-ID grid, not 90 accepted rendered options.

For the six Eye Makeup, Lip Stick and Facial Makeup cells, the existing
`%TEMP%/manekin-30-makeup-source-inputs-v1-20260929.json` audits five exact
source configurations per body/category: 30 raw MonoBehaviour payloads,
their enabled flags, resolved texture PPtrs and 22 distinct native Texture2Ds.
Those are suitable inputs for a constructed appearance selector; the face
layering, masks and native composition equations still need runtime checks.
The ten Hair and ten eyebrow source roots are independently exported, while
the 20 pupil roots have the shared-material identity described above. Thus
all 18 cells have five concrete source identity leads, and the corrected
garment requests address the previously duplicate outfit-recipe role. The
remaining acceptance work is applying and verifying those choices in Unity,
not locating an unproven native menu registry.

The package Editor owner should import the corrected oracles and run
`Tools/Unity/AgentScripts~/ValidateManekinRecipeCoverage.cs` against the
imported source roots. That script uses the package's certified modular
assembler and compares rest and posed geometry with the source FBX for five
Boy corrected complete, five Girl existing complete and ten mixed states,
with exact source request/manifest and part identity checks.
No Unity import, prefab update, animation test, visibility comparison or
rendering claim was performed in this offline audit. In particular, active
base-body skin under the selected garments, nude submeshes, accessory clipping
and game wardrobe visibility rules still need the Editor owner to inspect.
