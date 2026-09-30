# RenderDoc 1.45: Genshin capture build

This is a **custom Windows x64 RenderDoc 1.45 distribution**, not an official RenderDoc release. On 2026-09-27 the user confirmed that Genshin launched and frame captures could be taken with this build. The installed research client is 7.1.0. Future client, Windows, or driver versions have not been validated.

## Download and use

1. Download `renderdoc-1.45-genshin-win64.zip` from AnimeStudio's `docs/Genshin/tools/renderdoc/` directory. On GitHub, open the ZIP file and select **Download raw file**.
2. Extract the **entire ZIP** to a separate folder. Keep its DLLs, plugins, Qt, Python, and other support files together. Do not copy it over an existing RenderDoc installation.
3. Close Genshin normally if it is already running. Right-click the extracted **qrenderdoc.exe** and choose **Run as administrator**. Its title should show **RenderDoc v1.45**; use this instance for the capture.
4. In **Launch Application**, set **Executable Path** to your installed `GenshinImpact.exe`. Set **Working Directory** to the folder containing that executable. Leave **Command-line Arguments** and **Environment Variables** empty. The HoYoPlay launcher is not required.
5. Use the default capture options. Leave **Capture Child Processes**, **Enable API Validation**, and **Queue Capture** unchecked; use **0 seconds** debugger delay. Keep **Global Hook disabled**.
6. Click **Launch**. Wait for the game to finish startup and for RenderDoc to show an active **D3D11** target.
7. Navigate to the scene you want to inspect. Use **Capture Frame(s) Immediately** in RenderDoc's connected-target tab, or the capture key shown by its in-game overlay.
8. Save the capture as an `.rdc` under the Genshin study's ignored `Export/` directory, for example `docs/Genshin/Export/renderdoc-captures/` from the AnimeStudio root. Open it in this same custom build and check that the action list and output texture contain the intended scene. Use **Save As** to preserve temporary captures before closing the application.

There is no machine-specific `.cap` profile in the ZIP. After entering your paths, **Save Settings** can save your own reusable profile under `Export/`. No files need to be placed inside the game directory.

For capture provenance, record the current game version, this package's SHA-256, the scene/action, and any changed capture settings next to the RDC. Keep captures and extracted resources out of Git.

## What changed and why

The failing installed RenderDoc 1.46 attempt successfully injected and received a Genshin target handshake. It then logged `Application requested not to be hooked.` The game's corresponding log reported a crash during graphics-device creation. The same executable launched normally outside RenderDoc. NVIDIA Overlay was already disabled; its loaded capture module alone did not establish an overlay conflict.

The earlier FFXIV investigation preserved a working private RenderDoc 1.45 build. Its x64 `renderdoc.dll` has one byte changed to select the normal D3D11 device/context-wrapping path when `D3D11_CREATE_DEVICE_PREVENT_ALTERING_LAYER_SETTINGS_FROM_REGISTRY` is present. This overrides RenderDoc's response to that opt-out flag. The upstream suppressed-wrapping path does not supply the immediate-context output that the wrapping path supplies.

This package reuses that exact private DLL and its matching full 1.45 distribution. It does not contain the FFXIV launcher/session, process-memory, or loader workarounds. It does not modify the installed game, installed RenderDoc, or system DLLs.

Genshin startup and capture success are **user-confirmed**. The assistant verified package identities, the one-byte modification, archive contents, and the command-line runtime. The user's captures were not inspected for replay correctness or completeness. Because the successful test changed both the RenderDoc version and its wrapping behavior, it does not independently isolate the effect of each change. These observations do not establish an anti-cheat diagnosis.

## Pinned identities

| Component | SHA-256 |
| --- | --- |
| Original upstream 1.45 x64 `renderdoc.dll` | `313f450bfa1f7f7d5f50cc7ee80d9f99b68d3fe873e25eb865ff0433e359f7b3` |
| Packaged custom x64 `renderdoc.dll` | `cbcab42f67e784540fc321ce0fc2ddb215ac005510cbef6ba903965f17411d18` |
| Matching 1.45 `qrenderdoc.exe` | `c9904905fe380b2869d48c7a4209c2331370e0bfda502a24da26ec4031cf885b` |

The patch is specific to this binary: RVA `0x344761`, raw file offset `0x343b61`, original surrounding bytes `8b c7 c1 e8 07 24 01 74 19`, with `74` changed to `eb`. Do not apply this offset to RenderDoc 1.46 or any other build. The package manifest records hashes for every payload file. The sibling `.zip.sha256` file records the archive hash.

Recreate the package from a full matching 1.45 runtime using `docs/Genshin/tools/Package-RenderDoc.ps1`. It accepts the pinned original or already-patched DLL, copies the runtime, applies/verifies the exact one-byte change, includes this guide and the original licenses, and verifies the completed ZIP. Example from `docs/Genshin/`:

```powershell
pwsh -File tools/Package-RenderDoc.ps1 -RuntimeDirectory '<full matching RenderDoc 1.45 runtime>'
```

The source runtime is never modified. Staging files are generated under ignored `Export/`; the distributable ZIP and checksum are written under `tools/renderdoc/`. Use `-OutputZip` to select a fresh output when reproducing: existing archives are not overwritten.

## If startup or capture fails

- Verify you opened the extracted **custom 1.45** instance, rather than an installed 1.46 instance. Version alone is insufficient: verify the custom DLL hash above.
- Launch through RenderDoc before graphics initialization. Attaching to an already-rendering game is too late for the documented capture workflow.
- Preserve the new RenderDoc log and Genshin startup log before another attempt replaces them. Record whether injection, D3D11 registration, frame capture, and replay each succeeded.
- Do not enable Global Hook to address this specific failure: the original attempt already injected successfully.
- No NVIDIA-overlay toggle was needed in the successful investigation; it was already off.

## Upstream sources and licenses

- [RenderDoc 1.45 release](https://github.com/baldurk/renderdoc/releases/tag/v1.45)
- [RenderDoc 1.45 D3D11 hook implementation](https://github.com/baldurk/renderdoc/blob/v1.45/renderdoc/driver/d3d11/d3d11_hooks.cpp)
- [RenderDoc capture and injection documentation](https://github.com/baldurk/renderdoc/blob/v1.x/docs/window/capture_attach.rst)
- [Microsoft D3D11 creation flags](https://learn.microsoft.com/en-us/windows/win32/api/d3d11/ne-d3d11-d3d11_create_device_flag)

The original `LICENSE.md` and `LICENSE.rtf` and all bundled runtime license notices are retained in the ZIP. RenderDoc is MIT-licensed; bundled components retain their respective licenses. This project is not affiliated with RenderDoc or HoYoverse.
