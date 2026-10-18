using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.Stage;

namespace WC4MapEditor.Core.Parsers.BTL;

/// <summary>
/// 玩法规则校验器，对齐 Java 版 Wc4StageDAO.check()。
/// <para>
/// 与 <see cref="BTLFormatChecker"/> 的分工：
/// 格式层管「文件能不能解析、各段完不完整」；本类管「解析出来的数据合不合游戏规则」
/// （建筑设施等级、单位参数、军团科技等）。
/// </para>
/// <para>
/// 关键设计：<b>检查与修复共用同一套逻辑</b> —— <see cref="Check"/> 是在数据副本上
/// 跑一遍 <see cref="Fix"/> 并收集说明。这样两者永远不会给出不一致的结论。
/// 原数据绝不会被 <see cref="Check"/> 修改。
/// </para>
/// <para>
/// 需要配置表的项目（建筑类型、兵种 ID）取不到配置时<b>跳过</b>，
/// 绝不把「没有配置」当成「数据非法」。
/// </para>
/// </summary>
public static class BTLRuleChecker
{
    // ---------- 规则常量（与 Java check() / 手机端校验器一致）----------

    public const int MaxFacilityLevel = 4;   // 工业/科技/补给站/机场
    public const int MaxWeaponLevel = 2;     // 导弹/核弹
    public const int MaxKeyPoint = 2;        // 据点、单位红圈
    public const int MaxOrganization = 4;    // 编队
    public const int MinRank = 1, MaxRank = 11;
    public const int MinNobility = 1, MaxNobility = 4;
    public const int MinSkill = 1, MaxSkill = 5;
    public const int MinTechLevel = 1, MaxTechLevel = 10;
    public const int MaxUnitLevel = 6;

    /// <summary>兵种 ID ≥ 此值视为要塞类，不能放在有建筑的地块（Java: baType &gt;= 35）</summary>
    public const int FortressUnitThreshold = 35;

    /// <summary>
    /// 防空武器的合法取值。按项目内的口径：
    /// 11-14 机枪、15-18 防空炮、19-22 防空导弹，0 表示无。
    /// <para>
    /// 注意与 Java 版 check() 里的写法<b>不同</b>（Java 用的是 0/11-14/21-24/31-34），
    /// 这里以项目内的字段说明为准 —— 若实际数据出现 23 以上，可再调整此集合。
    /// </para>
    /// </summary>
    public static readonly HashSet<int> ValidAirDefenseWeapon =
        [0, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22];

    public static readonly HashSet<int> ValidRadar = [0, 2];

    public const int DefaultHealthBonus = 100;
    public const int OceanTerrainGroup = 1;
    public const int SeaBuildingType = 31;
    public const int DefaultBuildingType = 2;
    public const int DefaultUnitType = 1;

    // ---------- 入口 ----------

    /// <summary>校验玩法规则（只读，不会修改 <paramref name="data"/>）</summary>
    public static List<BTLIssue> Check(byte[] data)
    {
        if (data == null || data.Length < BTLSize.HEADER_SIZE)
            return [new BTLIssue(BTLIssueLevel.Error, "规则", "数据过短，无法校验")];

        // 在副本上修复，据此生成报告 —— 与 Fix 共用逻辑，结论必然一致
        var copy = (byte[])data.Clone();
        var log = new List<(BTLIssueLevel Level, string Category, string Message, long Offset)>();
        Fix(copy, log);

        return log.Select(e => new BTLIssue(e.Level, e.Category, e.Message, e.Offset)).ToList();
    }

    /// <summary>就地修复违规数据，返回修复的条目数</summary>
    public static int Fix(byte[] data) => Fix(data, null);

