using MoonSharp.Interpreter;

namespace WC4MapEditor.Scripting;

/// <summary>
/// 暴露给 Lua 的 <c>map</c> 对象。
/// <para>
/// 它只是 <see cref="IMapScriptApi"/> 的薄包装：把 C# 方法转成 Lua 可调用的形式，
/// 并统一处理日志输出与"影响条数"统计。真正的数据操作在 Core 里。
/// </para>
/// <para>
/// 成员名保持 PascalCase 与 C# 一致，脚本里写作
/// <c>map.AddBuilding(10, 20, 11, "北京")</c>、<c>map.RemoveBuildingsByProbability(30, true)</c>。
/// </para>
/// </summary>
[MoonSharpUserData]
public sealed class LuaMapApi
{
    private readonly IMapScriptApi _api;
    private readonly Script _script;
    private readonly Action<string> _log;
    private readonly Action<int> _addAffected;

    internal LuaMapApi(IMapScriptApi api, Script script, Action<string> log, Action<int> addAffected)
    {
        _api = api;
        _script = script;
        _log = log;
        _addAffected = addAffected;
    }

    // ---------- 地图信息 ----------

    /// <summary>地图列数</summary>
    public int Width => _api.MapWidth;

    /// <summary>地图行数</summary>
    public int Height => _api.MapHeight;

    /// <summary>建筑总数</summary>
    public int BuildingCount => _api.BuildingCount;

    /// <summary>格子总数</summary>
    public int TileCount => _api.TileCount;

    // ---------- 建筑 ----------

    /// <summary>
    /// 遍历所有建筑：<c>map.ForEachBuilding(function(b) ... end)</c>。
    /// <para>回调返回 false 可提前结束遍历。</para>
    /// </summary>
    public void ForEachBuilding(DynValue callback)
    {
        if (callback == null || callback.Type != DataType.Function)
        {
            _log("[脚本] ForEachBuilding 需要一个函数作为参数");
            return;
        }

        _api.ForEachBuilding(info =>
        {
            var result = callback.Function.Call(info);
            // 只有显式返回 false 才中断，nil / 无返回值都视为继续
            return !(result.Type == DataType.Boolean && result.Boolean == false);
        });
    }

    /// <summary>取指定格子的建筑，没有则返回 nil</summary>
    public ScriptBuildingInfo? GetBuilding(int col, int row) => _api.GetBuilding(col, row);

    /// <summary>
    /// 在指定格子添加建筑。
    /// <para>name 可以是数字 ID、中文名称，或省略表示无名称。</para>
    /// </summary>
    public ScriptOperationResult AddBuilding(int col, int row, int buildingType, string? name = null)
    {
        var result = _api.AddBuilding(col, row, buildingType, name);
        Report(result);
        return result;
    }

    /// <summary>删除指定格子的建筑</summary>
    public ScriptOperationResult RemoveBuilding(int col, int row)
    {
        var result = _api.RemoveBuilding(col, row);
        Report(result);
        return result;
    }

    /// <summary>
    /// 按概率删除建筑（每个建筑独立掷骰）。
    /// <para>keepNamed 为 true 时保留有名称的建筑（城市/据点）。</para>
    /// </summary>
    public ScriptOperationResult RemoveBuildingsByProbability(int probability, bool keepNamed = false)
    {
        var result = _api.RemoveBuildingsByProbability(probability, keepNamed);
        Report(result);
        return result;
    }

    /// <summary>
    /// 列出建筑对象可写的字段名（给脚本自省用：先看有哪些字段，再 SetBuildingField）。
    /// </summary>
    public IReadOnlyList<string> GetBuildingFields() => _api.GetBuildingFields();

    /// <summary>改建筑的某个属性：字段名大小写不敏感，清单见 <see cref="GetBuildingFields"/></summary>
    public ScriptOperationResult SetBuildingField(int col, int row, string field, double value)
        => Report(_api.SetBuildingField(col, row, field, value));

