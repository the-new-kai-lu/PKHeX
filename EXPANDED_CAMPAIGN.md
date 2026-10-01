# Expanded HGSS campaign-save v1

This fork supports a separate opt-in campaign-state format for the expanded
HGSS project. It is **not hg-engine**, does not add custom species, and is not
an automatic upgrade of ordinary HGSS saves.

The physical file remains 512 KiB with two `0x40000` partitions. The game adds a
versioned `EHGSCAMP` allocation of `0x800` bytes at general offset `0xF614`.
The general block becomes `0xFE28` bytes; the unchanged-size PC block starts at
`0xFF00`. The six extra save blocks remain at their original locations.

The allocation contains independent Emerald and Platinum primary flag/variable
namespaces and reserved bytes. Standard Pokémon records and editing offsets are
exposed through a lossless adapter. Unrelated edits preserve both campaign
allocations, opaque bytes, invalid/incomplete partitions, and extra data.

Detection happens before vanilla/hg-engine fallback. Unsupported versions,
malformed headers, incompatible mixed-format files, and files without a complete
native general/PC transaction are refused. A recognized DeSmuME `.dsv` wrapper
is split by its existing handler before raw-format detection; its original footer
is retained on export. Arbitrary trailing bytes are not silently discarded.

## Transactions

General and PC data must come from one complete partition with matching native
counters and valid schemas/checksums. The adapter does not combine newer general
data with another partition's PC data. Selection uses the native comparison:
zero follows `0xFFFFFFFF` at rollover, otherwise counters compare unsigned;
equal complete counters choose partition 0. Incomplete partitions are retained,
not silently repaired.

`SAV4HGSS.IsExpandedCampaign` identifies the format. Read-only campaign spans
are available separately from the normal editing view. Cloning, same-format
copying, type overrides, exporting, and checksum handling preserve the adapter.
Cross-format copies cannot discard or mix its retained state. The desktop window
title identifies an expanded campaign save.

Use matching expanded-game/editor builds. Retail encounter legality does not
describe imported campaign encounters, and unmodified games/editors are not
compatibility targets. No automatic or in-place migration is provided.

## Validation boundary

The focused Core suite passed 89 tests without skips: 77 explicitly synthetic
campaign cases plus 12 existing vanilla/hg-engine regressions. Coverage includes
raw/DSV edit round trips, complete-transaction selection, counter rollover,
unknown versions, malformed data, clone/copy behavior, and byte preservation.
These fixtures are not game-created saves or proof of GUI/native gameplay.

An unrelated full-suite failure, `EffortExpLegalityTests.ZeroEVs_ReturnsZero`,
also fails on the exact prior commit `9a5ed35` (expected zero, actual `-999`).
Five other initial failures were isolated-build fixture-path setup problems;
they pass on both base and changed code when existing repository fixtures are
staged in the expected isolated output paths. No unrelated test or golden was
changed to obtain these results.

Native game builds and actual editor UI export/reopen validation are separate
release gates and remain unverified for this new format.