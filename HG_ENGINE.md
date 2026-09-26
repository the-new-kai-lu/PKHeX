# HG-engine Fakemon fork

This fork supports the matching `the-new-kai-lu/hg-engine` closest-stock profile: 512 KiB saves, 18 boxes, general block size 0xFDB0, storage offset 0xFE00 and an expanded 0x700-byte Pokédex. Load/export through the ordinary PKHeX UI or `SaveUtil.GetSaveFile` / `SaveFile.Write`; raw `.sav` files and the existing supported emulator wrappers use the same detection path.

The adapter recognizes block sizes, IDs, magic and CRCs. It retains expanded bytes outside the standard HGSS editing view, including custom caught/seen/gender flags, and preserves unwritten/corrupt backup blocks. Both partitions and box checksums remain in the game's format when exported. Retail HGSS saves continue to use the retail path.

Custom species 1076–1086 are Voltuff, Surguenon, Raijinque, Embernewt, Pyrovaran, Magmalisk, Rimevaran, Fimbulisk, Sedgling, Cragaviar and Ragnaroc. Names, stats, types, abilities, growth rates and breeding fields come from the matching ROM. The custom IDs appear in this save profile's species selector; unrelated engine species with shifted Gen 5+ IDs are not exposed. Custom species have no retail encounter legality and are reported as such, not marked legal. Cosmetic sprite previews currently use the editor's unknown-species fallback.

This is a specific profile, not a general hg-engine format detector. The earlier 30-box/expanded-bag builds have a different layout and are not automatically converted. Keep original saves when changing ROM profiles. Canonical base-species personal data is exported from this ROM; the editor retains retail alternate-form metadata. Nonstandard modern form mechanics are outside this profile.

## Verification

- Core and Windows UI projects build with .NET 10 (Windows UI cross-compiled in WSL with `-p:EnableWindowsTargeting=true`).
- `HGEngineTests` cover all eleven species in encrypted party/box round trips, names/levels/stats, custom Dex flags, cloning, byte-preserving projection, backup fallback, format rejection and retail isolation.
- An editor-created Voltuff save loaded in the rebuilt ROM with the correct party display and follower; an in-game save reopened and exported through PKHeX successfully.
- Full upstream test run: 601 passed, 1 skipped, 1 failed (`EffortExpLegalityTests.ZeroEVs_ReturnsZero`). The same failure was reproduced on untouched upstream commit `17157eb18`; it is unrelated to these changes.

Build: `dotnet build PKHeX.WinForms/PKHeX.WinForms.csproj -c Debug` on Windows. On Linux add `-p:EnableWindowsTargeting=true` for compile verification; the Windows UI itself requires Windows.
