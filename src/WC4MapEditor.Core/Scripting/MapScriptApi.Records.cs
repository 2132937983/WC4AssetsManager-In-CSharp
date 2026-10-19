using System.Collections.ObjectModel;
using System.Reflection;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Scripting;

namespace WC4MapEditor.Core.Scripting;

// MapScriptApi 的记录列表部分：事件 / 方案 / 天气 / 战略建设 / 空袭 / 空中支援 / 援军。
// 每种记录由一个 ScriptRecordKind<T> 描述"读 / 新增 / 改 / 删"怎么转发到对应 Modifier，
// 脚本因此走与编辑窗口、控制台命令同一套校验与撤销快照，不会绕过常规编辑路径。
public sealed partial class MapScriptApi
{
    public int GetRecordCount(string kind) => KindOf(kind)?.Count ?? 0;

    public ScriptRecord? GetRecord(string kind, int index) => KindOf(kind)?.Get(index);

    public IReadOnlyList<string> GetRecordFields(string kind) => KindOf(kind)?.WritableFields ?? Array.Empty<string>();

    public ScriptOperationResult AddRecord(string kind, int col = -1, int row = -1)
        => WithKind(kind, k => k.Add(col, row));

    public ScriptOperationResult SetRecordField(string kind, int index, string field, double value)
        => WithKind(kind, k => k.SetField(index, field, value));

    public ScriptOperationResult RemoveRecord(string kind, int index)
        => WithKind(kind, k => k.Remove(index));

    public ScriptOperationResult RemoveAllRecords(string kind)
        => WithKind(kind, k => k.RemoveAll());

    private ScriptOperationResult WithKind(string kind, Func<IScriptRecordKind, ScriptOperationResult> op)
    {
        var recordKind = KindOf(kind);
        return recordKind == null
            ? ScriptOperationResult.Fail(Loc.F("Script.Record.UnknownKind", kind))
            : op(recordKind);
    }

    /// <summary>按种类名取记录描述；种类名不区分大小写，未知返回 null。</summary>
    private IScriptRecordKind? KindOf(string? kind)
    {
        switch ((kind ?? "").Trim().ToLowerInvariant())
        {
            case ScriptRecordKinds.Event:
            {
                var module = ModuleOf<EventModifier>();
                return new ScriptRecordKind<MapEvent>(_mapData.Events, [],
                    (_, _) => module.CreateEvent(),
                    module.UpdateEvent, module.DeleteEvent, module.DeleteAllEvents,
                    e => [new("TriggerConditionName", e.GetTriggerConditionName()), new("TriggerEventName", e.GetTriggerEventName())]);
            }
            case ScriptRecordKinds.Case:
            {
                var module = ModuleOf<MapCaseModifier>();
                return new ScriptRecordKind<MapCase>(_mapData.Cases, [],
                    (_, _) => module.AddCase(module.CreateCase()),
                    module.UpdateCase, module.DeleteCase, module.DeleteAllCases);
            }
            case ScriptRecordKinds.Weather:
            {
                var module = ModuleOf<WeatherModifier>();
                return new ScriptRecordKind<Weather>(_mapData.Weathers, [],
                    (_, _) => module.AddWeather(module.CreateNextWeather()),
                    module.UpdateWeather, module.DeleteWeather, module.DeleteAllWeathers,
                    w => [new("WeatherTypeName", w.GetWeatherTypeName())]);
            }
            case ScriptRecordKinds.Strategic:
            {
                var module = ModuleOf<StrategicConstructionModifier>();
                return new ScriptRecordKind<StrategicConstruction>(_mapData.StrategyConstructions, [],
                    (_, _) => module.AddConstruction(module.CreateDefaultConstruction()),
                    module.UpdateConstruction, module.DeleteConstruction, module.DeleteAllConstructions,
                    s => [new("ConstructionName", StrategicConstructionModifier.DescribeCode(s.ConstructionCode))]);
            }
            case ScriptRecordKinds.AirRaid:
            {
                var module = ModuleOf<AirForceModifier>();
                return new ScriptRecordKind<AirForce>(_mapData.AirForces, [],
                    (col, row) => module.Apply(col, row),
                    module.UpdateAirForce, module.DeleteAirForce, module.DeleteAllAirForces,
                    a => [new("UnitTypeName", a.GetUnitTypeName()), new("AmmoTypeName", a.GetAmmoTypeName())]);
            }
            case ScriptRecordKinds.AirSupport:
            {
                var module = ModuleOf<AirForceModifier>();
                return new ScriptRecordKind<AirSupport>(_mapData.AirSupports, [],
                    (_, _) => module.AddAirSupport(AirSupport.CreateDefault(module.DefaultLegion())),
                    module.UpdateAirSupport, module.DeleteAirSupport, module.DeleteAllAirSupports,
                    a => [new("AirForceTypeName", a.GetAirForceTypeName()), new("AmmoTypeName", a.GetAmmoTypeName())]);
            }
            case ScriptRecordKinds.Reinforcement:
                return _mapData.Header.UsesV3Records
                    ? ReinforcementKind(ModuleOf<ReinforcementV3Modifier>(), _mapData.ReinforcementsV3)
                    : ReinforcementKind(ModuleOf<ReinforcementModifier>(), _mapData.Reinforcements);
            default:
                return null;
        }
    }

