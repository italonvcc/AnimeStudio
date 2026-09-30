# Mona submerged Idle: action source audit

This account covers the stage-6 action and attachment question for the source
`Eff_Avatar_Girl_Catalyst_Mona_LiquidStrike_Idle`. It does not assign script
behavior from class names or numeric patterns. The source effect is the installed
Genshin Impact 7.1 root GameObject in CAB
`CAB-9f2563ac8ccd85196764846f5b861cbc`, PathID `451401874049533967`,
`06860440.blk` offset `11021908` (block SHA-256
`CE7DBFAAE3F02168E642101F5EB32055501B5DE0055385C5E16D975F46C52296`).
The [Idle source account](Mona-Idle-Source-Data.md) covers its particle, renderer,
mesh and material data. The user's captured state is staying still while
submerged; the older `LiquidStrike_InFloor` root is a separate action.

## What is proven in the prefab

All three root `MonoBehaviour` components are enabled. Their Unity headers parse
through byte 32: a GameObject pointer, enabled byte, MonoScript pointer and an
empty aligned `m_Name`. The MonoScript objects resolve within the same CAB and
identify `MoleMole` classes in `Assembly-CSharp.dll`. These are identity facts,
not evidence of when or how their code runs.

| Script | Component PathID | Bytes / type hash | MonoScript PathID | Untyped custom bytes |
| --- | ---: | --- | ---: | --- |
| `MonoEffect` | `5717950287282407900` | 184 / `96EFAF2845705C9CDD16AF444B33E53C` | `-6095123100385083407` | `[32,184)` |
| `MonoEffectPluginFollow` | `-8372187366204061082` | 312 / `3A140E5C1F4C6A3CFFB17AB0F3E28A47` | `-4579206049002199124` | `[32,312)` |
| `MonoEffectPluginAudio` | `-3699506857928399399` | 296 / `3328A57742DE2EE75925DEA077773399` | `-7651656729664257369` | `[32,296)` |

The raw component SHA-256 values in table order are
`C892AAD1C07A1C96DF42E2B7A73F3C2E7CB9492286801EC66AE9225DDAAF90BA`,
`4A8D3895DE91E0E7C1C3A289B558936678D727F7A9F16EC816905C47E0333A55`,
and `63F1264976BFB699B40D9A7A89A1AA91D33263DE4CD3ED3C50C8270D31555CF9`.

The Audio payload has two independently bounded Unity-style length-prefixed
UTF-8 strings. Bytes `[40,44)` give length 42, with text at `[44,86)`:
`Play_sfx_char_mona_catalyst_invisable_idle`. Bytes `[116,120)` give length
44, with text at `[120,164)`:
`Stop_sfx_char_mona_catalyst_invisable_sprint`. The spellings are the source
spellings. The field names, call direction, order, conditions and times are not
established by the strings. No meaningful text appears in the root `MonoEffect`
or Follow custom spans. Numbers in those spans have no verified field identities.

AnimeStudio's current action request selects this prefab directly by CAB, PathID,
name and block source. It labels the user-clarified target; it is **not** an
extracted in-game event schedule. `GenshinCharacterExporter` collects
character-prefixed effect GameObjects and `EventPattern_...Mona...` candidates,
and marks an association when a raw string matches an exact effect name. This
discovery rule has no parser for trigger time, following space, termination or
animation-state conditions. `GenshinVfxExporter` similarly reports action timing
as unknown. The `ConfigAnimatorEventPattern` candidate
`EventPattern_Char_Girl_Catalyst_Mona_Audio` is MonoBehaviour PathID
`-3809805883267094433` in CAB `CAB-fd46fc1f71c104e134bcc35345729688`,
block `00035183.blk`. The prior `docs/Genshin/Export/config-inspection.json`
contains extracted strings `Audio_Girl_Catalyst_Mona_LiquidStrike_Strike`,
`..._AS`, `..._BS` and hide/show sound events. That record has `structured=null`.
Its extracted string list has no `Eff_` name, including the exact Idle root.
It supplies candidate audio/action naming, not a direct Idle effect reference or
timestamp. The relevant animation names and event pattern need a typed source
link before a synchronized action can be claimed.

The previously exported Mona `SprintCycle`, `SprintBS`, `SprintSkill_*` and
`SwimDash*` `.anim` clips all show `m_Events: []`. This check only covers
Unity AnimationClip events in those named clips; it cannot rule out a separate
game animator-event pattern, action config or code-driven trigger. In
particular, it does not yield an Idle spawn or stop timestamp.

