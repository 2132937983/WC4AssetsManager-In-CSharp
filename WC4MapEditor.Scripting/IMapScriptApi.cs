namespace WC4MapEditor.Scripting;

/// <summary>
/// 脚本可以操作的地图数据接口。
/// <para>
/// 由 Core 层实现（内部转发到 BuildingModifier / BelongModifier 等）。
/// 接口放在 Scripting 项目里，是为了让依赖方向保持
/// <c>Core → Scripting</c>（若反过来就会与 Core 形成循环依赖）。
/// </para>
/// <para>
/// <b>扩展方式</b>：以后要开放新的数据操作，只要在这里加一个方法、
/// 在 Core 的 <c>MapScriptApi</c> 里实现，脚本里立刻可用 —— 不需要再设计任何命令语法。
/// </para>
/// </summary>
public interface IMapScriptApi
{
    // ---------- 地图信息 ----------

    /// <summary>地图列数</summary>
    int MapWidth { get; }

    /// <summary>地图行数</summary>
    int MapHeight { get; }

    /// <summary>建筑总数</summary>
    int BuildingCount { get; }

    /// <summary>格子总数</summary>
    int TileCount { get; }

    // ---------- 建筑 ----------

    /// <summary>
    /// 遍历所有建筑。<paramref name="visitor"/> 返回 false 可提前结束。
    /// </summary>
    void ForEachBuilding(Func<ScriptBuildingInfo, bool> visitor);

    /// <summary>取指定格子的建筑，没有则返回 null</summary>
    ScriptBuildingInfo? GetBuilding(int col, int row);

    /// <summary>
    /// 在指定格子添加建筑。
    /// <paramref name="name"/> 可为数字 ID、中文名称或空（表示无名称）。
    /// </summary>
    ScriptOperationResult AddBuilding(int col, int row, int buildingType, string? name = null);

    /// <summary>删除指定格子的建筑</summary>
    ScriptOperationResult RemoveBuilding(int col, int row);

    /// <summary>
    /// 列出建筑对象上<b>可写</b>的字段名。
    /// <para>给脚本自省用：先拿字段清单，再用 <see cref="SetBuildingField"/> 按名改写 ——
    /// 这样以后模型结构里新增字段，脚本层不用跟着加方法。</para>
    /// </summary>
    IReadOnlyList<string> GetBuildingFields();

    /// <summary>改指定格子建筑的<b>类型</b>（0-255）。</summary>
    ScriptOperationResult SetBuildingType(int col, int row, int buildingType);

    /// <summary>
    /// 改指定格子建筑的<b>名称</b>。
    /// <para>null / 空串 / <c>"-1"</c> 表示清除名称；中文名查不到会新建 stringtable 条目
    ///（规则同 <see cref="AddBuilding"/>，有副作用）。</para>
    /// </summary>
    ScriptOperationResult SetBuildingName(int col, int row, string? name);

    /// <summary>
    /// 按<b>字段名</b>改建筑的某个属性（字段名大小写不敏感），清单见 <see cref="GetBuildingFields"/>。
    /// <para>数值按字段宽度自动钳位：byte 字段 0-255、名称 0-65535。</para>
    /// </summary>
    ScriptOperationResult SetBuildingField(int col, int row, string field, double value);

    /// <summary>
    /// 把建筑从一格移到另一格（目标格已有建筑时先删掉；源格归属会一并剪切到目标格）。
    /// </summary>
    ScriptOperationResult MoveBuilding(int fromCol, int fromRow, int toCol, int toRow);

    /// <summary>
    /// 按概率删除建筑（每个建筑独立掷骰）。
    /// <paramref name="keepNamed"/> 为 true 时保留有名称的建筑（城市/据点）。
    /// </summary>
    ScriptOperationResult RemoveBuildingsByProbability(int probability, bool keepNamed = false);

    // ---------- 归属 ----------

    /// <summary>取指定格子的归属值（0xFF = 无归属）</summary>
    int GetBelong(int col, int row);

    /// <summary>设置指定格子的归属值</summary>
    ScriptOperationResult SetBelong(int col, int row, int belongValue);

    /// <summary>取指定格子的省区值（0xFFFF = 无省区）</summary>
    int GetProvince(int col, int row);

    /// <summary>
    /// 设置指定格子的省区值（0-65535，<c>0xFFFF</c> = 无省区）。
    /// <para>省区值约定上是"省会格的索引"，随手填数字容易破坏数据结构；
    /// 要一次性铺一整块省区请用 <see cref="FloodFillTerrain"/> 那一类区域操作，或走省区编辑模式。</para>
    /// </summary>
    ScriptOperationResult SetProvince(int col, int row, int provinceValue);

    // ---------- 地形 ----------

    /// <summary>取指定格子的第一层地形类型</summary>
    int GetTerrain(int col, int row);

    // ---------- 单位 ----------

    /// <summary>单位总数</summary>
    int ArmyCount { get; }

