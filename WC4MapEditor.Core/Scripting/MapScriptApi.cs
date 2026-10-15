using System.Reflection;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Services;
using WC4MapEditor.Scripting;

namespace WC4MapEditor.Core.Scripting;

/// <summary>
/// <see cref="IMapScriptApi"/> 的 Core 实现：把脚本调用转发到既有的 Modifier。
/// <para>
/// 转发而不是直接改数据，是为了复用 Modifier 里的校验、索引维护与
/// <c>MarkModified()</c> 通知逻辑，避免脚本绕过常规编辑路径。
/// </para>
/// </summary>
public sealed class MapScriptApi : IMapScriptApi
{
    private readonly MapData _mapData;
    private readonly EditModeManager _modeManager;

    public MapScriptApi(MapData mapData, EditModeManager modeManager)
    {
        _mapData = mapData ?? throw new ArgumentNullException(nameof(mapData));
        _modeManager = modeManager ?? throw new ArgumentNullException(nameof(modeManager));
    }

    // ---------- 地图信息 ----------

    public int MapWidth => _mapData.MapWidth;
    public int MapHeight => _mapData.MapHeight;
    public int TileCount => _mapData.MapWidth * _mapData.MapHeight;
    public int BuildingCount => _mapData.Buildings.Count;

    // ---------- 建筑 ----------

    public void ForEachBuilding(Func<ScriptBuildingInfo, bool> visitor)
    {
        if (visitor == null) return;

        // 遍历副本：脚本回调里可能会增删建筑（比如边遍历边删），
        // 直接枚举原集合会抛"集合已修改"异常。
        var snapshot = _mapData.Buildings.ToList();
        foreach (var building in snapshot)
        {
            if (!visitor(ToInfo(building))) return;
        }
    }

    public ScriptBuildingInfo? GetBuilding(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;

        for (int i = 0; i < _mapData.Buildings.Count; i++)
        {
            var building = _mapData.Buildings[i];
            if (building.Coordinate == row * _mapData.MapWidth + col)
                return ToInfo(building);
        }
        return null;
    }

    public ScriptOperationResult AddBuilding(int col, int row, int buildingType, string? name = null)
    {
        if (!IsValidCoord(col, row))
            return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");

        if (buildingType < 0 || buildingType > 255)
            return ScriptOperationResult.Fail($"建筑类型 {buildingType} 超出范围 (0-255)");

        var modifier = _modeManager.GetModifier<BuildingModifier>();
        if (modifier == null) return ScriptOperationResult.Fail("建筑修改器不可用");

        int index = row * _mapData.MapWidth + col;
        var building = Building.CreateDefault(index);
        building.BuildingType = (byte)buildingType;
        building.Name = BuildingNameResolver.Resolve(name);

        var result = modifier.Apply(col, row, building);
        return result.Success
            ? ScriptOperationResult.Ok(result.Message ?? "已添加建筑", 1)
            : ScriptOperationResult.Fail(result.Message ?? "添加建筑失败");
    }

    public ScriptOperationResult RemoveBuilding(int col, int row)
    {
        if (!IsValidCoord(col, row))
            return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");

        var modifier = _modeManager.GetModifier<BuildingModifier>();
        if (modifier == null) return ScriptOperationResult.Fail("建筑修改器不可用");

        var result = modifier.Remove(col, row);
        return result.Success
            ? ScriptOperationResult.Ok(result.Message ?? "已删除建筑", 1)
            : ScriptOperationResult.Fail(result.Message ?? "删除建筑失败");
    }

    public ScriptOperationResult RemoveBuildingsByProbability(int probability, bool keepNamed = false)
    {
        var modifier = _modeManager.GetModifier<BuildingModifier>();
        if (modifier == null) return ScriptOperationResult.Fail("建筑修改器不可用");

        var result = modifier.RemoveBuildingsByProbability(probability, keepNamed);
        return result.Success
            ? ScriptOperationResult.Ok(result.Message ?? "已按概率删除建筑", result.AffectedCount)
            : ScriptOperationResult.Fail(result.Message ?? "按概率删除建筑失败");
    }

    /// <summary>
    /// 建筑对象上允许脚本改写的字段白名单。
    /// <para>
    /// 用反射按名定位 <see cref="Building"/> 的字段，写值时按字段宽度钳位 ——
    /// 这样以后结构里新增字段，只要在这里补个名字即可，不必再写一堆方法。
    /// 不开放 <c>Coordinate</c>（改坐标请用 <c>MoveBuilding</c>，它还要同步归属与索引）。
    /// </para>
    /// </summary>
    private static readonly string[] BuildingWritableFields =
    {
        "Name", "BuildingType", "Appearance", "LandmarkBuilding", "DecorativeBuilding",
        "SkillUnlock", "RewardCount", "HatredValue", "KeyPoint", "OccupationEvent",
        "FireIgnition", "FireDuration", "AirDefenseWeapon", "AirDefenseRadar",
        "FactoryLevel", "ResearchLevel", "MedicalLevel", "AviationLevel",
        "MissileLevel", "NuclearLevel",
    };

    public IReadOnlyList<string> GetBuildingFields() => BuildingWritableFields;

    public ScriptOperationResult SetBuildingType(int col, int row, int buildingType)
        => SetBuildingField(col, row, "BuildingType", buildingType);

    public ScriptOperationResult SetBuildingName(int col, int row, string? name)
    {
        if (!IsValidCoord(col, row))
            return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");

        int idx = _mapData.FindBuildingIndex(col, row);
        if (idx < 0) return ScriptOperationResult.Fail($"({col},{row}) 没有建筑");

        var modifier = _modeManager.GetModifier<BuildingModifier>();
        if (modifier == null) return ScriptOperationResult.Fail("建筑修改器不可用");

        var building = _mapData.Buildings[idx];
        building.Name = BuildingNameResolver.Resolve(name);

        var result = modifier.UpdateBuilding(building);
        return result.Success
            ? ScriptOperationResult.Ok(
                $"已设置建筑名称 ({col},{row}) = {(string.IsNullOrWhiteSpace(name) ? "无名称" : name)}", 1)
            : ScriptOperationResult.Fail(result.Message ?? "设置建筑名称失败");
    }