    /// <summary>改建筑类型（0-255）</summary>
    public ScriptOperationResult SetBuildingType(int col, int row, int buildingType)
        => Report(_api.SetBuildingType(col, row, buildingType));

    /// <summary>改建筑名称（空串 / "-1" = 清除；中文名查不到会新建 stringtable 条目）</summary>
    public ScriptOperationResult SetBuildingName(int col, int row, string? name)
        => Report(_api.SetBuildingName(col, row, name));

    /// <summary>把建筑从一格移到另一格（源格归属一并剪切到目标格）</summary>
    public ScriptOperationResult MoveBuilding(int fromCol, int fromRow, int toCol, int toRow)
        => Report(_api.MoveBuilding(fromCol, fromRow, toCol, toRow));

    // ---------- 归属 / 省区 / 地形 ----------

    /// <summary>取指定格子的归属值（255 = 无归属）</summary>
    public int GetBelong(int col, int row) => _api.GetBelong(col, row);

    /// <summary>设置指定格子的归属值</summary>
    public ScriptOperationResult SetBelong(int col, int row, int belongValue)
    {
        var result = _api.SetBelong(col, row, belongValue);
        Report(result);
        return result;
    }

    /// <summary>取指定格子的省区值（65535 = 无省区）</summary>
    public int GetProvince(int col, int row) => _api.GetProvince(col, row);

    /// <summary>设置指定格子的省区值（0-65535，65535 = 无省区）</summary>
    public ScriptOperationResult SetProvince(int col, int row, int provinceValue)
        => Report(_api.SetProvince(col, row, provinceValue));

    /// <summary>取指定格子的第一层地形类型</summary>
    public int GetTerrain(int col, int row) => _api.GetTerrain(col, row);

    // ---------- 单位 ----------

    /// <summary>单位总数</summary>
    public int ArmyCount => _api.ArmyCount;

    /// <summary>
    /// 遍历所有单位：<c>map.ForEachArmy(function(a) ... end)</c>（v1 / v3 内部自动分派）。
    /// <para>回调返回 false 可提前结束遍历。</para>
    /// </summary>
    public void ForEachArmy(DynValue callback)
    {
        if (callback == null || callback.Type != DataType.Function)
        {
            _log("[脚本] ForEachArmy 需要一个函数作为参数");
            return;
        }

        _api.ForEachArmy(info =>
        {
            var result = callback.Function.Call(info);
            return !(result.Type == DataType.Boolean && result.Boolean == false);
        });
    }

    /// <summary>取指定格子的单位，没有则返回 nil</summary>
    public ScriptArmyInfo? GetArmy(int col, int row) => _api.GetArmy(col, row);

    /// <summary>列出单位可写字段名</summary>
    public IReadOnlyList<string> GetArmyFields() => _api.GetArmyFields();

    /// <summary>按字段名改单位属性（字段名大小写不敏感）</summary>
    public ScriptOperationResult SetArmyField(int col, int row, string field, double value)
        => Report(_api.SetArmyField(col, row, field, value));

