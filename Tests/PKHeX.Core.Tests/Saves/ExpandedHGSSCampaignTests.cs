using System;
using System.IO;
using Xunit;
using static System.Buffers.Binary.BinaryPrimitives;

namespace PKHeX.Core.Tests;

/// <summary>
/// SYNTHETIC raw campaign-v1 allocations, not native saves or evidence of game/emulator compatibility.
/// Native HGSS footer counter (-16) and last-entry trailing bytes (-20) are deliberately independent.
/// </summary>
public class ExpandedHGSSCampaignTests
{
    private const int BankSize = 0x40000;
    private const int GeneralSize = 0xFE28;
    private const int StorageStart = 0xFF00;
    private const int StorageSize = 0x12310;
    private const int CampaignOffset = 0xF614;
    private const int CampaignSize = 0x800;
    private const int PartySize = 0xEC;
    private const int StoredSize = 0x88;
    private const int BoxOffset = 2 * 0x1000 + 3 * 0x88;

    private static readonly BlockInfo4[] Extras =
    [
        new(0, 0x23000, 0x2AC0),
        new(1, 0x26000, 0x0BB0),
        new(2, 0x27000, 0x1D60),
        new(3, 0x29000, 0x1D60),
        new(4, 0x2B000, 0x1D60),
        new(5, 0x2D000, 0x1D60),
    ];

