# Offline weapon exporter regressions

Run from the AnimeStudio repository. Tests never connect to Unity. Real fixtures
read the installed client; use a fresh ignored output directory outside Unity.

```powershell
dotnet run --project Tests/Genshin/WeaponExportRegression -c Release
```

For already built real-asset tests, invoke `WeaponExportRegression.exe`, not
`dotnet WeaponExportRegression.dll`: the existing native loader resolves libraries
beside the process executable. `dotnet run` also uses the application host.
The following modes call the production export service:

- `--real-batch <map> <new-output> <source-file>...`: the researched Amenoma and
  Kasabouzu fixtures, plus an exact source-mesh alias to test dependency coverage.
- `--appearance-policy <map> <new-output> <source-file>...`: missing optional
  appearance must not block geometry; requested appearance and missing meshes fail.
- `--real-effects <map> <supplemental-map> <request> <new-output> [--no-materials]`:
  structured effect batching, retained file links and option behavior.
- `--real-catalog-shard <map> <new-output> <zero-based-index> <count>`: disjoint
  subsets of the full frozen catalog. Separate processes must use different output
  roots; `ANIMESTUDIO_EXPORT_WORKERS=2` bounds each process's internal work. Shards
  cannot share alias coverage, so their raw failure counts differ from one full batch.
- `--real-selection <map> <new-output> <key-array.json>`: targeted production batch
  reruns after diagnosing failures. Successful accounting does not assert every
  selected model exported; inspect the ledger and individual reports.

Use `Tests/Genshin/ValidateWeaponCatalog.ps1` to compare all shard keys with a full
catalog report, check contained paths, and optionally verify every output hash.
`ValidateWeaponExport.ps1` checks one successful source export;
`InspectWeaponFbx.py` independently reads binary FBX geometry and skin clusters.
None establishes Unity import, playback, runtime effects or visual fidelity.

The deliberately failing native-loader fixture is the exception to host usage:
`dotnet WeaponExportRegression.dll --native-load-failure` checks that unavailable
native libraries report an error without crashing a partial object's finalizer.

Current fixture inputs, source/build identities, results and source limitations:
[Weapon-Export-Offline-Validation.md](../../../docs/Genshin/Weapon-Export-Offline-Validation.md).
