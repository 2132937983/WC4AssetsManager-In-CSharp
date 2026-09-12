using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class AirForceModifier : ModifierBase
{
    public override string Name => "airforce";
    public override string DisplayName => "空军修改器";

    private AirForce? _copiedAirForce;
    private AirSupport? _copiedAirSupport;
    private readonly Random _random = new();

    private static readonly int[] DefaultAirForceTypes = [0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B, 0x1E];
    private static readonly int[] DefaultAmmoTypes = [0, 0x1D, 0x1E, 0x1F, 0x20];

    #region 基础CRUD

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        AirForce airForce;
        if (parameter is AirForce af)
            airForce = af;
        else
        {
            airForce = AirForce.CreateDefault();
            airForce.Coordinate = row * _mapData.MapWidth + col;
        }

        _mapData.AirForces.Add(airForce);
        MarkModified();
        return ModifierResult.Ok($"已添加空军: {airForce.GetUnitTypeName()}");
    }

    public override ModifierResult Remove(int col, int row)
    {
        return ModifierResult.Fail("空军不支持按坐标删除，请使用DeleteAirForce");
    }

    public override bool CanApply(int col, int row) => _mapData != null;
    public override bool CanRemove(int col, int row) => false;

    public override object? GetDataAt(int col, int row) => null;
    public override bool SetDataAt(int col, int row, object data) => false;

    #endregion

    #region 空军CRUD

    public ModifierResult AddAirForce(AirForce airForce)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        _mapData.AirForces.Add(airForce);
        MarkModified();
        return ModifierResult.Ok($"已添加空军: {airForce.GetUnitTypeName()}");
    }

    public ModifierResult UpdateAirForce(int index, AirForce airForce)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.AirForces.Count) return ModifierResult.Fail("空军索引超出范围");

        _mapData.AirForces[index] = airForce;
        MarkModified();
        return ModifierResult.Ok($"已更新空军: {airForce.GetUnitTypeName()}");
    }

    public ModifierResult DeleteAirForce(int index)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.AirForces.Count) return ModifierResult.Fail("空军索引超出范围");

        _mapData.AirForces.RemoveAt(index);
        MarkModified();
        return ModifierResult.Ok("已删除空军数据");
    }

    public ModifierResult DeleteAllAirForces()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int count = _mapData.AirForces.Count;
        _mapData.AirForces.Clear();
        MarkModified();
        return ModifierResult.Ok($"已删除所有空军数据，共 {count} 个");
    }

    #endregion

    #region 空中支援CRUD

    public ModifierResult AddAirSupport(AirSupport airSupport)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        _mapData.AirSupports.Add(airSupport);
        MarkModified();
        return ModifierResult.Ok($"已添加空中支援: {airSupport.GetAirForceTypeName()}");
    }

    public ModifierResult UpdateAirSupport(int index, AirSupport airSupport)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.AirSupports.Count) return ModifierResult.Fail("空中支援索引超出范围");

        _mapData.AirSupports[index] = airSupport;
        MarkModified();
        return ModifierResult.Ok($"已更新空中支援: {airSupport.GetAirForceTypeName()}");
    }

    public ModifierResult DeleteAirSupport(int index)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.AirSupports.Count) return ModifierResult.Fail("空中支援索引超出范围");

        _mapData.AirSupports.RemoveAt(index);
        MarkModified();
        return ModifierResult.Ok("已删除空中支援数据");
    }

    public ModifierResult DeleteAllAirSupports()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int count = _mapData.AirSupports.Count;
        _mapData.AirSupports.Clear();
        MarkModified();
        return ModifierResult.Ok($"已删除所有空中支援数据，共 {count} 个");
    }

    #endregion

    #region 复制/粘贴

    public ModifierResult CopyAirForce(int index)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.AirForces.Count) return ModifierResult.Fail("空军索引超出范围");

        _copiedAirForce = _mapData.AirForces[index];
        return ModifierResult.Ok($"已复制空军: {_copiedAirForce.Value.GetUnitTypeName()}");
    }

    public ModifierResult PasteAirForce()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_copiedAirForce == null) return ModifierResult.Fail("没有已复制的空军数据");

        _mapData.AirForces.Add(_copiedAirForce.Value);
        MarkModified();
        return ModifierResult.Ok("已粘贴空军数据");
    }

    public ModifierResult CopyAirSupport(int index)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (index < 0 || index >= _mapData.AirSupports.Count) return ModifierResult.Fail("空中支援索引超出范围");

        _copiedAirSupport = _mapData.AirSupports[index];
        return ModifierResult.Ok($"已复制空中支援: {_copiedAirSupport.Value.GetAirForceTypeName()}");
    }

    public ModifierResult PasteAirSupport()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_copiedAirSupport == null) return ModifierResult.Fail("没有已复制的空中支援数据");

        _mapData.AirSupports.Add(_copiedAirSupport.Value);
        MarkModified();
        return ModifierResult.Ok("已粘贴空中支援数据");
    }

    #endregion

    #region 从建筑生成空军

    public ModifierResult GenerateAirForceFromBuildings()
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        int generatedCount = 0;
        var existingCoords = new HashSet<int>();
        foreach (var af in _mapData.AirForces)
            existingCoords.Add(af.Coordinate);

        foreach (var building in _mapData.Buildings)
        {
            if (existingCoords.Contains(building.Coordinate)) continue;

            int belongValue = _mapData.GetBelongValueByIndex(building.Coordinate);
            if (belongValue == 0xFF || belongValue < 0) continue;

            if (building.AviationLevel <= 0) continue;

            var airForce = AirForce.CreateDefault();
            airForce.Coordinate = building.Coordinate;
            airForce.UnitType = 0x14;
            airForce.AmmoType = 0;
            airForce.OwnerLegion = belongValue;
            airForce.TriggerRound = 0;

            _mapData.AirForces.Add(airForce);
            existingCoords.Add(building.Coordinate);
            generatedCount++;
        }

        if (generatedCount > 0) MarkModified();
        return ModifierResult.Ok($"已从建筑生成 {generatedCount} 个空军");
    }

    #endregion

    #region 随机生成空中支援

    public ModifierResult GenerateRandomAirSupport(int count, int maxRound = 50)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");
        if (_mapData.AirForces.Count == 0) return ModifierResult.Fail("没有空军数据，无法生成空中支援");

        var airForceTypes = GetAvailableAirForceTypes();
        var ammoTypes = GetAvailableAmmoTypes();

        int generatedCount = 0;
        for (int i = 0; i < count; i++)
        {
            var airForce = _mapData.AirForces[_random.Next(_mapData.AirForces.Count)];

            var airSupport = AirSupport.CreateDefault();
            airSupport.AirForceSequence = airForceTypes[_random.Next(airForceTypes.Count)];
            airSupport.AmmoType = ammoTypes[_random.Next(ammoTypes.Count)];
            airSupport.OwnerLegion = airForce.OwnerLegion;
            airSupport.TriggerRound = _random.Next(1, maxRound + 1);

            _mapData.AirSupports.Add(airSupport);
            generatedCount++;
        }

        MarkModified();
        return ModifierResult.Ok($"已随机生成 {generatedCount} 个空中支援");
    }

    public ModifierResult GenerateRandomAirSupportByLegion(int legionValue, int countPerLegion, int minRound, int maxRound)
    {
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        var airForceTypes = GetAvailableAirForceTypes();
        var ammoTypes = GetAvailableAmmoTypes();

        minRound = Math.Max(1, minRound);
        maxRound = Math.Max(1, maxRound);
        if (minRound > maxRound) (minRound, maxRound) = (maxRound, minRound);

        int generatedCount = 0;

        if (legionValue == -1)
        {
            var legionIds = _mapData.Legions.Select(l => l.ActionId).Distinct().ToList();
            if (legionIds.Count == 0) legionIds.Add(0);

            foreach (var legionId in legionIds)
            {
                for (int i = 0; i < countPerLegion; i++)
                {
                    var airSupport = AirSupport.CreateDefault();
                    airSupport.AirForceSequence = airForceTypes[_random.Next(airForceTypes.Count)];
                    airSupport.AmmoType = ammoTypes[_random.Next(ammoTypes.Count)];
                    airSupport.OwnerLegion = legionId;
                    airSupport.TriggerRound = _random.Next(minRound, maxRound + 1);

                    _mapData.AirSupports.Add(airSupport);
                    generatedCount++;
                }
            }
        }
        else
        {
            for (int i = 0; i < countPerLegion; i++)
            {
                var airSupport = AirSupport.CreateDefault();
                airSupport.AirForceSequence = airForceTypes[_random.Next(airForceTypes.Count)];
                airSupport.AmmoType = ammoTypes[_random.Next(ammoTypes.Count)];
                airSupport.OwnerLegion = legionValue;
                airSupport.TriggerRound = _random.Next(minRound, maxRound + 1);

                _mapData.AirSupports.Add(airSupport);
                generatedCount++;
            }
        }

        MarkModified();
        return ModifierResult.Ok($"已随机生成 {generatedCount} 个空中支援 (军团={legionValue}, 回合={minRound}-{maxRound})");
    }

    private List<int> GetAvailableAirForceTypes()
    {
        var types = new List<int>();

        try
        {
            var config = ConfigManager.Instance.GetAirForceEditConfig();
            if (config?.AirForceList?.Count > 0)
                types.AddRange(config.AirForceList.Select(x => x.Value));
        }
        catch { }

        if (types.Count == 0)
            types.AddRange(DefaultAirForceTypes);

        return types;
    }

    private List<int> GetAvailableAmmoTypes()
    {
        var types = new List<int>();

        try
        {
            var config = ConfigManager.Instance.GetAirForceEditConfig();
            if (config?.AirForcePowerList?.Count > 0)
                types.AddRange(config.AirForcePowerList.Select(x => x.Value));
        }
        catch { }

        if (types.Count == 0)
            types.AddRange(DefaultAmmoTypes);

        return types;
    }

    #endregion

    #region 查询

    public IReadOnlyList<AirForce> GetAllAirForces()
    {
        return _mapData?.AirForces ?? [];
    }

    public IReadOnlyList<AirSupport> GetAllAirSupports()
    {
        return _mapData?.AirSupports ?? [];
    }

    public AirForce? GetAirForce(int index)
    {
        if (_mapData == null || index < 0 || index >= _mapData.AirForces.Count) return null;
        return _mapData.AirForces[index];
    }

    public AirSupport? GetAirSupport(int index)
    {
        if (_mapData == null || index < 0 || index >= _mapData.AirSupports.Count) return null;
        return _mapData.AirSupports[index];
    }

    #endregion
}