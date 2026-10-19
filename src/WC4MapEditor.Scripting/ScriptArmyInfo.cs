namespace WC4MapEditor.Scripting;

/// <summary>
/// 暴露给脚本的单位快照（只读值对象）。
/// <para>
/// v1 / v3 两种格式由接口内部自动分派，脚本看到的字段统一：v3 专有字段在 v1 地图上恒为 0。
/// 与建筑一样，改属性请用 <c>map.SetArmyField(col, row, 字段名, 值)</c> —— 给快照赋值不生效。
/// </para>
/// </summary>
public sealed class ScriptArmyInfo
{
    /// <summary>所在格子的列</summary>
    public int Col { get; init; }

    /// <summary>所在格子的行</summary>
    public int Row { get; init; }

    /// <summary>线性索引（row * MapWidth + col）</summary>
    public int Index { get; init; }

    /// <summary>单位格式版本：1 = v1，3 = v3（来自 Header.BtlVersion）</summary>
    public int Version { get; init; }

    /// <summary>兵种（0=步兵 1=骑兵 2=弓箭手 4=海军 5=空军）</summary>
    public int UnitType { get; init; }

    /// <summary>兵种中文名</summary>
    public string UnitTypeName { get; init; } = "";

    /// <summary>等级</summary>
    public int Level { get; init; }

    /// <summary>编制</summary>
    public int Organization { get; init; }

    /// <summary>朝向（0=左 1=右）</summary>
    public int Direction { get; init; }

    /// <summary>朝向中文名</summary>
    public string DirectionName { get; init; } = "";

    /// <summary>机动</summary>
    public int Mobility { get; init; }

    /// <summary>建造回合</summary>
    public int BuiltRound { get; init; }

    /// <summary>经验</summary>
    public int Experience { get; init; }

    /// <summary>血量加成（基准 100）</summary>
    public int HealthBonus { get; init; }

    /// <summary>当前血量</summary>
    public int CurrentHealth { get; init; }

    /// <summary>最大血量</summary>
    public int MaxHealth { get; init; }

    /// <summary>将领 ID（0 = 无）</summary>
    public int General { get; init; }

    /// <summary>军衔</summary>
    public int Rank { get; init; }

    /// <summary>关键据点（0=否 1=红圈 2=绿圈）</summary>
    public int KeyPoint { get; init; }

    /// <summary>关键据点中文名</summary>
    public string KeyPointName { get; init; } = "";

    /// <summary>政策（0=自由行动 1=猛攻 2=缓慢进攻 3=坚守 4=撤退）</summary>
    public int Policy { get; init; }

    /// <summary>政策中文名</summary>
    public string PolicyName { get; init; } = "";

    /// <summary>方案（0-4）</summary>
    public int Plan { get; init; }

    /// <summary>士气（0=正常 253=混乱 254=士气双降 255=士气下降）</summary>
    public int Morale { get; init; }

    /// <summary>士气中文名</summary>
    public string MoraleName { get; init; } = "";

    /// <summary>是否可攻击（0/1）</summary>
    public int CanAttack { get; init; }

    /// <summary>
    /// 所属军团的归属值（军团列表索引，255 = 所在格子无归属）。单位的归属就是它所在格子的归属，不是单位自带的数据。
    /// </summary>
    public int Belong { get; init; }

    /// <summary>同 <see cref="Belong"/>（旧脚本沿用的名字）。</summary>
    public int LegionId { get; init; }

    // ---------- v3 专有（v1 地图上恒为 0）----------

    /// <summary>血量等级</summary>
    public int HpLevel { get; init; }

    /// <summary>运输船</summary>
    public int TransportShip { get; init; }

    /// <summary>仇恨值</summary>
    public int Hatred { get; init; }

    /// <summary>攻击目标</summary>
    public int AttackTarget { get; init; }

    /// <summary>阵亡对话</summary>
    public int DeathDialogue { get; init; }

    /// <summary>等级标记显示</summary>
    public int LevelMarkDisplay { get; init; }

    /// <summary>反防模式</summary>
    public int AntiDefenseMode { get; init; }
}
