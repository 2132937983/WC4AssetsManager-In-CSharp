using System.Runtime.InteropServices;

namespace WC4MapEditor.Core.Models;

/// <summary>
/// 增援数据结构 v3（104 字节，BtlVersion ≥ 3，与文件里的记录逐字节对应）。
/// 前 80 字节与 v1 同位：0x10 起是行为模式 / 方向 / 未知，v1 的胸章三个字段在 v3 里废弃（Waste1-3），末尾新增勋章与勋带。
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Reinforcement_3 : IReinforcementData
{
    /// <summary>文件中每条记录的字节数</summary>
    public const int Size = 104;

    public int Coordinate;      // 0x00 坐标
    public int UnitType;        // 0x04 兵种
    public int Level;           // 0x08 等级
    public int Organization;    // 0x0C 编制
    public int BehaviorMode;    // 0x10 行为模式
    public int Direction;       // 0x14 方向
    public int Unknown;         // 0x18 未知
    public int General;         // 0x1C 将领
    public int Rank;            // 0x20 军衔
    public int HpLevel;         // 0x24 HP 等级（将领品质）
    public int Skill1Level;     // 0x28 技能一等级
    public int Skill2Level;     // 0x2C 技能二等级
    public int Skill3Level;     // 0x30 技能三等级
    public int Skill4Level;     // 0x34 技能四等级
    public int Skill5Level;     // 0x38 技能五等级
    public int Waste1;          // 0x3C 废弃（v1 胸章一）
    public int Waste2;          // 0x40 废弃（v1 胸章二）
    public int Waste3;          // 0x44 废弃（v1 胸章三）

    /// <summary>0x48：所属军团的行动顺序（ActionId），见 <see cref="IReinforcementData.Owner"/></summary>
    public int OwnerLegion;

    public int SpawnRound;      // 0x4C 出现回合
    public int Medal1;          // 0x50 勋章一
    public int Medal2;          // 0x54 勋章二
    public int Medal3;          // 0x58 勋章三
    public int Ribbon1;         // 0x5C 勋带一
    public int Ribbon2;         // 0x60 勋带二
    public int Ribbon3;         // 0x64 勋带三

    int IReinforcementData.Coordinate { readonly get => Coordinate; set => Coordinate = value; }
    int IReinforcementData.UnitType { readonly get => UnitType; set => UnitType = value; }
    int IReinforcementData.Level { readonly get => Level; set => Level = value; }
    int IReinforcementData.Organization { readonly get => Organization; set => Organization = value; }
    int IReinforcementData.General { readonly get => General; set => General = value; }
    int IReinforcementData.Rank { readonly get => Rank; set => Rank = value; }
    int IReinforcementData.Quality { readonly get => HpLevel; set => HpLevel = value; }
    int IReinforcementData.Skill1Level { readonly get => Skill1Level; set => Skill1Level = value; }
    int IReinforcementData.Skill2Level { readonly get => Skill2Level; set => Skill2Level = value; }
    int IReinforcementData.Skill3Level { readonly get => Skill3Level; set => Skill3Level = value; }
    int IReinforcementData.Skill4Level { readonly get => Skill4Level; set => Skill4Level = value; }
    int IReinforcementData.Skill5Level { readonly get => Skill5Level; set => Skill5Level = value; }
    int IReinforcementData.Owner { readonly get => OwnerLegion; set => OwnerLegion = value; }
    int IReinforcementData.SpawnRound { readonly get => SpawnRound; set => SpawnRound = value; }

    /// <summary>
    /// 从字节数组解析增援数据 v3
    /// </summary>
    public static Reinforcement_3 FromBytes(ReadOnlySpan<byte> data, int offset) =>
        MemoryMarshal.Read<Reinforcement_3>(data[offset..]);

    /// <summary>
    /// 写入字节数组
    /// </summary>
    public void ToBytes(Span<byte> data, int offset) =>
        MemoryMarshal.Write(data[offset..], in this);

    /// <summary>
    /// 创建默认增援 v3：轻型步兵、1 级、1 编、第 1 回合出现（与 v1 的默认值一致）
    /// </summary>
    public static Reinforcement_3 CreateDefault() => new Reinforcement_3
    {
        UnitType = 1,
        Level = 1,
        Organization = 1,
        SpawnRound = 1
    };
}