    public ScriptOperationResult SetBuildingField(int col, int row, string field, double value)
    {
        if (!IsValidCoord(col, row))
            return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");

        string? matched = MatchField(BuildingWritableFields, field);
        if (matched == null)
            return ScriptOperationResult.Fail(
                $"未知建筑字段 '{field}'；可用字段：" + string.Join(", ", BuildingWritableFields));

        int idx = _mapData.FindBuildingIndex(col, row);
        if (idx < 0) return ScriptOperationResult.Fail($"({col},{row}) 没有建筑");

        var modifier = _modeManager.GetModifier<BuildingModifier>();
        if (modifier == null) return ScriptOperationResult.Fail("建筑修改器不可用");

        var building = _mapData.Buildings[idx];

        int v = (int)Math.Round(value);

        // 按字段类型钳位后写回：byte 字段 0-255、名称 0-65535，避免脚本传个 9999 把数据写花
        if (!TryWriteField(building, matched, value, out Building updated, out string err))
            return ScriptOperationResult.Fail(err);

        building = updated;

        var result = modifier.UpdateBuilding(building);
        return result.Success
            ? ScriptOperationResult.Ok($"已设置建筑 ({col},{row}) 的 {matched} = {v}", 1)
            : ScriptOperationResult.Fail(result.Message ?? "修改建筑失败");
    }

    public ScriptOperationResult MoveBuilding(int fromCol, int fromRow, int toCol, int toRow)
    {
        if (!IsValidCoord(fromCol, fromRow) || !IsValidCoord(toCol, toRow))
            return ScriptOperationResult.Fail($"坐标超出地图范围：({fromCol},{fromRow}) → ({toCol},{toRow})");

        var modifier = _modeManager.GetModifier<BuildingModifier>();
        if (modifier == null) return ScriptOperationResult.Fail("建筑修改器不可用");

        var result = modifier.MoveBuilding(fromCol, fromRow, toCol, toRow);
        return result.Success
            ? ScriptOperationResult.Ok(
                result.Message ?? $"已把建筑从 ({fromCol},{fromRow}) 移到 ({toCol},{toRow})", 1)
            : ScriptOperationResult.Fail(result.Message ?? "移动建筑失败");
    }

    // ---------- 归属 / 省区 / 地形 ----------

    public int GetBelong(int col, int row)
        => IsValidCoord(col, row) ? _mapData.GetBelongValue(col, row) : 0xFF;

    public ScriptOperationResult SetBelong(int col, int row, int belongValue)
    {
        if (!IsValidCoord(col, row))
            return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");

        if (belongValue < 0 || belongValue > 255)
            return ScriptOperationResult.Fail($"归属值 {belongValue} 超出范围 (0-255)");

        // SetBelongValue 在格子不归属数据范围内时会直接返回 false（不写入也不报错），
        // 这里必须把结果透出去，否则脚本以为写成功了，实际是静默丢失。
        if (!_mapData.SetBelongValue(col, row, belongValue))
            return ScriptOperationResult.Fail($"设置归属失败：({col},{row}) 不在归属数据范围内");

        return ScriptOperationResult.Ok($"已设置归属 ({col},{row}) = {belongValue}", 1);
    }

    public int GetProvince(int col, int row)
        => IsValidCoord(col, row) ? _mapData.GetProvinceRef(col, row).ProvinceValue : 0xFFFF;

    public ScriptOperationResult SetProvince(int col, int row, int provinceValue)
    {
        if (!IsValidCoord(col, row))
            return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");

        var modifier = _modeManager.GetModifier<ProvinceModifier>();
        if (modifier == null) return ScriptOperationResult.Fail("省区修改器不可用");

        var result = modifier.SetProvinceValue(col, row, provinceValue);
        return result.Success
            ? ScriptOperationResult.Ok(result.Message ?? $"已设置省区 ({col},{row}) = {provinceValue}", 1)
            : ScriptOperationResult.Fail(result.Message ?? "设置省区失败");
    }

    public int GetTerrain(int col, int row)
        => IsValidCoord(col, row) ? _mapData.GetTerrainRef(col, row).TileType1 : -1;

    // ---------- 单位 ----------

    public int ArmyCount => _mapData.Armies.Count + _mapData.ArmiesV3.Count;

    public ScriptOperationResult AddArmy(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");
        var result = RunArmyOp(col, row, a => a.Apply(col, row), v3 => v3.Apply(col, row));
        return ToScriptResult(result, "已放置单位");
    }

    public ScriptOperationResult RemoveArmy(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");
        var result = RunArmyOp(col, row, a => a.Remove(col, row), v3 => v3.Remove(col, row));
        return ToScriptResult(result, "已删除单位");
    }

    public ScriptOperationResult SetArmyLegion(int col, int row, int legionId)
    {
        if (!IsValidCoord(col, row)) return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");
        var result = RunArmyOp(col, row, a => a.SetLegionId(col, row, legionId), v3 => v3.SetLegionId(col, row, legionId));
        return ToScriptResult(result, $"已设置军团ID={legionId}");
    }

    public ScriptOperationResult SetArmyPlan(int col, int row, int plan)
    {
        if (!IsValidCoord(col, row)) return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");
        plan = Math.Clamp(plan, 0, 4);
        if (_mapData.Header.BtlVersion >= 3)
        {
            int idx = _mapData.FindArmyV3Index(col, row);
            if (idx < 0) return ScriptOperationResult.Fail($"({col},{row}) 无v3单位");
            var a = _mapData.ArmiesV3[idx]; a.Plan = (short)plan; _mapData.ArmiesV3[idx] = a;
        }
        else
        {
            int idx = _mapData.FindArmyIndex(col, row);
            if (idx < 0) return ScriptOperationResult.Fail($"({col},{row}) 无单位");
            var a = _mapData.Armies[idx]; a.Plan = (short)plan; _mapData.Armies[idx] = a;
        }
        return ScriptOperationResult.Ok($"已设置方案={plan}", 1);
    }

    public ScriptOperationResult CycleArmyPlan(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");
        var result = RunArmyOp(col, row, a => a.QuickModifyArmyPlan(col, row), v3 => v3.QuickModifyArmyPlan(col, row));
        return ToScriptResult(result, "已切换方案");
    }

