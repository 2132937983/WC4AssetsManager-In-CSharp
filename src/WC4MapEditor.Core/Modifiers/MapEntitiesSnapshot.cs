using System.Collections.ObjectModel;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// 地图全部实体列表 + 省份 + 归属的值快照（撤销用）。实体都是不含引用类型字段的结构体，数组拷贝即深拷贝。
/// </summary>
internal sealed class MapEntitiesSnapshot
{
    private Building[] _buildings = [];
    private Army[] _armies = [];
    private Army_3[] _armiesV3 = [];
    private Legion[] _legions = [];
    private Trap[] _traps = [];
    private Reinforcement[] _reinforcements = [];
    private Reinforcement_3[] _reinforcementsV3 = [];
    private Capital[] _capitals = [];
    private MapCase[] _cases = [];
    private Weather[] _weathers = [];
    private MapEvent[] _events = [];
    private AirForce[] _airForces = [];
    private UnitPlacement[] _unitPlaces = [];
    private StrategicConstruction[] _strategyConstructions = [];
    private AirSupport[] _airSupports = [];
    private Province[] _provinces = [];
    private byte[] _belongs = [];

    public static MapEntitiesSnapshot Capture(MapData map)
    {
        int total = map.MapWidth * map.MapHeight;
        var provinces = new Province[total];
        for (int i = 0; i < total; i++) provinces[i] = map.GetProvinceRef(i);

        int belongCount = map.Belongs?.Count ?? 0;
        var belongs = new byte[belongCount];
        for (int i = 0; i < belongCount; i++) belongs[i] = (byte)map.GetBelongValueByIndex(i);

        return new MapEntitiesSnapshot
        {
            _buildings = map.Buildings.ToArray(),
            _armies = map.Armies.ToArray(),
            _armiesV3 = map.ArmiesV3.ToArray(),
            _legions = map.Legions.ToArray(),
            _traps = map.Traps.ToArray(),
            _reinforcements = map.Reinforcements.ToArray(),
            _reinforcementsV3 = map.ReinforcementsV3.ToArray(),
            _capitals = map.Capitals.ToArray(),
            _cases = map.Cases.ToArray(),
            _weathers = map.Weathers.ToArray(),
            _events = map.Events.ToArray(),
            _airForces = map.AirForces.ToArray(),
            _unitPlaces = map.UnitPlaces.ToArray(),
            _strategyConstructions = map.StrategyConstructions.ToArray(),
            _airSupports = map.AirSupports.ToArray(),
            _provinces = provinces,
            _belongs = belongs,
        };
    }

    public bool SameAs(MapEntitiesSnapshot other)
        => Eq(_buildings, other._buildings)
        && Eq(_armies, other._armies)
        && Eq(_armiesV3, other._armiesV3)
        && Eq(_legions, other._legions)
        && Eq(_traps, other._traps)
        && Eq(_reinforcements, other._reinforcements)
        && Eq(_reinforcementsV3, other._reinforcementsV3)
        && Eq(_capitals, other._capitals)
        && Eq(_cases, other._cases)
        && Eq(_weathers, other._weathers)
        && Eq(_events, other._events)
        && Eq(_airForces, other._airForces)
        && Eq(_unitPlaces, other._unitPlaces)
        && Eq(_strategyConstructions, other._strategyConstructions)
        && Eq(_airSupports, other._airSupports)
        && Eq(_provinces, other._provinces)
        && Eq(_belongs, other._belongs);

    private static bool Eq<T>(T[] a, T[] b)
    {
        if (a.Length != b.Length) return false;
        var cmp = EqualityComparer<T>.Default;
        for (int i = 0; i < a.Length; i++)
            if (!cmp.Equals(a[i], b[i])) return false;
        return true;
    }

    public void Restore(MapData map)
    {
        Fill(map.Buildings, _buildings);
        Fill(map.Armies, _armies);
        Fill(map.ArmiesV3, _armiesV3);
        Fill(map.Legions, _legions);
        Fill(map.Traps, _traps);
        Fill(map.Reinforcements, _reinforcements);
        Fill(map.ReinforcementsV3, _reinforcementsV3);
        Fill(map.Capitals, _capitals);
        Fill(map.Cases, _cases);
        Fill(map.Weathers, _weathers);
        Fill(map.Events, _events);
        Fill(map.AirForces, _airForces);
        Fill(map.UnitPlaces, _unitPlaces);
        Fill(map.StrategyConstructions, _strategyConstructions);
        Fill(map.AirSupports, _airSupports);

        int total = Math.Min(map.MapWidth * map.MapHeight, _provinces.Length);
        for (int i = 0; i < total; i++) map.GetProvinceRef(i) = _provinces[i];
        int belongCount = Math.Min(map.Belongs?.Count ?? 0, _belongs.Length);
        for (int i = 0; i < belongCount; i++) map.SetBelongValueByIndex(i, _belongs[i]);

        map.IsModified = true;
    }

    private static void Fill<T>(ObservableCollection<T> target, T[] items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}