    private static int Fix(byte[] data,
        List<(BTLIssueLevel Level, string Category, string Message, long Offset)>? log)
    {
        if (data == null || data.Length < BTLSize.HEADER_SIZE) return 0;

        var header = BTLHeader.Parse(data);
        var off = StageOffsets.Calculate(
            header.ArmyCount, header.SelectableTileCount, header.BuildingCount);

        var buildingIds = ConfigManager.Instance.GetBuildingTypeIds();
        var armyIds = ConfigManager.Instance.GetArmyTypeIds();

        int count = 0;
        count += FixBuildings(data, header, off, log, buildingIds);
        count += FixArmies(data, header, off, log, armyIds);
        count += FixLegions(data, header, off, log);
        count += FixOwnerships(data, header, off, log);
        count += FixTileCount(data, header, log);
        count += FixDistrictsOnOcean(data, header, off, log);

        // 地雷只报告、不自动修：Java 是把地块上的 bmiMinesLv 置 0，
        // 但本项目的「地雷」是独立的 Trap 记录，没有对应等级字段，
        // 自动删记录会改变数量、牵动文件布局，风险远大于收益。
        CheckTraps(data, header, off, log);

        // 核心省区：省会的判定是「省区 ID 等于自身索引」（与 ProvinceRender 一致）
        CheckCoreProvinces(data, header, off, log);
        return count;
    }

    /// <summary>省区值表示"无"的哨兵（FF）</summary>
    public const int NoProvince = 65535;

    /// <summary>归属值表示"无归属"的哨兵（FF）</summary>
    public const byte NoBelong = 255;

    /// <summary>
    /// 归属修正（对应 Java 版三处「归属出错」）。
    /// <para>
    /// 归属值语义：填归属军团的<b>顺序</b>，从 00 开始；FF 表示无归属。
    /// 越界时按项目内的规则：取该地块所属省区的<b>省会</b>上的归属值；
    /// 省会也取不到就归 0。
    /// </para>
    /// </summary>
    private static int FixOwnerships(byte[] data, BTLHeader header,
        (int terrain, int province, int belong, int building, int dataEnd) off,
        List<(BTLIssueLevel Level, string Category, string Message, long Offset)>? log)
    {
        if (header.SelectableTileCount <= 0) return 0;

        int fixedCount = 0;

        for (int i = 0; i < header.SelectableTileCount; i++)
        {
            long pos = (long)off.belong + i;
            if (pos >= data.Length) break;

            byte belong = data[pos];

            // FF = 无归属，或落在军团范围内，都算合法
            if (belong == NoBelong) continue;
            if (belong >= 0 && belong < header.ArmyCount) continue;

            byte corrected = ResolveBelongByProvince(data, header, off, i);

            Report(log, BTLIssueLevel.Warning, "归属",
                $"地块 {i} 归属值 {belong} 越界（应为 0~{header.ArmyCount - 1} 或 {NoBelong}）", pos);

            data[pos] = corrected;
            fixedCount++;
        }

        return fixedCount;
    }

    /// <summary>
    /// 按「所属省区的省会」解析归属值：地块 → 省区 ID → 省会（省区 ID 即其坐标）→ 该格归属。
    /// 取不到时返回 0（默认军团）。
    /// </summary>
    private static byte ResolveBelongByProvince(byte[] data, BTLHeader header,
        (int terrain, int province, int belong, int building, int dataEnd) off, int coord)
    {
        if (header.SelectableTileCount <= 0) return 0;

        long provPos = (long)off.province + (long)coord * BTLSize.PROVINCE_SIZE;
        if (provPos + BTLSize.PROVINCE_SIZE > data.Length) return 0;

        int provinceId = data[provPos] | (data[provPos + 1] << 8);
        if (provinceId == NoProvince || provinceId < 0 || provinceId >= header.SelectableTileCount)
            return 0;

        // 省会的归属：省区 ID 即省会坐标
        long belongPos = (long)off.belong + provinceId;
        if (belongPos >= data.Length) return 0;

        byte capital = data[belongPos];
        if (capital == NoBelong || (capital >= 0 && capital < header.ArmyCount))
            return capital;

        return 0;
    }