    public ScriptOperationResult BatchModifyArmyPlanByLegion(int belong, int plan)
    {
        var result = RunArmyOp(0, 0, a => a.BatchModifyArmyPlanByLegion(belong, plan), v3 => v3.BatchModifyArmyPlanByLegion(belong, plan));
        return ToScriptResult(result, "已批量修改方案");
    }

    public ScriptOperationResult RemoveArmyByBelong(int belong)
    {
        var result = RunArmyOp(0, 0, a => a.DeleteArmiesByBelong(belong), v3 => v3.DeleteArmiesByBelong(belong));
        return ToScriptResult(result, $"已删除归属={belong}的单位");
    }

    public ScriptOperationResult RemoveAllArmies()
    {
        int count = _mapData.Armies.Count + _mapData.ArmiesV3.Count;
        _mapData.Armies.Clear();
        _mapData.ArmiesV3.Clear();
        return ScriptOperationResult.Ok($"已删除 {count} 个单位", count);
    }

    public ScriptOperationResult EraseArmyBrush(int col, int row, int radius)
    {
        if (!IsValidCoord(col, row)) return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");
        int deleted = 0;
        for (int dr = -radius; dr <= radius; dr++)
        {
            for (int dc = -radius; dc <= radius; dc++)
            {
                int r = row + dr, c = col + dc;
                if (r < 0 || r >= _mapData.MapHeight || c < 0 || c >= _mapData.MapWidth) continue;
                if (Math.Sqrt(dr * dr + dc * dc) > radius) continue;
                int idx = _mapData.FindArmyIndex(c, r);
                if (idx >= 0) { _mapData.RemoveArmyAt(idx); deleted++; }
                int v3Idx = _mapData.FindArmyV3Index(c, r);
                if (v3Idx >= 0) { _mapData.RemoveArmyV3At(v3Idx); deleted++; }
            }
        }
        return ScriptOperationResult.Ok($"橡皮擦删除了 {deleted} 个单位", deleted);
    }

    public ScriptOperationResult ModifyLegionStrength(int belong, int level)
    {
        var result = RunArmyOp(0, 0, a => a.ModifyLegionStrength(belong, level), v3 => v3.ModifyLegionStrength(belong, level));
        return ToScriptResult(result, "已修改军团强度");
    }

    public ScriptOperationResult RandomizeLegionArmies(int belong)
    {
        var result = RunArmyOp(0, 0, a => a.RandomizeLegionArmies(belong), v3 => v3.RandomizeLegionArmies(belong));
        return ToScriptResult(result, "已随机化军团单位");
    }

    public ScriptOperationResult GenerateArmiesByProbability(int belong, int probability)
    {
        probability = Math.Clamp(probability, 1, 100);
        var result = RunArmyOp(0, 0, a => a.GenerateArmiesByProbability(belong, probability), v3 => v3.GenerateArmiesByProbability(belong, probability));
        return ToScriptResult(result, "已按概率生成单位");
    }

    public ScriptOperationResult AutoAssignGenerals(int belong, int count)
    {
        var result = RunArmyOp(0, 0, a => a.AutoAssignGeneralsToArmies(belong, count, false, true), v3 => v3.AutoAssignGeneralsToArmies3(belong, count, false, true));
        return ToScriptResult(result, "已自动分配将领");
    }

    // ---------- 军团 ----------

    public int LegionCount => _mapData.Legions.Count;

    public ScriptOperationResult SetLegionColor(int legionId, int r, int g, int b)
    {
        var legion = _modeManager.GetModifier<LegionModifier>();
        if (legion == null) return ScriptOperationResult.Fail("军团修改器不可用");
        var result = legion.SetLegionColor(legionId, (byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));
        return ToScriptResult(result, "已设置颜色");
    }

    public ScriptOperationResult SetLegionActionId(int legionId, int actionId)
    {
        var legion = _modeManager.GetModifier<LegionModifier>();
        if (legion == null) return ScriptOperationResult.Fail("军团修改器不可用");
        var result = legion.SetLegionActionId(legionId, actionId);
        return ToScriptResult(result, "已设置ActionId");
    }

    public ScriptOperationResult SetLegionCountryIdAt(int index, int countryId)
    {
        var legion = _modeManager.GetModifier<LegionModifier>();
        if (legion == null) return ScriptOperationResult.Fail("军团修改器不可用");
        var result = legion.SetLegionCountryIdAt(index, countryId);
        return ToScriptResult(result, "已设置国家ID");
    }

    public ScriptOperationResult SetLegionActionIdAt(int index, int actionId)
    {
        var legion = _modeManager.GetModifier<LegionModifier>();
        if (legion == null) return ScriptOperationResult.Fail("军团修改器不可用");
        var result = legion.SetLegionActionIdAt(index, actionId);
        return ToScriptResult(result, "已设置ActionId");
    }

    public int GetLegionCountryIdAt(int index)
        => (index >= 0 && index < _mapData.Legions.Count) ? _mapData.Legions[index].CountryId : -1;

    public ScriptOperationResult SetPlayerLegion(int actionId)
    {
        var legion = _modeManager.GetModifier<LegionModifier>();
        if (legion == null) return ScriptOperationResult.Fail("军团修改器不可用");
        var result = legion.SetPlayerControlledLegion(actionId);
        return ToScriptResult(result, "已设置玩家控制军团");
    }

    public ScriptOperationResult ApplyDefaultLegionColors()
    {
        var legion = _modeManager.GetModifier<LegionModifier>();
        if (legion == null) return ScriptOperationResult.Fail("军团修改器不可用");
        var result = legion.ApplyDefaultColorsToAllLegions();
        return ToScriptResult(result, "已应用默认颜色");
    }

    public ScriptOperationResult ApplyLegionColorsFromSettings()
    {
        var legion = _modeManager.GetModifier<LegionModifier>();
        if (legion == null) return ScriptOperationResult.Fail("军团修改器不可用");
        var result = legion.ApplyAllLegionsColorFromSettings();
        return ToScriptResult(result, "已从配置更新颜色");
    }

    public ScriptOperationResult RandomizeLegionLevels()
    {
        var legion = _modeManager.GetModifier<LegionModifier>();
        if (legion == null) return ScriptOperationResult.Fail("军团修改器不可用");
        var result = legion.RandomizeAllLegionLevels();
        return ToScriptResult(result, "已随机化军团等级");
    }

