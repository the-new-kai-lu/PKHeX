# Stock-based pokeheartgold Fakemon profile

## Goal and changes

This `fakemon-stock` branch supports the separate pokeheartgold ROM with eleven added species and seven ported moves. The `master` branch retains the existing hg-engine work. This branch can still recognize that hg-engine format separately.

The new ROM keeps the original HGSS 512 KiB save layout: general block 0xF628, storage at 0xF700, storage block 0x12310, 18 boxes and a 0x340-byte Pokédex. No normalization or expansion adapter is used. The selected general block identifies version 1 using `FK` at offsets 0x15EA–0x15EB (unused Pokédex language padding), and byte 1 at 0x15F7 (Pokédex dummy byte). Unmarked retail saves retain retail behavior. The ROM initializes the marker; this editor does not automatically convert retail saves.

Species 1076–1086 reuse the approved custom stat/ability/growth/breeding data, with types normalized from the ROM's Gen 4 Mystery-type gap to PKHeX's type numbering. Canonical species and alternate forms use original HGSS personal data. Species selection excludes the unused 494–1075 range.

Custom caught, seen and first/second-gender flags map to bit indices 493–503 of the original four 512-bit Pokédex flag regions. Deoxys form history in bits 504–511 is preserved. Custom language and form records are unsupported because this format does not allocate them.

| Saved move ID | Move | Base PP |
| --- | --- | --- |
| 468 | Wild Charge | 15 |
| 469 | Snarl | 15 |
| 470 | Incinerate | 15 |
| 471 | Fire Lash | 15 |
| 472 | Icicle Crash | 10 |
| 473 | Bulldoze | 20 |
| 474 | Hurricane | 10 |

The save profile supplies move names, type icons, PP and PP Ups behavior without changing global retail move tables. These compact IDs differ from hg-engine and from modern retail IDs. No cross-profile Pokémon/save conversion is provided. A standalone `.pk4` has no profile marker; load it into the matching save before editing compact moves. Cosmetic custom sprite previews use the unknown-species fallback. Retail encounter legality and automatic suggested moves cannot validate this custom ROM.

## Verification

`FakemonStockTests` cover marker/version isolation, all eleven species in encrypted party/box save round trips, their levels and checksums, custom Dex flags without overwriting Deoxys history, canonical personal data preservation, normalized types, compact move IDs, names, PP, cloning and retail selector isolation. Existing `HGEngineTests` remain applicable to the separate expanded profile. All twelve focused stock/hg-engine tests passed; the full suite finished with 608 passed, 1 skipped and the one previously reproduced upstream failure noted below. The Windows UI cross-build passed with no warnings or errors.

Commands (Linux/WSL):

```sh
dotnet test Tests/PKHeX.Core.Tests/PKHeX.Core.Tests.csproj -c Debug --filter 'FullyQualifiedName~FakemonStockTests|FullyQualifiedName~HGEngineTests'
dotnet build PKHeX.WinForms/PKHeX.WinForms.csproj -c Debug -p:EnableWindowsTargeting=true
```

The Windows UI build is a compilation check; it does not establish Windows UI or emulator end-to-end behavior. The existing full test suite has a known upstream failure in `EffortExpLegalityTests.ZeroEVs_ReturnsZero`, reproduced previously on untouched upstream.

## Emulator save round trip

A headless DeSmuME run loaded the editor-created Voltuff fixture in the modified HeartGold ROM (SHA-256 `bbbc8b8ab9062fc5f1374a7522e76ddd56a87308457ae45b55b5f245bf00b9c7`) and saved in-game. Both general and storage counters advanced from 2847 to 2848, selecting the alternate save partition; 1,791 bytes changed. Independent CRC checks passed for both copies of both blocks. PKHeX reopened the actual game output with a valid stock profile and valid checksums, retaining Voltuff at level 10 and all eleven custom boxed species at level 50 with compact moves 468/470/472/474 and correct PP.

This exposed and fixed an inherited HGSS loader bug: assigning the version before initializing offsets overwrote adventure-data byte 0x1C and made valid game saves appear to have an invalid checksum. Loading now preserves every source byte; new marked and retail regression cases cover it. This save round trip does not establish every battle, evolution, animation or interactive editor behavior below.

## Manual end-to-end acceptance

1. Save in the modified HeartGold or SoulSilver ROM. Open it in this branch and confirm all eleven names are available, only the intended species appear, and the seven moves use the mapping above.
2. Edit party and boxed custom Pokémon: level, EVs/IVs, nature, ability, gender, held item, nickname, shiny status and moves/PP. Export, reopen, and compare the edited values. Also edit a canonical Pokémon to confirm stock stats and moves remain intact.
3. Load the export in the matching ROM. Inspect the party, summaries, boxes, followers, battle graphics and cries; execute each added move and save in-game. Reopen that result in PKHeX and compare species, moves, PP, stats and Dex state. Repeat once in PKMDS on its matching branch.
4. Exercise level-16 and level-49 evolutions in-game. Check EXP conversion preserves current level/fraction, and validate Embernewt's held-Icicle-Plate Ice Fang substitution and same-KO/level-up/NeverMeltIce rare evolution. The editor does not simulate those rules.
5. Retest caught/seen genders, all Deoxys forms, and untouched retail/hg-engine saves opened separately. A profile marker/version mismatch must not silently select this profile.

Record actual results, ROM commit/hash, editor commit, emulator version and any screenshots/logs. Passing code tests does not substitute for these interactive checks.