    public ScriptOperationResult AddArmy(int col, int row) => Report(_api.AddArmy(col, row));
    public ScriptOperationResult RemoveArmy(int col, int row) => Report(_api.RemoveArmy(col, row));
    public ScriptOperationResult SetArmyLegion(int col, int row, int legionId) => Report(_api.SetArmyLegion(col, row, legionId));
    public ScriptOperationResult SetArmyPlan(int col, int row, int plan) => Report(_api.SetArmyPlan(col, row, plan));
    public ScriptOperationResult CycleArmyPlan(int col, int row) => Report(_api.CycleArmyPlan(col, row));
    public ScriptOperationResult BatchModifyArmyPlanByLegion(int belong, int plan) => Report(_api.BatchModifyArmyPlanByLegion(belong, plan));
    public ScriptOperationResult RemoveArmyByBelong(int belong) => Report(_api.RemoveArmyByBelong(belong));
    public ScriptOperationResult RemoveAllArmies() => Report(_api.RemoveAllArmies());
    public ScriptOperationResult EraseArmyBrush(int col, int row, int radius) => Report(_api.EraseArmyBrush(col, row, radius));
    public ScriptOperationResult ModifyLegionStrength(int belong, int level) => Report(_api.ModifyLegionStrength(belong, level));
    public ScriptOperationResult RandomizeLegionArmies(int belong) => Report(_api.RandomizeLegionArmies(belong));
    public ScriptOperationResult GenerateArmiesByProbability(int belong, int probability) => Report(_api.GenerateArmiesByProbability(belong, probability));
    public ScriptOperationResult AutoAssignGenerals(int belong, int count) => Report(_api.AutoAssignGenerals(belong, count));

    // ---------- 军团 ----------

    /// <summary>军团总数</summary>
    public int LegionCount => _api.LegionCount;

    /// <summary>按列表索引取军团（索引即归属值），越界返回 nil</summary>
    public ScriptLegionInfo? GetLegionAt(int index) => _api.GetLegionAt(index);

    /// <summary>
    /// 遍历所有军团：<c>map.ForEachLegion(function(l) ... end)</c>，回调返回 false 可提前结束。
    /// </summary>
    public void ForEachLegion(DynValue callback)
    {
        if (callback == null || callback.Type != DataType.Function)
        {
            _log("[脚本] ForEachLegion 需要一个函数作为参数");
            return;
        }

        _api.ForEachLegion(info =>
        {
            var result = callback.Function.Call(info);
            return !(result.Type == DataType.Boolean && result.Boolean == false);
        });
    }

    /// <summary>列出军团可写字段名（不含 CountryId / ActionId，那两个走带校验的专用方法）</summary>
    public IReadOnlyList<string> GetLegionFields() => _api.GetLegionFields();

    /// <summary>按字段名改军团属性（按列表索引定位，字段名大小写不敏感）</summary>
    public ScriptOperationResult SetLegionField(int index, string field, double value)
        => Report(_api.SetLegionField(index, field, value));

    public ScriptOperationResult SetLegionColor(int legionId, int r, int g, int b) => Report(_api.SetLegionColor(legionId, r, g, b));
    public ScriptOperationResult SetLegionActionId(int legionId, int actionId) => Report(_api.SetLegionActionId(legionId, actionId));
    public ScriptOperationResult SetLegionCountryIdAt(int index, int countryId) => Report(_api.SetLegionCountryIdAt(index, countryId));
    public ScriptOperationResult SetLegionActionIdAt(int index, int actionId) => Report(_api.SetLegionActionIdAt(index, actionId));
    public int GetLegionCountryIdAt(int index) => _api.GetLegionCountryIdAt(index);
    public ScriptOperationResult SetPlayerLegion(int actionId) => Report(_api.SetPlayerLegion(actionId));
    public ScriptOperationResult SetPlayerLegionAt(int index) => Report(_api.SetPlayerLegionAt(index));
    public ScriptOperationResult MoveLegion(int from, int to, bool renumberActionIds = false) => Report(_api.MoveLegion(from, to, renumberActionIds));
    public ScriptOperationResult ApplyDefaultLegionColors() => Report(_api.ApplyDefaultLegionColors());
    public ScriptOperationResult ApplyLegionColorsFromSettings() => Report(_api.ApplyLegionColorsFromSettings());
    public ScriptOperationResult RandomizeLegionLevels() => Report(_api.RandomizeLegionLevels());
    public ScriptOperationResult RebuildLegionActionBelong() => Report(_api.RebuildLegionActionBelong());
    public ScriptOperationResult SetAllLegionsEconomy(int value) => Report(_api.SetAllLegionsEconomy(value));
    public ScriptOperationResult SetAllLegionsIndustry(int value) => Report(_api.SetAllLegionsIndustry(value));
    public ScriptOperationResult SetAllLegionsTech(int value) => Report(_api.SetAllLegionsTech(value));
    public ScriptOperationResult AddLegion() => Report(_api.AddLegion());
    public ScriptOperationResult RemoveLegionAt(int index) => Report(_api.RemoveLegionAt(index));
    public ScriptOperationResult SetLegionCampAt(int index, int camp) => Report(_api.SetLegionCampAt(index, camp));
    public ScriptOperationResult SetLegionDefeatConditionAt(int index, int value) => Report(_api.SetLegionDefeatConditionAt(index, value));