    /// <summary>
    /// 核心省区（省会）检查：省会的判定是「省区 ID 等于自身索引」。
    /// 该类地块应当有建筑；无建筑时报告（随机分配建筑类型涉及随机性，故不自动修）。
    /// </summary>
    private static void CheckCoreProvinces(byte[] data, BTLHeader header,
        (int terrain, int province, int belong, int building, int dataEnd) off,
        List<(BTLIssueLevel Level, string Category, string Message, long Offset)>? log)
    {
        if (header.SelectableTileCount <= 0) return;

        for (int i = 0; i < header.SelectableTileCount; i++)
        {
            long provPos = (long)off.province + (long)i * BTLSize.PROVINCE_SIZE;
            if (provPos + BTLSize.PROVINCE_SIZE > data.Length) break;

            int provinceId = data[provPos] | (data[provPos + 1] << 8);
            if (provinceId == NoProvince) continue;

            // 省会：省区 ID 等于自身索引
            if (provinceId != i) continue;

            if (HasBuildingAt(data, header, off, i)) continue;

            Report(log, BTLIssueLevel.Warning, "省区",
                $"核心省区地块 {i} 上没有建筑（省会应有建筑）", provPos);

            // 位于水中则另外提示（Java: 核心省区位于水中）
            if (IsOceanAt(data, header, off, i))
                Report(log, BTLIssueLevel.Warning, "省区",
                    $"核心省区地块 {i} 位于水中，请检查", provPos);
        }
    }

    /// <summary>
    /// 地雷（陷阱）检查：Java 版不允许建筑或要塞所在地块出现地雷。
    /// 只报告，不做修复（见上方说明）。
    /// </summary>
    private static void CheckTraps(byte[] data, BTLHeader header,
        (int terrain, int province, int belong, int building, int dataEnd) off,
        List<(BTLIssueLevel Level, string Category, string Message, long Offset)>? log)
    {
        if (header.TrapCount <= 0) return;

        // 陷阱区紧随单位区之后
        int unitSize = BTLArmyModule.GetArmySize(header.BtlVersion);
        long trapStart = (long)off.dataEnd + (long)header.TroopCount * unitSize;

        for (int i = 0; i < header.TrapCount; i++)
        {
            long pos = trapStart + (long)i * BTLSize.TRAP_SIZE;
            if (pos + BTLSize.TRAP_SIZE > data.Length) break;

            var trap = Trap.FromBytes(data, (int)pos);

            // 有建筑的地块上不能放地雷
            if (HasBuildingAt(data, header, off, trap.Coordinate))
                Report(log, BTLIssueLevel.Warning, "地雷",
                    $"地雷 #{i}（坐标 {trap.Coordinate}）位于有建筑的地块上，建筑或要塞上不应出现地雷", pos);
        }
    }

    private static void Report(
        List<(BTLIssueLevel Level, string Category, string Message, long Offset)>? log,
        BTLIssueLevel level, string category, string message, long offset)
        => log?.Add((level, category, message, offset));

    // ---------- 建筑 ----------

