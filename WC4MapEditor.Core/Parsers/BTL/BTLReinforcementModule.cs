using System.Diagnostics;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.BTL;

/// <summary>
/// BTL 增援数据解析模块（支持 v1/v3 版本）。
/// </summary>
public static class BTLReinforcementModule
{
    private const int ReinforcementSizeV1 = 80;
    private const int ReinforcementSizeV3 = 104;

    public static int GetReinforcementSize(int btlVersion)
        => btlVersion >= 3 ? ReinforcementSizeV3 : ReinforcementSizeV1;

    public static (List<Reinforcement> v1, List<Reinforcement_3> v3) Parse(byte[] data, int startOffset, int count, int btlVersion)
    {
        var reinforcementsV1 = new List<Reinforcement>();
        var reinforcementsV3 = new List<Reinforcement_3>();

        if (data == null) return (reinforcementsV1, reinforcementsV3);

        int reinforcementSize = GetReinforcementSize(btlVersion);
        for (int i = 0; i < count; i++)
        {
            int startPos = startOffset + (i * reinforcementSize);
            if (startPos + reinforcementSize > data.Length) break;

            if (btlVersion >= 3)
            {
                var r = Reinforcement_3.FromBytes(data, startPos);
                reinforcementsV3.Add(r);
            }
            else
            {
                var r = Reinforcement.FromBytes(data, startPos);
                reinforcementsV1.Add(r);
            }
        }

        Debug.WriteLine($"[BTLReinforcementModule] 解析 {count} 个增援 (v{btlVersion})");
        return (reinforcementsV1, reinforcementsV3);
    }

    public static int CalculateEndOffset(int startOffset, int count, int btlVersion)
        => startOffset + (count * GetReinforcementSize(btlVersion));
}
