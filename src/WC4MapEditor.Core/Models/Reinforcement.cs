using System.Runtime.InteropServices;

namespace WC4MapEditor.Core.Models;

/// <summary>
/// v1 / v3 援军记录的公共字段（结构体里用显式接口实现），援军模块、编辑窗口与渲染层借它按版本通用处理两种记录。
/// <para>
/// <see cref="Owner"/> 是所属军团的<b>行动顺序（<see cref="Legion.ActionId"/>）</b>，不是归属值（军团在列表中的索引）：
/// 统计游戏自带地图的三万多条援军，按 ActionId 找到的军团索引与所在格归属一致的约占 97%，
/// 而把所属值直接当归属值只对得上不到 9%。游戏数据里 ActionId 都从 1 起，所以 0 表示"未设置"。
/// </para>
/// </summary>
public interface IReinforcementData
{
    /// <summary>坐标（格子索引 = 行 × 地图宽 + 列）</summary>
    int Coordinate { get; set; }
    int UnitType { get; set; }
    int Level { get; set; }
    int Organization { get; set; }
    int General { get; set; }
    int Rank { get; set; }

    /// <summary>将领品质（v1 品质 / v3 HP 等级，同在 0x24）</summary>
    int Quality { get; set; }

    int Skill1Level { get; set; }
    int Skill2Level { get; set; }
    int Skill3Level { get; set; }
    int Skill4Level { get; set; }
    int Skill5Level { get; set; }

    /// <summary>所属军团的行动顺序（v1 OwnerCountry / v3 OwnerLegion，同在 0x48），0 = 未设置</summary>
    int Owner { get; set; }

    int SpawnRound { get; set; }
}

/// <summary>
/// 增援数据结构 v1/v2（80 字节，与文件里的记录逐字节对应，读写直接按内存布局拷贝）。
/// <para>
/// 0x14 / 0x18 两个整数文档里没有定义，以前的结构体漏掉了它们，导致将领之后的字段整体错位 8 字节、
/// 保存时把真正的所属军团与爆兵回合写成 0。这两个字段必须保留，原样读写。
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Reinforcement : IReinforcementData
{
    /// <summary>文件中每条记录的字节数</summary>
    public const int Size = 80;

    public int Coordinate;      // 0x00 坐标
    public int UnitType;        // 0x04 兵种
    public int Level;           // 0x08 等级
    public int Organization;    // 0x0C 编制
    public int Direction;       // 0x10 朝向

    /// <summary>0x14：未定义（v3 同位置是方向），游戏地图里出现过 0~2</summary>
    public int Reserved14;

    /// <summary>0x18：未定义（v3 同位置是未知字段），游戏地图里出现过 0~49</summary>
    public int Reserved18;

    public int General;         // 0x1C 将领
    public int Rank;            // 0x20 军衔
    public int Quality;         // 0x24 品质
    public int Skill1Level;     // 0x28 技能1等级
    public int Skill2Level;     // 0x2C 技能2等级
    public int Skill3Level;     // 0x30 技能3等级
    public int Skill4Level;     // 0x34 技能4等级
    public int Skill5Level;     // 0x38 技能5等级
    public int Badge1;          // 0x3C 胸章一
    public int Badge2;          // 0x40 胸章二
    public int Badge3;          // 0x44 胸章三

    /// <summary>0x48：所属军团的行动顺序（ActionId），见 <see cref="IReinforcementData.Owner"/></summary>
    public int OwnerCountry;

    public int SpawnRound;      // 0x4C 爆兵回合

    int IReinforcementData.Coordinate { readonly get => Coordinate; set => Coordinate = value; }
    int IReinforcementData.UnitType { readonly get => UnitType; set => UnitType = value; }
    int IReinforcementData.Level { readonly get => Level; set => Level = value; }
    int IReinforcementData.Organization { readonly get => Organization; set => Organization = value; }
    int IReinforcementData.General { readonly get => General; set => General = value; }
    int IReinforcementData.Rank { readonly get => Rank; set => Rank = value; }
    int IReinforcementData.Quality { readonly get => Quality; set => Quality = value; }
    int IReinforcementData.Skill1Level { readonly get => Skill1Level; set => Skill1Level = value; }
    int IReinforcementData.Skill2Level { readonly get => Skill2Level; set => Skill2Level = value; }
    int IReinforcementData.Skill3Level { readonly get => Skill3Level; set => Skill3Level = value; }
    int IReinforcementData.Skill4Level { readonly get => Skill4Level; set => Skill4Level = value; }
    int IReinforcementData.Skill5Level { readonly get => Skill5Level; set => Skill5Level = value; }
    int IReinforcementData.Owner { readonly get => OwnerCountry; set => OwnerCountry = value; }
    int IReinforcementData.SpawnRound { readonly get => SpawnRound; set => SpawnRound = value; }

    /// <summary>
    /// 从字节数组解析增援数据
    /// </summary>
    public static Reinforcement FromBytes(ReadOnlySpan<byte> data, int offset) =>
        MemoryMarshal.Read<Reinforcement>(data[offset..]);

    /// <summary>
    /// 写入字节数组
    /// </summary>
    public void ToBytes(Span<byte> data, int offset) =>
        MemoryMarshal.Write(data[offset..], in this);

    /// <summary>
    /// 创建默认增援：轻型步兵、1 级、1 编、第 1 回合出现（游戏要求爆兵回合至少为 1，兵种表里没有 0 号兵种）
    /// </summary>
    public static Reinforcement CreateDefault() => new Reinforcement
    {
        UnitType = 1,
        Level = 1,
        Organization = 1,
        SpawnRound = 1
    };
}
