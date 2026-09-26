using System;
using Xunit;
using static System.Buffers.Binary.BinaryPrimitives;

namespace PKHeX.Core.Tests;

public class HGEngineTests
{
    private static byte[] Fixture()
    {
        var data = new byte[0x80000];
        new Random(417).NextBytes(data);
        for (int slot = 0; slot < 2; slot++)
        {
            var part = data.AsSpan(slot * 0x40000);
            WriteUInt32LittleEndian(part[0x12B8..], 0xBEEFCAFE);
            Footer(part[..HGEngineSave.GeneralSize], 0, (uint)(2 - slot));
            Footer(part.Slice(HGEngineSave.StorageStart, 0x12310), 1, (uint)(2 - slot));
        }
        return data;
    }

    private static void Footer(Span<byte> block, ushort id, uint count)
    {
        WriteUInt32LittleEndian(block[^20..], count);
        WriteUInt32LittleEndian(block[^16..], count);
        WriteUInt32LittleEndian(block[^12..], (uint)block.Length);
        WriteUInt32LittleEndian(block[^8..], SAV4.MAGIC_JAPAN_INTL);
        WriteUInt16LittleEndian(block[^4..], id);
        WriteUInt16LittleEndian(block[^2..], Checksums.CRC16_CCITT(block[..^16]));
    }

    [Fact]
    public void ProjectionPreservesEveryByte()
    {
        var original = Fixture();
        Assert.True(HGEngineSave.IsRecognized(original));
        var adapter = new HGEngineSave(original);
        Assert.Equal(original, adapter.Export(adapter.Normalize()));
    }

    [Fact]
    public void CustomSpeciesPartyBoxAndDexRoundTrip()
    {
        var sav = Assert.IsType<SAV4HGSS>(SaveUtil.GetSaveFile(Fixture()));
        Assert.True(sav.IsHGEngine);
        Assert.Equal(18, sav.BoxCount);
        Assert.Equal(1086, sav.MaxSpeciesID);
        // Synthetic fixture: initialize fields normally provided by the game.
        sav.Language = 2;
        sav.OT = "TEST";
        sav.General[0x94] = 1;
        for (ushort species = 1076; species <= 1086; species++)
        {
            var pk = new PK4 { Species = species, PID = 12345, Version = GameVersion.HG,
                Language = 2, OriginalTrainerName = "TEST", Ability = HGEngineSpecies.Personal[species].Ability1,
                Move1 = 33, Move1_PP = 35, EXP = Experience.GetEXP(50, HGEngineSpecies.Personal[species].EXPGrowth) };
            pk.Nickname = SpeciesName.GetSpeciesNameGeneration(species, 2, 4);
            sav.SetPartySlotAtIndex(pk, 0);
            sav.SetBoxSlotAtIndex(pk, 0, species - 1076);
            sav.Dex.SetCaught(species);
            sav.Dex.SetSeen(species);
            var clone = (SAV4HGSS)sav.Clone();
            Assert.True(clone.GetCaught(species));
            var loaded = Assert.IsType<SAV4HGSS>(SaveUtil.GetSaveFile(sav.Write()));
            var party = loaded.GetPartySlotAtIndex(0);
            Assert.Equal(species, party.Species);
            Assert.Equal(50, party.CurrentLevel);
            Assert.Equal(HGEngineSpecies.Personal[species].HP, party.PersonalInfo.HP);
            Assert.True(party.ChecksumValid);
            Assert.Equal(species, loaded.GetBoxSlotAtIndex(0, species - 1076).Species);
            Assert.True(loaded.GetCaught(species));
            Assert.True(loaded.GetSeen(species));
            Assert.False(new LegalityAnalysis(party).Valid);
            Assert.Contains(new FilteredGameDataSource(loaded, GameInfo.Sources).Species, x => x.Value == species);
        }
    }

    [Fact]
    public void CorruptNewestGeneralUsesValidBackupAndPreservesCorruptBytes()
    {
        var bytes = Fixture();
        bytes[100] ^= 1;
        Assert.True(HGEngineSave.IsRecognized(bytes));
        var adapter = new HGEngineSave(bytes);
        Assert.Equal(bytes, adapter.Export(adapter.Normalize()));
        var sav = Assert.IsType<SAV4HGSS>(SaveUtil.GetSaveFile(bytes));
        Assert.Equal(bytes[0x40000 + 0x64], sav.General[0x64]);
    }

    [Fact]
    public void RejectWrongLayoutAndBadChecksums()
    {
        var bytes = Fixture();
        bytes[100] ^= 1;
        bytes[0x40000 + 100] ^= 1;
        Assert.False(HGEngineSave.IsRecognized(bytes));
        Assert.False(HGEngineSave.IsRecognized(new byte[0x80000]));
        Assert.False(HGEngineSave.IsRecognized(new byte[12]));
    }

    [Fact]
    public void RetailSaveIsNotReinterpreted()
    {
        var sav = new SAV4HGSS();
        Assert.False(sav.IsHGEngine);
        Assert.Equal(493, sav.MaxSpeciesID);
        Assert.Equal(PersonalTable.HGSS, sav.Personal);
        Assert.DoesNotContain(new FilteredGameDataSource(sav, GameInfo.Sources).Species, x => x.Value >= 1076);
    }
}