    private static int FixBuildings(byte[] data, BTLHeader header,
        (int terrain, int province, int belong, int building, int dataEnd) off,
        List<(BTLIssueLevel Level, string Category, string Message, long Offset)>? log,
        HashSet<int> buildingIds)
    {
        if (header.BuildingCount <= 0) return 0;
        bool hasCfg = buildingIds.Count > 0;
        int fixedCount = 0;

        for (int i = 0; i < header.BuildingCount; i++)
        {
            long pos = (long)off.building + i * BTLSize.BUILDING_SIZE;
            if (pos + BTLSize.BUILDING_SIZE > data.Length) break;

            var b = Building.FromBytes(data, (int)pos);
            bool changed = false;

            if (b.BuildingType == 0)
            {
                // Java: 无建筑时清空所有设施参数
                if (HasFacility(b))
                {
                    Report(log, BTLIssueLevel.Warning, "建筑",
                        $"建筑 #{i}（坐标 {b.Coordinate}）无建筑类型却有设施等级残留", pos);
                    ClearFacilities(ref b);
                    changed = true;
                }
            }
            else
            {
                bool isOcean = IsOceanAt(data, header, off, b.Coordinate);

                if (hasCfg && !buildingIds.Contains(b.BuildingType))
                {
                    Report(log, BTLIssueLevel.Error, "建筑",
                        $"建筑 #{i}（坐标 {b.Coordinate}）类型 {b.BuildingType} 不在 BuildingSettings.json 中", pos);
                    b.BuildingType = DefaultBuildingType;
                    changed = true;
                }

                if (isOcean && b.BuildingType < 30)
                {
                    Report(log, BTLIssueLevel.Error, "建筑",
                        $"建筑 #{i}（坐标 {b.Coordinate}）在海洋上却是陆上类型 {b.BuildingType}", pos);
                    b.BuildingType = SeaBuildingType;
                    changed = true;
                }
                else if (!isOcean && b.BuildingType > 30)
                {
                    Report(log, BTLIssueLevel.Error, "建筑",
                        $"建筑 #{i}（坐标 {b.Coordinate}）在陆地上却是海上类型 {b.BuildingType}", pos);
                    b.BuildingType = DefaultBuildingType;
                    changed = true;
                }

                if (b.BuildingType < 30 && b.Appearance > 1)
                {
                    Report(log, BTLIssueLevel.Warning, "建筑",
                        $"建筑 #{i}（坐标 {b.Coordinate}）非港口却有方向参数 {b.Appearance}", pos);
                    b.Appearance = 1; changed = true;
                }

                changed |= ClampLevel(log, "建筑", i, b.Coordinate, pos, "工业", ref b.FactoryLevel, MaxFacilityLevel);
                changed |= ClampLevel(log, "建筑", i, b.Coordinate, pos, "科技", ref b.ResearchLevel, MaxFacilityLevel);
                changed |= ClampLevel(log, "建筑", i, b.Coordinate, pos, "补给站", ref b.MedicalLevel, MaxFacilityLevel);
                changed |= ClampLevel(log, "建筑", i, b.Coordinate, pos, "机场", ref b.AviationLevel, MaxFacilityLevel);
                changed |= ClampLevel(log, "建筑", i, b.Coordinate, pos, "导弹", ref b.MissileLevel, MaxWeaponLevel);
                changed |= ClampLevel(log, "建筑", i, b.Coordinate, pos, "核弹", ref b.NuclearLevel, MaxWeaponLevel);

                if (!ValidRadar.Contains(b.AirDefenseRadar))
                {
                    Report(log, BTLIssueLevel.Warning, "建筑",
                        $"建筑 #{i}（坐标 {b.Coordinate}）雷达参数 {b.AirDefenseRadar} 非法（应为 0 或 2）", pos);
                    b.AirDefenseRadar = 0; changed = true;
                }

                if (!ValidAirDefenseWeapon.Contains(b.AirDefenseWeapon))
                {
                    Report(log, BTLIssueLevel.Warning, "建筑",
                        $"建筑 #{i}（坐标 {b.Coordinate}）防空参数 {b.AirDefenseWeapon} 非法", pos);
                    b.AirDefenseWeapon = 0; changed = true;
                }

                if (b.KeyPoint > MaxKeyPoint)
                {
                    Report(log, BTLIssueLevel.Warning, "建筑",
                        $"建筑 #{i}（坐标 {b.Coordinate}）据点参数 {b.KeyPoint} 超出上限 {MaxKeyPoint}", pos);
                    b.KeyPoint = 0; changed = true;
                }
            }

            if (!changed) continue;
            b.ToBytes(data, (int)pos);
            fixedCount++;
        }

        return fixedCount;
    }

    // ---------- 单位 ----------

