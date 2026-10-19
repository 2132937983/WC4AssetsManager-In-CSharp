using System.CommandLine;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 Army 命令组：v1 / v3 统一经 IArmyModule 分派（v3 记录族地图用 v3 单位表）。
public sealed partial class CliCommandHost
{
    private Command BuildArmyCommand()
    {
        var cmd = new Command("army", "部队修改操作");

        var listCmd = new Command("list", "列出所有部队");
        listCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var armies = mapData.Armies;
                _output.WriteLine($"v1部队总数: {armies.Count}");
                if (armies.Count > 0)
                {
                    _output.WriteLine();
                    _output.WriteLine($"{"序号",-6} {"类型",-6} {"坐标",-10} {Loc.T("Cli.Army.ColBelong", "归属"),-6} {"方案",-6}");
                    _output.WriteLine(new string('-', 40));
                    for (int i = 0; i < armies.Count; i++)
                    {
                        var army = armies[i];
                        _output.WriteLine($"{i,-6} {army.UnitType,-6} {FormatCell(mapData, army.CoordinateIndex)}    {BelongTextAt(mapData, army.CoordinateIndex),-6} {army.Plan,-6}");
                    }
                }
                var v3List = mapData.ArmiesV3;
                if (v3List.Count > 0)
                {
                    _output.WriteLine();
                    _output.WriteLine($"v3部队总数: {v3List.Count}");
                    _output.WriteLine();
                    _output.WriteLine($"{"序号",-6} {"类型",-6} {"坐标",-10} {Loc.T("Cli.Army.ColBelong", "归属"),-6} {"方案",-6}");
                    _output.WriteLine(new string('-', 40));
                    for (int i = 0; i < v3List.Count; i++)
                    {
                        var army = v3List[i];
                        _output.WriteLine($"{i,-6} {army.UnitType,-6} {FormatCell(mapData, army.CoordinateIndex)}    {BelongTextAt(mapData, army.CoordinateIndex),-6} {army.Plan,-6}");
                    }
                }
            }, readOnly: true);
        });

        var removeAllCmd = new Command("remove-all", "删除所有部队(v1+v3)");
        removeAllCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int count = mapData.Armies.Count + mapData.ArmiesV3.Count;
                RecordEntityChange("删除所有部队", () =>
                {
                    BoundModifier<ArmyModifier>(mapData, mgr).DeleteAllArmies();
                    BoundModifier<ArmyV3Modifier>(mapData, mgr).DeleteAllArmies();
                });
                _output.WriteLine($"已删除 {count} 个部队");
            });
        });

        var applyCmd = new Command("apply", "放置单位");
        var applyCol = new Argument<int>("col") { Description = "列" };
        var applyRow = new Argument<int>("row") { Description = "行" };
        applyCmd.Arguments.Add(applyCol);
        applyCmd.Arguments.Add(applyRow);
        applyCmd.SetAction(parseResult => RunArmyCommand(
            parseResult.GetValue(applyCol), parseResult.GetValue(applyRow), "放置单位", (units, c, r) => units.Apply(c, r)));

        var removeCmd = new Command("remove", "删除指定坐标单位");
        var rmCol = new Argument<int>("col") { Description = "列" };
        var rmRow = new Argument<int>("row") { Description = "行" };
        removeCmd.Arguments.Add(rmCol);
        removeCmd.Arguments.Add(rmRow);
        removeCmd.SetAction(parseResult => RunArmyCommand(
            parseResult.GetValue(rmCol), parseResult.GetValue(rmRow), "删除单位", (units, c, r) => units.Remove(c, r)));

        var copyCmd = new Command("copy", "复制单位");
        var cpCol = new Argument<int>("col") { Description = "列" };
        var cpRow = new Argument<int>("row") { Description = "行" };
        copyCmd.Arguments.Add(cpCol);
        copyCmd.Arguments.Add(cpRow);
        copyCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
                _output.WriteLine(ArmyModuleFor(mapData, mgr).CopyArmy(parseResult.GetValue(cpCol), parseResult.GetValue(cpRow)).Message),
                readOnly: true);
        });

        var pasteCmd = new Command("paste", "粘贴单位");
        var psCol = new Argument<int>("col") { Description = "列" };
        var psRow = new Argument<int>("row") { Description = "行" };
        pasteCmd.Arguments.Add(psCol);
        pasteCmd.Arguments.Add(psRow);
        pasteCmd.SetAction(parseResult => RunArmyCommand(
            parseResult.GetValue(psCol), parseResult.GetValue(psRow), "粘贴单位", (units, c, r) => units.PasteArmy(c, r)));

        var setLegionCmd = new Command("set-legion", Loc.T("Cli.Army.SetLegionDesc", "设置单位所属军团：单位的归属就是所在格子的归属，这里改的是该格的归属值"));
        var slCol = new Argument<int>("col") { Description = "列" };
        var slRow = new Argument<int>("row") { Description = "行" };
        var slLegion = new Argument<int>("belong") { Description = "归属值(0-255, 255=无归属)" };
        setLegionCmd.Arguments.Add(slCol);
        setLegionCmd.Arguments.Add(slRow);
        setLegionCmd.Arguments.Add(slLegion);
        setLegionCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            int col = parseResult.GetValue(slCol), row = parseResult.GetValue(slRow);
            if (!ArmyModuleFor(mapData, mgr).HasUnitAt(col, row))
            {
                _output.WriteLine(Loc.F("Cli.Army.NoUnitAt", col, row));
                return;
            }

            ModifierResult result = default;
            RecordSingleBelongChange(col, row, $"设置单位归属 ({col},{row})",
                () => result = mgr.GetModifier<BelongModifier>()!.SetBelongByCountryId(col, row, parseResult.GetValue(slLegion)));
            _output.WriteLine(result.Message);
        }));

        var cyclePlanCmd = new Command("cycle-plan", "循环切换单位方案(0-4)");
        var cp2Col = new Argument<int>("col") { Description = "列" };
        var cp2Row = new Argument<int>("row") { Description = "行" };
        cyclePlanCmd.Arguments.Add(cp2Col);
        cyclePlanCmd.Arguments.Add(cp2Row);
        cyclePlanCmd.SetAction(parseResult => RunArmyCommand(
            parseResult.GetValue(cp2Col), parseResult.GetValue(cp2Row), "切换单位方案", (units, c, r) => units.QuickModifyArmyPlan(c, r)));

        var setPlanCmd = new Command("set-plan", "设置单位方案");
        var spCol = new Argument<int>("col") { Description = "列" };
        var spRow = new Argument<int>("row") { Description = "行" };
        var spPlan = new Argument<int>("plan") { Description = "方案值(0-4)" };
        setPlanCmd.Arguments.Add(spCol);
        setPlanCmd.Arguments.Add(spRow);
        setPlanCmd.Arguments.Add(spPlan);
        setPlanCmd.SetAction(parseResult =>
        {
            int plan = Math.Clamp(parseResult.GetValue(spPlan), 0, 4);
            RunArmyCommand(parseResult.GetValue(spCol), parseResult.GetValue(spRow), "设置单位方案",
                (units, c, r) => units.SetPlan([new HexCoord(c, r)], plan));
        });

        var batchPlanCmd = new Command("batch-plan-by-legion", "按归属批量修改单位方案");
        var bpBelong = new Argument<int>("belong") { Description = "归属值(-1为所有)" };
        var bpPlan = new Argument<int>("plan") { Description = "方案值(0-4，-1为随机)" };
        batchPlanCmd.Arguments.Add(bpBelong);
        batchPlanCmd.Arguments.Add(bpPlan);
        batchPlanCmd.SetAction(parseResult =>
        {
            int belong = parseResult.GetValue(bpBelong);
            int plan = parseResult.GetValue(bpPlan);
            RunArmyBatch("按归属批量修改单位方案", units => units.BatchModifyArmyPlanByLegion(belong, plan));
        });

        var strengthCmd = new Command("legion-strength", "按配置修改军团强度");
        var lsBelong = new Argument<int>("belong") { Description = "归属值(-1为所有)" };
        var lsLevel = new Argument<int>("level") { Description = "强度等级(-1为自动)" };
        strengthCmd.Arguments.Add(lsBelong);
        strengthCmd.Arguments.Add(lsLevel);
        strengthCmd.SetAction(parseResult =>
        {
            int belong = parseResult.GetValue(lsBelong);
            int level = parseResult.GetValue(lsLevel);
            RunArmyBatch("修改军团强度", units => units.ModifyLegionStrength(belong, level));
        });

        var randomCmd = new Command("randomize-legion", "随机化军团单位");
        var rdBelong = new Argument<int>("belong") { Description = "归属值(-1为所有)" };
        randomCmd.Arguments.Add(rdBelong);
        randomCmd.SetAction(parseResult =>
        {
            int belong = parseResult.GetValue(rdBelong);
            RunArmyBatch("随机化军团单位", units => units.RandomizeLegionArmies(belong));
        });

        var genCmd = new Command("generate-by-prob", "按概率生成单位");
        var gnBelong = new Argument<int>("belong") { Description = "归属值(-1为所有)" };
        var gnProb = new Argument<int>("probability") { Description = "概率(1-100)" };
        genCmd.Arguments.Add(gnBelong);
        genCmd.Arguments.Add(gnProb);
        genCmd.SetAction(parseResult =>
        {
            int belong = parseResult.GetValue(gnBelong);
            int prob = Math.Clamp(parseResult.GetValue(gnProb), 1, 100);
            RunArmyBatch("按概率生成单位", units => units.GenerateArmiesByProbability(belong, prob));
        });

        var generalCmd = new Command("auto-general", "自动分配将领");
        var agBelong = new Argument<int>("belong") { Description = "归属值(-1为所有)" };
        var agCount = new Argument<int>("count") { Description = "将领数量(-1为最大数量)" };
        var agClearOpt = new Option<bool>("--clear") { Description = "先清除这些单位上已有的将领再重新分配（默认跳过已有将领的单位）" };
        var agNoAdaptOpt = new Option<bool>("--no-adapt") { Description = "不按专长适配：尽可能分配所有将领，专长不匹配时随机挑单位" };
        generalCmd.Arguments.Add(agBelong);
        generalCmd.Arguments.Add(agCount);
        generalCmd.Options.Add(agClearOpt);
        generalCmd.Options.Add(agNoAdaptOpt);
        generalCmd.SetAction(parseResult =>
        {
            int belong = parseResult.GetValue(agBelong);
            int count = parseResult.GetValue(agCount);
            bool clear = parseResult.GetValue(agClearOpt);
            bool adapt = !parseResult.GetValue(agNoAdaptOpt);
            RunArmyBatch("自动分配将领", units => units.AssignGenerals(belong, count, clear, adapt));
        });

        var rmByBelongCmd = new Command("remove-by-belong", "删除指定归属的所有单位（按单位所在格子的归属匹配）");
        var rbbBelong = new Argument<int>("belong") { Description = "归属值" };
        rmByBelongCmd.Arguments.Add(rbbBelong);
        rmByBelongCmd.SetAction(parseResult =>
        {
            int belong = parseResult.GetValue(rbbBelong);
            RunArmyBatch($"删除归属 {belong} 的单位", units => units.DeleteArmiesByBelong(belong));
        });

        var eraseCmd = new Command("erase-brush", "橡皮擦批量删除");
        var esCol = new Argument<int>("col") { Description = "中心列" };
        var esRow = new Argument<int>("row") { Description = "中心行" };
        var esRadius = new Argument<int>("radius") { Description = "半径" };
        eraseCmd.Arguments.Add(esCol);
        eraseCmd.Arguments.Add(esRow);
        eraseCmd.Arguments.Add(esRadius);
        eraseCmd.SetAction(parseResult =>
        {
            int col = parseResult.GetValue(esCol);
            int row = parseResult.GetValue(esRow);
            int radius = parseResult.GetValue(esRadius);
            RunArmyBatch("橡皮擦删除单位", units => units.EraseArmyBrush(col, row, radius));
        });

        // ---- 单位对象读写（与 Lua 的 SetArmyField 同一实现，v1/v3 内部自动分派）----
        var afCmd = new Command("fields", "列出单位可写的字段名");
        afCmd.SetAction(_ => WithModifiers((mapData, mgr) =>
            _output.WriteLine("单位可写字段：" + string.Join(", ", ObjectApi(mapData).GetArmyFields())),
            readOnly: true));

        var asCmd = new Command("show", "查看指定格子的单位对象");
        var asColArg = new Argument<int>("col") { Description = "列" };
        var asRowArg = new Argument<int>("row") { Description = "行" };
        asCmd.Arguments.Add(asColArg);
        asCmd.Arguments.Add(asRowArg);
        asCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            int col = parseResult.GetValue(asColArg);
            int row = parseResult.GetValue(asRowArg);
            var a = ObjectApi(mapData).GetArmy(col, row);
            if (a == null)
            {
                _output.WriteLine($"({col},{row}) 没有单位");
                return;
            }

            _output.WriteLine($"v{a.Version} 单位 ({a.Col},{a.Row}) 兵种={a.UnitTypeName}({a.UnitType}) 等级={a.Level} 编制={a.Organization}");
            _output.WriteLine($"  将领={a.General} 军衔={a.Rank} 方案={a.Plan} 士气={a.MoraleName} 政策={a.PolicyName}");
            _output.WriteLine($"  血量={a.CurrentHealth}/{a.MaxHealth} 加成={a.HealthBonus} 经验={a.Experience} 机动={a.Mobility} 朝向={a.DirectionName}");
            _output.WriteLine($"  据点={a.KeyPointName} 可攻击={a.CanAttack} {Loc.T("Cli.Army.ColBelong", "归属")}={a.Belong}");
            if (a.Version >= 3)
                _output.WriteLine($"  v3: 血量等级={a.HpLevel} 运输船={a.TransportShip} 仇恨={a.Hatred} 攻击目标={a.AttackTarget} 反防模式={a.AntiDefenseMode}");
        }, readOnly: true));

        var asfCmd = new Command("set-field", "按字段名改单位属性（字段名见 army fields）");
        var asfColArg = new Argument<int>("col") { Description = "列" };
        var asfRowArg = new Argument<int>("row") { Description = "行" };
        var asfFieldArg = new Argument<string>("field") { Description = "字段名，如 Level / General / Plan" };
        var asfValueArg = new Argument<double>("value") { Description = "数值（按字段宽度自动钳位）" };
        asfCmd.Arguments.Add(asfColArg);
        asfCmd.Arguments.Add(asfRowArg);
        asfCmd.Arguments.Add(asfFieldArg);
        asfCmd.Arguments.Add(asfValueArg);
        asfCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
            PrintOp(ObjectApi(mapData).SetArmyField(
                parseResult.GetValue(asfColArg), parseResult.GetValue(asfRowArg),
                parseResult.GetValue(asfFieldArg) ?? string.Empty, parseResult.GetValue(asfValueArg)))));

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(removeAllCmd);
        cmd.Subcommands.Add(applyCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(copyCmd);
        cmd.Subcommands.Add(pasteCmd);
        cmd.Subcommands.Add(setLegionCmd);
        cmd.Subcommands.Add(cyclePlanCmd);
        cmd.Subcommands.Add(setPlanCmd);
        cmd.Subcommands.Add(batchPlanCmd);
        cmd.Subcommands.Add(strengthCmd);
        cmd.Subcommands.Add(randomCmd);
        cmd.Subcommands.Add(genCmd);
        cmd.Subcommands.Add(generalCmd);
        cmd.Subcommands.Add(rmByBelongCmd);
        cmd.Subcommands.Add(eraseCmd);
        cmd.Subcommands.Add(afCmd);
        cmd.Subcommands.Add(asCmd);
        cmd.Subcommands.Add(asfCmd);

        return cmd;
    }

    /// <summary>格子上的归属值文本（0xFF = 无归属，显示为 "-"）；单位 / 陷阱的归属一律按所在格子算。</summary>
    private static string BelongTextAt(MapData mapData, int cell)
    {
        if (mapData.MapWidth <= 0 || cell >= mapData.MapWidth * mapData.MapHeight) return "-";
        var c = HexCoord.FromIndex(cell, mapData.MapWidth);
        int belong = mapData.GetBelongValue(c.Col, c.Row);
        return belong == 0xFF ? "-" : belong.ToString();
    }

    /// <summary>列表里的格子坐标 "(列,行)"；超出地图的下标（如占位用的 0xFFFF）显示为 "(#下标)"。</summary>
    private static string FormatCell(MapData mapData, int cell)
    {
        if (cell >= mapData.MapWidth * mapData.MapHeight) return $"(#{cell})";
        var c = HexCoord.FromIndex(cell, mapData.MapWidth);
        return $"({c.Col},{c.Row})";
    }

    /// <summary>按地图版本取单位模块（BtlVersion &gt;= 3 用 v3）。</summary>
    private static IArmyModule ArmyModuleFor(MapData mapData, EditModeManager mgr)
        => mapData.Header.UsesV3Records ? BoundModifier<ArmyV3Modifier>(mapData, mgr) : BoundModifier<ArmyModifier>(mapData, mgr);

    /// <summary>
    /// 优先复用 EditModeManager 里注册的模块实例（保留复制 / 粘贴等状态）；
    /// 管理器没有针对这份地图初始化时临时建一个，避免改到另一张地图。
    /// </summary>
    private static T BoundModifier<T>(MapData mapData, EditModeManager mgr) where T : ModifierBase, new()
    {
        var registered = mgr.GetModifier<T>();
        if (registered != null && registered.IsInitializedFor(mapData)) return registered;
        var fresh = new T();
        fresh.Initialize(mapData);
        return fresh;
    }

    /// <summary>单格单位命令：可撤销（非脚本批量模式下），输出模块消息。</summary>
    private void RunArmyCommand(int col, int row, string undoText, Func<IArmyModule, int, int, ModifierResult> op)
    {
        WithModifiers((mapData, mgr) =>
        {
            var units = ArmyModuleFor(mapData, mgr);
            ModifierResult result = default;
            RecordEntityChange($"{undoText} ({col},{row})", () => result = op(units, col, row));
            _output.WriteLine(result.Message);
        });
    }

    /// <summary>整图单位命令：可撤销（非脚本批量模式下），输出模块消息。</summary>
    private void RunArmyBatch(string undoText, Func<IArmyModule, ModifierResult> op)
    {
        WithModifiers((mapData, mgr) =>
        {
            var units = ArmyModuleFor(mapData, mgr);
            ModifierResult result = default;
            RecordEntityChange(undoText, () => result = op(units));
            _output.WriteLine(result.Message);
        });
    }
}
