using System;

namespace PKHeX.Core;

/// <summary>The stock-layout pokeheartgold Fakemon port, format version 1.</summary>
public static class FakemonStockProfile
{
    public const int DexOffset = 0x12B8;
    public const int MarkerOffset = DexOffset + 0x332;
    public const int VersionOffset = DexOffset + 0x33F;
    public const byte Version = 1;
    public const ushort FirstMove = 468;
    public const ushort LastMove = 474;
    public static readonly string[] MoveNames = ["Wild Charge", "Snarl", "Incinerate", "Fire Lash", "Icicle Crash", "Bulldoze", "Hurricane"];
    public static ReadOnlySpan<byte> MovePP => [15, 15, 15, 15, 10, 20, 10];
    public static ReadOnlySpan<byte> MoveTypes => [12, 16, 9, 9, 14, 4, 2];
    // Canonical IDs are for descriptions only. Saved move IDs must remain 468-474.
    public static ReadOnlySpan<ushort> CanonicalMoves => [(ushort)Move.WildCharge, (ushort)Move.Snarl, (ushort)Move.Incinerate, (ushort)Move.FireLash, (ushort)Move.IcicleCrash, (ushort)Move.Bulldoze, (ushort)Move.Hurricane];
    public static bool IsMove(ushort move) => move is >= FirstMove and <= LastMove;
    public static ushort GetCanonicalMove(ushort move) => IsMove(move) ? CanonicalMoves[move - FirstMove] : move;
    public static bool IsRecognized(ReadOnlySpan<byte> general) =>
        general.Length == SAV4HGSS.GeneralSize && general[MarkerOffset] == 0x46 &&
        general[MarkerOffset + 1] == 0x4B && general[VersionOffset] == Version;
    public static int GetDexIndex(ushort species) => HGEngineSpecies.IsCustom(species) ? 493 + species - HGEngineSpecies.First : species - 1;
    public static readonly PersonalTable4 Personal = HGEngineSpecies.CreateTable(false);
}
