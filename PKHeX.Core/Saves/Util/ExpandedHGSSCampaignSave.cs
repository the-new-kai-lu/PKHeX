using System;
using System.Collections.Generic;
using System.IO;
using static System.Buffers.Binary.BinaryPrimitives;

namespace PKHeX.Core;

/// <summary>
/// Lossless editing projection for the opt-in ExpandedHGSS campaign-save v1 allocation.
/// This is not the HG-engine layout and does not change Pokémon records or species tables.
/// </summary>
public sealed class ExpandedHGSSCampaignSave
{
    public const int FileSize = 0x80000;
    public const int PartitionSize = 0x40000;
    public const int GeneralSize = 0xFE28;
    public const int StorageStart = 0xFF00;
    public const int StorageSize = 0x12310;
    public const int CampaignOffset = 0xF614;
    public const int CampaignSize = 0x800;
    private const int LegacyStorageStart = 0xF700;
    private const int NativeFooterSize = 0x10;
    private const int TrailerSize = 4 + NativeFooterSize;
    private const int ExtraStart = 0x23000;

    private readonly byte[] original;
    private readonly bool[] complete = new bool[2];
    internal int GeneralBank { get; private set; }
    internal int StorageBank => GeneralBank;

    /// <summary>Complete allocation, including opaque header and tail bytes, in the active general bank.</summary>
    public ReadOnlySpan<byte> CampaignData => original.AsSpan(GeneralBank * PartitionSize + CampaignOffset, CampaignSize);
    public ReadOnlySpan<byte> HoennVariables => CampaignData.Slice(0x20, 256 * 2);
    public ReadOnlySpan<byte> HoennFlags => CampaignData.Slice(0x220, 300);
    public ReadOnlySpan<byte> SinnohVariables => CampaignData.Slice(0x34C, 288 * 2);
    public ReadOnlySpan<byte> SinnohFlags => CampaignData.Slice(0x58C, 364);

    public ExpandedHGSSCampaignSave(ReadOnlySpan<byte> data)
    {
        if (!IsRecognized(data))
            throw new InvalidDataException("Not an ExpandedHGSS campaign save.");
        original = data.ToArray();
        for (int bank = 0; bank < 2; bank++)
        {
            var part = data.Slice(bank * PartitionSize, PartitionSize);
            complete[bank] = ValidPair(part);
        }
        GeneralBank = SelectBank(data, complete);
    }

    /// <summary>
    /// Detects the family before stock/HG-engine fallback. Tagged unsupported or malformed files
    /// throw explicitly, including when another bank is readable; they must never be stripped.
    /// A supported bank with a bad CRC can still recover through its same-format backup.
    /// An untagged CRC-invalid bank is corrupt, not an unsupported header. Valid native
    /// vanilla general banks are incompatible; mixed-format files are never migrated.
    /// Recovery requires a complete same-bank general/PC transaction with equal counters.
    /// </summary>
    public static bool IsRecognized(ReadOnlySpan<byte> data)
    {
        bool family = false;
        for (int bank = 0; bank < 2; bank++)
        {
            int start = bank * PartitionSize;
            if (data.Length < start + CampaignOffset + 8)
                continue;
            var part = data[start..];
            family |= HasTag(part) || HasExpandedFooterShape(part);
        }
        if (!family)
            return false;
        if (data.Length != FileSize)
            throw new InvalidDataException("ExpandedHGSS campaign saves must be exactly 512 KiB.");

        bool hasCompletePair = false;
        for (int bank = 0; bank < 2; bank++)
        {
            var part = data.Slice(bank * PartitionSize, PartitionSize);
            bool validExpanded = ValidBlock(part[..GeneralSize], 0);
            // A full tag claims a version even when its CRC is bad. Without that tag,
            // reject a CRC-valid expanded block, but allow corrupt backup recovery.
            if (HasTag(part) || validExpanded)
                ValidateHeader(part.Slice(CampaignOffset, CampaignSize));
            if (ValidBlock(part[..SAV4HGSS.GeneralSize], 0))
                throw new InvalidDataException("ExpandedHGSS campaign saves cannot contain a valid vanilla general bank.");
            hasCompletePair |= ValidPair(part);
        }
        if (!hasCompletePair)
            throw new InvalidDataException("ExpandedHGSS campaign save has no complete same-bank general/storage transaction.");
        return true;
    }

    private static bool HasTag(ReadOnlySpan<byte> part) =>
        part.Slice(CampaignOffset, 8).SequenceEqual("EHGSCAMP"u8);

    // Family evidence only: an unsupported magic must not enable stock fallback.
    // Block validity below uses exactly the activated native build's SAVE_CHUNK_MAGIC.
    private static bool HasExpandedFooterShape(ReadOnlySpan<byte> part) =>
        part.Length >= GeneralSize &&
        ReadUInt32LittleEndian(part[(GeneralSize - 12)..]) == GeneralSize &&
        ReadUInt32LittleEndian(part[(GeneralSize - 8)..]) is SAV4.MAGIC_JAPAN_INTL or SAV4.MAGIC_KOREAN;

