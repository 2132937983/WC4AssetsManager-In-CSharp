using System.CommandLine;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 Building 命令组。会改数据的子命令都记撤销（在 GUI 控制台里执行时 Ctrl+Z 可撤回）。
public sealed partial class CliCommandHost
{
    private Command BuildBuildingCommand()
    {
        var cmd = new Command("building", "建筑修改操作");

        var addCmd = new Command("add", "在指定坐标添加建筑");
        var addColArg = new Argument<int>("col") { Description = "列坐标" };
        var addRowArg = new Argument<int>("row") { Description = "行坐标" };
        var addTypeOpt = new Option<int>("--type", "-t") { Description = "建筑类型(11=一级城,12=二级城,...,15=五级城)", DefaultValueFactory = _ => 11 };
        var addNameOpt = new Option<string>("--name", "-n") { Description = "建筑名称(数字ID或中文名称,默认0xFFFF=无名称)", DefaultValueFactory = _ => "-1" };
        var addIndexOpt = new Option<int>("--index", "-i") { Description = "格子索引(优先于坐标)" };
        addCmd.Arguments.Add(addColArg);
        addCmd.Arguments.Add(addRowArg);
        addCmd.Options.Add(addTypeOpt);
        addCmd.Options.Add(addNameOpt);
        addCmd.Options.Add(addIndexOpt);
        addCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int col = parseResult.GetValue(addColArg);
                int row = parseResult.GetValue(addRowArg);
                int buildingType = parseResult.GetValue(addTypeOpt);
                string nameInput = parseResult.GetValue(addNameOpt) ?? "-1";

                if (col < 0 || col >= mapData.MapWidth || row < 0 || row >= mapData.MapHeight)
                {
                    _output.WriteLine($"坐标 ({col},{row}) 超出范围 (0-{mapData.MapWidth - 1},0-{mapData.MapHeight - 1})");
                    return;
                }
                if (!TryResolveBuildingName(nameInput, out ushort name)) return;

                int coordIndex = row * mapData.MapWidth + col;
                var b = Building.CreateDefault(coordIndex);
                b.BuildingType = (byte)buildingType;
                b.Name = name;

                if (coordIndex > 65535)
                    _output.WriteLine($"[警告] 坐标序号 {coordIndex} 超出文件格式上限 65535，保存时将被截断为 {coordIndex & 0xFFFF}，游戏可能无法正确识别此建筑");

                ModifierResult result = default;
                RecordEntityChange($"添加建筑 ({col},{row})", () => result = building.Apply(col, row, b));
                _output.WriteLine(result.Message ?? $"已在 ({col},{row}) 添加建筑，类型={buildingType}({b.GetBuildingTypeName()})");
            });
        });

        var addByIdxCmd = new Command("add-by-idx", "按格子索引添加建筑");
        var addIdxArg = new Argument<int>("index") { Description = "格子索引" };
        var addIdxTypeOpt = new Option<int>("--type", "-t") { Description = "建筑类型", DefaultValueFactory = _ => 11 };
        var addIdxNameOpt = new Option<string>("--name", "-n") { Description = "建筑名称(数字ID或中文名称)", DefaultValueFactory = _ => "-1" };
        addByIdxCmd.Arguments.Add(addIdxArg);
        addByIdxCmd.Options.Add(addIdxTypeOpt);
        addByIdxCmd.Options.Add(addIdxNameOpt);
        addByIdxCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int idx = parseResult.GetValue(addIdxArg);
                int buildingType = parseResult.GetValue(addIdxTypeOpt);
                string nameInput = parseResult.GetValue(addIdxNameOpt) ?? "-1";

                if (idx < 0 || idx >= mapData.MapWidth * mapData.MapHeight)
                {
                    _output.WriteLine($"索引 {idx} 超出范围 (0-{mapData.MapWidth * mapData.MapHeight - 1})");
                    return;
                }
                if (!TryResolveBuildingName(nameInput, out ushort name)) return;

                int col = idx % mapData.MapWidth;
                int row = idx / mapData.MapWidth;
                var b = Building.CreateDefault(idx);
                b.BuildingType = (byte)buildingType;
                b.Name = name;

                if (idx > 65535)
                    _output.WriteLine($"[警告] 坐标序号 {idx} 超出文件格式上限 65535，保存时将被截断为 {idx & 0xFFFF}，游戏可能无法正确识别此建筑");

                ModifierResult result = default;
                RecordEntityChange($"添加建筑 ({col},{row})", () => result = building.Apply(col, row, b));
                _output.WriteLine(result.Message ?? $"已在索引{idx} ({col},{row}) 添加建筑，类型={buildingType}({b.GetBuildingTypeName()})");
            });
        });

        var removeCmd = new Command("remove", "删除指定坐标的建筑");
        var rmColArg = new Argument<int>("col") { Description = "列坐标" };
        var rmRowArg = new Argument<int>("row") { Description = "行坐标" };
        removeCmd.Arguments.Add(rmColArg);
        removeCmd.Arguments.Add(rmRowArg);
        removeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int col = parseResult.GetValue(rmColArg);
                int row = parseResult.GetValue(rmRowArg);
                ModifierResult result = default;
                RecordEntityChange($"删除建筑 ({col},{row})", () => result = building.Remove(col, row));
                _output.WriteLine(result.Message ?? $"已删除 ({col},{row}) 的建筑");
            });
        });

        var removeAllCmd = new Command("remove-all", "删除所有建筑");
        removeAllCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                RecordEntityChange("删除所有建筑", building.RemoveAll);
                _output.WriteLine("所有建筑已删除");
            });
        });

        var removeNonCapitalCmd = new Command("remove-non-capital", "删除不在省会格子上的建筑（坐标在地图外的建筑保留）");
        removeNonCapitalCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int count = 0;
                RecordEntityChange("删除不在省会格子上的建筑", () => count = building.RemoveNonCapitalBuildings());
                _output.WriteLine($"已删除 {count} 个不在省会格子上的建筑");
            });
        });

        var randomizeNamedCmd = new Command("randomize-named", "随机有名称建筑类型");
        randomizeNamedCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                ModifierResult result = default;
                RecordEntityChange("随机化有名称建筑类型", () => result = building.RandomizeNamedBuildingTypes(useCondition: false));
                _output.WriteLine(result.Message ?? "已完成");
            });
        });

        var randomizeUnnamedCmd = new Command("randomize-unnamed", "随机无名称建筑类型");
        randomizeUnnamedCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                ModifierResult result = default;
                RecordEntityChange("随机化无名称建筑类型", () => result = building.RandomizeUnnamedBuildingTypes(useCondition: false));
                _output.WriteLine(result.Message ?? "已完成");
            });
        });

        var randomizeByBelongCmd = new Command("randomize-by-belong", "按归属随机建筑（类型按 setting.txt 的重要程度加权，等级按随机参数）");
        var belongIdArg = new Argument<int>("belongId") { Description = "归属ID（-1 = 全部）" };
        randomizeByBelongCmd.Arguments.Add(belongIdArg);
        randomizeByBelongCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int belongId = parseResult.GetValue(belongIdArg);
                ModifierResult result = default;
                RecordEntityChange($"按归属随机建筑数据 (归属 {belongId})", () => result = building.RandomizeBuildingsByBelong(belongId));
                _output.WriteLine(result.Message ?? $"已按归属{belongId}随机 {result.AffectedCount} 个建筑");
            });
        });

        var generateCapitalsCmd = new Command("generate-capitals", "为所有建筑所在格子生成省会（默认只处理没有省份的格子）");
        var genCapOverwriteOpt = new Option<bool>("--overwrite", "-w") { Description = "已有省份的格子也改成以建筑为省会" };
        generateCapitalsCmd.Options.Add(genCapOverwriteOpt);
        generateCapitalsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                bool overwrite = parseResult.GetValue(genCapOverwriteOpt);
                int count = 0;
                RecordEntityChange("为所有建筑生成省会", () => count = building.GenerateCapitalsForAllBuildings(overwrite));
                _output.WriteLine($"已为 {count} 个建筑生成省会");
            });
        });

        var randomizeOnCapitalsCmd = new Command("randomize-on-capitals", "在没有建筑的省会格子上随机生成建筑");
        randomizeOnCapitalsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int count = 0;
                RecordEntityChange("在省会格子随机生成建筑", () => count = building.RandomizeBuildingsOnCapitals());
                _output.WriteLine($"已在 {count} 个省会格子上随机生成建筑");
            });
        });

        var smartAppearanceCmd = new Command("smart-appearance", "智能设置建筑外观");
        smartAppearanceCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int count = 0;
                RecordEntityChange("智能设置建筑外观值", () => count = building.SmartSetBuildingAppearance(mapData));
                _output.WriteLine($"已智能设置 {count} 个建筑外观");
            });
        });

        cmd.Subcommands.Add(addCmd);
        cmd.Subcommands.Add(addByIdxCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(removeAllCmd);
        cmd.Subcommands.Add(removeNonCapitalCmd);
        cmd.Subcommands.Add(randomizeNamedCmd);
        cmd.Subcommands.Add(randomizeUnnamedCmd);
        cmd.Subcommands.Add(randomizeByBelongCmd);
        cmd.Subcommands.Add(generateCapitalsCmd);
        cmd.Subcommands.Add(randomizeOnCapitalsCmd);
        var removeByProbCmd = new Command("remove-by-probability", "按概率删除建筑（每个建筑独立掷骰）");
        var removeProbArg = new Argument<int>("probability") { Description = "删除概率(0-100)" };
        var removeKeepNamedOpt = new Option<bool>("--keep-named", "-k") { Description = "保留有名称的建筑（城市/据点）" };
        removeByProbCmd.Arguments.Add(removeProbArg);
        removeByProbCmd.Options.Add(removeKeepNamedOpt);
        removeByProbCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var building = mgr.GetModifier<BuildingModifier>()!;
                int prob = Math.Clamp(parseResult.GetValue(removeProbArg), 0, 100);
                bool keepNamed = parseResult.GetValue(removeKeepNamedOpt);

                ModifierResult result = default;
                RecordEntityChange($"按 {prob}% 概率删除建筑",
                    () => result = building.RemoveBuildingsByProbability(prob, keepNamed));
                _output.WriteLine(result.Message ?? $"已删除 {result.AffectedCount} 个建筑");
            });
        });

        cmd.Subcommands.Add(smartAppearanceCmd);
        cmd.Subcommands.Add(removeByProbCmd);

        var listCmd = new Command("list", "列出所有建筑");
        var listTypeOpt = new Option<int>("--type", "-t") { Description = "按建筑类型过滤(0-255)", DefaultValueFactory = _ => -1 };
        var listNamedOpt = new Option<bool>("--named", "-n") { Description = "只显示有名称的建筑" };
        var listBelongOpt = new Option<int>("--belong", "-b") { Description = "按归属过滤(0-255)", DefaultValueFactory = _ => -1 };
        listCmd.Options.Add(listTypeOpt);
        listCmd.Options.Add(listNamedOpt);
        listCmd.Options.Add(listBelongOpt);
        listCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int filterType = parseResult.GetValue(listTypeOpt);
                bool onlyNamed = parseResult.GetValue(listNamedOpt);
                int filterBelong = parseResult.GetValue(listBelongOpt);

                Dictionary<int, string> nameMap = new();
                try
                {
                    var parser = ConfigManager.Instance.GetStringTableParser();
                    foreach (var kvp in parser.FindCityNames())
                        nameMap[kvp.Key] = kvp.Value;
                }
                catch { }

                int count = 0;
                foreach (var b in mapData.Buildings)
                {
                    int col = mapData.MapWidth > 0 ? b.Coordinate % mapData.MapWidth : 0;
                    int row = mapData.MapWidth > 0 ? b.Coordinate / mapData.MapWidth : 0;

                    if (filterType >= 0 && b.BuildingType != filterType)
                        continue;

                    int belong = mapData.GetBelongValue(col, row);
                    if (filterBelong >= 0 && belong != filterBelong)
                        continue;

                    bool hasName = b.Name != 0 && b.Name != BuildingNameResolver.NoName;
                    if (onlyNamed && !hasName)
                        continue;

                    string name = hasName && nameMap.TryGetValue(b.Name, out var n) ? n : "";
                    string typeName = b.GetBuildingTypeName();
                    string belongStr = belong == 0xFF ? "无" : belong.ToString();
                    string nameStr = hasName ? (name.Length > 0 ? name : $"#{b.Name}") : "无";

                    _output.WriteLine($"({col,3},{row,3}) 类型={b.BuildingType}({typeName}) 名称={nameStr} 归属={belongStr}");
                    count++;
                }

                if (count == mapData.Buildings.Count)
                    _output.WriteLine($"共 {count} 个建筑");
                else
                    _output.WriteLine($"共 {count} 个建筑（筛选自 {mapData.Buildings.Count}）");
            }, readOnly: true);
        });

        // ---- 对象属性读写（与 Lua 的 SetBuildingField 同一实现）----
        var fieldsCmd = new Command("fields", "列出建筑可写的字段名");
        fieldsCmd.SetAction(_ => WithModifiers((mapData, mgr) =>
            _output.WriteLine("建筑可写字段：" + string.Join(", ", ObjectApi(mapData).GetBuildingFields())),
            readOnly: true));

        var setFieldCmd = new Command("set-field", "按字段名改建筑属性（字段名见 building fields）");
        var sfColArg = new Argument<int>("col") { Description = "列" };
        var sfRowArg = new Argument<int>("row") { Description = "行" };
        var sfFieldArg = new Argument<string>("field") { Description = "字段名，如 FactoryLevel / KeyPoint / Appearance" };
        var sfValueArg = new Argument<double>("value") { Description = "数值（按字段宽度自动钳位）" };
        setFieldCmd.Arguments.Add(sfColArg);
        setFieldCmd.Arguments.Add(sfRowArg);
        setFieldCmd.Arguments.Add(sfFieldArg);
        setFieldCmd.Arguments.Add(sfValueArg);
        setFieldCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
            RecordEntityChange("修改建筑字段", () => PrintOp(ObjectApi(mapData).SetBuildingField(
                parseResult.GetValue(sfColArg), parseResult.GetValue(sfRowArg),
                parseResult.GetValue(sfFieldArg) ?? "", parseResult.GetValue(sfValueArg))))));

        var showCmd = new Command("show", "查看指定格子的建筑对象（全部属性）");
        var shColArg = new Argument<int>("col") { Description = "列" };
        var shRowArg = new Argument<int>("row") { Description = "行" };
        showCmd.Arguments.Add(shColArg);
        showCmd.Arguments.Add(shRowArg);
        showCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            int col = parseResult.GetValue(shColArg);
            int row = parseResult.GetValue(shRowArg);
            var b = ObjectApi(mapData).GetBuilding(col, row);
            if (b == null)
            {
                _output.WriteLine($"({col},{row}) 没有建筑");
                return;
            }

            _output.WriteLine($"建筑 ({b.Col},{b.Row}) 类型={b.BuildingTypeName}({b.BuildingType}) 名称={(b.HasName ? b.Name : "(无)")} 名称ID={b.NameId} 归属={b.Belong}");
            _output.WriteLine($"  外观={b.Appearance} 地标={b.LandmarkBuilding} 装饰={b.DecorativeBuilding} 技能解锁={b.SkillUnlock} 奖励={b.RewardCount} 仇恨={b.HatredValue}");
            _output.WriteLine($"  据点={b.KeyPointName}({b.KeyPoint}) 占领事件={b.OccupationEvent} 起火={b.FireIgnition} 燃烧={b.FireDuration}");
            _output.WriteLine($"  防空 武器={b.AirDefenseWeapon} 雷达={b.AirDefenseRadarName}({b.AirDefenseRadar})");
            _output.WriteLine($"  等级 工厂(工业)={b.FactoryLevel}/4 研究所(科研)={b.ResearchLevel}/4 补给站(医疗)={b.MedicalLevel}/4 机场(航空)={b.AviationLevel}/4 导弹={b.MissileLevel}/2 核弹={b.NuclearLevel}/2");
        }, readOnly: true));

        var setTypeCmd = new Command("set-type", "改建筑类型（0-255）");
        var stColArg = new Argument<int>("col") { Description = "列" };
        var stRowArg = new Argument<int>("row") { Description = "行" };
        var stTypeArg = new Argument<int>("type") { Description = "建筑类型，如 13=三级城" };
        setTypeCmd.Arguments.Add(stColArg);
        setTypeCmd.Arguments.Add(stRowArg);
        setTypeCmd.Arguments.Add(stTypeArg);
        setTypeCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
            RecordEntityChange("修改建筑类型", () => PrintOp(ObjectApi(mapData).SetBuildingType(
                parseResult.GetValue(stColArg), parseResult.GetValue(stRowArg), parseResult.GetValue(stTypeArg))))));

        var setNameCmd = new Command("set-name", "改建筑名称（名称留空或 -1 = 清除）");
        var snColArg = new Argument<int>("col") { Description = "列" };
        var snRowArg = new Argument<int>("row") { Description = "行" };
        var snNameArg = new Argument<string?>("name") { Description = "数字ID 或 中文名称；省略=清除", Arity = ArgumentArity.ZeroOrOne };
        setNameCmd.Arguments.Add(snColArg);
        setNameCmd.Arguments.Add(snRowArg);
        setNameCmd.Arguments.Add(snNameArg);
        setNameCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            string? nameInput = parseResult.GetValue(snNameArg);
            if (!TryResolveBuildingName(nameInput, out _)) return;
            RecordEntityChange("修改建筑名称", () => PrintOp(ObjectApi(mapData).SetBuildingName(
                parseResult.GetValue(snColArg), parseResult.GetValue(snRowArg), nameInput)));
        }));

        var moveCmd = new Command("move", "把建筑从一格移到另一格（源格归属一并剪切）");
        var mvFromColArg = new Argument<int>("fromCol") { Description = "源列" };
        var mvFromRowArg = new Argument<int>("fromRow") { Description = "源行" };
        var mvToColArg = new Argument<int>("toCol") { Description = "目标列" };
        var mvToRowArg = new Argument<int>("toRow") { Description = "目标行" };
        moveCmd.Arguments.Add(mvFromColArg);
        moveCmd.Arguments.Add(mvFromRowArg);
        moveCmd.Arguments.Add(mvToColArg);
        moveCmd.Arguments.Add(mvToRowArg);
        moveCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
            RecordEntityChange("移动建筑", () => PrintOp(ObjectApi(mapData).MoveBuilding(
                parseResult.GetValue(mvFromColArg), parseResult.GetValue(mvFromRowArg),
                parseResult.GetValue(mvToColArg), parseResult.GetValue(mvToRowArg))))));

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(showCmd);
        cmd.Subcommands.Add(fieldsCmd);
        cmd.Subcommands.Add(setFieldCmd);
        cmd.Subcommands.Add(setTypeCmd);
        cmd.Subcommands.Add(setNameCmd);
        cmd.Subcommands.Add(moveCmd);

        return cmd;
    }

    /// <summary>解析建筑名称（数字 ID / 城市名 / 空 = 无名称）；无效时打印原因并返回 false。</summary>
    private bool TryResolveBuildingName(string? input, out ushort nameId)
    {
        if (BuildingNameResolver.TryResolve(input, out nameId, out string? error)) return true;
        _output.WriteLine($"名称无效：{error}");
        return false;
    }
}