    private static int FixArmies(byte[] data, BTLHeader header,
        (int terrain, int province, int belong, int building, int dataEnd) off,
        List<(BTLIssueLevel Level, string Category, string Message, long Offset)>? log,
        HashSet<int> armyIds)
    {
        if (header.TroopCount <= 0) return 0;

        bool hasCfg = armyIds.Count > 0;
        int unitSize = BTLArmyModule.GetArmySize(header.BtlVersion);
        int fixedCount = 0;

        for (int i = 0; i < header.TroopCount; i++)
        {
            long pos = (long)off.dataEnd + i * unitSize;
            if (pos + unitSize > data.Length) break;

            var a = Army.FromBytes(data, (int)pos);
            bool changed = false;

            if (a.UnitType == 0)
            {
                if (a.Level != 0 || a.Organization != 0 || a.General != 0)
                {
                    Report(log, BTLIssueLevel.Warning, "单位",
                        $"单位 #{i}（坐标 {a.Coordinate}）无兵种却有单位参数残留", pos);
                    a.Level = 0; a.Organization = 0; a.General = 0;
                    a.Rank = 0; a.Nobility = 0; a.KeyPoint = 0;
                    changed = true;
                }
            }
            else
            {
                if (hasCfg && !armyIds.Contains(a.UnitType))
                {
                    Report(log, BTLIssueLevel.Error, "单位",
                        $"单位 #{i}（坐标 {a.Coordinate}）兵种 ID {a.UnitType} 不在 ArmySettings.json 中", pos);
                    a.UnitType = DefaultUnitType; changed = true;
                }

                // Java: 建筑上不能有要塞等单位
                if (a.UnitType >= FortressUnitThreshold && HasBuildingAt(data, header, off, a.Coordinate))
                {
                    Report(log, BTLIssueLevel.Error, "单位",
                        $"单位 #{i}（坐标 {a.Coordinate}）是要塞类兵种 {a.UnitType}，不能放在有建筑的地块上", pos);
                    a.UnitType = DefaultUnitType; changed = true;
                }

                if (a.Level > MaxUnitLevel || a.Level == 0)
                {
                    Report(log, BTLIssueLevel.Warning, "单位",
                        $"单位 #{i}（坐标 {a.Coordinate}）等级 {a.Level} 非法（应为 1~{MaxUnitLevel}）", pos);
                    a.Level = 1; changed = true;
                }

                if (a.Organization < 1 || a.Organization > MaxOrganization)
                {
                    Report(log, BTLIssueLevel.Warning, "单位",
                        $"单位 #{i}（坐标 {a.Coordinate}）编队 {a.Organization} 非法（应为 1~{MaxOrganization}）", pos);
                    a.Organization = 1; changed = true;
                }

                if (a.Direction > 1)
                {
                    Report(log, BTLIssueLevel.Warning, "单位",
                        $"单位 #{i}（坐标 {a.Coordinate}）方向 {a.Direction} 非法（应为 0 或 1）", pos);
                    a.Direction = 0; changed = true;
                }

                if (a.HealthBonus < 1)
                {
                    Report(log, BTLIssueLevel.Warning, "单位",
                        $"单位 #{i}（坐标 {a.Coordinate}）血量加成 {a.HealthBonus} 非法（应不小于 1）", pos);
                    a.HealthBonus = DefaultHealthBonus; changed = true;
                }

                if (a.General != 0)
                {
                    changed |= ClampRange(log, "单位", i, a.Coordinate, pos, "军衔", ref a.Rank, MinRank, MaxRank);
                    changed |= ClampRange(log, "单位", i, a.Coordinate, pos, "将领等级", ref a.Nobility, MinNobility, MaxNobility);
                    changed |= ClampRange(log, "单位", i, a.Coordinate, pos, "技能1", ref a.SkillLevel1, MinSkill, MaxSkill);
                    changed |= ClampRange(log, "单位", i, a.Coordinate, pos, "技能2", ref a.SkillLevel2, MinSkill, MaxSkill);
                    changed |= ClampRange(log, "单位", i, a.Coordinate, pos, "技能3", ref a.SkillLevel3, MinSkill, MaxSkill);
                    changed |= ClampRange(log, "单位", i, a.Coordinate, pos, "技能4", ref a.SkillLevel4, MinSkill, MaxSkill);
                    changed |= ClampRange(log, "单位", i, a.Coordinate, pos, "技能5", ref a.SkillLevel5, MinSkill, MaxSkill);
                }
                else if (a.Rank != 0 || a.Nobility != 0)
                {
                    Report(log, BTLIssueLevel.Warning, "单位",
                        $"单位 #{i}（坐标 {a.Coordinate}）无将领却有军衔/等级参数", pos);
                    a.Rank = 0; a.Nobility = 0; changed = true;
                }

                if (a.KeyPoint > MaxKeyPoint)
                {
                    Report(log, BTLIssueLevel.Warning, "单位",
                        $"单位 #{i}（坐标 {a.Coordinate}）红圈参数 {a.KeyPoint} 超出上限 {MaxKeyPoint}", pos);
                    a.KeyPoint = 0; changed = true;
                }
            }

            if (!changed) continue;
            a.ToBytes(data, (int)pos);
            fixedCount++;
        }

        return fixedCount;
    }

