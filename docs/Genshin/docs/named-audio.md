> Historical request-driven research. For the current My tools character workflow, see [character export](character-export.md). FBX animation baking was removed, and the new mannequin action is deferred.

# Named voices and combat sounds

The fork owns AKPK bank/media extraction, DIDX/DATA media indexing, typed event traversal, naming manifests and optional WAV decoding. Research scripts coordinate separately installed external tools; no third-party parser code or game map is bundled in either repository.

Validated client: 7.0.0. External tools: [wwiser](https://github.com/bnnm/wwiser) `2f0ad2106400b190c14d157c6650d9bc0c872063` (v20260808), [vgmstream](https://github.com/vgmstream/vgmstream/releases/tag/r2117) r2117, and [AnimeWwise](https://github.com/Escartem/AnimeWwise) map/reader revision `f521915c2202982971bed6881374473466a17839`. The map reports Genshin 7.0; its SHA-256 is `e091ec1c7cc83663dd247f4d58cdeec308c1f3e17ac6e1158f1ece2427fa6fe8`. AnimeWwise's CC BY-NC-SA-4.0 implementation and map stay external and ignored; only an API wrapper is committed here.

## Voices

Use a trusted local checkout of the upstream reader and map:

```powershell
python tools/voice_map_subset.py '<AnimeWwise reader directory>' '<hk4e.map>' 'vo_mona_' Export/mona-voice-map.json --revision '<map/reader revision>'
../../AnimeStudio.CLI/bin/Debug/net10.0-windows/AnimeStudio.CLI.exe --genshin-audio '<AudioAssets folder>' Export/mona-voice-map.json Export/mona-voices-new --decoder '<vgmstream-cli.exe>'
```

The GUI equivalent is **Export > Genshin Impact > Export named voices / event media…**. Select the evidenced JSON request, AudioAssets folder and an output parent. Select vgmstream for WAV or cancel that optional file picker to retain WEM only. This UI was compiled; end-to-end checks use the same service through CLI.

The sampled 21 English voice lines all matched and decoded, including greeting, attack and skill lines. Expanded validation recovered **168 English lines** as original WEM and 48 kHz PCM WAV. The external map contains 173 names per language; Japanese, Chinese and Korean media were not found in this installation. Five mapped English IDs were also absent: heavy-snow friendship, three fly-end variants and the gacha appearance line. They remain explicitly missing. Do not infer that every missing line is obsolete.

Each file keeps the mapped semantic name, media ID and language. The manifest retains the full external path, map provenance, original package/offset/size and content hash. External entries can have package language `sfx` even when stored in the English voice directory; the mapped language and directory are both checked. A numeric ID alone never establishes a character name.

Local evidence: `Export/mona-voice-map.json`, `mona-voice-sample/`, and `mona-voices/`. Valid WAV headers, sample counts and nonzero waveforms were checked. No claim of a separate human listening review is made.

## Combat events

Start with event names actually observed in `EventPattern_Char_Girl_Catalyst_Mona_Audio`, retaining that object's source/CAB/path ID in the research evidence. The wrapper runs production extraction, the external parser, then production traversal and decoding:

```powershell
python tools/prepare_event_audio.py '<AnimeStudio.CLI.exe>' '<wwiser.py>' '<AudioAssets folder>' Export/mona-combat-event-names.txt Export/mona-audio-new --decoder '<vgmstream-cli.exe>'
```

The generated `event-media.json` also works with the GUI named-audio command. Intermediate exported banks and wwiser XML are retained locally. The production graph adapter currently validates bank version **134** only; unsupported versions and XML parser errors fail explicitly.

The chain is source event name → FNV-1 typed Event → ActionPlay → explicit content-bank ID → container child IDs → Sound source ID → typed DIDX/DATA or package media. Bus/parent/state numeric values are not blindly treated as media links. GI stores many HIRC content and DATA-only media banks separately. The exporter can locate those media by typed DIDX records; multiple different payloads remain ambiguous. Byte-identical copies are coalesced with all locations recorded.

The representative set exported **13 playable WAVs** with no decode failures: Attack01, Attack02, five Attack04 sounds/variants, dash hide/show, phantom/phantom-rush and two `starchat` burst sounds (source spelling). Their durations range from about 0.57 to 4.18 seconds. Three media IDs have identical copies in two banks. The broad event report also preserves shared foley and unresolved references; `Play_sfx_char_mona_catalyst_attack03` was not found as a typed Event. Do not fabricate that association.

Local evidence: `mona-audio-banks/`, `mona-content-banks/`, `mona-combat-media-v2.json`, `mona-combat-audio-v3/`, and `mona-combat-locations.json`. Exporting all reachable variants does not reproduce Wwise random choices, switches, RTPCs, bus effects, game event timing or final mixed playback. These remain explicit in the manifest/XML for later reconstruction.