    // ---------- 记录列表 ----------
    // 事件 / 方案 / 天气 / 战略建设 / 空袭 / 空中支援 / 援军共用一套形状：
    //   map.EventCount                 条数
    //   map.GetEvent(i)                取第 i 条（从 0 起）→ 表：字段名 → 值，另有 Index 与 xxxName 文字标签；越界为 nil
    //   map.GetEventFields()           可写字段名
    //   map.SetEventField(i, 字段, 值)  改字段（先经对应模块校验）
    //   map.AddEvent()                 新增默认记录（空袭 / 援军为 AddAirRaid(col, row) / AddReinforcement(col, row)）
    //   map.RemoveEvent(i) / map.RemoveAllEvents()
    // 其余种类把 Event 换成 Case / Weather / Strategic / AirRaid / AirSupport / Reinforcement（复数同理）。

    // 事件
    public int EventCount => _api.GetRecordCount(ScriptRecordKinds.Event);
    public Table? GetEvent(int index) => ToTable(_api.GetRecord(ScriptRecordKinds.Event, index));
    public IReadOnlyList<string> GetEventFields() => _api.GetRecordFields(ScriptRecordKinds.Event);
    public ScriptOperationResult SetEventField(int index, string field, double value) => Report(_api.SetRecordField(ScriptRecordKinds.Event, index, field, value));
    public ScriptOperationResult AddEvent() => Report(_api.AddRecord(ScriptRecordKinds.Event));
    public ScriptOperationResult RemoveEvent(int index) => Report(_api.RemoveRecord(ScriptRecordKinds.Event, index));
    public ScriptOperationResult RemoveAllEvents() => Report(_api.RemoveAllRecords(ScriptRecordKinds.Event));

    // 方案
    public int CaseCount => _api.GetRecordCount(ScriptRecordKinds.Case);
    public Table? GetCase(int index) => ToTable(_api.GetRecord(ScriptRecordKinds.Case, index));
    public IReadOnlyList<string> GetCaseFields() => _api.GetRecordFields(ScriptRecordKinds.Case);
    public ScriptOperationResult SetCaseField(int index, string field, double value) => Report(_api.SetRecordField(ScriptRecordKinds.Case, index, field, value));
    public ScriptOperationResult AddCase() => Report(_api.AddRecord(ScriptRecordKinds.Case));
    public ScriptOperationResult RemoveCase(int index) => Report(_api.RemoveRecord(ScriptRecordKinds.Case, index));
    public ScriptOperationResult RemoveAllCases() => Report(_api.RemoveAllRecords(ScriptRecordKinds.Case));

    // 天气
    public int WeatherCount => _api.GetRecordCount(ScriptRecordKinds.Weather);
    public Table? GetWeather(int index) => ToTable(_api.GetRecord(ScriptRecordKinds.Weather, index));
    public IReadOnlyList<string> GetWeatherFields() => _api.GetRecordFields(ScriptRecordKinds.Weather);
    public ScriptOperationResult SetWeatherField(int index, string field, double value) => Report(_api.SetRecordField(ScriptRecordKinds.Weather, index, field, value));
    public ScriptOperationResult AddWeather() => Report(_api.AddRecord(ScriptRecordKinds.Weather));
    public ScriptOperationResult RemoveWeather(int index) => Report(_api.RemoveRecord(ScriptRecordKinds.Weather, index));
    public ScriptOperationResult RemoveAllWeathers() => Report(_api.RemoveAllRecords(ScriptRecordKinds.Weather));

