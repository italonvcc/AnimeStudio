# Current local AnimeStudio distribution

As of 2026-09-29, use `dist/net10.0-windows/AnimeStudio.GUI.exe` and the matching
`AnimeStudio.CLI.exe`. This is the sole launchable GUI/CLI build in this repository.
It contains the current working-tree Genshin export changes, including the prior
tangent repair; that repair was reused, not reimplemented during consolidation.

Run `./build.ps1` to replace this same Release distribution. It stages clean
outputs, rejects inconsistent GUI/CLI dependencies, verifies the packaged native
FBX library against `AnimeStudio.Libraries`, checks CLI startup, and records
source and payload SHA256 values. The current build is identified by
`dist/net10.0-windows/build-manifest.json`, including uncommitted source content.
An unchanged Git commit or apphost executable hash alone does not identify it.

Consolidation validation:

- Final build run `20260929T231559226Z`; GUI, CLI and patcher completed with zero
  errors. Existing compiler/dependency warnings remain in their logs.
- All77 packaged file hashes and both preserved map hashes verified. Source
  manifest contains1563 entries. Replacement retains a rollback directory until
  the staged build is in place and rejects linked output ancestors.
- Native FBX SHA256:
  `1C3CF66913FF1BFE35422BE72D9E0914F816D5BDF394CAA0ED6A14967032BCE4`.
  This is the prior tangent-fix binary, matching commit `f902829`'s native blob.
- Packaged CLI `--help` passed; seven existing shader-name regression checks passed.
  This housekeeping run did not redo game exports, GUI interaction or Unity rendering.
- Rebuilding replaced the same folder and preserved `Maps/genshin-7.1.bin` and
  `Maps/genshin-asset-map.bin`. Only the canonical GUI and CLI executables remain
  in the repository-wide launchable-file inventory.

Older Debug/Release, .NET9 and named-fix distributions were retired into verified
ZIPs under `artifacts/build-records/20260929T231012552Z/retired-builds/`.
`index.json` maps original directories to archives; each ZIP has a complete file
hash inventory. Historical logs and maps inside those directories are preserved.
Historical study references to those binary paths now refer to these archives.
Do not restore old executables into active paths just to follow an old report.

Six active Genshin export/audit scripts now locate the canonical launcher and its
shared `bin/` dependencies. No Manekin exports or Unity work were resumed.
The native copy policy is now `Always`, avoiding the observed stale .NET9 DLL
whose newer timestamp previously defeated `PreserveNewest`.