    // ---------- 军团 ----------

    private static int FixLegions(byte[] data, BTLHeader header,
        (int terrain, int province, int belong, int building, int dataEnd) off,
        List<(BTLIssueLevel Level, string Category, string Message, long Offset)>? log)
    {
        if (header.ArmyCount <= 0) return 0;
        int fixedCount = 0;

        var countryIds = ConfigManager.Instance.GetCountryIds();
        bool hasCountryCfg = countryIds.Count > 0;
        int defaultCountry = hasCountryCfg ? countryIds.Order().First() : 0;

        int? prevActionId = null;

        for (int i = 0; i < header.ArmyCount; i++)
        {
            long pos = (long)BTLSize.HEADER_SIZE + i * BTLSize.LEGION_SIZE;
            if (pos + BTLSize.LEGION_SIZE > data.Length) break;

            var legion = Legion.FromBytes(data, (int)pos);
            bool changed = false;

            // 国家 ID 必须在 CountrySettings.json 中
            if (hasCountryCfg && !countryIds.Contains(legion.CountryId))
            {
                Report(log, BTLIssueLevel.Error, "军团",
                    $"军团 #{i} 国家ID {legion.CountryId} 不在 CountrySettings.json 中", pos);
                legion.CountryId = defaultCountry;
                changed = true;
            }

            // 行动顺序应从小到大排列。
            // 只报告不重排：重排要同步改写所有归属值里的军团序号，牵动面太大。
            if (prevActionId.HasValue && legion.ActionId < prevActionId.Value)
                Report(log, BTLIssueLevel.Warning, "军团",
                    $"军团 #{i} 行动顺序 {legion.ActionId} 小于前一个 {prevActionId.Value}，未从小到大排列", pos);

            prevActionId = legion.ActionId;

            // 军团初始科技等级（Java 的 bm1_76）
            if (legion.InitialTechLevel < MinTechLevel || legion.InitialTechLevel > MaxTechLevel)
            {
                Report(log, BTLIssueLevel.Warning, "军团",
                    $"军团 #{i} 初始科技等级 {legion.InitialTechLevel} 非法（应为 {MinTechLevel}~{MaxTechLevel}）", pos);

                legion.InitialTechLevel = Clamp(legion.InitialTechLevel, MinTechLevel, MaxTechLevel);
                changed = true;
            }

            if (!changed) continue;
            legion.ToBytes(data, (int)pos);
            fixedCount++;
        }

        return fixedCount;
    }

    /// <summary>
    /// 地块总数一致性：头部声明的格数应为 地图长 × 地图宽。
    /// 不一致时修正头部字段（各区块长度本就按声明值解析，改头部即对齐）。
    /// </summary>
    private static int FixTileCount(byte[] data, BTLHeader header,
        List<(BTLIssueLevel Level, string Category, string Message, long Offset)>? log)
    {
        long real = (long)header.MapLength * header.MapWidth;
        if (real <= 0 || real == header.SelectableTileCount) return 0;

        Report(log, BTLIssueLevel.Error, "头部",
            $"地块总数不一致：头部声明 {header.SelectableTileCount}，实际地图 {header.MapLength}×{header.MapWidth} = {real}",
            TileCountHeaderOffset);

        // 头部里地块总数的偏移与 StageOffsets 一致（0x58）
        WriteInt32(data, TileCountHeaderOffset, (int)real);
        return 1;
    }