    // 战略建设
    public int StrategicCount => _api.GetRecordCount(ScriptRecordKinds.Strategic);
    public Table? GetStrategic(int index) => ToTable(_api.GetRecord(ScriptRecordKinds.Strategic, index));
    public IReadOnlyList<string> GetStrategicFields() => _api.GetRecordFields(ScriptRecordKinds.Strategic);
    public ScriptOperationResult SetStrategicField(int index, string field, double value) => Report(_api.SetRecordField(ScriptRecordKinds.Strategic, index, field, value));
    public ScriptOperationResult AddStrategic() => Report(_api.AddRecord(ScriptRecordKinds.Strategic));
    public ScriptOperationResult RemoveStrategic(int index) => Report(_api.RemoveRecord(ScriptRecordKinds.Strategic, index));
    public ScriptOperationResult RemoveAllStrategics() => Report(_api.RemoveAllRecords(ScriptRecordKinds.Strategic));

    // 空袭
    public int AirRaidCount => _api.GetRecordCount(ScriptRecordKinds.AirRaid);
    public Table? GetAirRaid(int index) => ToTable(_api.GetRecord(ScriptRecordKinds.AirRaid, index));
    public IReadOnlyList<string> GetAirRaidFields() => _api.GetRecordFields(ScriptRecordKinds.AirRaid);
    public ScriptOperationResult SetAirRaidField(int index, string field, double value) => Report(_api.SetRecordField(ScriptRecordKinds.AirRaid, index, field, value));
    public ScriptOperationResult AddAirRaid(int col, int row) => Report(_api.AddRecord(ScriptRecordKinds.AirRaid, col, row));
    public ScriptOperationResult RemoveAirRaid(int index) => Report(_api.RemoveRecord(ScriptRecordKinds.AirRaid, index));
    public ScriptOperationResult RemoveAllAirRaids() => Report(_api.RemoveAllRecords(ScriptRecordKinds.AirRaid));

    // 空中支援
    public int AirSupportCount => _api.GetRecordCount(ScriptRecordKinds.AirSupport);
    public Table? GetAirSupport(int index) => ToTable(_api.GetRecord(ScriptRecordKinds.AirSupport, index));
    public IReadOnlyList<string> GetAirSupportFields() => _api.GetRecordFields(ScriptRecordKinds.AirSupport);
    public ScriptOperationResult SetAirSupportField(int index, string field, double value) => Report(_api.SetRecordField(ScriptRecordKinds.AirSupport, index, field, value));
    public ScriptOperationResult AddAirSupport() => Report(_api.AddRecord(ScriptRecordKinds.AirSupport));
    public ScriptOperationResult RemoveAirSupport(int index) => Report(_api.RemoveRecord(ScriptRecordKinds.AirSupport, index));
    public ScriptOperationResult RemoveAllAirSupports() => Report(_api.RemoveAllRecords(ScriptRecordKinds.AirSupport));

    // 援军
    public int ReinforcementCount => _api.GetRecordCount(ScriptRecordKinds.Reinforcement);
    public Table? GetReinforcement(int index) => ToTable(_api.GetRecord(ScriptRecordKinds.Reinforcement, index));
    public IReadOnlyList<string> GetReinforcementFields() => _api.GetRecordFields(ScriptRecordKinds.Reinforcement);
    public ScriptOperationResult SetReinforcementField(int index, string field, double value) => Report(_api.SetRecordField(ScriptRecordKinds.Reinforcement, index, field, value));
    public ScriptOperationResult AddReinforcement(int col, int row) => Report(_api.AddRecord(ScriptRecordKinds.Reinforcement, col, row));
    public ScriptOperationResult RemoveReinforcement(int index) => Report(_api.RemoveRecord(ScriptRecordKinds.Reinforcement, index));
    public ScriptOperationResult RemoveAllReinforcements() => Report(_api.RemoveAllRecords(ScriptRecordKinds.Reinforcement));