    public ScriptOperationResult RebuildLegionActionBelong()
    {
        var legion = _modeManager.GetModifier<LegionModifier>();
        if (legion == null) return ScriptOperationResult.Fail("军团修改器不可用");
        var result = legion.UpdateAllLegionsActionIdAndBelong();
        return ToScriptResult(result, "已重建ActionId与归属");
    }

    public ScriptOperationResult SetAllLegionsEconomy(int value)
    {
        var legion = _modeManager.GetModifier<LegionModifier>();
        if (legion == null) return ScriptOperationResult.Fail("军团修改器不可用");
        var result = legion.SetAllLegionsEconomy(value);
        return ToScriptResult(result, $"已设置经济={value}");
    }

    public ScriptOperationResult SetAllLegionsIndustry(int value)
    {
        var legion = _modeManager.GetModifier<LegionModifier>();
        if (legion == null) return ScriptOperationResult.Fail("军团修改器不可用");
        var result = legion.SetAllLegionsIndustry(value);
        return ToScriptResult(result, $"已设置工业={value}");
    }

    public ScriptOperationResult SetAllLegionsTech(int value)
    {
        var legion = _modeManager.GetModifier<LegionModifier>();
        if (legion == null) return ScriptOperationResult.Fail("军团修改器不可用");
        var result = legion.SetAllLegionsTech(value);
        return ToScriptResult(result, $"已设置科技={value}");
    }

    public ScriptOperationResult AddLegion()
    {
        var legion = _modeManager.GetModifier<LegionModifier>();
        if (legion == null) return ScriptOperationResult.Fail("军团修改器不可用");
        var result = legion.AddLegion();
        return ToScriptResult(result, "已追加军团");
    }

    public ScriptOperationResult RemoveLegionAt(int index)
    {
        var legion = _modeManager.GetModifier<LegionModifier>();
        if (legion == null) return ScriptOperationResult.Fail("军团修改器不可用");
        var result = legion.RemoveLegionAt(index);
        return ToScriptResult(result, $"已删除索引 {index} 的军团");
    }

    public ScriptOperationResult SetLegionCampAt(int index, int camp)
    {
        var legion = _modeManager.GetModifier<LegionModifier>();
        if (legion == null) return ScriptOperationResult.Fail("军团修改器不可用");
        var result = legion.SetLegionCampAt(index, camp);
        return ToScriptResult(result, $"已设置索引 {index} 军团阵营 = {camp}");
    }

    public ScriptOperationResult SetLegionDefeatConditionAt(int index, int value)
    {
        var legion = _modeManager.GetModifier<LegionModifier>();
        if (legion == null) return ScriptOperationResult.Fail("军团修改器不可用");
        var result = legion.SetLegionDefeatConditionAt(index, value);
        return ToScriptResult(result, $"已设置索引 {index} 军团战败条件 = {value}");
    }

    // ---------- 地形修改 ----------

    public ScriptOperationResult SetTerrain(int col, int row, int type)
    {
        if (!IsValidCoord(col, row)) return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");
        var terrain = _modeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return ScriptOperationResult.Fail("地形修改器不可用");
        var brushInfo = new TerrainBrushInfo
        {
            Layer = terrain.EditLayer,
            TerrainType = type,
            Decoration = 0
        };
        var result = terrain.Apply(col, row, brushInfo);
        return ToScriptResult(result, $"已设置地形 ({col},{row})={type}");
    }

    public ScriptOperationResult ChangeTerrainType(int col, int row, int delta)
    {
        if (!IsValidCoord(col, row)) return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");
        var terrain = _modeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return ScriptOperationResult.Fail("地形修改器不可用");
        var result = terrain.ChangeTerrainType(col, row, delta);
        return ToScriptResult(result, "已切换地形类型");
    }

    public ScriptOperationResult ChangeDecoration(int col, int row, int delta)
    {
        if (!IsValidCoord(col, row)) return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");
        var terrain = _modeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return ScriptOperationResult.Fail("地形修改器不可用");
        var result = terrain.ChangeDecoration(col, row, delta);
        return ToScriptResult(result, "已切换变体");
    }

    public ScriptOperationResult SetRiver(int col, int row, int value)
    {
        if (!IsValidCoord(col, row)) return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");
        var terrain = _modeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return ScriptOperationResult.Fail("地形修改器不可用");
        var result = terrain.SetRiverValue(col, row, (byte)Math.Clamp(value, 0, 63));
        return ToScriptResult(result, $"已设置河流值={value}");
    }

    public ScriptOperationResult CopyTerrain(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");
        var terrain = _modeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return ScriptOperationResult.Fail("地形修改器不可用");
        var result = terrain.CopyTerrain(col, row);
        return ToScriptResult(result, "已复制地形");
    }

    public ScriptOperationResult PasteTerrain(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");
        var terrain = _modeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return ScriptOperationResult.Fail("地形修改器不可用");
        var result = terrain.PasteTerrain(col, row);
        return ToScriptResult(result, "已粘贴地形");
    }

    public ScriptOperationResult FloodFillTerrain(int col, int row, int type)
    {
        if (!IsValidCoord(col, row)) return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");
        var terrain = _modeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return ScriptOperationResult.Fail("地形修改器不可用");
        var result = terrain.FloodFill(col, row, (byte)type);
        return ToScriptResult(result, "洪水填充完成");
    }

    public ScriptOperationResult Greening(int probability)
    {
        var terrain = _modeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return ScriptOperationResult.Fail("地形修改器不可用");
        var result = terrain.ApplyGreening(Math.Clamp(probability, 0, 100));
        return ToScriptResult(result, "绿化完成");
    }

    public ScriptOperationResult RandomizeFlatTerrain(int probability)
    {
        var terrain = _modeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return ScriptOperationResult.Fail("地形修改器不可用");
        var result = terrain.RandomizeFlatTerrain(Math.Clamp(probability, 0, 100));
        return ToScriptResult(result, "随机平地变体完成");
    }

    public ScriptOperationResult RandomizeVariant(int probability)
    {
        var terrain = _modeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return ScriptOperationResult.Fail("地形修改器不可用");
        var result = terrain.RandomizeVariant(Math.Clamp(probability, 0, 100));
        return ToScriptResult(result, "随机变体完成");
    }

