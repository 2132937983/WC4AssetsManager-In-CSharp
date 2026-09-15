using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class ArmyModifier : ModifierBase
{
    public override string Name => "army";
    public override string DisplayName => "单位修改器";

    private Army? _copiedArmy;

    // 多选复制的单位组：key = col * 10000 + row，配合左上角偏移还原相对布局。
    private Dictionary<int, Army>? _copiedArmyGroup;
    private int _copiedArmyGroupMinCol;
    private int _copiedArmyGroupMinRow;

    private readonly Random _random = new();

    #region 基础CRUD

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        short coordIndex = (short)(row * _mapData.MapWidth + col);

        Army army;
        if (parameter is Army a)
        {
            army = a;
            army.Coordinate = coordIndex;
        }
        else
        {
            army = Army.CreateDefault(coordIndex);

            // 放置时该格的归属还没写入（由调用方随后设置），因此用省会归属推断所在军团，
            // 让新单位直接符合军团强度档位，而不是统一的无加成满血。
            int legionId = _mapData.GetProvinceCapitalBelong(col, row);
            if (legionId < 0) legionId = _mapData.GetBelongValue(col, row);

            var strengthByBelong = BuildStrengthConfigByLegion();
            if (strengthByBelong.TryGetValue(legionId, out var strengthCfg))
            {
                ApplyLegionLevelConfig(ref army, strengthCfg);
            }
        }

        int idx = _mapData.FindArmyIndex(col, row);
        if (idx >= 0)
            _mapData.ReplaceArmy(idx, army);
        else
            _mapData.Armies.Add(army);

        MarkModified();
        return ModifierResult.Ok($"已放置单位 ({col}, {row})");
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int idx = _mapData.FindArmyIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有单位");

        _mapData.RemoveArmyAt(idx);
        MarkModified();
        return ModifierResult.Ok($"已删除单位 ({col}, {row})");
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);

    public override bool CanRemove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return false;
        return _mapData?.FindArmyIndex(col, row) >= 0;
    }

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        int idx = _mapData!.FindArmyIndex(col, row);
        return idx >= 0 ? _mapData.Armies[idx] : null;
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        if (!IsValidCoord(col, row) || _mapData == null) return false;
        if (data is not Army army) return false;

        army.Coordinate = (short)(row * _mapData.MapWidth + col);
        int idx = _mapData.FindArmyIndex(col, row);
        if (idx >= 0)
            _mapData.ReplaceArmy(idx, army);
        else
            _mapData.Armies.Add(army);

        MarkModified();
        return true;
    }

    #endregion

    #region 复制/粘贴

    public ModifierResult CopyArmy(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        int idx = _mapData!.FindArmyIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有单位");
        _copiedArmy = _mapData.Armies[idx];
        // 单选复制会覆盖掉多选缓冲区，与建筑模式 CopyBuilding 的行为一致。
        _copiedArmyGroup = null;
        return ModifierResult.Ok("已复制单位数据");
    }

    public ModifierResult PasteArmy(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_copiedArmy == null) return ModifierResult.Fail("没有已复制的单位数据");
        return Apply(col, row, _copiedArmy);
    }

    /// <summary>
    /// 多选复制：把选区中所有存在单位的格子记录为「相对布局」，返回复制到的单位数量。
    /// </summary>
    public int CopyArmyGroup(IReadOnlySet<HexCoord> selectedHexes)
    {
        if (_mapData == null) return 0;

        _copiedArmyGroup = new Dictionary<int, Army>();
        _copiedArmyGroupMinCol = int.MaxValue;
        _copiedArmyGroupMinRow = int.MaxValue;

        foreach (var coord in selectedHexes)
        {
            int idx = _mapData.FindArmyIndex(coord.Col, coord.Row);
            if (idx < 0) continue;

            var army = _mapData.Armies[idx];
            _copiedArmyGroupMinCol = Math.Min(_copiedArmyGroupMinCol, coord.Col);
            _copiedArmyGroupMinRow = Math.Min(_copiedArmyGroupMinRow, coord.Row);
            _copiedArmyGroup[coord.Col * 10000 + coord.Row] = army;
        }

        _copiedArmy = null;
        return _copiedArmyGroup.Count;
    }

    /// <summary>
    /// 多选粘贴：以 (targetCol, targetRow) 作为选区左上角，按原相对布局批量放置。
    /// 返回实际写入的格子坐标，调用方可据此实现撤销。
    /// </summary>
    public List<HexCoord> PasteArmyGroup(int targetCol, int targetRow)
    {
        var pasted = new List<HexCoord>();
        if (_mapData == null) return pasted;
        if (_copiedArmyGroup == null || _copiedArmyGroup.Count == 0) return pasted;

        foreach (var (key, army) in _copiedArmyGroup)
        {
            int relCol = key / 10000 - _copiedArmyGroupMinCol;
            int relRow = key % 10000 - _copiedArmyGroupMinRow;
            int destCol = targetCol + relCol;
            int destRow = targetRow + relRow;

            if (destCol < 0 || destCol >= _mapData.MapWidth ||
                destRow < 0 || destRow >= _mapData.MapHeight)
                continue;

            Apply(destCol, destRow, army);
            pasted.Add(new HexCoord(destCol, destRow));
        }

        return pasted;
    }

    public bool HasCopiedArmyGroup => _copiedArmyGroup != null && _copiedArmyGroup.Count > 0;

    #endregion

    #region 更新/设置

    public ModifierResult UpdateArmy(Army army)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var coord = HexCoord.FromIndex(army.Coordinate, _mapData.MapWidth);
        int idx = _mapData.FindArmyIndex(coord.Col, coord.Row);
        if (idx >= 0)
            _mapData.ReplaceArmy(idx, army);
        else
            _mapData.Armies.Add(army);

        MarkModified();
        return ModifierResult.Ok("已更新单位数据");
    }

    public ModifierResult SetLegionId(int col, int row, int legionId)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int idx = _mapData.FindArmyIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有单位");

        var army = _mapData.Armies[idx];
        army.LegionId = legionId;
        _mapData.ReplaceArmy(idx, army);
        MarkModified();
        return ModifierResult.Ok($"已设置军团ID: {legionId}");
    }

    #endregion

    #region 删除操作

    public ModifierResult DeleteAllArmies()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int count = _mapData.Armies.Count;
        _mapData.Armies.Clear();
        MarkModified();
        return ModifierResult.Ok($"已删除所有单位，共 {count} 个");
    }

    public ModifierResult DeleteArmiesByBelong(int belongValue)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int deletedCount = 0;
        for (int i = _mapData.Armies.Count - 1; i >= 0; i--)
        {
            if (_mapData.Armies[i].LegionId == belongValue)
            {
                _mapData.RemoveArmyAt(i);
                deletedCount++;
            }
        }

        MarkModified();
        return ModifierResult.Ok($"已删除 {deletedCount} 个归属为 {belongValue} 的单位");
    }

    public ModifierResult DeleteArmiesByBelongs(IEnumerable<int> belongValues)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var belongSet = new HashSet<int>(belongValues);
        int deletedCount = 0;
        for (int i = _mapData.Armies.Count - 1; i >= 0; i--)
        {
            if (belongSet.Contains(_mapData.Armies[i].LegionId))
            {
                _mapData.RemoveArmyAt(i);
                deletedCount++;
            }
        }

        MarkModified();
        return ModifierResult.Ok($"已删除 {deletedCount} 个指定归属的单位");
    }

    public ModifierResult EraseArmyBrush(int centerCol, int centerRow, int radius)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int deletedCount = 0;
        for (int rowOffset = -radius; rowOffset <= radius; rowOffset++)
        {
            for (int colOffset = -radius; colOffset <= radius; colOffset++)
            {
                int targetRow = centerRow + rowOffset;
                int targetCol = centerCol + colOffset;

                if (targetRow < 0 || targetRow >= _mapData.MapHeight ||
                    targetCol < 0 || targetCol >= _mapData.MapWidth)
                    continue;

                double distance = Math.Sqrt(rowOffset * rowOffset + colOffset * colOffset);
                if (distance > radius) continue;

                int idx = _mapData.FindArmyIndex(targetCol, targetRow);
                if (idx >= 0)
                {
                    _mapData.RemoveArmyAt(idx);
                    deletedCount++;
                }
            }
        }

        MarkModified();
        return ModifierResult.Ok($"橡皮擦删除了 {deletedCount} 个单位");
    }

    #endregion

    #region 按配置修改军团强度 (E键)

    public ModifierResult ModifyLegionStrength(int belongValue, int levelId)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var cfg = ConfigManager.Instance;
        if (levelId == -1)
        {
            return AutoAdjustLegionStrengthByCountry(belongValue);
        }

        var config = cfg.GetLegionLevelConfig(levelId);
        if (config == null) return ModifierResult.Fail($"未找到强度等级 {levelId} 的配置");

        int modifiedCount = 0;
        for (int i = 0; i < _mapData.Armies.Count; i++)
        {
            var army = _mapData.Armies[i];
            int armyBelongValue = _mapData.GetBelongValueByIndex(army.Coordinate);
            if (belongValue == -1 || armyBelongValue == belongValue)
            {
                ApplyLegionLevelConfig(ref army, config);
                _mapData.ReplaceArmy(i, army);
                modifiedCount++;
            }
        }

        if (belongValue >= 0)
        {
            UpdateLegionAttributes(belongValue, config);
        }
        else
        {
            // Legion 是 struct，必须经 ReplaceLegion 写回；这里用索引遍历，
            // 否则在 foreach 中修改集合会抛 InvalidOperationException。
            for (int i = 0; i < _mapData.Legions.Count; i++)
            {
                var legion = _mapData.Legions[i];
                UpdateLegionAttributesForLegion(ref legion, config);
                _mapData.ReplaceLegion(i, legion);
            }
        }

        MarkModified();
        return ModifierResult.Ok($"已修改 {modifiedCount} 个单位的强度 (等级: {config.Name})");
    }

    public void ApplyLegionLevelConfig(ref Army army, LegionLevelConfig config)
    {
        army.CurrentHealth = (short)config.HpPercent;
        army.MaxHealth = (short)config.HpPercent;

        if (config.ArmyLvMax > 0)
            army.Level = (byte)_random.Next(1, config.ArmyLvMax + 1);

        if (config.ArmyHpBonus is { Count: >= 2 })
        {
            int variation = _random.Next(config.ArmyHpBonus[0], config.ArmyHpBonus[1] + 1);
            army.HealthBonus = (short)(100 + variation);
        }
        else
        {
            army.HealthBonus = 100;
        }

        // HealthBonus 抬高的是实际血量上限，而 CurrentHealth 是绝对值。
        // 若不同步缩放，会出现“上限提高但血量没提高”的血条不满现象。
        army.CurrentHealth = (short)(army.MaxHealth * army.HealthBonus / 100);

        if (config.ArmyNumMax > 0)
        {
            int maxFormation = ConfigManager.Instance.GetMaxFormation(army.UnitType);
            int actualMax = Math.Min(config.ArmyNumMax, maxFormation);
            army.Organization = actualMax > 0 ? (byte)_random.Next(1, actualMax + 1) : (byte)0;
        }

        if (army.General > 0)
            army.Nobility = (byte)_random.Next(config.GeneralHpMin, config.GeneralHpMax + 1);
    }

    public ModifierResult AutoAdjustLegionStrengthByCountry(int targetBelongValue)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var cfg = ConfigManager.Instance;
        var armyConfig = cfg.GetArmyEditConfig();
        var legionConfig = cfg.GetLegionEditConfig();

        var countryStrengthMap = new Dictionary<int, int>();

        foreach (int countryId in legionConfig.LowStrengthCountry)
        {
            if (armyConfig.LowStrengthArmy.Count > 0)
                countryStrengthMap[countryId] = armyConfig.LowStrengthArmy[_random.Next(armyConfig.LowStrengthArmy.Count)];
        }

        foreach (int countryId in legionConfig.MediumStrengthCountry)
        {
            if (armyConfig.MediumStrengthArmy.Count > 0)
                countryStrengthMap[countryId] = armyConfig.MediumStrengthArmy[_random.Next(armyConfig.MediumStrengthArmy.Count)];
        }

        foreach (int countryId in legionConfig.HighStrengthCountry)
        {
            if (armyConfig.HighStrengthArmy.Count > 0)
                countryStrengthMap[countryId] = armyConfig.HighStrengthArmy[_random.Next(armyConfig.HighStrengthArmy.Count)];
        }

        int processedCount = 0;
        int totalModifiedCount = 0;

        // 用索引遍历：循环内要写回 Legion（struct），在 foreach 中修改集合会抛异常
        for (int legionIndex = 0; legionIndex < _mapData.Legions.Count; legionIndex++)
        {
            var legion = _mapData.Legions[legionIndex];
            int actionId = legion.ActionId;
            int countryId = legion.CountryId;

            if (targetBelongValue != -1 && actionId != targetBelongValue)
                continue;

            if (!countryStrengthMap.TryGetValue(countryId, out int strengthLevel)) continue;

            var config = cfg.GetLegionLevelConfig(strengthLevel);
            if (config == null) continue;

            int modifiedCount = 0;
            for (int i = 0; i < _mapData.Armies.Count; i++)
            {
                var army = _mapData.Armies[i];
                int armyBelongValue = _mapData.GetBelongValueByIndex(army.Coordinate);
                if (armyBelongValue == actionId)
                {
                    ApplyLegionLevelConfig(ref army, config);
                    _mapData.ReplaceArmy(i, army);
                    modifiedCount++;
                }
            }

            // 原地写回军团自身属性（Blood/Rate），避免在遍历中修改集合
            UpdateLegionAttributesForLegion(ref legion, config);
            _mapData.ReplaceLegion(legionIndex, legion);

            totalModifiedCount += modifiedCount;
            processedCount++;
        }

        MarkModified();
        return ModifierResult.Ok($"已自动调整 {processedCount} 个军团的强度，共修改 {totalModifiedCount} 个单位");
    }

    private void UpdateLegionAttributes(int actionId, LegionLevelConfig config)
    {
        if (_mapData == null) return;

        for (int i = 0; i < _mapData.Legions.Count; i++)
        {
            var legion = _mapData.Legions[i];
            if (legion.ActionId == actionId)
            {
                UpdateLegionAttributesForLegion(ref legion, config);
                _mapData.ReplaceLegion(i, legion);
                break;
            }
        }
    }

    private void UpdateLegionAttributesForLegion(ref Legion legion, LegionLevelConfig config)
    {
        legion.CountryHpRate = (float)config.Blood;
        legion.CountryTaxRate = (float)config.Rate;
    }

    #endregion

    #region 随机化军团单位 (R键)

    public ModifierResult RandomizeLegionArmies(int belongValue)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var armyConfig = ConfigManager.Instance.GetArmyEditConfig();
        var armyOnLand = armyConfig.ArmyOnLand;
        var armyOnSea = armyConfig.ArmyOnSea;

        int modifiedCount = 0;
        for (int i = 0; i < _mapData.Armies.Count; i++)
        {
            var army = _mapData.Armies[i];
            int armyBelongValue = _mapData.GetBelongValueByIndex(army.Coordinate);

            if (belongValue == -1 || armyBelongValue == belongValue)
            {
                int terrainType = GetTerrainTypeAt(army.Coordinate);
                RandomizeArmyData(ref army, terrainType, armyOnLand, armyOnSea);
                _mapData.ReplaceArmy(i, army);
                modifiedCount++;
            }
        }

        MarkModified();
        return ModifierResult.Ok($"已随机化 {modifiedCount} 个单位");
    }

    public void RandomizeArmyData(ref Army army, int terrainType, List<int> armyOnLand, List<int> armyOnSea)
    {
        army.CurrentHealth = (short)_random.Next(50, 101);
        army.MaxHealth = 100;
        army.Level = (byte)_random.Next(1, 6);
        army.HealthBonus = (short)(100 + _random.Next(-10, 21));

        if (terrainType == 1)
        {
            if (armyOnSea.Count > 0)
                army.UnitType = (byte)armyOnSea[_random.Next(armyOnSea.Count)];
        }
        else
        {
            if (armyOnLand.Count > 0)
                army.UnitType = (byte)armyOnLand[_random.Next(armyOnLand.Count)];
        }

        int maxFormation = ConfigManager.Instance.GetMaxFormation(army.UnitType);
        army.Organization = maxFormation > 0 ? (byte)_random.Next(1, maxFormation + 1) : (byte)0;

        if (army.General > 0)
            army.Nobility = (byte)_random.Next(1, 4);
    }

    public void RandomizeArmyData(ref Army army, LegionLevelConfig strengthConfig, int terrainType, List<int> armyOnLand, List<int> armyOnSea)
    {
        army.CurrentHealth = (short)strengthConfig.HpPercent;
        army.MaxHealth = (short)strengthConfig.HpPercent;

        if (strengthConfig.ArmyLvMax > 0)
            army.Level = (byte)_random.Next(1, strengthConfig.ArmyLvMax + 1);

        if (strengthConfig.ArmyHpBonus is { Count: >= 2 })
        {
            int variation = _random.Next(strengthConfig.ArmyHpBonus[0], strengthConfig.ArmyHpBonus[1] + 1);
            army.HealthBonus = (short)(100 + variation);
        }
        else
        {
            army.HealthBonus = 100;
        }

        // 同上：HealthBonus 抬高的是上限，当前血量必须同步缩放，否则血条显示不满
        army.CurrentHealth = (short)(army.MaxHealth * army.HealthBonus / 100);

        if (terrainType == 1)
        {
            if (armyOnSea.Count > 0)
                army.UnitType = (byte)armyOnSea[_random.Next(armyOnSea.Count)];
        }
        else
        {
            if (armyOnLand.Count > 0)
                army.UnitType = (byte)armyOnLand[_random.Next(armyOnLand.Count)];
        }

        if (strengthConfig.ArmyNumMax > 0)
        {
            int maxFormation = ConfigManager.Instance.GetMaxFormation(army.UnitType);
            int actualMax = maxFormation > 0 ? Math.Min(strengthConfig.ArmyNumMax, maxFormation) : 0;
            army.Organization = actualMax > 0 ? (byte)_random.Next(1, actualMax + 1) : (byte)0;
        }

        if (army.General > 0 && strengthConfig.GeneralHpMax > 0)
            army.Nobility = (byte)_random.Next(strengthConfig.GeneralHpMin, strengthConfig.GeneralHpMax + 1);
    }

    private int GetTerrainTypeAt(int hexIndex)
    {
        if (_mapData == null) return 0;
        if (hexIndex < 0 || hexIndex >= _mapData.TerrainCount) return 0;

        int col = hexIndex % _mapData.MapWidth;
        int row = hexIndex / _mapData.MapWidth;
        var terrain = _mapData.GetTerrainAt(col, row);
        return terrain.TileType1;
    }

    #endregion

    /// <summary>
    /// 构建「军团 ActionId → 强度配置」映射，供生成单位时直接套用所在军团的强度档位。
    /// 分档规则与 <see cref="AutoAdjustLegionStrengthByCountry"/> 保持一致：
    /// 军团的国家ID → setting.txt 的强弱分档取一个单位等级ID → LegionLvSetting.json 的具体配置。
    /// </summary>
    private Dictionary<int, LegionLevelConfig> BuildStrengthConfigByLegion()
    {
        var result = new Dictionary<int, LegionLevelConfig>();
        if (_mapData == null) return result;

        var cfg = ConfigManager.Instance;
        var armyConfig = cfg.GetArmyEditConfig();
        var legionConfig = cfg.GetLegionEditConfig();

        var countryLevel = new Dictionary<int, int>();

        foreach (int countryId in legionConfig.LowStrengthCountry)
        {
            if (armyConfig.LowStrengthArmy.Count > 0)
                countryLevel[countryId] = armyConfig.LowStrengthArmy[_random.Next(armyConfig.LowStrengthArmy.Count)];
        }

        foreach (int countryId in legionConfig.MediumStrengthCountry)
        {
            if (armyConfig.MediumStrengthArmy.Count > 0)
                countryLevel[countryId] = armyConfig.MediumStrengthArmy[_random.Next(armyConfig.MediumStrengthArmy.Count)];
        }

        foreach (int countryId in legionConfig.HighStrengthCountry)
        {
            if (armyConfig.HighStrengthArmy.Count > 0)
                countryLevel[countryId] = armyConfig.HighStrengthArmy[_random.Next(armyConfig.HighStrengthArmy.Count)];
        }

        foreach (var legion in _mapData.Legions)
        {
            if (!countryLevel.TryGetValue(legion.CountryId, out int levelId)) continue;
            var config = cfg.GetLegionLevelConfig(levelId);
            if (config != null) result[legion.ActionId] = config;
        }

        return result;
    }

    #region 按概率生成单位 (I键)

    public ModifierResult GenerateArmiesByProbability(int belongValue, int probability)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var cfg = ConfigManager.Instance;
        var armyConfig = cfg.GetArmyEditConfig();
        var armyOnLand = armyConfig.ArmyOnLand;
        var armyOnSea = armyConfig.ArmyOnSea;

        var existingCoords = new HashSet<int>();
        foreach (var army in _mapData.Armies)
            existingCoords.Add(army.Coordinate);
        foreach (var army in _mapData.ArmiesV3)
            existingCoords.Add(army.Coordinate);

        var validProvinces = GetValidProvinces(belongValue);

        // 预构建军团强度映射：新单位直接按所在军团的强度档位生成
        var strengthByBelong = BuildStrengthConfigByLegion();

        int newArmyCount = 0;
        int totalCells = _mapData.MapWidth * _mapData.MapHeight;

        for (int hexIndex = 0; hexIndex < totalCells; hexIndex++)
        {
            if (existingCoords.Contains(hexIndex)) continue;

            var provinceValue = GetProvinceValue(hexIndex);
            if (provinceValue == null || provinceValue.Value == 0xFFFF) continue;
            if (!validProvinces.Contains(provinceValue.Value)) continue;

            if (_random.Next(1, 101) > probability) continue;

            int col = hexIndex % _mapData.MapWidth;
            int row = hexIndex / _mapData.MapWidth;
            int terrainType = GetTerrainTypeAt(hexIndex);

            int unitType;
            if (terrainType == 1)
            {
                if (armyOnSea.Count == 0) continue;
                unitType = armyOnSea[_random.Next(armyOnSea.Count)];
            }
            else
            {
                if (armyOnLand.Count == 0) continue;
                unitType = armyOnLand[_random.Next(armyOnLand.Count)];
            }

            int maxFormation = cfg.GetMaxFormation(unitType);
            int organization = maxFormation > 0 ? _random.Next(1, maxFormation + 1) : _random.Next(1, 5);

            int actualBelongValue = belongValue;
            if (actualBelongValue == -1)
                actualBelongValue = _mapData.GetBelongValueByIndex(provinceValue.Value);

            if (actualBelongValue >= 0)
                _mapData.SetBelongValueByIndex(hexIndex, actualBelongValue);

            if (_mapData.Header.BtlVersion >= 3)
            {
                var army3 = Army_3.CreateDefault(hexIndex);
                army3.UnitType = (byte)unitType;
                army3.Level = (byte)_random.Next(1, 6);
                army3.Organization = (byte)organization;
                army3.CurrentHealth = (short)_random.Next(50, 101);
                army3.MaxHealth = (short)_random.Next(100, 201);
                army3.Direction = (byte)_random.Next(0, 2);
                army3.Mobility = (byte)_random.Next(3, 11);
                army3.HealthBonus = 100;
                army3.Plan = 1;
                army3.Morale = 100;
                army3.SkillLevel1 = 1;
                army3.SkillLevel2 = 1;
                army3.SkillLevel3 = 1;
                army3.SkillLevel4 = 1;
                army3.SkillLevel5 = 1;
                army3.LevelMarkDisplay = 1;
                if (actualBelongValue >= 0)
                    army3.LegionId = actualBelongValue;

                // 保证血条为满：MaxHealth 是上限，CurrentHealth 必须同步
                army3.CurrentHealth = army3.MaxHealth;
                _mapData.ArmiesV3.Add(army3);
            }
            else
            {
                var army = Army.CreateDefault(hexIndex);
                army.UnitType = (byte)unitType;
                army.Level = (byte)_random.Next(1, 6);
                army.Organization = (byte)organization;
                army.Direction = (byte)_random.Next(0, 2);
                army.Mobility = (byte)_random.Next(3, 11);
                army.Plan = 1;
                army.SkillLevel1 = 1;
                army.SkillLevel2 = 1;
                army.SkillLevel3 = 1;
                army.SkillLevel4 = 1;
                army.SkillLevel5 = 1;
                army.CanAttack = 1;
                if (actualBelongValue >= 0)
                    army.LegionId = actualBelongValue;

                // 直接套用所在军团的强度档位（血量/等级/编制），新单位生成即为满血；
                // 找不到军团配置时兜底为满血，避免随机血量导致血条不满。
                if (strengthByBelong.TryGetValue(actualBelongValue, out var strengthCfg))
                {
                    ApplyLegionLevelConfig(ref army, strengthCfg);
                }
                else
                {
                    army.HealthBonus = 100;
                    army.CurrentHealth = army.MaxHealth;
                }

                _mapData.Armies.Add(army);
            }

            newArmyCount++;
        }

        MarkModified();
        return ModifierResult.Ok($"生成了 {newArmyCount} 个新单位");
    }

    private HashSet<int> GetValidProvinces(int belongValue)
    {
        if (_mapData == null) return [];

        var allProvinces = new HashSet<int>();
        int totalCells = _mapData.MapWidth * _mapData.MapHeight;

        for (int i = 0; i < totalCells; i++)
        {
            var pv = GetProvinceValue(i);
            if (pv.HasValue && pv.Value != 0xFFFF)
                allProvinces.Add(pv.Value);
        }

        if (belongValue == -1)
            return allProvinces;

        var validProvinces = new HashSet<int>();
        foreach (int province in allProvinces)
        {
            int provinceBelong = _mapData.GetBelongValueByIndex(province);
            if (provinceBelong == belongValue)
                validProvinces.Add(province);
        }

        return validProvinces;
    }

    private int? GetProvinceValue(int hexIndex)
    {
        if (_mapData == null) return null;
        if (hexIndex < 0 || hexIndex >= _mapData.TerrainCount) return null;

        int col = hexIndex % _mapData.MapWidth;
        int row = hexIndex / _mapData.MapWidth;
        var province = _mapData.GetProvinceRef(col, row);
        return province.ProvinceValue == 0xFFFF ? null : (int?)province.ProvinceValue;
    }

    #endregion

    #region 自动分配将领 (G键)

    public ModifierResult AutoAssignGeneralsToArmies(int targetBelongValue, int requestedGeneralCount, bool clearExisting, bool adaptToUnits)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_mapData.Armies.Count == 0) return ModifierResult.Fail("没有单位需要分配将领");
        if (_mapData.Legions.Count == 0) return ModifierResult.Fail("军团数据未加载，无法分配将领");

        var cfg = ConfigManager.Instance;

        var belongToCountryId = new Dictionary<int, int>();
        foreach (var legion in _mapData.Legions)
        {
            if (legion.ActionId >= 0 && legion.CountryId >= 0)
                belongToCountryId[legion.ActionId] = legion.CountryId;
        }

        var targetArmies = new List<Army>();
        for (int i = 0; i < _mapData.Armies.Count; i++)
        {
            var army = _mapData.Armies[i];
            int armyBelongValue = _mapData.GetBelongValueByIndex(army.Coordinate);
            if (army.LegionId > 0) armyBelongValue = army.LegionId;

            if (targetBelongValue != -1 && armyBelongValue != targetBelongValue) continue;

            if (clearExisting)
            {
                targetArmies.Add(army);
            }
            else
            {
                if (army.General == 0)
                    targetArmies.Add(army);
            }
        }

        if (targetArmies.Count == 0)
            return ModifierResult.Fail($"没有找到归属值为 {targetBelongValue} 的单位");

        if (clearExisting)
        {
            for (int i = 0; i < _mapData.Armies.Count; i++)
            {
                var army = _mapData.Armies[i];
                if (army.General > 0)
                {
                    cfg.RemoveAssignedGeneral(army.General);
                    army.General = 0;
                    army.Nobility = 0;
                    army.SkillLevel1 = 1;
                    army.SkillLevel2 = 1;
                    army.SkillLevel3 = 1;
                    army.SkillLevel4 = 1;
                    army.SkillLevel5 = 1;
                    _mapData.ReplaceArmy(i, army);
                }
            }
        }

        var armiesByBelong = new Dictionary<int, List<Army>>();
        foreach (var army in targetArmies)
        {
            int armyBelongValue = _mapData.GetBelongValueByIndex(army.Coordinate);
            if (army.LegionId > 0) armyBelongValue = army.LegionId;

            if (!armiesByBelong.ContainsKey(armyBelongValue))
                armiesByBelong[armyBelongValue] = new List<Army>();
            armiesByBelong[armyBelongValue].Add(army);
        }

        int totalAssignedCount = 0;

        foreach (var belongPair in armiesByBelong)
        {
            int belongVal = belongPair.Key;
            var belongArmies = belongPair.Value;

            if (!belongToCountryId.TryGetValue(belongVal, out int countryId)) continue;

            var availableGenerals = cfg.GetGeneralsByCountryId(countryId);
            availableGenerals = cfg.GetAvailableGenerals(availableGenerals);

            if (availableGenerals.Count == 0) continue;

            int actualGeneralCount = requestedGeneralCount;
            if (actualGeneralCount == -1)
                actualGeneralCount = Math.Min(belongArmies.Count, availableGenerals.Count);
            else
                actualGeneralCount = Math.Min(actualGeneralCount, Math.Min(availableGenerals.Count, belongArmies.Count));

            if (actualGeneralCount == 0) continue;

            int assignedCount;
            if (adaptToUnits)
            {
                var armyGroups = GroupArmiesBySpecialty(belongArmies);
                assignedCount = 0;
                assignedCount += AssignGeneralsToArmiesByUnitType(availableGenerals, armyGroups.Infantry, "Infantry", actualGeneralCount - assignedCount);
                assignedCount += AssignGeneralsToArmiesByUnitType(availableGenerals, armyGroups.Armor, "Armor", actualGeneralCount - assignedCount);
                assignedCount += AssignGeneralsToArmiesByUnitType(availableGenerals, armyGroups.Artillery, "Artillery", actualGeneralCount - assignedCount);
                assignedCount += AssignGeneralsToArmiesByUnitType(availableGenerals, armyGroups.Navy, "Navy", actualGeneralCount - assignedCount);
                assignedCount += AssignGeneralsToArmiesByUnitType(availableGenerals, armyGroups.AirForce, "AirForce", actualGeneralCount - assignedCount);
            }
            else
            {
                assignedCount = AssignGeneralsToArmiesRandomly(availableGenerals, belongArmies, actualGeneralCount);
            }

            totalAssignedCount += assignedCount;
        }

        MarkModified();
        return ModifierResult.Ok($"已成功分配 {totalAssignedCount} 个将领到单位");
    }

    private ArmyGroups GroupArmiesBySpecialty(List<Army> armies)
    {
        var groups = new ArmyGroups();
        var cfg = ConfigManager.Instance;

        foreach (var army in armies)
        {
            var specialty = cfg.GetUnitSpecialtyType(army.UnitType);
            if (specialty == null) continue;

            switch (specialty)
            {
                case "Infantry": groups.Infantry.Add(army); break;
                case "Armor": groups.Armor.Add(army); break;
                case "Artillery": groups.Artillery.Add(army); break;
                case "Navy": groups.Navy.Add(army); break;
                case "AirForce": groups.AirForce.Add(army); break;
            }
        }

        return groups;
    }

    private int AssignGeneralsToArmiesByUnitType(List<int> availableGenerals, List<Army> armies, string specialtyType, int maxCount)
    {
        if (armies.Count == 0 || maxCount <= 0) return 0;

        var cfg = ConfigManager.Instance;
        var matchingGenerals = availableGenerals.Where(g => cfg.GetGeneralSpecialty(g) == specialtyType).ToList();
        if (matchingGenerals.Count == 0) return 0;

        int assignedCount = 0;
        var availableArmies = armies.ToList();
        var shuffledGenerals = matchingGenerals.OrderBy(_ => _random.Next()).ToList();

        foreach (int generalId in shuffledGenerals)
        {
            if (availableArmies.Count == 0 || assignedCount >= maxCount) break;

            int index = _random.Next(availableArmies.Count);
            var army = availableArmies[index];
            availableArmies.RemoveAt(index);

            var generalSettings = cfg.GetGeneralSettingsById(generalId);
            army.General = (short)generalId;

            if (generalSettings != null)
            {
                army.Nobility = (byte)generalSettings.MilitaryRank;
                ApplyGeneralSkills(ref army, generalSettings);
            }

            cfg.AddAssignedGeneral(generalId);

            int armyIdx = _mapData!.FindArmyIndex(
                HexCoord.FromIndex(army.Coordinate, _mapData.MapWidth).Col,
                HexCoord.FromIndex(army.Coordinate, _mapData.MapWidth).Row);
            if (armyIdx >= 0)
                _mapData.ReplaceArmy(armyIdx, army);

            assignedCount++;
        }

        return assignedCount;
    }

    private int AssignGeneralsToArmiesRandomly(List<int> availableGenerals, List<Army> armies, int maxCount)
    {
        if (armies.Count == 0 || maxCount <= 0) return 0;

        var cfg = ConfigManager.Instance;
        int assignedCount = 0;
        var availableArmies = armies.ToList();
        var shuffledGenerals = availableGenerals.OrderBy(_ => _random.Next()).ToList();

        foreach (int generalId in shuffledGenerals)
        {
            if (availableArmies.Count == 0 || assignedCount >= maxCount) break;

            var generalSpecialty = cfg.GetGeneralSpecialty(generalId);
            var matchingArmies = availableArmies.Where(a => cfg.GetUnitSpecialtyType(a.UnitType) == generalSpecialty).ToList();

            Army selectedArmy;
            if (matchingArmies.Count > 0)
            {
                int index = _random.Next(matchingArmies.Count);
                selectedArmy = matchingArmies[index];
                availableArmies.Remove(selectedArmy);
            }
            else
            {
                int index = _random.Next(availableArmies.Count);
                selectedArmy = availableArmies[index];
                availableArmies.RemoveAt(index);
            }

            var generalSettings = cfg.GetGeneralSettingsById(generalId);
            selectedArmy.General = (short)generalId;

            if (generalSettings != null)
            {
                selectedArmy.Nobility = (byte)generalSettings.MilitaryRank;
                ApplyGeneralSkills(ref selectedArmy, generalSettings);
            }

            cfg.AddAssignedGeneral(generalId);

            int armyIdx = _mapData!.FindArmyIndex(
                HexCoord.FromIndex(selectedArmy.Coordinate, _mapData.MapWidth).Col,
                HexCoord.FromIndex(selectedArmy.Coordinate, _mapData.MapWidth).Row);
            if (armyIdx >= 0)
                _mapData.ReplaceArmy(armyIdx, selectedArmy);

            assignedCount++;
        }

        return assignedCount;
    }

    private static void ApplyGeneralSkills(ref Army army, GeneralSettings settings)
    {
        if (settings.Skills == null) return;

        for (int i = 0; i <= Math.Min(settings.Skills.Count - 1, 4); i++)
        {
            int skillId = settings.Skills[i];
            int skillLevel = skillId % 10;
            if (skillLevel == 0) skillLevel = 1;

            switch (i)
            {
                case 0: army.SkillLevel1 = (byte)skillLevel; break;
                case 1: army.SkillLevel2 = (byte)skillLevel; break;
                case 2: army.SkillLevel3 = (byte)skillLevel; break;
                case 3: army.SkillLevel4 = (byte)skillLevel; break;
                case 4: army.SkillLevel5 = (byte)skillLevel; break;
            }
        }
    }

    private class ArmyGroups
    {
        public List<Army> Infantry { get; } = [];
        public List<Army> Armor { get; } = [];
        public List<Army> Artillery { get; } = [];
        public List<Army> Navy { get; } = [];
        public List<Army> AirForce { get; } = [];
    }

    #endregion

    #region 方案管理 (T键/Y键)

    public ModifierResult QuickModifyArmyPlan(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int idx = _mapData.FindArmyIndex(col, row);
        if (idx < 0) return ModifierResult.Fail("该位置没有单位");

        var army = _mapData.Armies[idx];
        int oldPlan = army.Plan;
        army.Plan = (short)((oldPlan + 1) % 5);
        _mapData.ReplaceArmy(idx, army);

        MarkModified();
        return ModifierResult.Ok($"已修改单位方案: {oldPlan} -> {army.Plan}");
    }

    public ModifierResult QuickModifyArmyPlanForSelected(IEnumerable<Army> selectedArmies)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int modifiedCount = 0;
        foreach (var selected in selectedArmies)
        {
            int idx = _mapData.FindArmyIndex(
                HexCoord.FromIndex(selected.Coordinate, _mapData.MapWidth).Col,
                HexCoord.FromIndex(selected.Coordinate, _mapData.MapWidth).Row);
            if (idx < 0) continue;

            var army = _mapData.Armies[idx];
            army.Plan = (short)((army.Plan + 1) % 5);
            _mapData.ReplaceArmy(idx, army);
            modifiedCount++;
        }

        MarkModified();
        return ModifierResult.Ok($"已批量修改 {modifiedCount} 个单位的方案");
    }

    public ModifierResult BatchModifyArmyPlanByLegion(int belongValue, int planValue)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int modifiedCount = 0;

        for (int i = 0; i < _mapData.Armies.Count; i++)
        {
            var army = _mapData.Armies[i];
            if (belongValue != -1 && army.LegionId != belongValue) continue;

            army.Plan = planValue == -1 ? (short)_random.Next(1, 5) : (short)planValue;
            _mapData.ReplaceArmy(i, army);
            modifiedCount++;
        }

        for (int i = 0; i < _mapData.ArmiesV3.Count; i++)
        {
            var army3 = _mapData.ArmiesV3[i];
            if (belongValue != -1 && army3.LegionId != belongValue) continue;

            army3.Plan = planValue == -1 ? (short)_random.Next(1, 5) : (short)planValue;
            _mapData.ReplaceArmyV3(i, army3);
            modifiedCount++;
        }

        MarkModified();
        return ModifierResult.Ok($"已修改 {modifiedCount} 个单位的方案 (归属={belongValue}, 方案={planValue})");
    }

    public ModifierResult BatchUpdateArmyPlans(int targetLevel, int targetHpPercent)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int modifiedCount = 0;
        for (int i = 0; i < _mapData.Armies.Count; i++)
        {
            var army = _mapData.Armies[i];
            army.Level = (byte)Math.Clamp(targetLevel, 1, 255);
            army.CurrentHealth = (short)Math.Clamp(targetHpPercent, 0, 100);
            army.MaxHealth = 100;
            _mapData.ReplaceArmy(i, army);
            modifiedCount++;
        }

        MarkModified();
        return ModifierResult.Ok($"已更新 {modifiedCount} 个单位的方案");
    }

    #endregion
}