    /// <summary>记录快照 → Lua 表：字段名 → 数值，加 Index 与文字标签。</summary>
    private Table? ToTable(ScriptRecord? record)
    {
        if (record == null) return null;

        var table = new Table(_script);
        table.Set("Index", DynValue.NewNumber(record.Index));
        foreach (var (name, value) in record.Values)
            table.Set(name, DynValue.NewNumber(value));
        foreach (var (name, text) in record.Labels)
            table.Set(name, DynValue.NewString(text));
        return table;
    }

    // ---------- 地形修改 ----------

    public ScriptOperationResult SetTerrain(int col, int row, int type) => Report(_api.SetTerrain(col, row, type));
    public ScriptOperationResult ChangeTerrainType(int col, int row, int delta) => Report(_api.ChangeTerrainType(col, row, delta));
    public ScriptOperationResult ChangeDecoration(int col, int row, int delta) => Report(_api.ChangeDecoration(col, row, delta));
    public ScriptOperationResult SetRiver(int col, int row, int value) => Report(_api.SetRiver(col, row, value));
    public ScriptOperationResult CopyTerrain(int col, int row) => Report(_api.CopyTerrain(col, row));
    public ScriptOperationResult PasteTerrain(int col, int row) => Report(_api.PasteTerrain(col, row));
    public ScriptOperationResult FloodFillTerrain(int col, int row, int type) => Report(_api.FloodFillTerrain(col, row, type));
    public ScriptOperationResult Greening(int probability) => Report(_api.Greening(probability));
    public ScriptOperationResult RandomizeFlatTerrain(int probability) => Report(_api.RandomizeFlatTerrain(probability));
    public ScriptOperationResult RandomizeVariant(int probability) => Report(_api.RandomizeVariant(probability));
    public ScriptOperationResult CreateCoast() => Report(_api.CreateCoast());
    public ScriptOperationResult ProcessOceanSecondLayer() => Report(_api.ProcessOceanSecondLayer());
    public ScriptOperationResult ExportHd() => Report(_api.ExportHd());
    public ScriptOperationResult ConnectBuildings() => Report(_api.ConnectBuildings());

    // ---------- 截图 ----------

    /// <summary>
    /// 地图截图存成 PNG：<c>map.CaptureScreenshot('shot.png')</c> 整幅，
    /// 或 <c>map.CaptureScreenshot('city.png', 120, 78, 40, 30)</c> 取以 (120,78) 为中心 40×30 格的局部放大图。
    /// <para>返回结果的 Message 里带路径、像素尺寸、覆盖的格子范围与每格像素数，AI 据此折算坐标。</para>
    /// <para>
    /// 第 9 个参数给 true 则截"整个编辑器窗口"（界面截图）：
    /// <c>map.CaptureScreenshot('ui.png', -1, -1, 0, 0, 1400, true, false, true)</c> —— 区域参数会被忽略。
    /// </para>
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
        var result = _api.CaptureScreenshot(outputPath, centerCol, centerRow, cellsW, cellsH,
            maxPixels, showHexLabels, showGridLines, sceneMode);
        Report(result);
        return result;
    }

    // ---------- 输出 ----------

    /// <summary>输出一行到控制台</summary>
    public void Log(string message) => _log(message ?? "");

    /// <summary>统一上报：累加影响条数，失败时把原因打到输出里</summary>
    private ScriptOperationResult Report(ScriptOperationResult result)
    {
        if (result.AffectedCount > 0)
            _addAffected(result.AffectedCount);

        if (!result.Success && !string.IsNullOrEmpty(result.Message))
            _log($"[脚本] {result.Message}");

        return result;
    }
}