    /// <summary>头部中「地块总数」字段的字节偏移（与 StageOffsets/解析器一致）</summary>
    private const int TileCountHeaderOffset = 0x58;

    /// <summary>
    /// 海洋行政区划：海洋地块的区划值必须为 65535（FF FF）。
    /// </summary>
    private static int FixDistrictsOnOcean(byte[] data, BTLHeader header,
        (int terrain, int province, int belong, int building, int dataEnd) off,
        List<(BTLIssueLevel Level, string Category, string Message, long Offset)>? log)
    {
        if (header.SelectableTileCount <= 0) return 0;
        int fixedCount = 0;

        for (int i = 0; i < header.SelectableTileCount; i++)
        {
            if (!IsOceanAt(data, header, off, i)) continue;

            long pos = (long)off.province + (long)i * BTLSize.PROVINCE_SIZE;
            if (pos + BTLSize.PROVINCE_SIZE > data.Length) break;

            int value = data[pos] | (data[pos + 1] << 8);
            if (value == NoProvince) continue;

            Report(log, BTLIssueLevel.Warning, "区划",
                $"地块 {i} 是海洋，行政区划值应为 {NoProvince}，当前为 {value}", pos);

            data[pos] = 0xFF;
            data[pos + 1] = 0xFF;
            fixedCount++;
        }

        return fixedCount;
    }