    private static byte[] SyntheticRawFixture(int active = 1,
        GameVersion version = GameVersion.HG, uint magic = SAV4.MAGIC_JAPAN_INTL, int seed = 0)
    {
        var data = new byte[0x80000];
        // Every gap and opaque area contains deterministic nonzero bytes.
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)(1 + (i * 37 + seed * 19) % 251);
        for (int bank = 0; bank < 2; bank++)
        {
            var part = data.AsSpan(bank * BankSize, BankSize);
            var general = part[..GeneralSize];
            var storage = part.Slice(StorageStart, StorageSize);
            general.Clear();
            storage.Clear();
            WriteUInt32LittleEndian(general[0x78..], (uint)(12345 + bank + seed)); // Money.
            general[0x7D] = 2;
            general[0x80] = (byte)version;
            WriteUInt16LittleEndian(general[0x64..], (ushort)('A' + bank));
            WriteUInt16LittleEndian(general[0x66..], 0xFFFF);
            general[0x94] = 1;
            // Johto namespaces, original Trainer House payload, both donor regions and reserved data.
            for (int i = 0xDE4; i < 0x1230; i++)
                general[i] = (byte)(i * 13 + bank * 71);
            for (int i = 0xF580; i < CampaignOffset; i++)
                general[i] = (byte)(1 + (i + bank * 53) % 251);
            var campaign = general.Slice(CampaignOffset, CampaignSize);
            for (int i = 0; i < campaign.Length; i++)
                campaign[i] = (byte)(1 + (i * 17 + bank * 73 + seed * 31) % 251);
            "EHGSCAMP"u8.CopyTo(campaign);
            WriteUInt16LittleEndian(campaign[8..], 1);
            WriteUInt16LittleEndian(campaign[10..], CampaignSize);
            WriteUInt16LittleEndian(campaign[12..], 0x20);
            Pokemon(25, "Party", version).WriteEncryptedDataParty(general.Slice(0x98, PartySize));
            Pokemon(133, "Box", version).WriteEncryptedDataStored(storage.Slice(BoxOffset, StoredSize));
            WriteInt32LittleEndian(storage[0x12004..], 0x135 + bank);
            // One native transaction per partition: both counters must agree.
            uint counter = bank == active ? 8u : 7u;
            NativeFooter(general, 0, counter, (uint)(100 + bank), magic);
            NativeFooter(storage, 1, counter, (uint)(200 + bank), magic);
            // Realistic nonzero initialized extras, with their distinct checksum coverage.
            foreach (var extra in Extras)
            {
                var block = part.Slice(extra.Offset, extra.Length);
                WriteUInt32LittleEndian(block[^16..], magic);
                WriteUInt32LittleEndian(block[^12..], (uint)(bank + 1));
                WriteUInt32LittleEndian(block[^8..], (uint)(extra.Length - 16));
                WriteUInt16LittleEndian(block[^4..], (ushort)extra.ID);
            }
            BlockInfo.SetChecksums(Extras, part);
        }
        return data;
    }

    private static PK4 Pokemon(ushort species, string nickname, GameVersion version)
    {
        var pk = new PK4
        {
            Species = species, Nickname = nickname, IsNicknamed = true,
            PID = 0x13572468, ID32 = 12345, Language = 2, Version = version,
            OriginalTrainerName = "TEST", Ability = PersonalTable.HGSS[species].Ability1,
            EXP = Experience.GetEXP(37, PersonalTable.HGSS[species].EXPGrowth),
            Move1 = 33, Move1_PP = 35,
        };
        pk.ResetPartyStats();
        pk.RefreshChecksum();
        return pk;
    }

    private static void NativeFooter(Span<byte> block, ushort id, uint counter, uint entryTrailer, uint magic)
    {
        WriteUInt32LittleEndian(block[^20..], entryTrailer); // Payload, NOT a major save counter.
        WriteUInt32LittleEndian(block[^16..], counter);
        WriteUInt32LittleEndian(block[^12..], (uint)block.Length);
        WriteUInt32LittleEndian(block[^8..], magic);
        WriteUInt16LittleEndian(block[^4..], id);
        FixCRC(block);
    }

    private static void FixCRC(Span<byte> block) =>
        WriteUInt16LittleEndian(block[^2..], Checksums.CRC16_CCITT(block[..^16]));

    private static void SetNativeCounters(Span<byte> partition, uint general, uint storage)
    {
        // The single native counter lies in the CRC-excluded 16-byte footer.
        WriteUInt32LittleEndian(partition[(GeneralSize - 16)..], general);
        WriteUInt32LittleEndian(partition[(StorageStart + StorageSize - 16)..], storage);
    }

    private static SAV4HGSS Load(byte[] bytes) => Assert.IsType<SAV4HGSS>(SaveUtil.GetSaveFile(bytes));

    private static byte[] SyntheticDSV(byte[] raw)
    {
        // SYNTHETIC DeSmuME container: 122 nonzero opaque bytes with the exact native
        // end marker recognized by SaveHandlerDeSmuME. No emulator/save files are read.
        var footer = new byte[0x7A];
        for (int i = 0; i < footer.Length; i++)
            footer[i] = (byte)(1 + (i * 19) % 251);
        var marker = "|-DESMUME SAVE-|"u8;
        marker.CopyTo(footer.AsSpan(footer.Length - marker.Length));
        return [..raw, ..footer];
    }

    private static void AssertProtectedBytes(byte[] before, byte[] after)
    {
        for (int bank = 0; bank < 2; bank++)
        {
            int offset = bank * BankSize;
            Assert.Equal(before.AsSpan(offset + CampaignOffset, CampaignSize).ToArray(),
                after.AsSpan(offset + CampaignOffset, CampaignSize).ToArray());
            Assert.Equal(before.AsSpan(offset + 0xDE4, 0x1230 - 0xDE4).ToArray(),
                after.AsSpan(offset + 0xDE4, 0x1230 - 0xDE4).ToArray());
            Assert.Equal(before.AsSpan(offset + 0xF580, CampaignOffset - 0xF580).ToArray(),
                after.AsSpan(offset + 0xF580, CampaignOffset - 0xF580).ToArray());
            Assert.Equal(before.AsSpan(offset + GeneralSize, StorageStart - GeneralSize).ToArray(),
                after.AsSpan(offset + GeneralSize, StorageStart - GeneralSize).ToArray());
            Assert.Equal(before.AsSpan(offset + StorageStart + StorageSize, BankSize - StorageStart - StorageSize).ToArray(),
                after.AsSpan(offset + StorageStart + StorageSize, BankSize - StorageStart - StorageSize).ToArray());
        }
    }

    private static void AssertOnlyChanges(byte[] before, byte[] after, params (int Offset, int Length)[] allowed)
    {
        Assert.Equal(before.Length, after.Length);
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i] == after[i]) continue;
            bool expected = false;
            foreach (var range in allowed)
                expected |= i >= range.Offset && i < range.Offset + range.Length;
            Assert.True(expected, $"Unexpected byte change at 0x{i:X5}.");
        }
    }

    private static void AssertNativeCRC(byte[] bytes, int bank, bool general = true, bool storage = true)
    {
        var part = bytes.AsSpan(bank * BankSize, BankSize);
        if (general) AssertCRC(part[..GeneralSize]);
        if (storage) AssertCRC(part.Slice(StorageStart, StorageSize));
    }

    private static void AssertCRC(ReadOnlySpan<byte> block) =>
        Assert.Equal(Checksums.CRC16_CCITT(block[..^16]), ReadUInt16LittleEndian(block[^2..]));

    [Theory]
    [InlineData(0, GameVersion.HG, SAV4.MAGIC_JAPAN_INTL)]
    [InlineData(1, GameVersion.SS, SAV4.MAGIC_JAPAN_INTL)]
    public void ProjectionAndReadAreLosslessAndDoNotMutateInput(int active, GameVersion version, uint magic)
    {
        var raw = SyntheticRawFixture(active, version, magic);
        var before = (byte[])raw.Clone();
        Assert.True(ExpandedHGSSCampaignSave.IsRecognized(raw));
        Assert.False(HGEngineSave.IsRecognized(raw));
        var adapter = new ExpandedHGSSCampaignSave(raw);
        Assert.Equal(before, adapter.Export(adapter.Normalize()));
        var sav = Load(raw);
        Assert.Equal(before, raw);
        Assert.True(sav.IsExpandedCampaign);
        Assert.False(sav.IsHGEngine);
        Assert.True(sav.ChecksumsValid, sav.ChecksumInfo);
        Assert.Equal(version, sav.Version);
        Assert.Equal(PersonalTable.HGSS, sav.Personal);
        Assert.Equal(493, sav.MaxSpeciesID);
        Assert.Equal(18, sav.BoxCount);
        Assert.Equal((uint)(12345 + active), sav.Money);
        Assert.Equal(0x135 + active, sav.FlagsBoxContentChanged);
        Assert.Equal(before.AsSpan(active * BankSize + CampaignOffset, CampaignSize).ToArray(), sav.CampaignData.ToArray());
        Assert.Equal(512, sav.CampaignHoennVariables.Length);
        Assert.Equal(300, sav.CampaignHoennFlags.Length);
        Assert.Equal(576, sav.CampaignSinnohVariables.Length);
        Assert.Equal(364, sav.CampaignSinnohFlags.Length);
        Assert.Equal(sav.CampaignData.Slice(0x20, 512).ToArray(), sav.CampaignHoennVariables.ToArray());
        Assert.Equal(sav.CampaignData.Slice(0x220, 300).ToArray(), sav.CampaignHoennFlags.ToArray());
        Assert.Equal(sav.CampaignData.Slice(0x34C, 576).ToArray(), sav.CampaignSinnohVariables.ToArray());
        Assert.Equal(sav.CampaignData.Slice(0x58C, 364).ToArray(), sav.CampaignSinnohFlags.ToArray());
        sav.Money = 54321;
        sav.Write();
        Assert.Equal(before, raw); // Normalization, edits AND export never alias the source.
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void NoEditExportChangesOnlyExistingBoxBitmapAndCRC(int active)
    {
        var before = SyntheticRawFixture(active);
        var sav = Load(before);
        var after = sav.Write().ToArray();
        AssertProtectedBytes(before, after);
        AssertOnlyChanges(before, after,
            (active * BankSize + StorageStart + 0x12004, 4),
            (active * BankSize + StorageStart + StorageSize - 2, 2));
        AssertNativeCRC(after, 0);
        AssertNativeCRC(after, 1);
        var loaded = Load(after);
        Assert.True(loaded.ChecksumsValid, loaded.ChecksumInfo);
        Assert.Equal(0x3FFFF, loaded.FlagsBoxContentChanged);
        Assert.Equal("Party", loaded.GetPartySlotAtIndex(0).Nickname);
        Assert.Equal("Box", loaded.GetBoxSlotAtIndex(2, 3).Nickname);
        Assert.Equal((ushort)25, loaded.GetPartySlotAtIndex(0).Species);
        Assert.Equal((ushort)133, loaded.GetBoxSlotAtIndex(2, 3).Species);
        Assert.Equal(PersonalTable.HGSS[25], loaded.GetPartySlotAtIndex(0).PersonalInfo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PokemonNicknamesAndMoneyRoundTripWithoutTouchingCampaignOrBackup(int active)
    {
        var before = SyntheticRawFixture(active);
        var sav = Load(before);
        var party = sav.GetPartySlotAtIndex(0);
        var box = sav.GetBoxSlotAtIndex(2, 3);
        party.Nickname = "Birch";
        box.Nickname = "Rowan";
        sav.SetPartySlotAtIndex(party, 0, EntityImportSettings.None);
        sav.SetBoxSlotAtIndex(box, 2, 3, EntityImportSettings.None);
        sav.Money = 654321;
        var after = sav.Write().ToArray();
        AssertProtectedBytes(before, after);
        AssertOnlyChanges(before, after,
            (active * BankSize + 0x98, PartySize),
            (active * BankSize + 0x78, 4),
            (active * BankSize + GeneralSize - 2, 2),
            (active * BankSize + StorageStart + BoxOffset, StoredSize),
            (active * BankSize + StorageStart + 0x12004, 4),
            (active * BankSize + StorageStart + StorageSize - 2, 2));
        var loaded = Load(after);
        Assert.Equal(654321u, loaded.Money);
        Assert.Equal("Birch", loaded.GetPartySlotAtIndex(0).Nickname);
        Assert.Equal("Rowan", loaded.GetBoxSlotAtIndex(2, 3).Nickname);
        Assert.True(loaded.GetPartySlotAtIndex(0).ChecksumValid);
        Assert.True(loaded.GetBoxSlotAtIndex(2, 3).ChecksumValid);
        AssertNativeCRC(after, 0);
        AssertNativeCRC(after, 1);
    }

    [Fact]
    public void CloneAndSameFormatCopyRetainBothAllocationsAndRebindActiveViews()
    {
        var sourceRaw = SyntheticRawFixture(1, seed: 5);
        var source = Load(sourceRaw);
        source.Money = 765432;
        var clone = Assert.IsType<SAV4HGSS>(source.Clone());
        Assert.True(clone.IsExpandedCampaign);
        Assert.Equal(765432u, clone.Money);
        Assert.Equal(source.CampaignData.ToArray(), clone.CampaignData.ToArray());
        AssertProtectedBytes(sourceRaw, clone.Write().ToArray());
        clone.Money = 101010;
        Assert.Equal(765432u, source.Money);
        var target = Load(SyntheticRawFixture(0, seed: 9)); // Different complete active partitions.
        target.CopyChangesFrom(source);
        Assert.Equal(source.Write().ToArray(), target.Write().ToArray());
        target.Money = 202020;
        target.Dex.SetSeen(300);
        target.Dex.SetCaught(300);
        target.Mystery.SetMysteryGiftReceivedFlag(31, true);
        var loaded = Load(target.Write().ToArray());
        Assert.Equal(202020u, loaded.Money);
        Assert.True(loaded.GetSeen(300));
        Assert.True(loaded.GetCaught(300));
        Assert.True(loaded.Mystery.GetMysteryGiftReceivedFlag(31));
        AssertProtectedBytes(sourceRaw, loaded.Write().ToArray());
        Assert.Equal(765432u, source.Money);
        Assert.Equal(sourceRaw.AsSpan(CampaignOffset, CampaignSize).ToArray(),
            target.Write().Span.Slice(CampaignOffset, CampaignSize).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void UnwrittenBankIsNotInitializedOrRepaired(int blankBank)
    {
        var before = SyntheticRawFixture();
        before.AsSpan(blankBank * BankSize, BankSize).Fill(0xFF);
        var snapshot = (byte[])before.Clone();
        var sav = Load(before);
        Assert.Equal((uint)(12345 + 1 - blankBank), sav.Money);
        Assert.True(sav.ChecksumsValid, sav.ChecksumInfo);
        sav.Money = 321;
        var after = sav.Write().ToArray();
        Assert.Equal(snapshot, before);
        Assert.Equal(snapshot.AsSpan(blankBank * BankSize, BankSize).ToArray(), after.AsSpan(blankBank * BankSize, BankSize).ToArray());
        AssertProtectedBytes(snapshot, after);
        var clone = Assert.IsType<SAV4HGSS>(sav.Clone());
        var target = Load(SyntheticRawFixture());
        target.CopyChangesFrom(clone);
        Assert.Equal(after, target.Write().ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ChecksumRecoveryChoosesCompletePeerAndPreservesEntireInvalidPartition(int corruptBank)
    {
        var before = SyntheticRawFixture(corruptBank);
        before[corruptBank * BankSize + 0x1000] ^= 1;
        before[corruptBank * BankSize + StorageStart + 0x456] ^= 1;
        var adapter = new ExpandedHGSSCampaignSave(before);
        Assert.Equal(before, adapter.Export(adapter.Normalize()));
        var sav = Load(before);
        Assert.True(sav.ChecksumsValid, sav.ChecksumInfo);
        Assert.Equal((uint)(12345 + 1 - corruptBank), sav.Money);
        Assert.Equal(0x135 + 1 - corruptBank, sav.FlagsBoxContentChanged);
        sav.Money = 98765;
        var clone = Assert.IsType<SAV4HGSS>(sav.Clone());
        var target = Load(SyntheticRawFixture(1));
        target.CopyChangesFrom(clone);
        var after = target.Write().ToArray();
        Assert.Equal(before.AsSpan(corruptBank * BankSize, BankSize).ToArray(),
            after.AsSpan(corruptBank * BankSize, BankSize).ToArray());
        AssertProtectedBytes(before, after);
        Assert.Equal(98765u, Load(after).Money);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void LoneValidGeneralAndStorageInOppositePartitionsReject(int corruptGeneral, int corruptStorage)
    {
        var bytes = SyntheticRawFixture();
        bytes[corruptGeneral * BankSize + 0x1000] ^= 1;
        bytes[corruptStorage * BankSize + StorageStart + 0x456] ^= 1;
        var before = (byte[])bytes.Clone();
        Assert.Throws<InvalidDataException>(() => ExpandedHGSSCampaignSave.IsRecognized(bytes));
        Assert.Throws<InvalidDataException>(() => new SAV4HGSS(bytes));
        Assert.Throws<InvalidDataException>(() => SaveUtil.GetSaveFile(bytes));
        Assert.Throws<InvalidDataException>(() => SaveUtil.GetSaveFile(SyntheticDSV(bytes)));
        Assert.Equal(before, bytes);
    }

    [Theory]
    [InlineData(8u, 12u, 7u, 11u)] // Former independent-selection fixture: no native transaction exists.
    [InlineData(8u, 7u, 7u, 8u)]
    public void ChecksumValidBlocksWithoutAnyEqualCounterPairReject(uint general0, uint storage0, uint general1, uint storage1)
    {
        var bytes = SyntheticRawFixture();
        SetNativeCounters(bytes.AsSpan(0, BankSize), general0, storage0);
        SetNativeCounters(bytes.AsSpan(BankSize, BankSize), general1, storage1);
        AssertNativeCRC(bytes, 0);
        AssertNativeCRC(bytes, 1);
        var before = (byte[])bytes.Clone();
        Assert.Throws<InvalidDataException>(() => ExpandedHGSSCampaignSave.IsRecognized(bytes));
        Assert.Throws<InvalidDataException>(() => SaveUtil.GetSaveFile(bytes));
        Assert.Throws<InvalidDataException>(() => new SAV4HGSS(bytes));
        Assert.Equal(before, bytes);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void OlderCompletePairWinsOverNewerOrphanAndPreservesEntirePartition(int orphanBank, bool generalOrphan)
    {
        var before = SyntheticRawFixture(orphanBank);
        // The newer bank has exactly one good orphan block, not a complete transaction.
        before[orphanBank * BankSize + (generalOrphan ? StorageStart + 0x456 : 0x1000)] ^= 1;
        var snapshot = (byte[])before.Clone();
        int active = 1 - orphanBank;
        var adapter = new ExpandedHGSSCampaignSave(before);
        Assert.Equal(snapshot, adapter.Export(adapter.Normalize()));
        var sav = Load(before);
        Assert.Equal((uint)(12345 + active), sav.Money);
        Assert.Equal(0x135 + active, sav.FlagsBoxContentChanged);
        Assert.Equal(before.AsSpan(active * BankSize + CampaignOffset, CampaignSize).ToArray(), sav.CampaignData.ToArray());
        var noEdit = sav.Write().ToArray();
        Assert.Equal(snapshot.AsSpan(orphanBank * BankSize, BankSize).ToArray(), noEdit.AsSpan(orphanBank * BankSize, BankSize).ToArray());
        AssertOnlyChanges(snapshot, noEdit,
            (active * BankSize + StorageStart + 0x12004, 4),
            (active * BankSize + StorageStart + StorageSize - 2, 2));

        sav.Money = 234567;
        var clone = Assert.IsType<SAV4HGSS>(sav.Clone());
        var target = Load(SyntheticRawFixture(orphanBank)); // Copy must rebind to the older complete peer.
        target.CopyChangesFrom(clone);
        var edited = target.Write().ToArray();
        Assert.Equal(snapshot, before);
        Assert.Equal(snapshot.AsSpan(orphanBank * BankSize, BankSize).ToArray(), edited.AsSpan(orphanBank * BankSize, BankSize).ToArray());
        AssertProtectedBytes(snapshot, edited);
        AssertOnlyChanges(snapshot, edited,
            (active * BankSize + 0x78, 4),
            (active * BankSize + GeneralSize - 2, 2),
            (active * BankSize + StorageStart + 0x12004, 4),
            (active * BankSize + StorageStart + StorageSize - 2, 2));
        var reopened = Load(edited);
        Assert.Equal(234567u, reopened.Money);
        Assert.Equal(snapshot.AsSpan(active * BankSize + CampaignOffset, CampaignSize).ToArray(), reopened.CampaignData.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CounterMismatchedButChecksumValidPartitionIsPreservedAsIncomplete(int incompleteBank)
    {
        var before = SyntheticRawFixture(incompleteBank);
        SetNativeCounters(before.AsSpan(incompleteBank * BankSize, BankSize), 8, 6);
        AssertNativeCRC(before, incompleteBank);
        int active = 1 - incompleteBank;
        var adapter = new ExpandedHGSSCampaignSave(before);
        Assert.Equal(before, adapter.Export(adapter.Normalize()));
        var sav = Load(before);
        Assert.Equal((uint)(12345 + active), sav.Money);
        Assert.Equal(0x135 + active, sav.FlagsBoxContentChanged);
        sav.Money = 456789;
        var after = sav.Write().ToArray();
        Assert.Equal(before.AsSpan(incompleteBank * BankSize, BankSize).ToArray(),
            after.AsSpan(incompleteBank * BankSize, BankSize).ToArray());
        AssertProtectedBytes(before, after);
        Assert.Equal(456789u, Load(after).Money);
    }

    [Fact]
    public void CompleteMaxCounterBankWinsOverZeroGeneralWithInvalidDummyZeroStorage()
    {
        var before = SyntheticRawFixture(0);
        SetNativeCounters(before.AsSpan(0, BankSize), 0, 0);
        before.AsSpan(StorageStart, StorageSize).Clear(); // Invalid schema/CRC; its dummy counter is zero.
        SetNativeCounters(before.AsSpan(BankSize, BankSize), uint.MaxValue, uint.MaxValue);
        AssertCRC(before.AsSpan(0, GeneralSize));
        AssertNativeCRC(before, 1);
        var sav = Load(before);
        Assert.Equal(12346u, sav.Money);
        Assert.Equal(0x136, sav.FlagsBoxContentChanged);
        Assert.Equal(before.AsSpan(BankSize + CampaignOffset, CampaignSize).ToArray(), sav.CampaignData.ToArray());
        var after = sav.Write().ToArray();
        Assert.Equal(before.AsSpan(0, BankSize).ToArray(), after.AsSpan(0, BankSize).ToArray());
        AssertOnlyChanges(before, after, (BankSize + StorageStart + 0x12004, 4),
            (BankSize + StorageStart + StorageSize - 2, 2));
        Assert.Equal(12346u, Load(after).Money);
    }

    [Theory]
    [InlineData(0u, 1u, 1)]
    [InlineData(1u, 0u, 0)]
    [InlineData(uint.MaxValue - 1, uint.MaxValue, 1)]
    [InlineData(uint.MaxValue, uint.MaxValue - 1, 0)]
    [InlineData(uint.MaxValue, 0u, 1)]
    [InlineData(0u, uint.MaxValue, 0)]
    [InlineData(7u, 7u, 0)]
    [InlineData(uint.MaxValue, uint.MaxValue, 0)]
    [InlineData(0u, 0u, 0)]
    [InlineData(uint.MaxValue, 1u, 0)]
    [InlineData(1u, uint.MaxValue, 1)]
    [InlineData(uint.MaxValue - 1, 0u, 0)]
    [InlineData(0u, uint.MaxValue - 1, 1)]
    public void NativeCounterSelectionIgnoresLastEntryTrailerAndHandlesWrap(uint first, uint second, int active)
    {
        var bytes = SyntheticRawFixture();
        for (int bank = 0; bank < 2; bank++)
        {
            var part = bytes.AsSpan(bank * BankSize, BankSize);
            uint counter = bank == 0 ? first : second;
            uint opposingTrailer = bank == active ? 0u : 999u;
            NativeFooter(part[..GeneralSize], 0, counter, opposingTrailer, SAV4.MAGIC_JAPAN_INTL);
            NativeFooter(part.Slice(StorageStart, StorageSize), 1, counter, opposingTrailer, SAV4.MAGIC_JAPAN_INTL);
        }
        var sav = Load(bytes);
        Assert.Equal((uint)(12345 + active), sav.Money);
        Assert.Equal(0x135 + active, sav.FlagsBoxContentChanged);
        Assert.Equal(bytes.AsSpan(active * BankSize + CampaignOffset, CampaignSize).ToArray(), sav.CampaignData.ToArray());
        Assert.Equal((uint)(12345 + active), Load(sav.Write().ToArray()).Money);
    }

    [Theory]
    [InlineData(0, 8, 0)]
    [InlineData(1, 8, 2)]
    [InlineData(0, 10, 0x7FF)]
    [InlineData(1, 10, 0x801)]
    [InlineData(0, 12, 0x1F)]
    [InlineData(1, 12, 0x21)]
    public void UnsupportedTaggedHeaderRejectsBeforeStockFallbackEvenWithReadableOtherBank(int bank, int field, ushort value)
    {
        var bytes = SyntheticRawFixture();
        WriteUInt16LittleEndian(bytes.AsSpan(bank * BankSize + CampaignOffset + field), value);
        // For bank 0 cases a decoy stock footer makes the old detector report retail HGSS.
        // Its size/magic overlap bank 1's campaign header, so do not overwrite a tested bank 1 field.
        if (bank == 0)
        {
            var stock = bytes.AsSpan(BankSize, SAV4HGSS.GeneralSize);
            WriteUInt32LittleEndian(stock[^12..], SAV4HGSS.GeneralSize);
            WriteUInt32LittleEndian(stock[^8..], SAV4.MAGIC_JAPAN_INTL);
        }
        if (field == 8)
        {
            Assert.Throws<NotSupportedException>(() => ExpandedHGSSCampaignSave.IsRecognized(bytes));
            Assert.Throws<NotSupportedException>(() => SaveUtil.GetSaveFile(bytes));
            Assert.Throws<NotSupportedException>(() => new SAV4HGSS(bytes));
        }
        else
        {
            Assert.Throws<InvalidDataException>(() => ExpandedHGSSCampaignSave.IsRecognized(bytes));
            Assert.Throws<InvalidDataException>(() => SaveUtil.GetSaveFile(bytes));
            Assert.Throws<InvalidDataException>(() => new SAV4HGSS(bytes));
        }
    }

    [Fact]
    public void MalformedMagicAndPhysicalLengthRejectExplicitly()
    {
        var bytes = SyntheticRawFixture();
        bytes[CampaignOffset] ^= 1;
        FixCRC(bytes.AsSpan(0, GeneralSize)); // CRC-valid expanded allocation cannot lack its tag.
        Assert.Throws<InvalidDataException>(() => SaveUtil.GetSaveFile(bytes));
        bytes = SyntheticRawFixture();
        Assert.Throws<InvalidDataException>(() => SaveUtil.GetSaveFile(bytes.AsMemory(0, bytes.Length - 1)));
        Assert.Throws<InvalidDataException>(() => SaveUtil.GetSaveFile(bytes.AsMemory(0, CampaignOffset + 8)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoUsableMainBlockRejectsInsteadOfAppearingStock(bool general)
    {
        var bytes = SyntheticRawFixture();
        for (int bank = 0; bank < 2; bank++)
            bytes[bank * BankSize + (general ? 0x1000 : StorageStart + 0x456)] ^= 1;
        Assert.Throws<InvalidDataException>(() => SaveUtil.GetSaveFile(bytes));
        Assert.Throws<InvalidDataException>(() => new SAV4HGSS(bytes));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void MixedCampaignAndValidNativeVanillaBankRejectsWithoutMigration(int vanillaBank, bool corruptExpandedTag)
    {
        var before = SyntheticRawFixture();
        var part = before.AsSpan(vanillaBank * BankSize, BankSize);
        part.Fill(0xFF);
        var general = part[..SAV4HGSS.GeneralSize];
        var storage = part.Slice(0xF700, StorageSize);
        general.Clear();
        storage.Clear();
        general[0x7D] = 2;
        general[0x80] = (byte)GameVersion.HG;
        NativeFooter(general, 0, 99, 99, SAV4.MAGIC_JAPAN_INTL);
        NativeFooter(storage, 1, 99, 99, SAV4.MAGIC_JAPAN_INTL);
        AssertCRC(general);
        if (corruptExpandedTag)
            before[(1 - vanillaBank) * BankSize + CampaignOffset] ^= 1; // Footer shape must still prevent stock fallback.
        var snapshot = (byte[])before.Clone();
        Assert.Throws<InvalidDataException>(() => ExpandedHGSSCampaignSave.IsRecognized(before));
        Assert.Throws<InvalidDataException>(() => new SAV4HGSS(before));
        Assert.Throws<InvalidDataException>(() => SaveUtil.GetSaveFile(before));
        Assert.Throws<InvalidDataException>(() => SaveUtil.GetSaveFile(SyntheticDSV(before)));
        Assert.Equal(snapshot, before);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CampaignV1RequiresNativeMagicForGeneralAndStorage(bool koreanGeneral, bool koreanStorage)
    {
        var bytes = SyntheticRawFixture();
        for (int bank = 0; bank < 2; bank++)
        {
            var part = bytes.AsSpan(bank * BankSize, BankSize);
            if (koreanGeneral)
                WriteUInt32LittleEndian(part[(GeneralSize - 8)..], SAV4.MAGIC_KOREAN);
            if (koreanStorage)
                WriteUInt32LittleEndian(part[(StorageStart + StorageSize - 8)..], SAV4.MAGIC_KOREAN);
            // The footer magic is not CRC-covered: these are still checksum-correct,
            // but not valid chunks in this opt-in native build.
            AssertCRC(part[..GeneralSize]);
            AssertCRC(part.Slice(StorageStart, StorageSize));
        }
        Assert.Throws<InvalidDataException>(() => ExpandedHGSSCampaignSave.IsRecognized(bytes));
        Assert.Throws<InvalidDataException>(() => SaveUtil.GetSaveFile(bytes));
        Assert.Throws<InvalidDataException>(() => new SAV4HGSS(bytes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CRCValidExpandedBankWithMissingTagRejectsEvenWithSupportedPeer(int badBank)
    {
        var before = SyntheticRawFixture();
        before[badBank * BankSize + CampaignOffset] ^= 1;
        FixCRC(before.AsSpan(badBank * BankSize, GeneralSize));
        var snapshot = (byte[])before.Clone();
        Assert.Throws<InvalidDataException>(() => ExpandedHGSSCampaignSave.IsRecognized(before));
        Assert.Throws<InvalidDataException>(() => SaveUtil.GetSaveFile(before));
        Assert.Throws<InvalidDataException>(() => new SAV4HGSS(before));
        Assert.Equal(snapshot, before);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void BadTagAndBadCRCGeneralBankRecoversAndIsNeverRepaired(int badBank, bool alsoCorruptHeaderFields)
    {
        var before = SyntheticRawFixture(badBank);
        before[badBank * BankSize + CampaignOffset] ^= 1;
        if (alsoCorruptHeaderFields)
        {
            var header = before.AsSpan(badBank * BankSize + CampaignOffset);
            WriteUInt16LittleEndian(header[8..], 2);
            WriteUInt16LittleEndian(header[10..], 0x7FF);
            WriteUInt16LittleEndian(header[12..], 0x21);
        }
        Assert.NotEqual(Checksums.CRC16_CCITT(before.AsSpan(badBank * BankSize, GeneralSize - 16)),
            ReadUInt16LittleEndian(before.AsSpan(badBank * BankSize + GeneralSize - 2)));
        var snapshot = (byte[])before.Clone();
        Assert.True(ExpandedHGSSCampaignSave.IsRecognized(before));
        var adapter = new ExpandedHGSSCampaignSave(before);
        Assert.Equal(snapshot, adapter.Export(adapter.Normalize()));
        var sav = Load(before);
        Assert.True(sav.IsExpandedCampaign);
        Assert.True(sav.ChecksumsValid, sav.ChecksumInfo);
        Assert.Equal((uint)(12345 + 1 - badBank), sav.Money);
        Assert.Equal(before.AsSpan((1 - badBank) * BankSize + CampaignOffset, CampaignSize).ToArray(),
            sav.CampaignData.ToArray());
        sav.Money = 56789;
        var clone = Assert.IsType<SAV4HGSS>(sav.Clone());
        var target = Load(SyntheticRawFixture());
        target.CopyChangesFrom(clone);
        var after = target.Write().ToArray();
        Assert.Equal(snapshot, before);
        Assert.Equal(snapshot.AsSpan(badBank * BankSize, BankSize).ToArray(),
            after.AsSpan(badBank * BankSize, BankSize).ToArray());
        AssertProtectedBytes(snapshot, after);
        Assert.Equal(56789u, Load(after).Money);
    }

    [Fact]
    public void BothBadTagsAndBadCRCsRejectRatherThanBecomingBlankOrStock()
    {
        var bytes = SyntheticRawFixture();
        bytes[CampaignOffset] ^= 1;
        bytes[BankSize + CampaignOffset] ^= 1;
        Assert.Throws<InvalidDataException>(() => ExpandedHGSSCampaignSave.IsRecognized(bytes));
        Assert.Throws<InvalidDataException>(() => SaveUtil.GetSaveFile(bytes));
        Assert.Throws<InvalidDataException>(() => SaveUtil.GetSaveFile(SyntheticDSV(bytes)));
    }

    [Fact]
    public void EngineDexSignatureDoesNotSelectEngineLayoutOrSpeciesTables()
    {
        var bytes = SyntheticRawFixture();
        for (int bank = 0; bank < 2; bank++)
        {
            var general = bytes.AsSpan(bank * BankSize, GeneralSize);
            WriteUInt32LittleEndian(general[0x12B8..], 0xBEEFCAFE);
            FixCRC(general);
        }
        Assert.False(HGEngineSave.IsRecognized(bytes));
        var sav = Load(bytes);
        Assert.True(sav.IsExpandedCampaign);
        Assert.False(sav.IsHGEngine);
        Assert.Equal(493, sav.MaxSpeciesID);
        Assert.Equal(PersonalTable.HGSS, sav.Personal);
        Assert.True(Load(sav.Write().ToArray()).IsExpandedCampaign);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidNativeFooterDoesNotWinCounterSelection(bool badSize)
    {
        var before = SyntheticRawFixture(0);
        var general = before.AsSpan(0, GeneralSize);
        if (badSize)
            WriteUInt32LittleEndian(general[^12..], GeneralSize - 1);
        else
            WriteUInt16LittleEndian(general[^4..], 1);
        var sav = Load(before);
        Assert.Equal(12346u, sav.Money);
        Assert.Equal(before.AsSpan(0, GeneralSize).ToArray(), sav.Write().Span[..GeneralSize].ToArray());
    }

    [Fact]
    public void UnrelatedExportDoesNotRepairInvalidExtraBlock()
    {
        var before = SyntheticRawFixture();
        before[0x23000 + 0x100] ^= 1;
        var sav = Load(before);
        Assert.False(sav.ChecksumsValid);
        var after = sav.Write().ToArray();
        AssertOnlyChanges(before, after, (BankSize + StorageStart + 0x12004, 4),
            (BankSize + StorageStart + StorageSize - 2, 2));
        AssertProtectedBytes(before, after);
        Assert.False(Load(after).ChecksumsValid);
    }

    [Fact]
    public void ExplicitExtraBlockEditReceivesChecksumWithoutChangingOtherExtras()
    {
        var before = SyntheticRawFixture();
        var sav = Load(before);
        sav.Data[0x23000 + 0x100] ^= 1;
        var after = sav.Write().ToArray();
        Assert.True(Load(after).ChecksumsValid);
        AssertOnlyChanges(before, after, (0x23000 + 0x100, 1), (0x23000 + 0x2AC0 - 2, 2),
            (BankSize + StorageStart + 0x12004, 4), (BankSize + StorageStart + StorageSize - 2, 2));
    }

    [Fact]
    public void CrossFormatCopyRejectsBeforeMutatingEitherSave()
    {
        var expanded = Load(SyntheticRawFixture());
        var stock = new SAV4HGSS();
        var engineBytes = new byte[0x80000];
        for (int bank = 0; bank < 2; bank++)
        {
            var part = engineBytes.AsSpan(bank * BankSize, BankSize);
            WriteUInt32LittleEndian(part[0x12B8..], 0xBEEFCAFE);
            NativeFooter(part[..HGEngineSave.GeneralSize], 0, 1, 1, SAV4.MAGIC_JAPAN_INTL);
            NativeFooter(part.Slice(HGEngineSave.StorageStart, StorageSize), 1, 1, 1, SAV4.MAGIC_JAPAN_INTL);
        }
        var engine = Load(engineBytes);
        Assert.True(engine.IsHGEngine);
        Assert.False(engine.IsExpandedCampaign);
        var before = expanded.Data.ToArray();
        foreach (var other in new SaveFile[] { stock, engine, new SAV4Pt(), new SAV4DP() })
        {
            var otherBefore = other.Data.ToArray();
            Assert.Throws<ArgumentException>(() => expanded.CopyChangesFrom(other));
            Assert.Throws<ArgumentException>(() => other.CopyChangesFrom(expanded));
            Assert.Equal(before, expanded.Data.ToArray());
            Assert.Equal(otherBefore, other.Data.ToArray());
        }
        Assert.Empty(stock.CampaignData.ToArray());
        Assert.Empty(engine.CampaignData.ToArray());
        Assert.False(ExpandedHGSSCampaignSave.IsRecognized(engineBytes));
        Assert.False(ExpandedHGSSCampaignSave.IsRecognized(new byte[0x80000]));
        Assert.False(ExpandedHGSSCampaignSave.IsRecognized(new byte[8]));
    }

    [Fact]
    public void TypeOverrideDoesNotReinterpretNormalizedCampaignViewAsStock()
    {
        var sav = Load(SyntheticRawFixture());
        sav.Money = 202020;
        Assert.True(SaveUtil.TryOverride(sav, SaveFileType.HGSS, out var copy));
        var expandedCopy = Assert.IsType<SAV4HGSS>(copy);
        Assert.True(expandedCopy.IsExpandedCampaign);
        Assert.Equal(sav.Write().ToArray(), expandedCopy.Write().ToArray());
        Assert.False(SaveUtil.TryOverride(sav, SaveFileType.Pt, out _));
        Assert.False(SaveUtil.TryOverride(sav, SaveFileType.DP, out _));
    }

    [Theory]
    [InlineData(0, GameVersion.HG)]
    [InlineData(1, GameVersion.SS)]
    public void SyntheticDSVLoadsExportsAndReopensThroughHighLevelSaveUtil(int active, GameVersion version)
    {
        var raw = SyntheticRawFixture(active, version);
        var wrapped = SyntheticDSV(raw);
        var snapshot = (byte[])wrapped.Clone();
        // The raw adapter remains strict: container handling belongs to SaveUtil.
        Assert.Throws<InvalidDataException>(() => ExpandedHGSSCampaignSave.IsRecognized(wrapped));
        Assert.True(SaveUtil.TryGetSaveFile(wrapped, out var result, "synthetic-campaign.dsv"));
        var sav = Assert.IsType<SAV4HGSS>(result);
        Assert.True(sav.IsExpandedCampaign);
        Assert.False(sav.IsHGEngine);
        Assert.True(sav.Metadata.HasFooter);
        Assert.Equal(".dsv", sav.Metadata.GetSuggestedExtension());
        Assert.Equal(0x80000, sav.Data.Length);
        Assert.Equal(snapshot, wrapped);
        var noEdit = sav.Write().ToArray();
        Assert.Equal(0x8007A, noEdit.Length);
        Assert.Equal(snapshot.AsSpan(0x80000).ToArray(), noEdit.AsSpan(0x80000).ToArray());
        AssertProtectedBytes(snapshot, noEdit);
        AssertOnlyChanges(snapshot, noEdit,
            (active * BankSize + StorageStart + 0x12004, 4),
            (active * BankSize + StorageStart + StorageSize - 2, 2));
        var loaded = Load(noEdit);
        Assert.True(loaded.IsExpandedCampaign);
        Assert.True(loaded.ChecksumsValid, loaded.ChecksumInfo);
        Assert.Equal(version, loaded.Version);
        Assert.Equal("Party", loaded.GetPartySlotAtIndex(0).Nickname);
        Assert.Equal("Box", loaded.GetBoxSlotAtIndex(2, 3).Nickname);
        Assert.Equal(sav.CampaignData.ToArray(), loaded.CampaignData.ToArray());

        var party = loaded.GetPartySlotAtIndex(0);
        var box = loaded.GetBoxSlotAtIndex(2, 3);
        party.Nickname = "DSVParty";
        box.Nickname = "DSVBox";
        loaded.SetPartySlotAtIndex(party, 0, EntityImportSettings.None);
        loaded.SetBoxSlotAtIndex(box, 2, 3, EntityImportSettings.None);
        loaded.Money = 246810;
        var edited = loaded.Write().ToArray();
        AssertProtectedBytes(snapshot, edited);
        Assert.Equal(snapshot.AsSpan(0x80000).ToArray(), edited.AsSpan(0x80000).ToArray());
        AssertOnlyChanges(snapshot, edited,
            (active * BankSize + 0x98, PartySize),
            (active * BankSize + 0x78, 4),
            (active * BankSize + GeneralSize - 2, 2),
            (active * BankSize + StorageStart + BoxOffset, StoredSize),
            (active * BankSize + StorageStart + 0x12004, 4),
            (active * BankSize + StorageStart + StorageSize - 2, 2));
        var reopened = Load(edited);
        Assert.True(reopened.IsExpandedCampaign);
        Assert.True(reopened.ChecksumsValid, reopened.ChecksumInfo);
        Assert.Equal(246810u, reopened.Money);
        Assert.Equal("DSVParty", reopened.GetPartySlotAtIndex(0).Nickname);
        Assert.Equal("DSVBox", reopened.GetBoxSlotAtIndex(2, 3).Nickname);
        Assert.True(reopened.GetPartySlotAtIndex(0).ChecksumValid);
        Assert.True(reopened.GetBoxSlotAtIndex(2, 3).ChecksumValid);
        Assert.Equal(snapshot, wrapped);
        Assert.Equal(edited, reopened.Clone().Write().ToArray());
        var excluded = reopened.Write(BinaryExportSetting.ExcludeFooter).ToArray();
        Assert.Equal(0x80000, excluded.Length);
        Assert.Equal(edited.AsSpan(0, 0x80000).ToArray(), excluded);
        Assert.True(Load(excluded).IsExpandedCampaign);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0x79)]
    [InlineData(0x7A)]
    [InlineData(0x7B)]
    public void UnrecognizedTrailingBytesAreNotTreatedAsDSV(int trailingLength)
    {
        var bytes = new byte[0x80000 + trailingLength];
        SyntheticRawFixture().CopyTo(bytes, 0);
        bytes.AsSpan(0x80000).Fill(0xA5); // No DeSmuME end marker, even at the expected length.
        Assert.Throws<InvalidDataException>(() => SaveUtil.GetSaveFile(bytes));
        if (trailingLength == 0x79)
        {
            var marker = "|-DESMUME SAVE-|"u8;
            marker.CopyTo(bytes.AsSpan(bytes.Length - marker.Length));
            Assert.Throws<InvalidDataException>(() => SaveUtil.GetSaveFile(bytes)); // Marker alone is insufficient.
        }
    }

    [Theory]
    [InlineData(8, 2)]
    [InlineData(10, 0x7FF)]
    [InlineData(12, 0x21)]
    public void GenuineDSVDoesNotBypassUnsupportedRawHeaderRejection(int field, ushort value)
    {
        var raw = SyntheticRawFixture();
        WriteUInt16LittleEndian(raw.AsSpan(CampaignOffset + field), value);
        var wrapped = SyntheticDSV(raw);
        if (field == 8)
            Assert.Throws<NotSupportedException>(() => SaveUtil.GetSaveFile(wrapped));
        else
            Assert.Throws<InvalidDataException>(() => SaveUtil.GetSaveFile(wrapped));
    }

    [Theory]
    [InlineData(false, SAV4.MAGIC_JAPAN_INTL)]
    [InlineData(false, SAV4.MAGIC_KOREAN)]
    [InlineData(true, SAV4.MAGIC_JAPAN_INTL)]
    public void ExistingVanillaAndEngineDSVPathsKeepTheirFormats(bool engine, uint magic)
    {
        // SYNTHETIC non-campaign regression, with blank extras and independent native blocks.
        var raw = new byte[0x80000];
        raw.AsSpan().Fill(0xFF);
        int generalSize = engine ? HGEngineSave.GeneralSize : SAV4HGSS.GeneralSize;
        int storageStart = engine ? HGEngineSave.StorageStart : 0xF700;
        for (int bank = 0; bank < 2; bank++)
        {
            var part = raw.AsSpan(bank * BankSize, BankSize);
            var general = part[..generalSize];
            var storage = part.Slice(storageStart, StorageSize);
            general.Clear();
            storage.Clear();
            general[0x7D] = 2;
            general[0x80] = (byte)GameVersion.HG;
            if (engine)
                WriteUInt32LittleEndian(general[0x12B8..], 0xBEEFCAFE);
            NativeFooter(general, 0, (uint)(2 - bank), (uint)(2 - bank), magic);
            NativeFooter(storage, 1, (uint)(2 - bank), (uint)(2 - bank), magic);
        }
        var wrapped = SyntheticDSV(raw);
        var before = (byte[])wrapped.Clone();
        var sav = Load(wrapped);
        Assert.False(sav.IsExpandedCampaign);
        Assert.Equal(engine, sav.IsHGEngine);
        Assert.True(sav.Metadata.HasFooter);
        Assert.Equal(before, wrapped); // Loading is side-effect free; vanilla Write retains its existing aliasing.
        var after = sav.Write().ToArray();
        Assert.Equal(before.AsSpan(0x80000).ToArray(), after.AsSpan(0x80000).ToArray());
        var reopened = Load(after);
        Assert.False(reopened.IsExpandedCampaign);
        Assert.Equal(engine, reopened.IsHGEngine);
    }
}