    /// <summary>援军按"格子组"编辑：改某条时要换算成（格子，组内序号）；Coordinate 由模块按格子改写，不开放直接改。</summary>
    private IScriptRecordKind ReinforcementKind<T>(ReinforcementModifierBase<T> module, ObservableCollection<T> list)
        where T : struct, IReinforcementData
    {
        return new ScriptRecordKind<T>(list, [nameof(IReinforcementData.Coordinate)],
            (col, row) =>
            {
                int indexInHex = module.AddNew(col, row);
                return indexInHex < 0
                    ? ModifierResult.Fail(Loc.F("Script.OutOfMap", col, row))
                    : ModifierResult.Ok(Loc.F("Module.Reinforcement.Placed", col, row, indexInHex + 1));
            },
            (index, record) =>
            {
                var cell = HexCoord.FromIndex(list[index].Coordinate, _mapData.MapWidth);
                int indexInHex = 0;
                for (int i = 0; i < index; i++)
                {
                    if (list[i].Coordinate == list[index].Coordinate) indexInHex++;
                }
                return module.UpdateReinforcementAt(cell.Col, cell.Row, indexInHex, record);
            },
            module.DeleteReinforcement, module.DeleteAllReinforcements);
    }

    /// <summary>取模块：优先用 EditModeManager 里为本地图初始化过的实例，否则临时建一个绑定到本地图的。</summary>
    private T ModuleOf<T>() where T : ModifierBase, new()
    {
        var registered = _modeManager.GetModifier<T>();
        if (registered != null && registered.IsInitializedFor(_mapData)) return registered;

        var fresh = new T();
        fresh.Initialize(_mapData);
        return fresh;
    }
}

/// <summary>一种记录列表在脚本层的读写入口（见 <see cref="MapScriptApi"/> 的记录列表部分）。</summary>
internal interface IScriptRecordKind
{
    int Count { get; }
    IReadOnlyList<string> WritableFields { get; }
    ScriptRecord? Get(int index);
    ScriptOperationResult Add(int col, int row);
    ScriptOperationResult SetField(int index, string field, double value);
    ScriptOperationResult Remove(int index);
    ScriptOperationResult RemoveAll();
}

/// <summary>
/// 结构体记录列表的通用实现：字段名按反射取（与数据结构一一对应，以后加字段脚本层自动可用），
/// 写值按字段宽度钳位后交给模块的 Update 方法校验并落盘。
/// </summary>
internal sealed class ScriptRecordKind<T> : IScriptRecordKind where T : struct
{
    private static readonly FieldInfo[] Reflected = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance);

    private readonly IList<T> _list;
    private readonly string[] _writable;
    private readonly Func<int, int, ModifierResult> _add;
    private readonly Func<int, T, ModifierResult> _update;
    private readonly Func<int, ModifierResult> _remove;
    private readonly Func<ModifierResult> _removeAll;
    private readonly Func<T, KeyValuePair<string, string>[]>? _labels;

    public ScriptRecordKind(IList<T> list, string[] readOnlyFields,
        Func<int, int, ModifierResult> add, Func<int, T, ModifierResult> update,
        Func<int, ModifierResult> remove, Func<ModifierResult> removeAll,
        Func<T, KeyValuePair<string, string>[]>? labels = null)
    {
        _list = list;
        _writable = Reflected.Select(f => f.Name).Where(n => !readOnlyFields.Contains(n)).ToArray();
        _add = add;
        _update = update;
        _remove = remove;
        _removeAll = removeAll;
        _labels = labels;
    }

    public int Count => _list.Count;

    public IReadOnlyList<string> WritableFields => _writable;

    public ScriptRecord? Get(int index)
    {
        if ((uint)index >= (uint)_list.Count) return null;

        object boxed = _list[index];
        return new ScriptRecord
        {
            Index = index,
            Values = Reflected.Select(f => new KeyValuePair<string, double>(f.Name, Convert.ToDouble(f.GetValue(boxed)))).ToArray(),
            Labels = _labels?.Invoke(_list[index]) ?? Array.Empty<KeyValuePair<string, string>>()
        };
    }

    public ScriptOperationResult Add(int col, int row) => ToResult(_add(col, row));

    public ScriptOperationResult SetField(int index, string field, double value)
    {
        if ((uint)index >= (uint)_list.Count)
            return ScriptOperationResult.Fail(Loc.F("Script.Record.IndexOutOfRange", index, _list.Count));

        string? matched = MapScriptApi.MatchField(_writable, field);
        if (matched == null)
            return ScriptOperationResult.Fail(Loc.F("Script.Record.UnknownField", field, string.Join(", ", _writable)));

        if (!MapScriptApi.TryWriteField(_list[index], matched, value, out T updated, out string error))
            return ScriptOperationResult.Fail(error);

        var result = _update(index, updated);
        return result.Success
            ? ScriptOperationResult.Ok(Loc.F("Script.Record.FieldSet", index, matched, (long)Math.Round(value)), 1)
            : ScriptOperationResult.Fail(result.Message ?? Loc.T("Script.OperationFailed", "操作失败"));
    }

    public ScriptOperationResult Remove(int index)
    {
        if ((uint)index >= (uint)_list.Count)
            return ScriptOperationResult.Fail(Loc.F("Script.Record.IndexOutOfRange", index, _list.Count));
        return ToResult(_remove(index));
    }

    public ScriptOperationResult RemoveAll() => ToResult(_removeAll());

    private static ScriptOperationResult ToResult(ModifierResult result)
        => result.Success
            ? ScriptOperationResult.Ok(result.Message ?? "", Math.Max(result.AffectedCount, 1))
            : ScriptOperationResult.Fail(result.Message ?? Loc.T("Script.OperationFailed", "操作失败"));
}
