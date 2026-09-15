using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class LegionModifier : ModifierBase
{
    public override string Name => "legion";
    public override string DisplayName => "军团修改器";

    private int _selectedLegionId = 1;
    private readonly Random _random = new();

    private static readonly int[] DefaultLegionColors =
    [
        0xFF0000, 0x0000FF, 0xFF8800, 0x00AA00,
        0x880088, 0x00AAAA, 0xFFFF00, 0xAA00AA
    ];

    public int SelectedLegionId
    {
        get => _selectedLegionId;
        set => _selectedLegionId = Math.Clamp(value, 1, 8);
    }

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int legionId = parameter switch
        {
            int id => id,
            Legion l => l.CountryId,
            _ => _selectedLegionId
        };

        _mapData.SetBelongValue(col, row, legionId);
        MarkModified();
        return ModifierResult.Ok($"已设置军团领域 ({col}, {row}) = 军团 {legionId}");
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        _mapData.SetBelongValue(col, row, 0xFF);
        MarkModified();
        return ModifierResult.Ok($"已清除军团领域 ({col}, {row})");
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);
    public override bool CanRemove(int col, int row) => IsValidCoord(col, row);

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        int countryId = _mapData!.GetBelongValue(col, row);
        int idx = _mapData.FindLegionIndex(countryId);
        return idx >= 0 ? _mapData.Legions[idx] : null;
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        if (data is Legion legion)
        {
            return UpdateLegion(legion).Success;
        }
        return false;
    }

    #region 基础CRUD

    public ModifierResult UpdateLegion(Legion legion)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int idx = _mapData.FindLegionIndex(legion.CountryId);
        if (idx >= 0)
            _mapData.ReplaceLegion(idx, legion);
        else
            _mapData.Legions.Add(legion);

        MarkModified();
        return ModifierResult.Ok($"已更新军团 {legion.CountryId}");
    }

    /// <summary>
    /// 按列表索引更新军团（编辑界面专用）。
    /// <para>
    /// 不能用 <see cref="UpdateLegion"/>：它按 CountryId 查表定位，
    /// 一旦用户把 CountryId 改成了地图里不存在的值，就会被当成新军团插入，
    /// 表现就是"编辑已有军团反而多出一个新军团"。
    /// </para>
    /// </summary>
    public ModifierResult UpdateLegionAt(int index, Legion legion)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.Legions.Count)
            return ModifierResult.Fail("军团索引超出范围");

        _mapData.ReplaceLegion(index, legion);
        MarkModified();
        return ModifierResult.Ok($"已更新军团 {index + 1}");
    }

    public Legion? GetLegion(int legionId)
    {
        int idx = _mapData?.FindLegionIndex(legionId) ?? -1;
        return idx >= 0 ? _mapData!.Legions[idx] : null;
    }

    /// <summary>
    /// 新增一个军团（对应 VB 版 LegionSetting 的"+"按钮）。
    /// <para>
    /// 行动顺序、国家 ID 各自取现有最大值 +1（列表为空时从 0 / 1 开始），
    /// 其余字段取 <see cref="Legion.CreateDefault"/> 的默认值：
    /// 初始经济 1000、初始工业 100、初始科技 50、国家血率 1.0、国家税率 0.1、
    /// 颜色白色(FFFFFF)、阵营 0。
    /// </para>
    /// <para>
    /// 注意：VB 原版的"+"按钮是逐字段置 0 的模板，新增出来的军团完全不可用，
    /// 这里改为带默认值（本项目其余创建军团的路径同样使用 CreateDefault）。
    /// </para>
    /// </summary>
    public ModifierResult AddLegion(Legion? legion = null)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var added = legion ?? CreateAppendedLegion(_mapData);
        _mapData.Legions.Add(added);
        MarkModified();
        return ModifierResult.Ok($"已新增军团（国家 {added.CountryId}），当前共 {_mapData.Legions.Count} 个");
    }

    /// <summary>
    /// 构造"追加到列表末尾"的新军团：行动顺序、国家 ID 各自取现有最大值 +1，
    /// 避免与已有军团冲突（直接用 0 会与第一个军团撞号）。
    /// </summary>
    private static Legion CreateAppendedLegion(MapData mapData)
    {
        int actionId = 0;
        int countryId = 1;

        for (int i = 0; i < mapData.Legions.Count; i++)
        {
            var existing = mapData.Legions[i];
            if (existing.ActionId >= actionId) actionId = existing.ActionId + 1;
            if (existing.CountryId >= countryId) countryId = existing.CountryId + 1;
        }

        var legion = Legion.CreateDefault(countryId);
        legion.ActionId = actionId;
        // CreateDefault 把阵营设成国家 ID，新增军团统一从 0（无阵营）开始
        legion.Camp = 0;
        // VB 的 CreateDefault 不含初始科技等级（保持 0），新增军团给 1 才有可玩的起点
        legion.InitialTechLevel = 1;
        return legion;
    }

    /// <summary>
    /// 删除指定索引的军团（对齐 VB 版 LegionSetting 的"-"按钮）。
    /// </summary>
    public ModifierResult RemoveLegionAt(int index)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.Legions.Count)
            return ModifierResult.Fail("军团索引超出范围");

        _mapData.Legions.RemoveAt(index);
        MarkModified();
        return ModifierResult.Ok($"已删除军团，当前共 {_mapData.Legions.Count} 个");
    }

    public Legion? GetLegionByActionId(int actionId)
    {
        if (_mapData == null) return null;

        for (int i = 0; i < _mapData.Legions.Count; i++)
        {
            if (_mapData.Legions[i].ActionId == actionId)
                return _mapData.Legions[i];
        }

        return null;
    }

    public ModifierResult SetLegionColor(int legionId, byte r, byte g, byte b)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        int idx = _mapData.FindLegionIndex(legionId);
        if (idx < 0) return ModifierResult.Fail($"未找到军团 {legionId}");

        var legion = _mapData.Legions[idx];
        legion.ColorR = r;
        legion.ColorG = g;
        legion.ColorB = b;
        _mapData.ReplaceLegion(idx, legion);
        MarkModified();
        return ModifierResult.Ok($"已设置军团 {legionId} 颜色");
    }

    public ModifierResult SetLegionActionId(int legionId, int actionId)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        int idx = _mapData.FindLegionIndex(legionId);
        if (idx < 0) return ModifierResult.Fail($"未找到军团 {legionId}");

        var legion = _mapData.Legions[idx];
        legion.ActionId = actionId;
        _mapData.ReplaceLegion(idx, legion);
        MarkModified();
        return ModifierResult.Ok($"已设置军团 {legionId} ActionId");
    }

    #endregion

    #region 随机化军团等级与经济 (R键)

    public ModifierResult RandomizeAllLegionLevels()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_mapData.Legions.Count == 0) return ModifierResult.Fail("没有军团数据");

        int updatedCount = 0;
        for (int i = 0; i < _mapData.Legions.Count; i++)
        {
            var legion = _mapData.Legions[i];

            legion.MobilityLevel = _random.Next(0, 6);
            legion.RifleLevel = _random.Next(0, 6);
            legion.CamouflageLevel = _random.Next(0, 6);
            legion.EngineerLevel = _random.Next(0, 6);
            legion.GrenadeLevel = _random.Next(0, 6);
            legion.MortarLevel = _random.Next(0, 6);
            legion.MarchLevel = _random.Next(0, 6);
            legion.BulletproofVestLevel = _random.Next(0, 6);
            legion.ArmorLevel = _random.Next(0, 6);
            legion.MainGunLevel = _random.Next(0, 6);
            legion.VehicleBodyLevel = _random.Next(0, 6);
            legion.EngineLevel = _random.Next(0, 6);
            legion.MachineGunLevel = _random.Next(0, 6);
            legion.RaidLevel = _random.Next(0, 6);
            legion.VehicleAirDefenseLevel = _random.Next(0, 6);
            legion.ReinforcedBodyLevel = _random.Next(0, 6);
            legion.ArtilleryLevel = _random.Next(0, 6);
            legion.RocketLevel = _random.Next(0, 6);
            legion.TowingLevel = _random.Next(0, 6);
            legion.ArtilleryArmorLevel = _random.Next(0, 6);
            legion.FirepowerLevel = _random.Next(0, 6);

            legion.NavyRocketLevel = _random.Next(0, 6);
            legion.DisguiseLevel = _random.Next(0, 6);
            legion.HullLevel = _random.Next(0, 6);
            legion.PropulsionLevel = _random.Next(0, 6);
            legion.NavyArmorLevel = _random.Next(0, 6);
            legion.WeaponLevel = _random.Next(0, 6);
            legion.NavalGunLevel = _random.Next(0, 6);
            legion.TorpedoLevel = _random.Next(0, 6);
            legion.MinesweepingLevel = _random.Next(0, 6);
            legion.ShipAirDefenseLevel = _random.Next(0, 6);
            legion.ModernHullLevel = _random.Next(0, 6);

            legion.AviationFuelLevel = _random.Next(0, 6);
            legion.AviationEngineLevel = _random.Next(0, 6);
            legion.AviationBombLevel = _random.Next(0, 6);
            legion.AirRaidLevel = _random.Next(0, 6);
            legion.BombingLevel = _random.Next(0, 6);
            legion.StrategicBombingLevel = _random.Next(0, 6);
            legion.AirdropLevel = _random.Next(0, 6);
            legion.JetEngineLevel = _random.Next(0, 6);

            legion.BunkerLevel = _random.Next(0, 6);
            legion.FortressGunLevel = _random.Next(0, 6);
            legion.CoastalGunLevel = _random.Next(0, 6);
            legion.RocketLauncherLevel = _random.Next(0, 6);
            legion.FortificationLevel = _random.Next(0, 6);
            legion.AntiAircraftGunLevel = _random.Next(0, 6);
            legion.AntiAircraftCannonLevel = _random.Next(0, 6);
            legion.AntiAircraftMissileLevel = _random.Next(0, 6);
            legion.RadarLevel = _random.Next(0, 6);
            legion.MissileWarheadLevel = _random.Next(0, 6);
            legion.SolidRocketEngineLevel = _random.Next(0, 6);
            legion.NuclearBombBreakthroughLevel = _random.Next(0, 6);
            legion.NuclearFusionLevel = _random.Next(0, 6);

            legion.InitialEconomy = _random.Next(100, 5001);
            legion.InitialIndustry = _random.Next(10, 501);
            legion.InitialTech = _random.Next(0, 201);

            _mapData.ReplaceLegion(i, legion);
            updatedCount++;
        }

        MarkModified();
        return ModifierResult.Ok($"已随机化 {updatedCount} 个军团的等级与经济");
    }

    #endregion

    #region 应用默认颜色 (C键)

    public ModifierResult ApplyDefaultColorsToAllLegions()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_mapData.Legions.Count == 0) return ModifierResult.Fail("没有军团数据");

        int updatedCount = 0;
        for (int i = 0; i < _mapData.Legions.Count; i++)
        {
            var legion = _mapData.Legions[i];
            int colorIndex = i % DefaultLegionColors.Length;
            int color = DefaultLegionColors[colorIndex];

            legion.ColorR = (byte)((color >> 16) & 0xFF);
            legion.ColorG = (byte)((color >> 8) & 0xFF);
            legion.ColorB = (byte)(color & 0xFF);

            _mapData.ReplaceLegion(i, legion);
            updatedCount++;
        }

        MarkModified();
        return ModifierResult.Ok($"已应用默认颜色到 {updatedCount} 个军团");
    }

    #endregion

    #region 从配置更新军团颜色 (U键)

    public ModifierResult ApplyAllLegionsColorFromSettings()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_mapData.Legions.Count == 0) return ModifierResult.Fail("没有军团数据");

        var cfg = ConfigManager.Instance;
        int updatedCount = 0;
        var notFoundIds = new List<int>();

        for (int i = 0; i < _mapData.Legions.Count; i++)
        {
            var legion = _mapData.Legions[i];
            int colorValue = cfg.GetCountryColor(legion.CountryId, -1);

            if (colorValue >= 0)
            {
                legion.ColorR = (byte)((colorValue >> 16) & 0xFF);
                legion.ColorG = (byte)((colorValue >> 8) & 0xFF);
                legion.ColorB = (byte)(colorValue & 0xFF);
                _mapData.ReplaceLegion(i, legion);
                updatedCount++;
            }
            else
            {
                notFoundIds.Add(legion.CountryId);
            }
        }

        MarkModified();
        string msg = $"已从配置更新 {updatedCount} 个军团的颜色";
        if (notFoundIds.Count > 0)
            msg += $"，未找到颜色配置的countryid: {string.Join(", ", notFoundIds.Distinct())}";

        return ModifierResult.Ok(msg);
    }

    #endregion

    #region 修改所有军团ActionId与归属 (I键)

    public ModifierResult UpdateAllLegionsActionIdAndBelong()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_mapData.Legions.Count == 0) return ModifierResult.Fail("没有军团数据");

        var actionIdToCountryId = new Dictionary<int, int>();
        int updatedLegions = 0;

        for (int i = 0; i < _mapData.Legions.Count; i++)
        {
            var legion = _mapData.Legions[i];
            int originalActionId = legion.ActionId;
            actionIdToCountryId[originalActionId] = legion.CountryId;

            if (legion.ActionId != legion.CountryId)
            {
                legion.ActionId = legion.CountryId;
                _mapData.ReplaceLegion(i, legion);
                updatedLegions++;
            }
        }

        int updatedBelongings = 0;
        if (_mapData.Belongs != null)
        {
            for (int i = 0; i < _mapData.Belongs.Count; i++)
            {
                int currentBelong = _mapData.GetBelongValueByIndex(i);

                if (actionIdToCountryId.TryGetValue(currentBelong, out int newBelong))
                {
                    if (currentBelong != newBelong)
                    {
                        _mapData.SetBelongValueByIndex(i, newBelong);
                        updatedBelongings++;
                    }
                }
            }
        }

        MarkModified();
        return ModifierResult.Ok($"已修改 {updatedLegions} 个军团的ActionId和 {updatedBelongings} 个归属数据");
    }

    #endregion

    #region 设置玩家控制军团

    public ModifierResult SetPlayerControlledLegion(int actionId)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_mapData.Legions.Count == 0) return ModifierResult.Fail("没有军团数据");

        int updatedCount = 0;
        for (int i = 0; i < _mapData.Legions.Count; i++)
        {
            var legion = _mapData.Legions[i];
            if (legion.ActionId == actionId)
            {
                legion.IsPlayerControlled = 0;
                updatedCount++;
            }
            else
            {
                legion.IsPlayerControlled = 1;
            }
            _mapData.ReplaceLegion(i, legion);
        }

        MarkModified();
        return ModifierResult.Ok($"已设置军团 {actionId} 为玩家控制");
    }

    #endregion

    #region 首都编辑（F 键 / X 键：在当前选中格子添加/删除）

    /// <summary>取指定格子上的首都，没有则返回 null（对齐 VB GetCapitalAtPosition）</summary>
    public Capital? GetCapitalAtPosition(int hexIndex)
    {
        if (_mapData == null) return null;

        for (int i = 0; i < _mapData.Capitals.Count; i++)
        {
            if (_mapData.Capitals[i].Coordinate == hexIndex)
                return _mapData.Capitals[i];
        }

        return null;
    }

    public ModifierResult AddCapital(int hexIndex)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (hexIndex < 0 || hexIndex >= _mapData.MapWidth * _mapData.MapHeight)
            return ModifierResult.Fail("坐标超出范围");

        var existing = _mapData.Capitals.FirstOrDefault(c => c.Coordinate == hexIndex);
        if (existing.Coordinate == hexIndex)
            return ModifierResult.Fail("该位置已有首都");

        var capital = new Capital { Coordinate = hexIndex };
        _mapData.Capitals.Add(capital);
        MarkModified();
        return ModifierResult.Ok($"已在位置 {hexIndex} 添加首都");
    }

    public ModifierResult RemoveCapital(int hexIndex)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        for (int i = 0; i < _mapData.Capitals.Count; i++)
        {
            if (_mapData.Capitals[i].Coordinate == hexIndex)
            {
                _mapData.Capitals.RemoveAt(i);
                MarkModified();
                return ModifierResult.Ok($"已删除位置 {hexIndex} 的首都");
            }
        }

        return ModifierResult.Fail("该位置没有首都");
    }

    public ModifierResult ToggleCapital(int hexIndex)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        for (int i = 0; i < _mapData.Capitals.Count; i++)
        {
            if (_mapData.Capitals[i].Coordinate == hexIndex)
            {
                _mapData.Capitals.RemoveAt(i);
                MarkModified();
                return ModifierResult.Ok($"已删除位置 {hexIndex} 的首都");
            }
        }

        var capital = new Capital { Coordinate = hexIndex };
        _mapData.Capitals.Add(capital);
        MarkModified();
        return ModifierResult.Ok($"已在位置 {hexIndex} 添加首都");
    }

    #endregion

    #region 批量设置军团经济/工业/科技

    public ModifierResult SetAllLegionsEconomy(int value)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_mapData.Legions.Count == 0) return ModifierResult.Fail("没有军团数据");

        int count = 0;
        for (int i = 0; i < _mapData.Legions.Count; i++)
        {
            var legion = _mapData.Legions[i];
            legion.InitialEconomy = value;
            _mapData.ReplaceLegion(i, legion);
            count++;
        }

        MarkModified();
        return ModifierResult.Ok($"已设置 {count} 个军团的经济为 {value}");
    }

    public ModifierResult SetAllLegionsIndustry(int value)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_mapData.Legions.Count == 0) return ModifierResult.Fail("没有军团数据");

        int count = 0;
        for (int i = 0; i < _mapData.Legions.Count; i++)
        {
            var legion = _mapData.Legions[i];
            legion.InitialIndustry = value;
            _mapData.ReplaceLegion(i, legion);
            count++;
        }

        MarkModified();
        return ModifierResult.Ok($"已设置 {count} 个军团的工业为 {value}");
    }

    public ModifierResult SetAllLegionsTech(int value)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_mapData.Legions.Count == 0) return ModifierResult.Fail("没有军团数据");

        int count = 0;
        for (int i = 0; i < _mapData.Legions.Count; i++)
        {
            var legion = _mapData.Legions[i];
            legion.InitialTech = value;
            _mapData.ReplaceLegion(i, legion);
            count++;
        }

        MarkModified();
        return ModifierResult.Ok($"已设置 {count} 个军团的科技为 {value}");
    }

    #endregion

    #region 更新征服国家配置

    public ModifierResult UpdateConquerCountrySettings(int conquerId, List<ConquerCountryConfig> countrySettings)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_mapData.Legions.Count == 0) return ModifierResult.Fail("没有军团数据");
        if (countrySettings == null) return ModifierResult.Fail("征服国家配置列表不能为null");

        var legionConfig = ConfigManager.Instance.GetLegionEditConfig();
        if (legionConfig == null) return ModifierResult.Fail("编辑军团配置未加载");

        var lowStrength = legionConfig.LowStrengthCountry;
        var mediumStrength = legionConfig.MediumStrengthCountry;
        var highStrength = legionConfig.HighStrengthCountry;
        var lowStrengthGift = legionConfig.LowStrengthGift;
        var mediumStrengthGift = legionConfig.MediumStrengthGift;
        var highStrengthGift = legionConfig.HighStrengthGift;

        var legions = _mapData.Legions;
        var matchingObjects = countrySettings.Where(obj => obj.ConquerId == conquerId).ToList();

        if (matchingObjects.Count < legions.Count)
        {
            int maxId = countrySettings.Count > 0 ? countrySettings.Max(obj => obj.Id) : 0;

            for (int i = 0; i < legions.Count - matchingObjects.Count; i++)
            {
                maxId++;
                int newId = conquerId * 1000 + (maxId % 1000);

                int legionIndex = matchingObjects.Count + i;
                int countryId = 1;
                int camp = 1;
                if (legionIndex < legions.Count)
                {
                    countryId = legions[legionIndex].CountryId;
                    camp = legions[legionIndex].Camp;
                }

                var giftRanges = GetGiftRanges(countryId, lowStrength, mediumStrength, highStrength, lowStrengthGift, mediumStrengthGift, highStrengthGift);

                var newObj = new ConquerCountryConfig
                {
                    Id = newId,
                    ConquerId = conquerId,
                    Seat = 99,
                    Star = 1,
                    Camp = camp,
                    CountryId = countryId,
                    WarTurn = 1,
                    PrizeExp = _random.Next(giftRanges[0].Item1, giftRanges[0].Item2 + 1),
                    PrizeGold = _random.Next(giftRanges[1].Item1, giftRanges[1].Item2 + 1),
                    PrizeIndustry = _random.Next(giftRanges[2].Item1, giftRanges[2].Item2 + 1),
                    PrizeEnergy = _random.Next(giftRanges[0].Item1, giftRanges[0].Item2 + 1),
                    PrizeTech = _random.Next(giftRanges[0].Item1, giftRanges[0].Item2 + 1),
                    CostMoney = _random.Next(giftRanges[0].Item1, giftRanges[0].Item2 + 1),
                    CostGear = _random.Next(giftRanges[1].Item1, giftRanges[1].Item2 + 1),
                    CostAtomic = _random.Next(giftRanges[2].Item1, giftRanges[2].Item2 + 1),
                    Photo = $"conquest_{conquerId}",
                    TechCategoryIds = [],
                    CloseTechTypes = [],
                    Coefficient = 0.0,
                    Surrender = true
                };
                countrySettings.Add(newObj);
                matchingObjects.Add(newObj);
            }
        }
        else if (matchingObjects.Count > legions.Count)
        {
            matchingObjects = matchingObjects.OrderBy(obj => obj.Id).ToList();
            var objectsToKeep = matchingObjects.Take(legions.Count).ToList();
            var objectsToRemove = matchingObjects.Skip(legions.Count).ToList();

            foreach (var obj in objectsToRemove)
                countrySettings.Remove(obj);

            matchingObjects = objectsToKeep;
        }

        for (int i = 0; i < legions.Count && i < matchingObjects.Count; i++)
        {
            var legion = legions[i];
            var obj = matchingObjects[i];
            int countryId = legion.CountryId;
            int camp = legion.Camp;

            int star = 1;
            if (highStrength.Contains(countryId))
                star = 3;
            else if (mediumStrength.Contains(countryId))
                star = 2;

            var giftRanges = GetGiftRanges(countryId, lowStrength, mediumStrength, highStrength, lowStrengthGift, mediumStrengthGift, highStrengthGift);

            obj.Camp = camp;
            obj.CountryId = countryId;
            obj.Star = star;
            obj.Seat = 99;

            if (highStrength.Contains(countryId))
                obj.WarTurn = 1;
            else if (mediumStrength.Contains(countryId))
                obj.WarTurn = 3;
            else
                obj.WarTurn = 4;

            obj.PrizeExp = 99999;
            obj.PrizeGold = 99999;
            obj.PrizeIndustry = 99999;
            obj.PrizeEnergy = 99999;
            obj.PrizeTech = 99999;
            obj.CostMoney = _random.Next(giftRanges[0].Item1, giftRanges[0].Item2 + 1);
            obj.CostGear = _random.Next(giftRanges[1].Item1, giftRanges[1].Item2 + 1);
            obj.CostAtomic = _random.Next(giftRanges[2].Item1, giftRanges[2].Item2 + 1);
            obj.Photo = $"conquest_{conquerId}";
            obj.Surrender = true;
            obj.Id = conquerId * 1000 + (i + 1);
        }

        var nonMatching = countrySettings.Where(obj => obj.ConquerId != conquerId).ToList();
        int insertIndex = 0;
        for (int i = 0; i < nonMatching.Count; i++)
        {
            if (nonMatching[i].ConquerId < conquerId)
                insertIndex = i + 1;
        }
        insertIndex = Math.Clamp(insertIndex, 0, nonMatching.Count);

        // 对齐 VB：countrySettings.InsertRange(insertIndex, matchingObjects)
        // 只有一处插入点，避免"循环内插入 + 循环外兜底"造成重复
        countrySettings.Clear();
        countrySettings.AddRange(nonMatching.Take(insertIndex));
        countrySettings.AddRange(matchingObjects);
        countrySettings.AddRange(nonMatching.Skip(insertIndex));

        // 写回 json（对齐 VB LegionModifier.UpdateConquerCountrySettings 的保存步骤）
        bool saved = TrySaveConquerCountrySettings(countrySettings, out var savePath, out var saveError);

        MarkModified();
        return saved
            ? ModifierResult.Ok($"已更新征服国家配置 (征服参数={conquerId})，共 {legions.Count} 个对象 -> {savePath}")
            : ModifierResult.Fail($"征服国家配置已更新，但写回 json 失败: {saveError}");
    }

    /// <summary>
    /// 写回 ConquerCountrySettings.json。
    /// 路径优先由 AssetManager 解析（AssetEntry.FullPath），并兜底用 AssetsRoot 拼接。
    /// 输出格式与 VB 一致：数组内每个对象占一行、无缩进。
    /// </summary>
    private static bool TrySaveConquerCountrySettings(
        List<ConquerCountryConfig> countrySettings, out string path, out string error)
    {
        path = string.Empty;
        error = string.Empty;
        try
        {
            const string relativePath = "json/ConquerCountrySettings.json";

            var manager = AssetManager.Default;
            path = manager.Find(relativePath)?.FullPath ?? string.Empty;

            if (string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(manager.AssetsRoot))
                path = Path.Combine(manager.AssetsRoot, "json", "ConquerCountrySettings.json");

            if (string.IsNullOrEmpty(path))
            {
                error = "未找到 ConquerCountrySettings.json 路径";
                return false;
            }

            var opts = new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var sb = new StringBuilder();
            sb.AppendLine("[");
            for (int i = 0; i < countrySettings.Count; i++)
            {
                sb.Append(JsonSerializer.Serialize(countrySettings[i], opts));
                sb.AppendLine(i < countrySettings.Count - 1 ? "," : string.Empty);
            }
            sb.AppendLine("]");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            Debug.WriteLine($"[LegionModifier] 已写回 {countrySettings.Count} 条征服国家配置 -> {path}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Debug.WriteLine($"[LegionModifier] 写回 ConquerCountrySettings.json 失败: {ex.Message}");
            return false;
        }
    }

    private static List<(int, int)> GetGiftRanges(
        int countryId,
        List<int> lowStrength,
        List<int> mediumStrength,
        List<int> highStrength,
        List<List<(int, int)>> lowStrengthGift,
        List<List<(int, int)>> mediumStrengthGift,
        List<List<(int, int)>> highStrengthGift)
    {
        var defaults = new List<(int, int)> { (0, 99999), (0, 99999), (0, 99999) };

        if (highStrength.Contains(countryId))
            return highStrengthGift.Count > 0 && highStrengthGift[0].Count >= 3
                ? highStrengthGift[0] : defaults;
        if (mediumStrength.Contains(countryId))
            return mediumStrengthGift.Count > 0 && mediumStrengthGift[0].Count >= 3
                ? mediumStrengthGift[0] : defaults;
        return lowStrengthGift.Count > 0 && lowStrengthGift[0].Count >= 3
            ? lowStrengthGift[0] : defaults;
    }

    #endregion

    #region 查询

    public IReadOnlyList<Legion> GetAllLegions()
    {
        return _mapData?.Legions ?? [];
    }

    public int GetLegionCount()
    {
        return _mapData?.Legions.Count ?? 0;
    }

    #endregion
}