    private static void WriteInt32(byte[] data, int offset, int value)
    {
        if (offset + 4 > data.Length) return;
        data[offset] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    // ---------- 首都（MapData 层）----------
    // 首都是独立列表（MapData.Capitals），不在 Building 结构里，
    // 且判定「该格是不是等级城」需要读建筑数据，所以这部分走 MapData 而非原始字节。

    /// <summary>可设首都的等级城建筑类型范围（15-19）。低于 15 的普通城市不能设首都。</summary>
    public const int MinCapitalBuildingType = 15;
    public const int MaxCapitalBuildingType = 19;

    /// <summary>
    /// 首都校验：每个首都都必须位于等级城（15-19）之上。
    /// 对齐 Java: <c>if (capital != 0 &amp;&amp; buType &lt; 15) capital = 0;</c>
    /// </summary>
    public static List<BTLIssue> CheckCapitals(MapData mapData)
    {
        var issues = new List<BTLIssue>();
        if (mapData?.Capitals == null) return issues;

        foreach (var capital in mapData.Capitals)
        {
            var building = FindBuildingAt(mapData, capital.Coordinate);

            if (building == null)
            {
                issues.Add(new BTLIssue(BTLIssueLevel.Warning, "首都",
                    $"首都（坐标 {capital.Coordinate}）所在格没有建筑"));
                continue;
            }

            if (building.Value.BuildingType >= MinCapitalBuildingType &&
                building.Value.BuildingType <= MaxCapitalBuildingType)
                continue;

            issues.Add(new BTLIssue(BTLIssueLevel.Warning, "首都",
                $"首都（坐标 {capital.Coordinate}）位于建筑类型 {building.Value.BuildingType} 上，" +
                $"首都只能设在等级城（{MinCapitalBuildingType}-{MaxCapitalBuildingType}）上"));
        }

        return issues;
    }

    /// <summary>
    /// 首都修复：移除不在等级城上的首都（对应 Java 的 capital = 0）。
    /// 返回移除的数量。
    /// </summary>
    public static int FixCapitals(MapData mapData)
    {
        if (mapData?.Capitals == null) return 0;

        int removed = 0;

        for (int i = mapData.Capitals.Count - 1; i >= 0; i--)
        {
            var capital = mapData.Capitals[i];
            var building = FindBuildingAt(mapData, capital.Coordinate);

            bool valid = building != null
                && building.Value.BuildingType >= MinCapitalBuildingType
                && building.Value.BuildingType <= MaxCapitalBuildingType;

            if (valid) continue;

            mapData.Capitals.RemoveAt(i);
            removed++;
        }

        return removed;
    }

    private static Building? FindBuildingAt(MapData mapData, int coordinate)
    {
        if (mapData?.Buildings == null) return null;

        foreach (var b in mapData.Buildings)
        {
            if (b.Coordinate == coordinate) return b;
        }

        return null;
    }

    // ---------- 辅助 ----------

    private static bool ClampLevel(
        List<(BTLIssueLevel Level, string Category, string Message, long Offset)>? log,
        string category, int index, int coord, long pos, string label, ref byte value, int max)
    {
        if (value <= max) return false;

        Report(log, BTLIssueLevel.Warning, category,
            $"{category} #{index}（坐标 {coord}）{label}等级 {value} 超出上限 {max}", pos);
        value = 0;   // Java: 设施等级超标一律归 0
        return true;
    }

    private static bool ClampRange(
        List<(BTLIssueLevel Level, string Category, string Message, long Offset)>? log,
        string category, int index, int coord, long pos, string label,
        ref byte value, int min, int max)
    {
        if (value >= min && value <= max) return false;

        Report(log, BTLIssueLevel.Warning, category,
            $"{category} #{index}（坐标 {coord}）{label} {value} 非法（应为 {min}~{max}）", pos);
        value = (byte)Clamp(value, min, max);
        return true;
    }

    private static int Clamp(int value, int min, int max)
        => value < min ? min : value > max ? max : value;

    private static bool HasFacility(Building b)
        => b.FactoryLevel != 0 || b.ResearchLevel != 0 || b.MedicalLevel != 0
        || b.AviationLevel != 0 || b.MissileLevel != 0 || b.NuclearLevel != 0
        || b.AirDefenseRadar != 0 || b.AirDefenseWeapon != 0 || b.KeyPoint != 0
        || b.Appearance != 0 || b.RewardCount != 0;

    private static void ClearFacilities(ref Building b)
    {
        b.Appearance = 0;
        b.RewardCount = 0;
        b.KeyPoint = 0;
        b.AirDefenseWeapon = 0;
        b.AirDefenseRadar = 0;
        b.FactoryLevel = 0;
        b.ResearchLevel = 0;
        b.MedicalLevel = 0;
        b.AviationLevel = 0;
        b.MissileLevel = 0;
        b.NuclearLevel = 0;
    }

    /// <summary>该地块是否为海洋（地形第一层组 ID 为 1）</summary>
    private static bool IsOceanAt(byte[] data, BTLHeader header,
        (int terrain, int province, int belong, int building, int dataEnd) off, int coord)
    {
        if (header.SelectableTileCount <= 0 || coord < 0 || coord >= header.SelectableTileCount)
            return false;

        long pos = (long)off.terrain + coord * BTLSize.TERRAIN_SIZE;
        if (pos + BTLSize.TERRAIN_SIZE > data.Length) return false;

        return data[pos] == OceanTerrainGroup;
    }

    /// <summary>该地块上是否有建筑（用于「要塞不能放在建筑上」的判断）</summary>
    private static bool HasBuildingAt(byte[] data, BTLHeader header,
        (int terrain, int province, int belong, int building, int dataEnd) off, int coord)
    {
        if (header.BuildingCount <= 0) return false;

        for (int i = 0; i < header.BuildingCount; i++)
        {
            long pos = (long)off.building + i * BTLSize.BUILDING_SIZE;
            if (pos + BTLSize.BUILDING_SIZE > data.Length) break;

            var b = Building.FromBytes(data, (int)pos);
            if (b.BuildingType != 0 && b.Coordinate == coord) return true;
        }

        return false;
    }
}
