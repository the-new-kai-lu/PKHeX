using System;
using Xunit;
using static System.Buffers.Binary.BinaryPrimitives;

namespace PKHeX.Core.Tests;

/// <summary>
/// Synthetic retail-layout saves, not blank SAV4HGSS instances or HG-engine projections.
/// Both 0x40000 partitions contain independently checksummed general and storage blocks.
/// These tests cover editor serialization, not booting a save in a game/emulator.
/// </summary>
public class HGSSBaselineTests
{
    private const int PartitionSize = 0x40000;
    private const int StorageStart = 0xF700;
    private const int StorageSize = 0x12310;
    private const int EventWork = 0xDE4;
    private const int EventFlag = 0x10C4;
    private const int BoxSlotSize = 0x88; // Gen 4 stored PKM size.

    private static byte[] Fixture(GameVersion origin, int generalActive, int storageActive)
    {
        var data = new byte[2 * PartitionSize];
        data.AsSpan().Fill(0xFF); // Uninitialized extra blocks (Hall of Fame, videos).
        for (int slot = 0; slot < 2; slot++)
        {
            var partition = data.AsSpan(slot * PartitionSize, PartitionSize);
            var general = partition[..SAV4HGSS.GeneralSize];
            var storage = partition.Slice(StorageStart, StorageSize);
            general.Clear();
            storage.Clear();

            // Distinct story state in each partition: all work variables and all event flags.
            for (int i = EventWork; i < EventFlag + (0xB60 / 8); i++)
                general[i] = (byte)((i * 37 + slot * 73) & 0xFF);
            WriteUInt16LittleEndian(general[0x64..], (ushort)(slot == 0 ? 'A' : 'B'));
            WriteUInt16LittleEndian(general[0x66..], 0xFFFF);
            WriteUInt32LittleEndian(general[0x74..], (uint)(12345 + slot));
            general[0x7D] = 2; // English
            general[0x1C] = (byte)(0x1C + slot); // Unrelated saved data must survive parsing.
            general[0x80] = (byte)origin;

            // Non-block bytes must not be normalized away by an editor export.
            for (int i = SAV4HGSS.GeneralSize; i < StorageStart; i++)
                partition[i] = (byte)(i + slot);
            for (int i = StorageStart + StorageSize; i < 0x23000; i++)
                partition[i] = (byte)(i * 3 + slot);
            partition[0x3F000] = (byte)(0x51 + slot);

            WriteInt32LittleEndian(storage[0x12004..], 0x135 + slot);
            Footer(general, 0, slot == generalActive ? 8u : 7u);
            Footer(storage, 1, slot == storageActive ? 12u : 11u);
        }

        AssertBlockChecksums(data);
        var sav = Assert.IsType<SAV4HGSS>(SaveUtil.GetSaveFile(data));
        Assert.True(sav.State.Exportable);
        Assert.False(sav.IsHGEngine);
        Assert.True(sav.ChecksumsValid, sav.ChecksumInfo);
        sav.SetPartySlotAtIndex(Pokemon(25, origin, 37), 0, EntityImportSettings.None);
        sav.SetBoxSlotAtIndex(Pokemon(133, origin, 28), 2, 3, EntityImportSettings.None);
        data = sav.Write().ToArray(); // Seal Pokémon-bearing blocks and the untouched backup.

        // Retain a non-all-boxes-changed marker to exercise the documented write bookkeeping.
        var activeStorage = data.AsSpan(storageActive * PartitionSize + StorageStart, StorageSize);
        WriteInt32LittleEndian(activeStorage[0x12004..], 0x135 + storageActive);
        FixChecksum(activeStorage);
        AssertBlockChecksums(data);
        Assert.True(Assert.IsType<SAV4HGSS>(SaveUtil.GetSaveFile(data)).ChecksumsValid);
        return data;
    }

    [Theory]
    [InlineData(GameVersion.HG, 0)]
    [InlineData(GameVersion.SS, 1)]
    public void LoadingPreservesEveryByteAndNativeVersion(GameVersion origin, int active)
    {
        var data = Fixture(origin, active, 1 - active);
        var before = data.AsSpan().ToArray();
        var sav = Assert.IsType<SAV4HGSS>(SaveUtil.GetSaveFile(data));
        Assert.Equal(before, data);
        Assert.True(sav.ChecksumsValid, sav.ChecksumInfo);
        Assert.Equal(origin, sav.Version);
        Assert.Equal((byte)origin, sav.ROMCode);
        Assert.Equal(before, sav.Data.ToArray());
    }

    [Fact]
    public void BlankInitializationSetsVersionAtTrainerOffsetOnly()
    {
        var sav = new SAV4HGSS();
        Assert.Equal(GameVersion.HGSS, sav.Version);
        Assert.Equal((byte)GameVersion.HGSS, sav.ROMCode);
        Assert.Equal(0, sav.General[0x1C]);
    }