    public ScriptOperationResult CreateCoast()
    {
        var terrain = _modeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return ScriptOperationResult.Fail("地形修改器不可用");
        var result = terrain.CreateCoast();
        return ToScriptResult(result, "海岸线创建完成");
    }

    /// <summary>
    /// 截图：真正的渲染在 GUI 场景层（Rendering 程序集），这里只发请求并整理消息。
    /// AI 就是靠它"看"地图的，所以返回消息要把"图像 ↔ 格子"的换算讲清楚。
    /// </summary>
    public ScriptOperationResult CaptureScreenshot(
        string outputPath,
        int centerCol = -1,
        int centerRow = -1,
        int cellsW = 0,
        int cellsH = 0,
        int maxPixels = 1400,
        bool showHexLabels = true,
        bool showGridLines = false,
        bool sceneMode = false)
    {
        var request = new Services.MapScreenshotRequest
        {
            CenterCol = centerCol,
            CenterRow = centerRow,
            CellsW = Math.Max(0, cellsW),
            CellsH = Math.Max(0, cellsH),
            MaxPixels = maxPixels <= 0 ? 1400 : Math.Min(maxPixels, 4096),
            OutputPath = string.IsNullOrWhiteSpace(outputPath) ? null : outputPath,
            ShowHexLabels = showHexLabels,
            ShowGridLines = showGridLines,
            MarkCenter = centerCol >= 0 && centerRow >= 0,
            SceneMode = sceneMode,
        };

        var result = _modeManager.CaptureScreenshot(request);
        if (!result.Ok) return ScriptOperationResult.Fail(result.Message);

        return ScriptOperationResult.Ok(
            $"截图已保存: {result.Path}（{result.PixelWidth}×{result.PixelHeight} 像素）　{result.Message}", 1);
    }

    public ScriptOperationResult ProcessOceanSecondLayer()
    {
        var terrain = _modeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return ScriptOperationResult.Fail("地形修改器不可用");
        var result = terrain.ProcessOceanSecondLayer();
        return ToScriptResult(result, "海洋第二层处理完成");
    }

    public ScriptOperationResult ExportHd()
    {
        var terrain = _modeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return ScriptOperationResult.Fail("地形修改器不可用");
        var result = terrain.ExportHdFile();
        return ToScriptResult(result, "HD文件导出完成");
    }

    public ScriptOperationResult ConnectBuildings()
    {
        var terrain = _modeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return ScriptOperationResult.Fail("地形修改器不可用");
        var result = terrain.ConnectBuildings();
        return ToScriptResult(result, "建筑连接完成");
    }

    // ---------- 单位（对象读写） ----------

    /// <summary>
    /// 单位对象上允许脚本改写的字段白名单。
    /// <para>末尾几个是 v3 专有字段：v1 结构里没有，写在 v1 地图上会由
    /// <see cref="TryWriteField{T}"/> 明确拒绝，而不是静默丢掉。</para>
    /// </summary>
    private static readonly string[] ArmyWritableFields =
    {
        "UnitType", "Level", "Organization", "Direction", "Mobility", "BuiltRound",
        "Experience", "HealthBonus", "CurrentHealth", "MaxHealth", "General", "Rank",
        "KeyPoint", "Policy", "Plan", "Morale", "CanAttack", "LegionId",
        "HpLevel", "TransportShip", "Hatred", "AttackTarget", "DeathDialogue",
        "LevelMarkDisplay", "AntiDefenseMode",
    };

    public IReadOnlyList<string> GetArmyFields() => ArmyWritableFields;

    public void ForEachArmy(Func<ScriptArmyInfo, bool> visitor)
    {
        if (visitor == null) return;

        // 遍历副本：回调里可能增删单位
        if (_mapData.Header.BtlVersion >= 3)
        {
            foreach (var army in _mapData.ArmiesV3.ToList())
                if (!visitor(ToInfo(army))) return;
        }
        else
        {
            foreach (var army in _mapData.Armies.ToList())
                if (!visitor(ToInfo(army))) return;
        }
    }

    public ScriptArmyInfo? GetArmy(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;

        if (_mapData.Header.BtlVersion >= 3)
        {
            int idx = _mapData.FindArmyV3Index(col, row);
            return idx >= 0 && idx < _mapData.ArmiesV3.Count ? ToInfo(_mapData.ArmiesV3[idx]) : null;
        }

        int target = row * _mapData.MapWidth + col;
        for (int i = 0; i < _mapData.Armies.Count; i++)
        {
            if (_mapData.Armies[i].Coordinate == target) return ToInfo(_mapData.Armies[i]);
        }
        return null;
    }

    public ScriptOperationResult SetArmyField(int col, int row, string field, double value)
    {
        if (!IsValidCoord(col, row))
            return ScriptOperationResult.Fail($"坐标 ({col},{row}) 超出地图范围");

        string? matched = MatchField(ArmyWritableFields, field);
        if (matched == null)
            return ScriptOperationResult.Fail(
                $"未知单位字段 '{field}'；可用字段：" + string.Join(", ", ArmyWritableFields));

        // v3 与 v1 的表不同（ArmiesV3 / Armies），写回各自的 UpdateArmy
        if (_mapData.Header.BtlVersion >= 3)
        {
            var modifier = _modeManager.GetModifier<ArmyV3Modifier>();
            if (modifier == null) return ScriptOperationResult.Fail("v3单位修改器不可用");

            int idx = _mapData.FindArmyV3Index(col, row);
            if (idx < 0) return ScriptOperationResult.Fail($"({col},{row}) 没有单位");

            if (!TryWriteField(_mapData.ArmiesV3[idx], matched, value, out Army_3 updated, out string err))
                return ScriptOperationResult.Fail(err);

            var result = modifier.UpdateArmy(updated);
            return result.Success
                ? ScriptOperationResult.Ok(ArmyFieldMessage(col, row, matched, value), 1)
                : ScriptOperationResult.Fail(result.Message ?? "修改单位失败");
        }

        var v1Modifier = _modeManager.GetModifier<ArmyModifier>();
        if (v1Modifier == null) return ScriptOperationResult.Fail("单位修改器不可用");

        int v1Idx = -1;
        int target = row * _mapData.MapWidth + col;
        for (int i = 0; i < _mapData.Armies.Count; i++)
        {
            if (_mapData.Armies[i].Coordinate == target) { v1Idx = i; break; }
        }
        if (v1Idx < 0) return ScriptOperationResult.Fail($"({col},{row}) 没有单位");

        if (!TryWriteField(_mapData.Armies[v1Idx], matched, value, out Army v1Updated, out string err1))
            return ScriptOperationResult.Fail(err1);

        var r1 = v1Modifier.UpdateArmy(v1Updated);
        return r1.Success
            ? ScriptOperationResult.Ok(ArmyFieldMessage(col, row, matched, value), 1)
            : ScriptOperationResult.Fail(r1.Message ?? "修改单位失败");
    }

