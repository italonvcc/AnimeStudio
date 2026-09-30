# Genshin RenderDoc capture validation

## Outcome, 2026-09-27

The user confirmed that the custom RenderDoc 1.45 runtime launched Genshin and produced frame captures. See the [download and setup guide](../tools/renderdoc/README.md) and [packaged runtime](../tools/renderdoc/renderdoc-1.45-genshin-win64.zip).

The research client is 7.1.0. The stock 1.46 attempt injected successfully, then logged `Application requested not to be hooked.` Genshin's matching startup log reported a crash at graphics-device creation. The same executable launched directly without RenderDoc. NVIDIA Overlay was already disabled.

The tested replacement is the preserved FFXIV custom 1.45 runtime, copied separately and launched against the Genshin executable without its launcher. Only RenderDoc's x64 device-wrapping branch is modified; no FFXIV process or launcher workarounds were used. The guide records the exact patch and original/custom hashes.

## Package reproduction and checks

From `docs/Genshin/`, using the local runtime directory from this investigation:

```powershell
./tools/Package-RenderDoc.ps1 -RuntimeDirectory ./Export/renderdoc-genshin/runtime
git diff --check
```

For a fresh environment, pass a matching full 1.45 runtime to `-RuntimeDirectory`; machine-specific install paths are intentionally omitted. The script accepts the pinned original or custom DLL, reconstructs/verifies the original hash, applies the one-byte change to a staging copy, and verifies the expected custom hash.

- ZIP size: **99,429,863 bytes** (about 94.8 MiB), below GitHub's 100 MiB regular-file limit.
- ZIP SHA-256: `d3fef010efb1a8154e26f34579224f5e6c127fedb190c8ef2f761cf81332f04a`.
- Archive: **66 runtime files**, the usage guide, and `manifest.json`; **68 files total**.
- Every archived payload's length and SHA-256 were checked against the manifest inputs. The archived manifest was read back and validated.
- The archive was extracted into a fresh ignored `Export/` directory. The extracted custom DLL and GUI hashes matched their pinned values.
- The extracted `renderdoccmd.exe version` ran successfully and reported **x64 v1.45**, upstream commit **`2fc0bc04cb95499635f63986a55bc6f67849dd9f`**. The custom one-byte DLL change is additional to that upstream commit.
- The file list contains no `.rdc`, `.cap`, `.log`, or `.dmp` files. It includes the upstream licenses; no game assets or machine-specific launch settings are bundled.

## Acceptance and limits

| Check | Evidence |
| --- | --- |
| Game starts under this custom runtime | User confirmed |
| Frame captures can be taken | User confirmed |
| Packaged runtime matches the working private build | Pinned DLL/GUI hashes and full archive read-back checks |
| Extracted command-line runtime starts | Version command passed |
| Captures replay with the intended scene and resources | Not independently inspected |
| Exact cause isolated between version change and wrapping change | Not established; both changed in the successful test |
| Compatibility with future client/driver/Windows versions | Not established |

The failed launch is consistent with RenderDoc's D3D11 suppressed-wrapping path leaving an immediate-context output unset. This remains a causal explanation supported by source and prior FFXIV evidence, rather than an independently isolated Genshin experiment. No anti-cheat cause was established.

For future captures, keep the original RDC under ignored `Export/` and record game version, package hash, scene/action, and capture settings alongside it. Shader/resource attribution requires inspection of the actual capture; startup success alone does not establish it.