    private static void ValidateHeader(ReadOnlySpan<byte> campaign)
    {
        if (!campaign[..8].SequenceEqual("EHGSCAMP"u8))
            throw new InvalidDataException("Malformed ExpandedHGSS campaign magic.");
        ushort version = ReadUInt16LittleEndian(campaign[8..]);
        if (version != 1)
            throw new NotSupportedException($"Unsupported ExpandedHGSS campaign version {version}.");
        if (ReadUInt16LittleEndian(campaign[10..]) != CampaignSize ||
            ReadUInt16LittleEndian(campaign[12..]) != 0x20)
            throw new InvalidDataException("Malformed ExpandedHGSS campaign length or header size.");
    }

    private static bool ValidBlock(ReadOnlySpan<byte> block, ushort id) =>
        ReadUInt32LittleEndian(block[^12..]) == block.Length &&
        ReadUInt32LittleEndian(block[^8..]) == SAV4.MAGIC_JAPAN_INTL &&
        ReadUInt16LittleEndian(block[^4..]) == id &&
        Checksums.CRC16_CCITT(block[..^NativeFooterSize]) == ReadUInt16LittleEndian(block[^2..]);

    private static bool ValidPair(ReadOnlySpan<byte> part) =>
        HasTag(part) &&
        ValidBlock(part[..GeneralSize], 0) &&
        ValidBlock(part.Slice(StorageStart, StorageSize), 1) &&
        ReadUInt32LittleEndian(part[(GeneralSize - NativeFooterSize)..]) ==
        ReadUInt32LittleEndian(part[(StorageStart + StorageSize - NativeFooterSize)..]);

    private static int SelectBank(ReadOnlySpan<byte> data, bool[] valid)
    {
        if (!valid[0]) return 1;
        if (!valid[1]) return 0;
        // Native SaveCounterCompare: only max <-> 0 is a rollover; otherwise compare
        // unsigned counters. Do not use SAV4's generic 20-byte/two-counter heuristics.
        // Invalid/incomplete banks must be excluded before comparing counter zero.
        int offset = GeneralSize - NativeFooterSize;
        uint first = ReadUInt32LittleEndian(data[offset..]);
        uint second = ReadUInt32LittleEndian(data[(offset + PartitionSize)..]);
        if (first == uint.MaxValue && second == 0) return 1;
        if (first == 0 && second == uint.MaxValue) return 0;
        return first >= second ? 0 : 1; // Equal complete transactions choose bank 0.
    }

    public byte[] Normalize()
    {
        var result = (byte[])original.Clone();
        for (int bank = 0; bank < 2; bank++)
        {
            var source = original.AsSpan(bank * PartitionSize, PartitionSize);
            var target = result.AsSpan(bank * PartitionSize, PartitionSize);
            source[..CampaignOffset].CopyTo(target);
            source.Slice(CampaignOffset + CampaignSize, TrailerSize).CopyTo(target[CampaignOffset..]);
            WriteUInt32LittleEndian(target[(SAV4HGSS.GeneralSize - 12)..], SAV4HGSS.GeneralSize);
            source.Slice(StorageStart, StorageSize).CopyTo(target[LegacyStorageStart..]);
            if (complete[bank])
                FixCRC(target[..SAV4HGSS.GeneralSize]);
        }
        return result;
    }

    internal void CopyFrom(ExpandedHGSSCampaignSave other)
    {
        other.original.CopyTo(original, 0);
        other.complete.CopyTo(complete, 0);
        GeneralBank = other.GeneralBank;
    }

    internal void SetChecksums(Span<byte> normalized, IReadOnlyList<BlockInfo4> extras)
    {
        for (int bank = 0; bank < 2; bank++)
        {
            if (!complete[bank])
                continue;
            var part = normalized.Slice(bank * PartitionSize, PartitionSize);
            FixCRC(part[..SAV4HGSS.GeneralSize]);
            FixCRC(part.Slice(LegacyStorageStart, StorageSize));
            // Do not silently repair corrupt/unwritten extra blocks on an unrelated edit.
            // Explicit extra-block edits still receive their normal checksum.
            foreach (var block in extras)
            {
                if (!part.Slice(block.Offset, block.Length).SequenceEqual(original.AsSpan(bank * PartitionSize + block.Offset, block.Length)))
                    BlockInfo.SetChecksums([block], part);
            }
        }
    }

    public byte[] Export(ReadOnlySpan<byte> normalized)
    {
        if (normalized.Length != FileSize)
            throw new ArgumentException("Expected a full normalized HGSS image.", nameof(normalized));
        var result = (byte[])original.Clone();
        for (int bank = 0; bank < 2; bank++)
        {
            // Preserve the entire incomplete partition, even if one orphan block is valid.
            if (!complete[bank])
                continue;
            var source = normalized.Slice(bank * PartitionSize, PartitionSize);
            var target = result.AsSpan(bank * PartitionSize, PartitionSize);
            source[..CampaignOffset].CopyTo(target);
            source.Slice(CampaignOffset, TrailerSize).CopyTo(target[(CampaignOffset + CampaignSize)..]);
            WriteUInt32LittleEndian(target[(GeneralSize - 12)..], GeneralSize);
            FixCRC(target[..GeneralSize]);
            source.Slice(LegacyStorageStart, StorageSize).CopyTo(target[StorageStart..]);
            FixCRC(target.Slice(StorageStart, StorageSize));
            source[ExtraStart..].CopyTo(target[ExtraStart..]);
        }
        return result;
    }

    private static void FixCRC(Span<byte> block) =>
        WriteUInt16LittleEndian(block[^2..], Checksums.CRC16_CCITT(block[..^NativeFooterSize]));
}