    private static PK4 Pokemon(ushort species, GameVersion origin, byte level)
    {
        var pk = new PK4
        {
            Species = species,
            PID = 0x13572468,
            ID32 = 12345,
            Version = origin,
            Language = 2,
            OriginalTrainerName = "TEST",
            Ability = PersonalTable.HGSS[species].Ability1,
            EXP = Experience.GetEXP(level, PersonalTable.HGSS[species].EXPGrowth),
            Move1 = 33,
            Move1_PP = 35,
        };
        pk.Nickname = SpeciesName.GetSpeciesNameGeneration(species, 2, 4);
        pk.RefreshChecksum();
        return pk;
    }

    private static void Footer(Span<byte> block, ushort id, uint revision)
    {
        WriteUInt32LittleEndian(block[^20..], revision);
        WriteUInt32LittleEndian(block[^16..], revision);
        WriteUInt32LittleEndian(block[^12..], (uint)block.Length);
        WriteUInt32LittleEndian(block[^8..], SAV4.MAGIC_JAPAN_INTL);
        WriteUInt16LittleEndian(block[^4..], id);
        FixChecksum(block);
        Assert.Equal(Checksums.CRC16_CCITT(block[..^16]), ReadUInt16LittleEndian(block[^2..]));
    }

    private static void FixChecksum(Span<byte> block) =>
        WriteUInt16LittleEndian(block[^2..], Checksums.CRC16_CCITT(block[..^16]));

    private static void AssertBlockChecksums(byte[] data)
    {
        for (int slot = 0; slot < 2; slot++)
        {
            var partition = data.AsSpan(slot * PartitionSize, PartitionSize);
            AssertBlockChecksum(partition[..SAV4HGSS.GeneralSize]);
            AssertBlockChecksum(partition.Slice(StorageStart, StorageSize));
        }
    }

    private static void AssertBlockChecksum(ReadOnlySpan<byte> block) =>
        Assert.Equal(Checksums.CRC16_CCITT(block[..^16]), ReadUInt16LittleEndian(block[^2..]));

    private static void AssertStoryUnchanged(byte[] expected, byte[] actual)
    {
        for (int slot = 0; slot < 2; slot++)
        {
            int offset = slot * PartitionSize;
            Assert.Equal(expected.AsSpan(offset + EventWork, EventFlag - EventWork).ToArray(),
                actual.AsSpan(offset + EventWork, EventFlag - EventWork).ToArray());
            Assert.Equal(expected.AsSpan(offset + EventFlag, 0xB60 / 8).ToArray(),
                actual.AsSpan(offset + EventFlag, 0xB60 / 8).ToArray());
        }
    }

