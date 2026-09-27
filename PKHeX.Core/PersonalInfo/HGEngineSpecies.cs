using System;

namespace PKHeX.Core;

/// <summary>Species data exported from this fork's built HG-engine ROM.</summary>
public static class HGEngineSpecies
{
    public const ushort First = 1076;
    public const ushort Last = 1086;
    public static bool IsCustom(ushort species) => species is >= First and <= Last;
    public static readonly string[] Names = ["Voltuff", "Surguenon", "Raijinque", "Embernewt", "Pyrovaran", "Magmalisk", "Rimevaran", "Fimbulisk", "Sedgling", "Cragaviar", "Ragnaroc"];
    private static readonly byte[][] Entries =
    [
        [45, 60, 50, 55, 55, 50, 13, 13, 45, 0, 4, 0, 0, 0, 0, 0, 127, 20, 70, 3, 5, 8, 9, 9, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0], // Voltuff
        [65, 85, 50, 95, 55, 55, 13, 1, 45, 0, 68, 0, 0, 0, 0, 0, 127, 20, 70, 5, 5, 8, 9, 9, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0], // Surguenon
        [85, 125, 75, 120, 120, 75, 13, 1, 45, 0, 68, 1, 0, 0, 0, 0, 127, 20, 70, 5, 5, 8, 9, 9, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0], // Raijinque
        [60, 50, 50, 45, 60, 50, 10, 10, 45, 0, 0, 1, 0, 0, 0, 0, 127, 20, 70, 3, 1, 14, 18, 18, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0], // Embernewt
        [85, 60, 65, 55, 85, 55, 10, 10, 45, 0, 1, 1, 0, 0, 0, 0, 127, 20, 70, 5, 1, 14, 18, 18, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0], // Pyrovaran
        [110, 110, 95, 85, 110, 90, 10, 10, 45, 0, 5, 1, 0, 0, 0, 0, 127, 20, 70, 5, 1, 14, 18, 18, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0], // Magmalisk
        [85, 60, 65, 55, 85, 55, 17, 15, 45, 0, 1, 1, 0, 0, 0, 0, 127, 20, 70, 5, 1, 14, 18, 18, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0], // Rimevaran
        [110, 110, 95, 85, 110, 90, 17, 15, 45, 0, 5, 1, 0, 0, 0, 0, 127, 20, 70, 5, 1, 14, 18, 18, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0], // Fimbulisk
        [40, 55, 55, 55, 60, 50, 4, 11, 45, 0, 0, 1, 0, 0, 0, 0, 127, 20, 70, 3, 2, 4, 11, 11, 0, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0], // Sedgling
        [70, 75, 55, 75, 75, 55, 4, 2, 45, 0, 4, 1, 0, 0, 0, 0, 127, 20, 70, 5, 2, 4, 11, 11, 0, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0], // Cragaviar
        [100, 120, 75, 115, 115, 75, 4, 2, 45, 0, 68, 1, 0, 0, 0, 0, 127, 20, 70, 5, 2, 4, 11, 11, 0, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0], // Ragnaroc
    ];
    public static readonly PersonalTable4 Personal = CreateTable();
    internal static PersonalTable4 CreateTable(bool engine = true)
    {
        var data = new byte[(Last + 1) * PersonalInfo4.SIZE];
        for (int i = 0; i < PersonalTable.HGSS.Count; i++)
            PersonalTable.HGSS[i].Write().CopyTo(data, i * PersonalInfo4.SIZE);
        var canonical = Util.GetBinaryResource("personal_hg_engine");
        for (int i = 0; engine && i <= 493; i++)
        {
            // Retain PKHeX's form-index metadata in the last three bytes.
            canonical.AsSpan(i * PersonalInfo4.SIZE, 41).CopyTo(data.AsSpan(i * PersonalInfo4.SIZE));
            if (data[i * PersonalInfo4.SIZE + 23] == 0)
                data[i * PersonalInfo4.SIZE + 23] = data[i * PersonalInfo4.SIZE + 22];
        }
        for (int i = 0; i < Entries.Length; i++)
        {
            int offset = (First + i) * PersonalInfo4.SIZE;
            Entries[i].CopyTo(data, offset);
            if (!engine)
            {
                // ROM type 9 is the unused Mystery type; PKHeX omits it.
                if (data[offset + 6] > 9) data[offset + 6]--;
                if (data[offset + 7] > 9) data[offset + 7]--;
            }
        }
        return new PersonalTable4(data, Last);
    }
    public static string[] ExtendNames(string[] original)
    {
        var result = new string[Math.Max(original.Length, Last + 1)];
        original.CopyTo(result, 0);
        for (int i = original.Length; i < First; i++) result[i] = $"Unused {i}";
        Names.CopyTo(result, First);
        return result;
    }
}