    /// <summary>在指定格子放置单位</summary>
    ScriptOperationResult AddArmy(int col, int row);

    /// <summary>删除指定格子的单位</summary>
    ScriptOperationResult RemoveArmy(int col, int row);

    /// <summary>设置单位军团ID</summary>
    ScriptOperationResult SetArmyLegion(int col, int row, int legionId);

    /// <summary>设置单位方案(0-4)</summary>
    ScriptOperationResult SetArmyPlan(int col, int row, int plan);

    /// <summary>循环切换单位方案</summary>
    ScriptOperationResult CycleArmyPlan(int col, int row);

    /// <summary>按归属批量修改单位方案</summary>
    ScriptOperationResult BatchModifyArmyPlanByLegion(int belong, int plan);

    /// <summary>删除指定归属的所有单位</summary>
    ScriptOperationResult RemoveArmyByBelong(int belong);

    /// <summary>删除所有单位(v1+v3)</summary>
    ScriptOperationResult RemoveAllArmies();

    /// <summary>橡皮擦批量删除(v1+v3)</summary>
    ScriptOperationResult EraseArmyBrush(int col, int row, int radius);

    /// <summary>按配置修改军团强度(belong=-1为所有, level=-1为自动)</summary>
    ScriptOperationResult ModifyLegionStrength(int belong, int level);

    /// <summary>随机化军团单位(belong=-1为所有)</summary>
    ScriptOperationResult RandomizeLegionArmies(int belong);

    /// <summary>按概率生成单位(belong=-1为所有)</summary>
    ScriptOperationResult GenerateArmiesByProbability(int belong, int probability);

    /// <summary>自动分配将领</summary>
    ScriptOperationResult AutoAssignGenerals(int belong, int count);

    /// <summary>
    /// 遍历所有单位（v1 / v3 由实现内部自动分派）。<paramref name="visitor"/> 返回 false 可提前结束。
    /// </summary>
    void ForEachArmy(Func<ScriptArmyInfo, bool> visitor);

    /// <summary>取指定格子的单位，没有则返回 null</summary>
    ScriptArmyInfo? GetArmy(int col, int row);

    /// <summary>
    /// 列出单位对象上<b>可写</b>的字段名（供脚本自省）。
    /// <para>v3 专有字段（HpLevel / TransportShip / Hatred …）在 v1 地图上写会被忽略 ——
    /// v1 结构里没有这些字段，写之前先看 <see cref="ScriptArmyInfo.Version"/>。</para>
    /// </summary>
    IReadOnlyList<string> GetArmyFields();

    /// <summary>
    /// 按<b>字段名</b>改单位的某个属性（大小写不敏感），清单见 <see cref="GetArmyFields"/>。
    /// </summary>
    ScriptOperationResult SetArmyField(int col, int row, string field, double value);

    // ---------- 军团 ----------

    /// <summary>军团总数</summary>
    int LegionCount { get; }

    /// <summary>按<b>列表索引</b>取军团（索引就是归属值），越界返回 null</summary>
    ScriptLegionInfo? GetLegionAt(int index);

    /// <summary>遍历所有军团，<paramref name="visitor"/> 返回 false 可提前结束。</summary>
    void ForEachLegion(Func<ScriptLegionInfo, bool> visitor);

    /// <summary>
    /// 列出军团对象上<b>可写</b>的字段名（供脚本自省）。
    /// <para>
    /// 刻意不含 <c>CountryId</c> / <c>ActionId</c>：这两个是军团定位键，改它们要走带校验的
    /// <see cref="SetLegionCountryIdAt"/> / <see cref="SetLegionActionIdAt"/>，
    /// 否则会出现"按国家找不到军团"或重复国家 ID（BTL 校验器会报）。
    /// </para>
    /// </summary>
    IReadOnlyList<string> GetLegionFields();

    /// <summary>
    /// 按<b>字段名</b>改军团的某个属性（大小写不敏感），清单见 <see cref="GetLegionFields"/>。
    /// </summary>
    ScriptOperationResult SetLegionField(int index, string field, double value);

    /// <summary>设置军团颜色</summary>
    ScriptOperationResult SetLegionColor(int legionId, int r, int g, int b);

    /// <summary>设置军团ActionId</summary>
    ScriptOperationResult SetLegionActionId(int legionId, int actionId);

    /// <summary>
    /// 按<b>军团列表索引</b>设置国家ID（CountryId）。
    /// <para>
    /// 为什么不能用 <see cref="SetLegionActionId"/> 那套「军团ID」定位：那个参数走
    /// <c>FindLegionIndex()</c>，而它是<b>按 CountryId 匹配</b>的。要改 CountryId 本身，
    /// 就必须用不会因为这次改动而失配的定位方式，所以这里收列表索引。
    /// </para>
    /// </summary>
    ScriptOperationResult SetLegionCountryIdAt(int index, int countryId);

    /// <summary>按<b>军团列表索引</b>设置 ActionId（行动顺序）</summary>
    ScriptOperationResult SetLegionActionIdAt(int index, int actionId);