    private static string ArmyFieldMessage(int col, int row, string field, double value)
        => $"已设置单位 ({col},{row}) 的 {field} = {(int)Math.Round(value)}";

    /// <summary>
    /// 兵种中文名。v1 的 <see cref="Army"/> 自带 <c>GetUnitTypeName()</c>，
    /// v3 的 <see cref="Army_3"/> 没有，所以抽成共用函数（枚举语义两版一致）。
    /// </summary>
    private static string GetUnitTypeName(int unitType) => unitType switch
    {
        0 => "步兵",
        1 => "骑兵",
        2 => "弓箭手",
        4 => "海军",
        5 => "空军",
        _ => $"未知({unitType})"
    };

    // ---------- 军团（对象读写） ----------

    /// <summary>
    /// 军团对象上允许脚本改写的字段白名单。
    /// <para>
    /// 刻意<b>不含</b> <c>CountryId</c> / <c>ActionId</c>：这两个是军团定位键，
    /// 改它们必须走带校验的 <c>SetLegionCountryIdAt</c> / <c>SetLegionActionIdAt</c>，
    /// 否则会出现"按国家找不到军团"或重复国家 ID（BTL 校验器会报）。
    /// </para>
    /// </summary>
    private static readonly string[] LegionWritableFields =
    {
        "InitialEconomy", "InitialIndustry", "InitialTech", "IsPlayerControlled",
        "Camp", "DefeatCondition", "CountryHpRate", "CountryTaxRate", "ColorR", "ColorG", "ColorB",
        "AtomicBombCount", "HydrogenBombCount", "NeutronBombCount", "AntimatterBombCount",
        "InitialTechLevel", "CountryAiBehavior",
        "MobilityLevel", "RifleLevel", "CamouflageLevel", "EngineerLevel", "GrenadeLevel",
        "MortarLevel", "MarchLevel", "BulletproofVestLevel", "ArmorLevel", "MainGunLevel",
        "VehicleBodyLevel", "EngineLevel", "MachineGunLevel", "RaidLevel", "VehicleAirDefenseLevel",
        "ReinforcedBodyLevel", "ArtilleryLevel", "RocketLevel", "TowingLevel", "ArtilleryArmorLevel",
        "FirepowerLevel", "NavyRocketLevel", "DisguiseLevel", "HullLevel", "PropulsionLevel",
        "NavyArmorLevel", "WeaponLevel", "NavalGunLevel", "TorpedoLevel", "MinesweepingLevel",
        "ShipAirDefenseLevel", "ModernHullLevel", "AviationFuelLevel", "AviationEngineLevel",
        "AviationBombLevel", "AirRaidLevel", "BombingLevel", "StrategicBombingLevel", "AirdropLevel",
        "JetEngineLevel", "BunkerLevel", "FortressGunLevel", "CoastalGunLevel", "RocketLauncherLevel",
        "FortificationLevel", "AntiAircraftGunLevel", "AntiAircraftCannonLevel",
        "AntiAircraftMissileLevel", "RadarLevel", "MissileWarheadLevel", "SolidRocketEngineLevel",
        "NuclearBombBreakthroughLevel", "NuclearFusionLevel", "InitialLaserGunLevel",
    };

    public IReadOnlyList<string> GetLegionFields() => LegionWritableFields;

    public ScriptLegionInfo? GetLegionAt(int index)
        => index >= 0 && index < _mapData.Legions.Count ? ToInfo(_mapData.Legions[index], index) : null;

    public void ForEachLegion(Func<ScriptLegionInfo, bool> visitor)
    {
        if (visitor == null) return;

        var snapshot = _mapData.Legions.ToList();
        for (int i = 0; i < snapshot.Count; i++)
        {
            if (!visitor(ToInfo(snapshot[i], i))) return;
        }
    }

    public ScriptOperationResult SetLegionField(int index, string field, double value)
    {
        if (index < 0 || index >= _mapData.Legions.Count)
            return ScriptOperationResult.Fail(
                $"军团索引 {index} 超出范围（当前共 {_mapData.Legions.Count} 个）");

        string? matched = MatchField(LegionWritableFields, field);
        if (matched == null)
            return ScriptOperationResult.Fail(
                $"未知军团字段 '{field}'；可用字段：" + string.Join(", ", LegionWritableFields));

        var modifier = _modeManager.GetModifier<LegionModifier>();
        if (modifier == null) return ScriptOperationResult.Fail("军团修改器不可用");

        if (!TryWriteField(_mapData.Legions[index], matched, value, out Legion updated, out string err))
            return ScriptOperationResult.Fail(err);

        var result = modifier.UpdateLegionAt(index, updated);
        return result.Success
            ? ScriptOperationResult.Ok($"已设置军团[{index}] 的 {matched} = {(int)Math.Round(value)}", 1)
            : ScriptOperationResult.Fail(result.Message ?? "修改军团失败");
    }

    // ---------- 内部辅助 ----------

    /// <summary>在白名单里按名匹配字段（大小写不敏感），找不到返回 null。</summary>
    private static string? MatchField(IReadOnlyList<string> whitelist, string? field)
    {
        string key = (field ?? "").Trim();
        foreach (var f in whitelist)
        {
            if (string.Equals(f, key, StringComparison.OrdinalIgnoreCase)) return f;
        }
        return null;
    }