    private static void AssertOnlyExpectedBytesChanged(byte[] before, byte[] after, int storageActive, int? editedBoxOffset = null)
    {
        Assert.Equal(0x80000, after.Length);
        int storage = storageActive * PartitionSize + StorageStart;
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i] == after[i])
                continue;
            bool bookkeeping = i >= storage + 0x12004 && i < storage + 0x12008 // changed-box mask
                || i >= storage + StorageSize - 2 && i < storage + StorageSize; // CRC16
            bool pokemon = editedBoxOffset is int box && i >= box && i < box + BoxSlotSize;
            Assert.True(bookkeeping || pokemon, $"Unexpected change at save offset 0x{i:X5}.");
        }
    }

    [Theory]
    [InlineData(GameVersion.HG, 0, 1)]
    [InlineData(GameVersion.SS, 1, 0)]
    public void RetailNoOpExportPreservesBothPartitionsAndStory(GameVersion origin, int generalActive, int storageActive)
    {
        var before = Fixture(origin, generalActive, storageActive);
        Assert.False(HGEngineSave.IsRecognized(before));
        var sav = Assert.IsType<SAV4HGSS>(SaveUtil.GetSaveFile(before));
        Assert.False(sav.IsHGEngine);
        Assert.Equal(493, sav.MaxSpeciesID);
        Assert.Equal(origin, sav.Version);
        Assert.True(sav.State.Exportable);
        Assert.True(sav.ChecksumsValid);
        Assert.Equal((ushort)(12345 + generalActive), sav.TID16);
        Assert.Equal(0x135 + storageActive, sav.FlagsBoxContentChanged);
        Assert.Equal(generalActive, SAV4BlockDetection.CompareFooters(before,
            SAV4HGSS.GeneralSize - 20, PartitionSize + SAV4HGSS.GeneralSize - 20));
        Assert.Equal(storageActive, SAV4BlockDetection.CompareFooters(before,
            StorageStart + StorageSize - 20, PartitionSize + StorageStart + StorageSize - 20));

        // Check every exposed work variable and flag, not just selected story milestones.
        for (int i = 0; i < sav.EventWorkCount; i++)
            Assert.Equal(ReadUInt16LittleEndian(before.AsSpan(generalActive * PartitionSize + EventWork + 2 * i)), sav.GetWork(i));
        for (int i = 0; i < sav.EventFlagCount; i++)
        {
            int offset = generalActive * PartitionSize + EventFlag + (i >> 3);
            Assert.Equal((before[offset] & (1 << (i & 7))) != 0, sav.GetEventFlag(i));
        }

        var after = sav.Write().ToArray();
        AssertStoryUnchanged(before, after);
        AssertOnlyExpectedBytesChanged(before, after, storageActive);
        AssertBlockChecksums(after);
        Assert.Equal(0x3_FFFF, ReadInt32LittleEndian(after.AsSpan(storageActive * PartitionSize + StorageStart + 0x12004)));
        var loaded = Assert.IsType<SAV4HGSS>(SaveUtil.GetSaveFile(after));
        Assert.False(loaded.IsHGEngine);
        Assert.True(loaded.ChecksumsValid, loaded.ChecksumInfo);
        var party = (PK4)loaded.GetPartySlotAtIndex(0);
        var box = (PK4)loaded.GetBoxSlotAtIndex(2, 3);
        Assert.True(party.ChecksumValid);
        Assert.True(box.ChecksumValid);
        Assert.Equal(origin, party.Version);
        Assert.Equal(origin, box.Version);
        Assert.Equal((ushort)25, party.Species);
        Assert.Equal((ushort)133, box.Species);
        Assert.Equal(Experience.GetEXP(37, party.PersonalInfo.EXPGrowth), party.EXP);
        Assert.Equal(Experience.GetEXP(28, box.PersonalInfo.EXPGrowth), box.EXP);
        Assert.Equal(PersonalTable.HGSS[25].Ability1, party.Ability);
        Assert.Equal(PersonalTable.HGSS[133].Ability1, box.Ability);
    }

    [Theory]
    [InlineData(GameVersion.HG, 0, 1)]
    [InlineData(GameVersion.SS, 1, 0)]
    public void IsolatedBoxEditRoundTripsWithoutChangingPartyStoryOrBackup(GameVersion origin, int generalActive, int storageActive)
    {
        var before = Fixture(origin, generalActive, storageActive);
        var sav = Assert.IsType<SAV4HGSS>(SaveUtil.GetSaveFile(before));
        var partyBefore = (PK4)sav.GetPartySlotAtIndex(0);
        var box = (PK4)sav.GetBoxSlotAtIndex(2, 3);
        Assert.True(partyBefore.ChecksumValid);
        Assert.True(box.ChecksumValid);
        Assert.Equal(Experience.GetEXP(37, partyBefore.PersonalInfo.EXPGrowth), partyBefore.EXP);
        Assert.Equal(PersonalTable.HGSS[25].Ability1, partyBefore.Ability);
        Assert.Equal(Experience.GetEXP(28, box.PersonalInfo.EXPGrowth), box.EXP);
        Assert.Equal(PersonalTable.HGSS[133].Ability1, box.Ability);

        uint editedEXP = Experience.GetEXP(36, box.PersonalInfo.EXPGrowth);
        int editedAbility = box.Ability == 1 ? 2 : 1;
        box.EXP = editedEXP;
        box.Ability = editedAbility;
        box.RefreshChecksum();
        sav.SetBoxSlotAtIndex(box, 2, 3, EntityImportSettings.None);

        var after = sav.Write().ToArray();
        int editedBoxOffset = storageActive * PartitionSize + StorageStart + sav.GetBoxSlotOffset(2, 3);
        AssertStoryUnchanged(before, after);
        AssertOnlyExpectedBytesChanged(before, after, storageActive, editedBoxOffset);
        AssertBlockChecksums(after);
        var loaded = Assert.IsType<SAV4HGSS>(SaveUtil.GetSaveFile(after));
        Assert.False(loaded.IsHGEngine);
        Assert.True(loaded.ChecksumsValid, loaded.ChecksumInfo);
        var party = (PK4)loaded.GetPartySlotAtIndex(0);
        var edited = (PK4)loaded.GetBoxSlotAtIndex(2, 3);
        Assert.True(party.ChecksumValid);
        Assert.True(edited.ChecksumValid);
        Assert.Equal(origin, party.Version);
        Assert.Equal(origin, edited.Version);
        Assert.Equal(partyBefore.Data.ToArray(), party.Data.ToArray());
        Assert.Equal((ushort)133, edited.Species);
        Assert.Equal(editedEXP, edited.EXP);
        Assert.Equal(editedAbility, edited.Ability);
        Assert.Equal((byte)36, edited.CurrentLevel);
    }
}