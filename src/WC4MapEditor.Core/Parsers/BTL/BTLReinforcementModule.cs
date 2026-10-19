using System.Diagnostics;
using System.Runtime.CompilerServices;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.BTL;

/// <summary>
/// BTL 增援数据解析模块（v1 族 80 字节 / v3 族 104 字节，v2 文件属于 v3 族，见 <see cref="BTLHeader.UsesV3RecordFamily"/>）。
/// </summary>
public static class BTLReinforcementModule
{
    static BTLReinforcementModule()
    {
        // 记录按内存布局直接读写（MemoryMarshal），结构体长度必须与文件记录一致；
        // 不一致说明结构体字段被改坏（以前 v1 少了两个字段只有 72 字节，读写整体错位）
        if (Unsafe.SizeOf<Reinforcement>() != Reinforcement.Size || Unsafe.SizeOf<Reinforcement_3>() != Reinforcement_3.Size)
            throw new InvalidOperationException(
                $"援军结构体长度与文件记录不符: v1={Unsafe.SizeOf<Reinforcement>()}/{Reinforcement.Size}, v3={Unsafe.SizeOf<Reinforcement_3>()}/{Reinforcement_3.Size}");
    }

    public static int GetReinforcementSize(int btlVersion) => GetReinforcementSize(BTLHeader.UsesV3RecordFamily(btlVersion));

    public static int GetReinforcementSize(bool v3Records) => v3Records ? Reinforcement_3.Size : Reinforcement.Size;

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

            if (BTLHeader.UsesV3RecordFamily(btlVersion))
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
