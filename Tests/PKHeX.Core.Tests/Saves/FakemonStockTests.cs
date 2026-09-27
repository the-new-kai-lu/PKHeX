using System;
using System.Linq;
using Xunit;
using static System.Buffers.Binary.BinaryPrimitives;

namespace PKHeX.Core.Tests;

public class FakemonStockTests
{
    private static byte[] Fixture(bool marked = true)
    {
        var data = new byte[0x80000];
        for (int slot = 0; slot < 2; slot++)
        {
            var part = data.AsSpan(slot * 0x40000, 0x40000);
            part[0x23000..].Fill(0xFF); // Uninitialized optional extra blocks.
            WriteUInt32LittleEndian(part[FakemonStockProfile.DexOffset..], 0xBEEFCAFE);
            part[0x1C] = 0x1A; // Adventure data, not ROMCode.
            part[0x80] = (byte)GameVersion.HG;
            if (marked)
            {
                part[FakemonStockProfile.MarkerOffset] = 0x46;
                part[FakemonStockProfile.MarkerOffset + 1] = 0x4B;
                part[FakemonStockProfile.VersionOffset] = 1;
            }
            Footer(part[..SAV4HGSS.GeneralSize], 0, (uint)(2 - slot));
            Footer(part.Slice(0xF700, 0x12310), 1, (uint)(2 - slot));
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

    private static SAV4HGSS Load(Memory<byte> data) => Assert.IsType<SAV4HGSS>(SaveUtil.GetSaveFile(data));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LoadingDoesNotMutateSaveBytesOrInvalidateChecksums(bool marked)
    {
        var data = Fixture(marked);
        var original = (byte[])data.Clone();
        var sav = Load(data);
        Assert.Equal(original, data);
        Assert.True(sav.ChecksumsValid);
        Assert.Equal(GameVersion.HG, sav.Version);
        Assert.Equal(0x1A, sav.General[0x1C]);
    }

    [Fact]
    public void MarkerAndVersionSelectOnlyTheStockProfile()
    {
        var sav = Load(Fixture());
        Assert.True(sav.IsFakemonStock);
        Assert.False(sav.IsHGEngine);
        Assert.Equal(1086, sav.MaxSpeciesID);
        Assert.Equal(474, sav.MaxMoveID);
        Assert.True(((SAV4HGSS)sav.Clone()).IsFakemonStock);
        Assert.False(Load(Fixture(false)).IsFakemonStock);
        sav.General[FakemonStockProfile.VersionOffset] = 2;
        Assert.False(sav.IsFakemonStock);
    }

    [Fact]
    public void CanonicalPersonalDataRemainsRetailAndCustomTypesAreNormalized()
    {
        for (int i = 0; i < PersonalTable.HGSS.Count; i++)
            Assert.Equal(PersonalTable.HGSS[i].Write(), FakemonStockProfile.Personal[i].Write());
        Assert.Equal((byte)MoveType.Electric, FakemonStockProfile.Personal[1076].Type1);
        Assert.Equal((byte)MoveType.Fire, FakemonStockProfile.Personal[1079].Type1);
        Assert.Equal((byte)MoveType.Dark, FakemonStockProfile.Personal[1082].Type1);
        Assert.Equal((byte)MoveType.Ice, FakemonStockProfile.Personal[1082].Type2);
        Assert.Equal((byte)MoveType.Water, FakemonStockProfile.Personal[1084].Type2);
    }

    [Fact]
    public void AllSpeciesAndCustomDexBitsRoundTripWithoutTouchingDeoxys()
    {
        var sav = Load(Fixture());
        sav.Language = 2;
        sav.OT = "TEST";
        sav.General[0x94] = 1;
        for (int region = 0; region < 4; region++)
            sav.General[FakemonStockProfile.DexOffset + 4 + region * 0x40 + 63] = (byte)(0xA0 + region);
        var forms = sav.Dex.GetForms(386);
        for (ushort species = 1076; species <= 1086; species++)
        {
            var pk = sav.BlankPKM;
            pk.Species = species;
            pk.PID = 12345;
            pk.Language = 2;
            pk.Version = GameVersion.HG;
            pk.Ability = pk.PersonalInfo.Ability1;
            pk.EXP = Experience.GetEXP(50, pk.PersonalInfo.EXPGrowth);
            pk.Nickname = HGEngineSpecies.Names[species - 1076];
            sav.SetPartySlotAtIndex(pk, 0);
            sav.SetBoxSlotAtIndex(pk, 0, species - 1076);
            sav.Dex.SetCaught(species);
            sav.Dex.SetSeen(species);
            sav.Dex.SetSeenGenderFirst(species, 1);
            Assert.True(sav.GetCaught(species));
            Assert.True(sav.GetSeen(species));
            Assert.Equal(1, sav.Dex.GetSeenGenderFirst(species));
        }
        Assert.Equal(forms, sav.Dex.GetForms(386));
        for (int region = 0; region < 4; region++)
            Assert.Equal(0xA0 + region, sav.General[FakemonStockProfile.DexOffset + 4 + region * 0x40 + 63]);
        var written = sav.Write();
        Assert.Equal(0x80000, written.Length);
        var loaded = Load(written);
        Assert.True(loaded.IsFakemonStock);
        for (ushort species = 1076; species <= 1086; species++)
        {
            var pk = loaded.GetBoxSlotAtIndex(0, species - 1076);
            Assert.Equal(species, pk.Species);
            Assert.Equal(50, pk.CurrentLevel);
            Assert.True(pk.ChecksumValid);
            Assert.True(Assert.IsType<PK4>(pk).IsFakemonStock);
            Assert.True(loaded.GetCaught(species));
        }
        Assert.Equal((ushort)1086, loaded.GetPartySlotAtIndex(0).Species);
        Assert.False(new LegalityAnalysis(loaded.GetPartySlotAtIndex(0)).Valid);
    }

    [Fact]
    public void CustomLanguageWritesAndGapFlagsCannotCorruptStockData()
    {
        var sav = Load(Fixture());
        var before = sav.General.ToArray();
        for (ushort species = 494; species <= 1086; species++)
        {
            Assert.False(sav.Dex.HasLanguage(species));
            sav.Dex.SetLanguage(species, 2);
            Assert.False(sav.Dex.GetLanguageBitIndex(species, 1));
            if (!HGEngineSpecies.IsCustom(species)) sav.Dex.SetCaught(species);
        }
        Assert.Equal(before, sav.General.ToArray());
        sav.Dex.CompleteDex();
        Assert.True(sav.IsFakemonStock);
        for (ushort species = 1076; species <= 1086; species++)
        {
            Assert.True(sav.GetCaught(species));
            Assert.True(sav.GetSeen(species));
        }
    }

    [Fact]
    public void CompactMovesRetainNamesPpTypesAndSavedIds()
    {
        var sav = Load(Fixture());
        var sources = new FilteredGameDataSource(sav, GameInfo.Sources);
        Assert.DoesNotContain(sources.Species, x => x.Value is >= 494 and < 1076);
        Assert.DoesNotContain(sources.Moves, x => x.Value > 474);
        for (ushort move = 468; move <= 474; move++)
        {
            var pk = sav.BlankPKM;
            pk.Species = 1078;
            pk.Move1 = move;
            pk.Move1_PPUps = 3;
            pk.HealPP();
            var i = move - 468;
            Assert.Equal(FakemonStockProfile.MoveNames[i], sources.Moves.Single(x => x.Value == move).Text);
            Assert.Equal(FakemonStockProfile.MovePP[i] * 8 / 5, pk.Move1_PP);
            Assert.Equal(FakemonStockProfile.MoveTypes[i], MoveInfo.GetType(move, pk));
            Assert.Equal(pk.GetBasePP(move), pk.Clone().GetBasePP(move));
            sav.SetBoxSlotAtIndex(pk, 0, i);
            var loaded = Load(sav.Write()).GetBoxSlotAtIndex(0, i);
            Assert.Equal(move, loaded.Move1);
            Assert.Equal(pk.Move1_PP, loaded.Move1_PP);
        }
        var retail = Load(Fixture(false));
        Assert.Equal(467, retail.MaxMoveID);
        Assert.DoesNotContain(new FilteredGameDataSource(retail, GameInfo.Sources).Moves, x => x.Value >= 468);
    }
}
