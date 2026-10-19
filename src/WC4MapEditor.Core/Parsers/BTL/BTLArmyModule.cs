using System.Diagnostics;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.BTL;

/// <summary>
/// BTL 部队数据解析模块（v1 族 48 字节 / v3 族 64 字节，v2 文件属于 v3 族，见 <see cref="BTLHeader.UsesV3RecordFamily"/>）。
/// </summary>
public static class BTLArmyModule
{
    private const int ArmySizeV1 = 48;
    private const int ArmySizeV3 = 64;

    public static int GetArmySize(int btlVersion) => GetArmySize(BTLHeader.UsesV3RecordFamily(btlVersion));

    public static int GetArmySize(bool v3Records) => v3Records ? ArmySizeV3 : ArmySizeV1;

    public static (List<Army> v1, List<Army_3> v3) Parse(byte[] data, int startOffset, int count, int btlVersion)
    {
        var armiesV1 = new List<Army>();
        var armiesV3 = new List<Army_3>();

        if (data == null) return (armiesV1, armiesV3);

        int armySize = GetArmySize(btlVersion);
        for (int i = 0; i < count; i++)
        {
            int startPos = startOffset + (i * armySize);
            if (startPos + armySize > data.Length) break;

            if (BTLHeader.UsesV3RecordFamily(btlVersion))
            {
                var army = Army_3.FromBytes(data, startPos);
                armiesV3.Add(army);
            }
            else
            {
                var army = Army.FromBytes(data, startPos);
                armiesV1.Add(army);
            }
        }

        Debug.WriteLine($"[BTLArmyModule] 解析 {count} 个部队 (v{btlVersion})");
        return (armiesV1, armiesV3);
    }

    public static int CalculateEndOffset(int startOffset, int count, int btlVersion)
        => startOffset + (count * GetArmySize(btlVersion));
}