    /// <summary>
    /// 用反射把数值写进结构体的某个字段，按字段类型钳位（byte 0-255、short、ushort、int、float）。
    /// <para>
    /// 这么写而不是逐字段赋值，是因为建筑/单位/军团的字段量太大（军团光科技等级就几十个）：
    /// 以后结构里加字段，只需在白名单补个名字，脚本层立刻可用。
    /// </para>
    /// </summary>
    private static bool TryWriteField<T>(T src, string field, double value, out T updated, out string error)
        where T : struct
    {
        updated = src;

        var f = typeof(T).GetField(field, BindingFlags.Public | BindingFlags.Instance);
        if (f == null)
        {
            error = $"字段 '{field}' 在 {typeof(T).Name} 上不存在（该版本的数据结构没有它）";
            return false;
        }

        object boxed = src;
        if (f.FieldType == typeof(byte))
            f.SetValue(boxed, (byte)Math.Clamp((int)Math.Round(value), 0, 255));
        else if (f.FieldType == typeof(short))
            f.SetValue(boxed, (short)Math.Clamp((int)Math.Round(value), short.MinValue, short.MaxValue));
        else if (f.FieldType == typeof(ushort))
            f.SetValue(boxed, (ushort)Math.Clamp((int)Math.Round(value), 0, 0xFFFF));
        else if (f.FieldType == typeof(int))
            f.SetValue(boxed, (int)Math.Round(value));
        else if (f.FieldType == typeof(float))
            f.SetValue(boxed, (float)value);
        else
        {
            error = $"字段 '{field}' 类型 {f.FieldType.Name} 暂不支持脚本写入";
            return false;
        }

        updated = (T)boxed;
        error = "";
        return true;
    }

    private ScriptArmyInfo ToInfo(Army army)
    {
        int col = _mapData.MapWidth > 0 ? army.Coordinate % _mapData.MapWidth : 0;
        int row = _mapData.MapWidth > 0 ? army.Coordinate / _mapData.MapWidth : 0;

        return new ScriptArmyInfo
        {
            Col = col,
            Row = row,
            Index = army.Coordinate,
            Version = 1,
            UnitType = army.UnitType,
            UnitTypeName = army.GetUnitTypeName(),
            Level = army.Level,
            Organization = army.Organization,
            Direction = army.Direction,
            DirectionName = army.GetDirectionName(),
            Mobility = army.Mobility,
            BuiltRound = army.BuiltRound,
            Experience = army.Experience,
            HealthBonus = army.HealthBonus,
            CurrentHealth = army.CurrentHealth,
            MaxHealth = army.MaxHealth,
            General = army.General,
            Rank = army.Rank,
            KeyPoint = army.KeyPoint,
            KeyPointName = army.GetKeyPointName(),
            Policy = army.Policy,
            PolicyName = army.GetPolicyName(),
            Plan = army.Plan,
            Morale = army.Morale,
            MoraleName = army.GetMoraleName(),
            CanAttack = army.CanAttack,
            LegionId = army.LegionId
        };
    }

    private ScriptArmyInfo ToInfo(Army_3 army)
    {
        int col = _mapData.MapWidth > 0 ? army.Coordinate % _mapData.MapWidth : 0;
        int row = _mapData.MapWidth > 0 ? army.Coordinate / _mapData.MapWidth : 0;

        return new ScriptArmyInfo
        {
            Col = col,
            Row = row,
            Index = army.Coordinate,
            Version = 3,
            UnitType = army.UnitType,
            UnitTypeName = GetUnitTypeName(army.UnitType),   // Army_3 没有 GetUnitTypeName()
            Level = army.Level,
            Organization = army.Organization,
            Direction = army.Direction,
            DirectionName = army.GetDirectionName(),
            Mobility = army.Mobility,
            BuiltRound = army.BuiltRound,
            Experience = army.Experience,
            HealthBonus = army.HealthBonus,
            CurrentHealth = army.CurrentHealth,
            MaxHealth = army.MaxHealth,
            General = army.General,
            Rank = army.Rank,
            KeyPoint = army.KeyPoint,
            KeyPointName = army.GetKeyPointName(),
            Policy = army.Policy,
            PolicyName = army.GetPolicyName(),
            Plan = army.Plan,
            Morale = army.Morale,
            MoraleName = army.GetMoraleName(),
            CanAttack = 0,
            LegionId = army.LegionId,
            HpLevel = army.HpLevel,
            TransportShip = army.TransportShip,
            Hatred = army.Hatred,
            AttackTarget = army.AttackTarget,
            DeathDialogue = army.DeathDialogue,
            LevelMarkDisplay = army.LevelMarkDisplay,
            AntiDefenseMode = army.AntiDefenseMode
        };
    }

