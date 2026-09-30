# Branch consolidation — 2026-09-30

The fork's default branch is `master`. Consolidation preserves the histories of
`ac7c0be` (master, including FBX tangent and shader-study changes) and `34f9cfa`
(weapon export, structured VFX, Manekin source work and metadata-only Unity imports).
All other local work branches are ancestors of those two tips. No upstream branches
are changed. A verified complete-history recovery bundle and original branch refs
are retained locally under ignored `artifacts/branch-consolidation/`.

## Merge decisions

- Keep the newer root agent policy, canonical build and metadata-only import contract.
- Retain the native FBX tangent/binormal and handedness changes, finalizer guard,
  GI 7.1 shader parsing, and newer shader-name fallback together.
- Resolve equivalent texture wrapping fields once; retain `m_ColorSpace = -1` so
  missing metadata is not presented as a known color space.
- Preserve property-keyed material/cubemap bindings and source renderer slots,
  alongside the older `NativeTextures/` companion export. These currently produce
  duplicate native texture files. Their validation rules differ; the older report
  does not certify the newer sidecar writer. Consolidating those writers is separate work.
- Reconcile two older shared regression fixtures with the current API: bind-pose
  conflicts carry typed source provenance, and only retained meshes constrain an
  incoming replacement's bind. Check both preservation and rejection behavior.

## Validation

- Canonical `./build.ps1`: passed, GUI/CLI Release distribution and dependency/native
  checks; build record `artifacts/build-records/20260930T144308133Z/`. Build ran on the
  resolved merge before its commit; its manifest records source hashes and dirty state.
- `Tests/Genshin/WeaponExportRegression`: 59/59 passed.
- `Tests/Genshin/ShaderNameRegression`, `BlendShapeBindingRegression`, and
  `MonaClipSelectionRegression`: passed.
- `docs/Genshin/tools/RegressionChecks`: 237 checks passed after the two fixture updates.
- Fresh CLI model export of the existing BoyStoreS0017 fixture: passed. Exact selector
  `Beyd_Avatar_Boy_Suit_S0017_Store@-242358498629974930`, existing local Genshin 7.1 map;
  output `artifacts/branch-consolidation/BoyStoreS0017/`.
- Export checks: versioned model descriptor, 25 material slots, 63 existing native
  texture references, zero generated C# scripts. Fifty native files represent 25
  source identities; each duplicate pair matches payload SHA-256, format, mip count
  and color space. Evidence: `artifacts/branch-consolidation/native-payload-check.json`.
- Build emitted existing NuGet dependency vulnerability warnings. Live Unity import,
  texture selection, animation playback and visual fidelity were not rerun for this
  Git consolidation. This record does not broaden previous Unity validation coverage.
