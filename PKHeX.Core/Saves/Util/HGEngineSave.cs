using System;
using static System.Buffers.Binary.BinaryPrimitives;

namespace PKHeX.Core;

/// <summary>Lossless adapter for the fork's closest-stock, 18-box HG-engine save layout.
/// Expanded Dex and miscellaneous bytes are retained outside the vanilla editing view.</summary>
public sealed class HGEngineSave
{
    public const int GeneralSize = 0xFDB0;
    public const int StorageStart = 0xFE00;
    private const int StorageSize = 0x12310;
    private const int PartitionSize = 0x40000;
    private const int DexOffset = 0x12B8;
    private readonly byte[] original;
    private readonly bool[] validGeneral = new bool[2];
    private readonly bool[] validStorage = new bool[2];
    public Memory<byte> Dex { get; }

    public HGEngineSave(ReadOnlySpan<byte> data)
    {
        original = data.ToArray();
        for (int slot = 0; slot < 2; slot++)
        {
            validGeneral[slot] = ValidBlock(data.Slice(slot * PartitionSize, GeneralSize), 0);
            validStorage[slot] = ValidBlock(data.Slice(slot * PartitionSize + StorageStart, StorageSize), 1);
        }
        var active = GetActiveGeneral(data) * PartitionSize;
        Dex = original.AsMemory(active + DexOffset, 0x700);
    }

    public static bool IsRecognized(ReadOnlySpan<byte> data)
    {
        if (data.Length != 0x80000) return false;
        bool general = false, storage = false;
        for (int slot = 0; slot < 2; slot++)
        {
            var g = data.Slice(slot * PartitionSize, GeneralSize);
            general |= ValidBlock(g, 0) && ReadUInt32LittleEndian(g[DexOffset..]) == 0xBEEFCAFE;
            storage |= ValidBlock(data.Slice(slot * PartitionSize + StorageStart, StorageSize), 1);
        }
        return general && storage;
    }

    private static bool ValidBlock(ReadOnlySpan<byte> block, int id) =>
        ReadUInt32LittleEndian(block[^12..]) == block.Length &&
        ReadUInt32LittleEndian(block[^8..]) == SAV4.MAGIC_JAPAN_INTL &&
        ReadUInt16LittleEndian(block[^4..]) == id &&
        Checksums.CRC16_CCITT(block[..^16]) == ReadUInt16LittleEndian(block[^2..]);

    private static int GetActiveGeneral(ReadOnlySpan<byte> data)
    {
        if (!ValidBlock(data[..GeneralSize], 0)) return 1;
        if (!ValidBlock(data.Slice(PartitionSize, GeneralSize), 0)) return 0;
        return SAV4.GetActiveBlock(data, 0, GeneralSize);
    }

    internal void CopyFrom(HGEngineSave other) => other.original.CopyTo(original, 0);

    public byte[] Normalize()
    {
        var result = (byte[])original.Clone();
        for (int slot = 0; slot < 2; slot++)
        {
            var source = original.AsSpan(slot * PartitionSize);
            var target = result.AsSpan(slot * PartitionSize);
            // Prefix through Dex, daycare/mail, original misc fields, then unchanged tail.
            source[..DexOffset].CopyTo(target);
            CopyDex(source.Slice(DexOffset), target.Slice(DexOffset), false);
            source.Slice(0x19BC, 0xA68).CopyTo(target[0x15FC..]);
            source.Slice(0x2424, 0x2E0).CopyTo(target[0x2064..]);
            source.Slice(0x2AD0, 0xD2D0).CopyTo(target[0x2348..]);
            source.Slice(GeneralSize - 16, 16).CopyTo(target[(SAV4HGSS.GeneralSize - 16)..]);
            WriteUInt32LittleEndian(target[(SAV4HGSS.GeneralSize - 12)..], SAV4HGSS.GeneralSize);
            source.Slice(StorageStart, StorageSize).CopyTo(target[0xF700..]);
            if (ValidBlock(source[..GeneralSize], 0))
                FixCRC(target[..SAV4HGSS.GeneralSize]);
            else
                target.Slice(SAV4HGSS.GeneralSize - 20, 20).Fill(0xFF);
            if (!ValidBlock(source.Slice(StorageStart, StorageSize), 1))
                target.Slice(0xF700 + StorageSize - 20, 20).Fill(0xFF);
        }
        return result;
    }

    public byte[] Export(ReadOnlySpan<byte> normalized)
    {
        var result = (byte[])original.Clone();
        for (int slot = 0; slot < 2; slot++)
        {
            var source = normalized[(slot * PartitionSize)..];
            var target = result.AsSpan(slot * PartitionSize);
            source[..DexOffset].CopyTo(target);
            CopyDex(source[DexOffset..], target[DexOffset..], true);
            source.Slice(0x15FC, 0xA68).CopyTo(target[0x19BC..]);
            source.Slice(0x2064, 0x2E0).CopyTo(target[0x2424..]);
            source.Slice(0x2348, 0xD2D0).CopyTo(target[0x2AD0..]);
            source.Slice(SAV4HGSS.GeneralSize - 16, 16).CopyTo(target[(GeneralSize - 16)..]);
            WriteUInt32LittleEndian(target[(GeneralSize - 12)..], GeneralSize);
            source.Slice(0xF700, StorageSize).CopyTo(target[StorageStart..]);
            source.Slice(0x23000, PartitionSize - 0x23000).CopyTo(target[0x23000..]);
            // Preserve an invalid/unwritten backup instead of silently repairing it.
            if (validGeneral[slot])
                FixCRC(target[..GeneralSize]);
            else
                original.AsSpan(slot * PartitionSize, GeneralSize).CopyTo(target);
            if (!validStorage[slot])
                original.AsSpan(slot * PartitionSize + StorageStart, StorageSize).CopyTo(target[StorageStart..]);
        }
        return result;
    }

    private static void CopyDex(ReadOnlySpan<byte> source, Span<byte> target, bool expanding)
    {
        source[..4].CopyTo(target);
        ReadOnlySpan<int> engine = [4, 0x400, 0x500, 0x600];
        for (int region = 0; region < 4; region++)
        {
            int stock = 4 + region * 0x40;
            source.Slice(expanding ? stock : engine[region], 0x40)
                .CopyTo(target.Slice(expanding ? engine[region] : stock));
        }
        source.Slice(0x104, 0x23C).CopyTo(target[0x104..]);
    }

    private static void FixCRC(Span<byte> block) =>
        WriteUInt16LittleEndian(block[^2..], Checksums.CRC16_CCITT(block[..^16]));
}