    private static ScriptLegionInfo ToInfo(Legion legion, int index) => new()
    {
        Index = index,
        ActionId = legion.ActionId,
        CountryId = legion.CountryId,
        InitialEconomy = legion.InitialEconomy,
        InitialIndustry = legion.InitialIndustry,
        InitialTech = legion.InitialTech,
        IsPlayerControlled = legion.IsPlayerControlled,
        IsPlayer = legion.IsPlayer,
        Camp = legion.Camp,
        DefeatCondition = legion.DefeatCondition,
        CountryHpRate = legion.CountryHpRate,
        CountryTaxRate = legion.CountryTaxRate,
        ColorR = legion.ColorR,
        ColorG = legion.ColorG,
        ColorB = legion.ColorB,
        AtomicBombCount = legion.AtomicBombCount,
        HydrogenBombCount = legion.HydrogenBombCount,
        NeutronBombCount = legion.NeutronBombCount,
        AntimatterBombCount = legion.AntimatterBombCount,
        InitialTechLevel = legion.InitialTechLevel,
        CountryAiBehavior = legion.CountryAiBehavior,
        AverageArmyLevel = legion.GetAverageArmyLevel(),
        AverageNavyLevel = legion.GetAverageNavyLevel(),
        AverageAirForceLevel = legion.GetAverageAirForceLevel(),
        AverageDefenseLevel = legion.GetAverageDefenseLevel(),

        MobilityLevel = legion.MobilityLevel,
        RifleLevel = legion.RifleLevel,
        CamouflageLevel = legion.CamouflageLevel,
        EngineerLevel = legion.EngineerLevel,
        GrenadeLevel = legion.GrenadeLevel,
        MortarLevel = legion.MortarLevel,
        MarchLevel = legion.MarchLevel,
        BulletproofVestLevel = legion.BulletproofVestLevel,
        ArmorLevel = legion.ArmorLevel,
        MainGunLevel = legion.MainGunLevel,
        VehicleBodyLevel = legion.VehicleBodyLevel,
        EngineLevel = legion.EngineLevel,
        MachineGunLevel = legion.MachineGunLevel,
        RaidLevel = legion.RaidLevel,
        VehicleAirDefenseLevel = legion.VehicleAirDefenseLevel,
        ReinforcedBodyLevel = legion.ReinforcedBodyLevel,
        ArtilleryLevel = legion.ArtilleryLevel,
        RocketLevel = legion.RocketLevel,
        TowingLevel = legion.TowingLevel,
        ArtilleryArmorLevel = legion.ArtilleryArmorLevel,
        FirepowerLevel = legion.FirepowerLevel,
        NavyRocketLevel = legion.NavyRocketLevel,
        DisguiseLevel = legion.DisguiseLevel,
        HullLevel = legion.HullLevel,
        PropulsionLevel = legion.PropulsionLevel,
        NavyArmorLevel = legion.NavyArmorLevel,
        WeaponLevel = legion.WeaponLevel,
        NavalGunLevel = legion.NavalGunLevel,
        TorpedoLevel = legion.TorpedoLevel,
        MinesweepingLevel = legion.MinesweepingLevel,
        ShipAirDefenseLevel = legion.ShipAirDefenseLevel,
        ModernHullLevel = legion.ModernHullLevel,
        AviationFuelLevel = legion.AviationFuelLevel,
        AviationEngineLevel = legion.AviationEngineLevel,
        AviationBombLevel = legion.AviationBombLevel,
        AirRaidLevel = legion.AirRaidLevel,
        BombingLevel = legion.BombingLevel,
        StrategicBombingLevel = legion.StrategicBombingLevel,
        AirdropLevel = legion.AirdropLevel,
        JetEngineLevel = legion.JetEngineLevel,
        BunkerLevel = legion.BunkerLevel,
        FortressGunLevel = legion.FortressGunLevel,
        CoastalGunLevel = legion.CoastalGunLevel,
        RocketLauncherLevel = legion.RocketLauncherLevel,
        FortificationLevel = legion.FortificationLevel,
        AntiAircraftGunLevel = legion.AntiAircraftGunLevel,
        AntiAircraftCannonLevel = legion.AntiAircraftCannonLevel,
        AntiAircraftMissileLevel = legion.AntiAircraftMissileLevel,
        RadarLevel = legion.RadarLevel,
        MissileWarheadLevel = legion.MissileWarheadLevel,
        SolidRocketEngineLevel = legion.SolidRocketEngineLevel,
        NuclearBombBreakthroughLevel = legion.NuclearBombBreakthroughLevel,
        NuclearFusionLevel = legion.NuclearFusionLevel,
        InitialLaserGunLevel = legion.InitialLaserGunLevel
    };

    private ModifierResult RunArmyOp(int col, int row,
        Func<ArmyModifier, ModifierResult> v1Op,
        Func<ArmyV3Modifier, ModifierResult> v3Op)
    {
        if (_mapData.Header.BtlVersion >= 3)
        {
            var v3 = new ArmyV3Modifier();
            v3.Initialize(_mapData);
            return v3Op(v3);
        }
        var v1 = _modeManager.GetModifier<ArmyModifier>();
        return v1 == null ? ModifierResult.Fail("单位修改器不可用") : v1Op(v1);
    }

    private static ScriptOperationResult ToScriptResult(ModifierResult result, string defaultMsg)
        => result.Success
            ? ScriptOperationResult.Ok(result.Message ?? defaultMsg, 1)
            : ScriptOperationResult.Fail(result.Message ?? "操作失败");

    // ---------- 内部 ----------

    private bool IsValidCoord(int col, int row)
        => col >= 0 && row >= 0 && col < _mapData.MapWidth && row < _mapData.MapHeight;

    private ScriptBuildingInfo ToInfo(Building building)
    {
        int col = _mapData.MapWidth > 0 ? building.Coordinate % _mapData.MapWidth : 0;
        int row = _mapData.MapWidth > 0 ? building.Coordinate / _mapData.MapWidth : 0;

        int nameId = building.Name;
        bool hasName = nameId != 0 && nameId != BuildingNameResolver.NoName;

        return new ScriptBuildingInfo
        {
            Col = col,
            Row = row,
            Index = building.Coordinate,
            BuildingType = building.BuildingType,
            BuildingTypeName = building.GetBuildingTypeName(),
            NameId = nameId,
            Name = hasName ? GetCityName(nameId) : "",
            HasName = hasName,
            Belong = _mapData.GetBelongValue(col, row),

            Appearance = building.Appearance,
            LandmarkBuilding = building.LandmarkBuilding,
            DecorativeBuilding = building.DecorativeBuilding,
            SkillUnlock = building.SkillUnlock,
            RewardCount = building.RewardCount,
            HatredValue = building.HatredValue,
            KeyPoint = building.KeyPoint,
            KeyPointName = building.GetKeyPointName(),
            OccupationEvent = building.OccupationEvent,
            FireIgnition = building.FireIgnition,
            FireDuration = building.FireDuration,
            AirDefenseWeapon = building.AirDefenseWeapon,
            AirDefenseRadar = building.AirDefenseRadar,
            AirDefenseRadarName = building.GetAirDefenseRadarName(),
            FactoryLevel = building.FactoryLevel,
            ResearchLevel = building.ResearchLevel,
            MedicalLevel = building.MedicalLevel,
            AviationLevel = building.AviationLevel,
            MissileLevel = building.MissileLevel,
            NuclearLevel = building.NuclearLevel
        };
    }

    private static string GetCityName(int nameId)
    {
        try
        {
            var parser = Config.ConfigManager.Instance.GetStringTableParser();
            foreach (var kvp in parser.FindCityNames())
            {
                if (kvp.Key == nameId) return kvp.Value;
            }
        }
        catch
        {
            // 名称表不可用时退化为空字符串，不影响脚本继续跑
        }
        return "";
    }
}