The asset's custom MonoBehaviour type trees are stripped: the existing
AnimeStudio `.json` snapshots report `parsed: null` for all three scripts.
The installed `Managed/Metadata/global-metadata.dat` is 82,724,152 bytes and
starts `4D 48 59 00` (`MHY\0`), rather than the usual plain IL2CPP metadata
header. A bounded plaintext class-name scan of that file and
`GenshinImpact.exe` did not find `MonoEffectPluginFollow` or
`MonoEffectPluginAudio`. This does not prove the class metadata is absent;
its on-disk format is modified. The MonoScript object gives names, namespace
and assembly but no serializable field list or method behavior. Authoritative
next inputs are the game's script field layout or decoded metadata/serializer,
and a typed action configuration linking this exact prefab to animation/event
timing and attachment. A capture with measured spawn, parent transform and stop
conditions could independently constrain runtime behavior, but class names and
the present stationary frame cannot do so.

## Export and Unity boundary

`GenshinEffectActionDecoder` now checks the 2017.4.30f1 Unity header against the
already parsed GameObject and MonoScript pointers, then reports header size and
bounded aligned UTF-8 literals for the Audio type hash. It scans candidate
length-prefix locations so other Audio instances need not share Idle's offsets.
This matters in the existing Mona corpus: the `MonoEffectPluginAudio` raw samples
include lengths 224, 292, 296, 300 and 352 bytes; one 280-byte sample has no
string at Idle's first offset. The scan reports bytes only and leaves all custom
field semantics unresolved.
`GenshinStructuredVfxExporter` writes `scriptReference` (file/path ID,
resolution, class, namespace, assembly), `actionData` and the full unresolved
custom span. The raw `.bin`, byte count, SHA-256 and type hash remain the
authority. `coverage.status=Partial` and `nativePlaybackReady=false` remain.
The source strings are observations within an unresolved span, not executable
audio events. No guessed Follow or MonoEffect controls enter the export.

The package importer currently treats these three scripts as deferred warnings
only in its explicit partial native-review mode
(`Editor/Genshin/Effects/GenshinEffectImporter.cs`). Strict complete import still
reports partial custom components as errors. The saved standalone prefab plays
its five child particle systems after instantiation; this proves neither game
spawn timing nor attachment/follow and interruption behavior. The Editor owner
must evaluate any future action adapter against this source contract. No live
Unity import was performed for this exporter-only change.

## Reproduction and checks

Final integrated source exports in the local Unity project:

| Effect | Export directory under `Assets/_Games/Genshin/Effects/Exports/` | Effect JSON SHA-256 |
| --- | --- | --- |
| Submerged Idle | `mona-idle-action-integrated-20260929T1805Z/` | `91CC96C34F676BE1CFDF88296535F64A9AF39B2BB20CAEA06762B641564FB0F5` |
| InFloor regression | `mona-infloor-action-integrated-20260929T1806Z/` | `C22453FF22CC630A60D0097728FEC426B44025BC922436EEC85275FF79004829` |

The Idle request uses the preexisting `mona-idle-root-20260929.json`
supplemental map and `request.json` from the previous renderer-typed export.
The InFloor request uses `mona-infloor-root-20260929.json` and its existing
renderer-typed request. Input provenance is recorded in the respective
[Idle](Mona-Idle-Source-Data.md) and [InFloor](Mona-InFloor-Source-Data.md)
accounts. Both manifests have zero failures, missing selections and unresolved
dependencies. Their `toolRevision` is the base commit
`9e69ff5c1b59f20003b609f961b3930cad1c6789` plus uncommitted changes.
The built CLI and copied Utility DLL SHA-256 values are
`9589EE3518E70083077F73DBC09100D0B23D2F13FCC537441C3D4C76AE40B246`
and `2FCF90AC9ED6714D42D8CDF6ADC7630EC2CBAEBED0DF70738AF880B92F1B627E`.
These builds also include the separate particle-extension decoder changes; the
particle field proof and coverage are recorded in its own source account.

CLI and GUI `net10.0-windows --no-restore` builds succeeded with zero errors.
`Tests/Genshin/ValidateMonaActionExport.ps1` passed against the integrated Idle
export: exact component/script IDs, lengths, hashes, resolved script metadata,
raw hashes, Audio text positions and explicit unknown ranges. The updated Idle
and InFloor export regressions passed against the two fresh exports. A coordinated
Unity import of these integrated files remains unperformed. These checks do not validate
the script's runtime behavior, action timing or a game-aligned final image.