    /// <summary>
    /// 按<b>军团列表索引</b>读取国家ID（越界返回 -1）。
    /// <para>归属值约定的就是军团列表索引，所以要按势力名写归属，得先把索引和 CountryId 对上。</para>
    /// </summary>
    int GetLegionCountryIdAt(int index);

    /// <summary>设置玩家控制军团</summary>
    ScriptOperationResult SetPlayerLegion(int actionId);

    /// <summary>应用默认颜色到所有军团</summary>
    ScriptOperationResult ApplyDefaultLegionColors();

    /// <summary>从配置更新所有军团颜色</summary>
    ScriptOperationResult ApplyLegionColorsFromSettings();

    /// <summary>随机化所有军团等级与经济</summary>
    ScriptOperationResult RandomizeLegionLevels();

    /// <summary>重建所有军团ActionId与归属</summary>
    ScriptOperationResult RebuildLegionActionBelong();

    /// <summary>批量设置军团经济</summary>
    ScriptOperationResult SetAllLegionsEconomy(int value);

    /// <summary>批量设置军团工业</summary>
    ScriptOperationResult SetAllLegionsIndustry(int value);

    /// <summary>批量设置军团科技</summary>
    ScriptOperationResult SetAllLegionsTech(int value);

    /// <summary>追加一个默认军团</summary>
    ScriptOperationResult AddLegion();

    /// <summary>按索引删除军团</summary>
    ScriptOperationResult RemoveLegionAt(int index);

    /// <summary>按索引设置军团阵营值</summary>
    ScriptOperationResult SetLegionCampAt(int index, int camp);

    /// <summary>按索引设置军团战败条件</summary>
    ScriptOperationResult SetLegionDefeatConditionAt(int index, int value);

    // ---------- 地形修改 ----------

    /// <summary>设置地形类型</summary>
    ScriptOperationResult SetTerrain(int col, int row, int type);

    /// <summary>切换地形类型(delta=正数递增/负数递减)</summary>
    ScriptOperationResult ChangeTerrainType(int col, int row, int delta);

    /// <summary>切换地形变体</summary>
    ScriptOperationResult ChangeDecoration(int col, int row, int delta);

    /// <summary>设置河流值(0-63)</summary>
    ScriptOperationResult SetRiver(int col, int row, int value);

    /// <summary>复制地形</summary>
    ScriptOperationResult CopyTerrain(int col, int row);

    /// <summary>粘贴地形</summary>
    ScriptOperationResult PasteTerrain(int col, int row);

    /// <summary>洪水填充地形</summary>
    ScriptOperationResult FloodFillTerrain(int col, int row, int type);

    /// <summary>绿化平地(probability=0-100)</summary>
    ScriptOperationResult Greening(int probability);

    /// <summary>随机平地变体(probability=0-100)</summary>
    ScriptOperationResult RandomizeFlatTerrain(int probability);

    /// <summary>随机当前层变体(probability=0-100)</summary>
    ScriptOperationResult RandomizeVariant(int probability);

    /// <summary>创建海岸线</summary>
    ScriptOperationResult CreateCoast();

    /// <summary>处理海洋第二层</summary>
    ScriptOperationResult ProcessOceanSecondLayer();

    /// <summary>导出HD文件</summary>
    ScriptOperationResult ExportHd();

    /// <summary>连接建筑(A*寻路)</summary>
    ScriptOperationResult ConnectBuildings();

    // ---------- 截图（给 AI 视觉 / 人工核对用） ----------

    /// <summary>
    /// 把地图渲染成 PNG 存到磁盘。
    /// <para>
    /// <paramref name="centerCol"/>/<paramref name="centerRow"/> 为负 = 整幅地图；
    /// 给中心点与 <paramref name="cellsW"/>/<paramref name="cellsH"/> = 局部区域放大图
    /// （区域越小格子在图上越大，AI 才看得清细节）。
    /// </para>
    /// <para>
    /// <paramref name="showHexLabels"/> 默认开：图上会带坐标标注，AI 才能把图像位置和格子号对上。
    /// 返回消息里带文件路径与"覆盖哪些格子、每格多少像素"，便于据此换算。
    /// </para>
    /// <para>
    /// <paramref name="sceneMode"/> 为 true 时截的是"<b>整个编辑器窗口</b>"（工具栏、编辑面板、
    /// 地图视口、状态栏都在内），此时上面那些区域参数全部忽略，只有 <paramref name="maxPixels"/>
    /// 仍生效（按长边等比缩小）。用来给 AI 看界面、或留一张"当时界面长什么样"的凭证。
    /// </para>
    /// </summary>
    ScriptOperationResult CaptureScreenshot(
        string outputPath,
        int centerCol = -1,
        int centerRow = -1,
        int cellsW = 0,
        int cellsH = 0,
        int maxPixels = 1400,
        bool showHexLabels = true,
        bool showGridLines = false,
        bool sceneMode = false);
}
