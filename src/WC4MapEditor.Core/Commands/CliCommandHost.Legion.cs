using System.CommandLine;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 Legion 命令组。改数据的命令都记录撤销（GUI 控制台里可 Ctrl+Z）。
public sealed partial class CliCommandHost
{
    private Command BuildLegionCommand()
    {
        var cmd = new Command("legion", "军团修改操作");

        var listCmd = new Command("list", "列出所有军团");
        listCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legions = mapData.Legions;
                _output.WriteLine($"军团总数: {legions.Count}");
                _output.WriteLine();
                // 国家名与军团编辑窗口一致（CountrySettings.json → string table 的 country_{id} → 国家{id}）；
                // 索引列是 belong 要填的值 —— 归属存的是军团在列表里的位置，不是国家ID。
                _output.WriteLine($"{"索引",-5} {"国家ID",-7} {"国家名",-24} {"颜色",-10} {"玩家控制",-8}");
                _output.WriteLine(new string('-', 62));
                for (int i = 0; i < legions.Count; i++)
                {
                    var legion = legions[i];
                    _output.WriteLine($"{i,-5} {legion.CountryId,-7} {CountryNameProvider.Get(legion.CountryId),-24} ({legion.ColorR:X2}{legion.ColorG:X2}{legion.ColorB:X2})    {(legion.IsPlayer ? "是" : "否"),-8}");
                }
            }, readOnly: true);
        });

        var getCmd = new Command("get", "查询军团详情");
        var getIdArg = new Argument<int>("legionId") { Description = "军团ID" };
        getCmd.Arguments.Add(getIdArg);
        getCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int id = parseResult.GetValue(getIdArg);
                var l = legion.GetLegion(id);
                if (l == null) { _output.WriteLine($"军团 {id} 不存在"); return; }
                var v = l.Value;
                _output.WriteLine($"军团 {v.CountryId}（{CountryNameProvider.Get(v.CountryId)}）  列表索引={mapData.FindLegionIndex(v.CountryId)}");
                _output.WriteLine($"  ActionId={v.ActionId}, 玩家控制={(v.IsPlayer ? "是" : "否")}（控制值 {v.IsPlayerControlled}）, 阵营={v.Camp}");
                _output.WriteLine($"  颜色=({v.ColorR},{v.ColorG},{v.ColorB})");
                _output.WriteLine($"  经济={v.InitialEconomy}, 工业={v.InitialIndustry}, 科技={v.InitialTech}");
            }, readOnly: true);
        });

        var getByActionCmd = new Command("get-by-action", "按ActionId查询军团");
        var gbaArg = new Argument<int>("actionId") { Description = "ActionId" };
        getByActionCmd.Arguments.Add(gbaArg);
        getByActionCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int actionId = parseResult.GetValue(gbaArg);
                var l = legion.GetLegionByActionId(actionId);
                if (l == null) { _output.WriteLine($"ActionId={actionId} 的军团不存在"); return; }
                var v = l.Value;
                _output.WriteLine($"军团 {v.CountryId}: ActionId={v.ActionId}, {v.DisplayName}");
            }, readOnly: true);
        });

        var applyCmd = new Command("apply", "设置军团领域到指定格子");
        var apCol = new Argument<int>("col") { Description = "列" };
        var apRow = new Argument<int>("row") { Description = "行" };
        applyCmd.Arguments.Add(apCol);
        applyCmd.Arguments.Add(apRow);
        applyCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int col = parseResult.GetValue(apCol);
                int row = parseResult.GetValue(apRow);
                ModifierResult result = default;
                RecordSingleBelongChange(col, row, $"设置军团领域 ({col},{row})", () => result = legion.Apply(col, row));
                _output.WriteLine(result.Message ?? $"已设置军团领域 ({col},{row})");
            });
        });

        var removeCmd = new Command("remove", "清除指定格子军团领域");
        var rmCol = new Argument<int>("col") { Description = "列" };
        var rmRow = new Argument<int>("row") { Description = "行" };
        removeCmd.Arguments.Add(rmCol);
        removeCmd.Arguments.Add(rmRow);
        removeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int col = parseResult.GetValue(rmCol);
                int row = parseResult.GetValue(rmRow);
                ModifierResult result = default;
                RecordSingleBelongChange(col, row, $"清除军团领域 ({col},{row})", () => result = legion.Remove(col, row));
                _output.WriteLine(result.Message ?? $"已清除军团领域 ({col},{row})");
            });
        });

        var selectCmd = new Command("select", "选中当前军团（第 N 个，1 起；军团模式左键设置领域用它）");
        var selId = new Argument<int>("legionId") { Description = "军团序号（1 起，按地图军团数钳位）" };
        selectCmd.Arguments.Add(selId);
        selectCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                legion.SelectedLegionId = parseResult.GetValue(selId);
                _output.WriteLine($"已选中军团 {legion.SelectedLegionId}");
            });
        });

        var addCmd = new Command("add", "追加一个默认军团");
        addCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                ModifierResult result = default;
                RecordEntityChange("追加军团", () => result = legion.AddLegion());
                _output.WriteLine(result.Message ?? "已追加军团");
            });
        });

        var removeAtCmd = new Command("remove-at", "按索引删除军团（同步改写归属与单位等的所属军团序号）");
        var raIdx = new Argument<int>("index") { Description = "军团列表索引(0-based)" };
        removeAtCmd.Arguments.Add(raIdx);
        removeAtCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int index = parseResult.GetValue(raIdx);
                ModifierResult result = default;
                RecordEntityChange($"删除军团 {index}", () => result = legion.RemoveLegionAt(index));
                _output.WriteLine(result.Message ?? $"已删除索引 {index} 的军团");
            });
        });

        var setColorCmd = new Command("set-color", "设置军团颜色");
        var setColorIdArg = new Argument<int>("legionId") { Description = "军团ID" };
        var setColorRArg = new Argument<int>("r") { Description = "红色(0-255)" };
        var setColorGArg = new Argument<int>("g") { Description = "绿色(0-255)" };
        var setColorBArg = new Argument<int>("b") { Description = "蓝色(0-255)" };
        setColorCmd.Arguments.Add(setColorIdArg);
        setColorCmd.Arguments.Add(setColorRArg);
        setColorCmd.Arguments.Add(setColorGArg);
        setColorCmd.Arguments.Add(setColorBArg);
        setColorCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int legionId = parseResult.GetValue(setColorIdArg);
                byte r = (byte)Math.Clamp(parseResult.GetValue(setColorRArg), 0, 255);
                byte g = (byte)Math.Clamp(parseResult.GetValue(setColorGArg), 0, 255);
                byte b = (byte)Math.Clamp(parseResult.GetValue(setColorBArg), 0, 255);
                ModifierResult result = default;
                RecordEntityChange($"设置军团 {legionId} 颜色", () => result = legion.SetLegionColor(legionId, r, g, b));
                _output.WriteLine(result.Message ?? $"已设置军团 {legionId} 颜色为 ({r},{g},{b})");
            });
        });

        var setActionCmd = new Command("set-action", "设置军团ActionId");
        var setActionIdArg = new Argument<int>("legionId") { Description = "军团ID" };
        var setActionValArg = new Argument<int>("actionId") { Description = "ActionId" };
        setActionCmd.Arguments.Add(setActionIdArg);
        setActionCmd.Arguments.Add(setActionValArg);
        setActionCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int legionId = parseResult.GetValue(setActionIdArg);
                int actionId = parseResult.GetValue(setActionValArg);
                ModifierResult result = default;
                RecordEntityChange($"设置军团 {legionId} 行动顺序", () => result = legion.SetLegionActionId(legionId, actionId));
                _output.WriteLine(result.Message ?? $"已设置军团 {legionId} ActionId={actionId}");
            });
        });

        var setPlayerCmd = new Command("set-player", "按行动顺序设置玩家控制军团（控制值 0 = 玩家，其余改为 1）");
        var spActionId = new Argument<int>("actionId") { Description = "ActionId" };
        setPlayerCmd.Arguments.Add(spActionId);
        setPlayerCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int actionId = parseResult.GetValue(spActionId);
                ModifierResult result = default;
                RecordEntityChange("设置玩家军团", () => result = legion.SetPlayerControlledLegion(actionId));
                _output.WriteLine(result.Message ?? $"已设置玩家控制军团 ActionId={actionId}");
            });
        });

        var defaultColorsCmd = new Command("apply-default-colors", "应用默认颜色到所有军团");
        defaultColorsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                ModifierResult result = default;
                RecordEntityChange("应用默认军团颜色", () => result = legion.ApplyDefaultColorsToAllLegions());
                _output.WriteLine(result.Message ?? "已应用默认颜色");
            });
        });

        var settingsColorsCmd = new Command("apply-settings-colors", "从配置更新所有军团颜色");
        settingsColorsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                ModifierResult result = default;
                RecordEntityChange("从配置更新军团颜色", () => result = legion.ApplyAllLegionsColorFromSettings());
                _output.WriteLine(result.Message ?? "已从配置更新颜色");
            });
        });

        var randomLevelsCmd = new Command("randomize-levels", "随机化所有军团等级与经济");
        randomLevelsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                ModifierResult result = default;
                RecordEntityChange("随机化军团等级与经济", () => result = legion.RandomizeAllLegionLevels());
                _output.WriteLine(result.Message ?? "已随机化军团等级");
            });
        });

        var rebuildCmd = new Command("rebuild-action-belong", "按列表顺序重排所有军团的行动顺序（ActionId = 序号 + 1；归属是军团序号，不受影响）");
        rebuildCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                ModifierResult result = default;
                RecordEntityChange("重排行动顺序", () => result = legion.UpdateAllLegionsActionIdAndBelong());
                _output.WriteLine(result.Message ?? "已重排行动顺序");
            });
        });

        var setEconomyCmd = new Command("set-economy", "批量设置军团经济");
        var econVal = new Argument<int>("value") { Description = "经济值" };
        setEconomyCmd.Arguments.Add(econVal);
        setEconomyCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int val = parseResult.GetValue(econVal);
                ModifierResult result = default;
                RecordEntityChange($"统一军团经济 = {val}", () => result = legion.SetAllLegionsEconomy(val));
                _output.WriteLine(result.Message ?? $"已设置所有军团经济={val}");
            });
        });

        var setIndustryCmd = new Command("set-industry", "批量设置军团工业");
        var indVal = new Argument<int>("value") { Description = "工业值" };
        setIndustryCmd.Arguments.Add(indVal);
        setIndustryCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int val = parseResult.GetValue(indVal);
                ModifierResult result = default;
                RecordEntityChange($"统一军团工业 = {val}", () => result = legion.SetAllLegionsIndustry(val));
                _output.WriteLine(result.Message ?? $"已设置所有军团工业={val}");
            });
        });

        var setTechCmd = new Command("set-tech", "批量设置军团科技");
        var techVal = new Argument<int>("value") { Description = "科技值" };
        setTechCmd.Arguments.Add(techVal);
        setTechCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var legion = mgr.GetModifier<LegionModifier>()!;
                int val = parseResult.GetValue(techVal);
                ModifierResult result = default;
                RecordEntityChange($"统一军团科技 = {val}", () => result = legion.SetAllLegionsTech(val));
                _output.WriteLine(result.Message ?? $"已设置所有军团科技={val}");
            });
        });

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(getCmd);
        cmd.Subcommands.Add(getByActionCmd);
        cmd.Subcommands.Add(applyCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(selectCmd);
        cmd.Subcommands.Add(addCmd);
        cmd.Subcommands.Add(removeAtCmd);
        cmd.Subcommands.Add(setColorCmd);
        cmd.Subcommands.Add(setActionCmd);
        cmd.Subcommands.Add(setPlayerCmd);
        cmd.Subcommands.Add(defaultColorsCmd);
        cmd.Subcommands.Add(settingsColorsCmd);
        cmd.Subcommands.Add(randomLevelsCmd);
        cmd.Subcommands.Add(rebuildCmd);
        // ---- 军团对象读写（与 Lua 的 SetLegionField 同一实现）----
        // 注意：这里按"列表索引"定位（索引 == 归属值），不是按国家ID —— 归属数据存的就是索引。
        var lfCmd = new Command("fields", "列出军团可写的字段名");
        lfCmd.SetAction(_ => WithModifiers((mapData, mgr) =>
            _output.WriteLine("军团可写字段：" + string.Join(", ", ObjectApi(mapData).GetLegionFields())),
            readOnly: true));

        var lsaCmd = new Command("show-at", "按列表索引查看军团（索引即归属值）");
        var lsaIndexArg = new Argument<int>("index") { Description = "军团列表索引（= 归属值）" };
        lsaCmd.Arguments.Add(lsaIndexArg);
        lsaCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            int idx = parseResult.GetValue(lsaIndexArg);
            var l = ObjectApi(mapData).GetLegionAt(idx);
            if (l == null)
            {
                _output.WriteLine($"军团索引 {idx} 超出范围（当前共 {mapData.Legions.Count} 个）");
                return;
            }

            _output.WriteLine($"军团[{idx}] 国家ID={l.CountryId} 行动顺序={l.ActionId} 玩家控制={(l.IsPlayer ? "是" : "否")} 阵营={l.Camp} 战败条件={l.DefeatCondition}");
            _output.WriteLine($"  初始 经济={l.InitialEconomy} 工业={l.InitialIndustry} 科技={l.InitialTech} 初始科技等级={l.InitialTechLevel} AI={l.CountryAiBehavior}");
            _output.WriteLine($"  颜色=({l.ColorR},{l.ColorG},{l.ColorB}) 血率={l.CountryHpRate:F2} 税率={l.CountryTaxRate:F2}");
            _output.WriteLine($"  核弹 原子={l.AtomicBombCount} 氢={l.HydrogenBombCount} 三相弹={l.NeutronBombCount} 反物质={l.AntimatterBombCount}");
            _output.WriteLine($"  平均等级 陆={l.AverageArmyLevel:F1} 海={l.AverageNavyLevel:F1} 空={l.AverageAirForceLevel:F1} 防御={l.AverageDefenseLevel:F1}");
        }, readOnly: true));

        var lsCmd = new Command("set-field", "按字段名改军团属性（字段名见 legion fields）");
        var lsIndexArg = new Argument<int>("index") { Description = "军团列表索引（= 归属值）" };
        var lsFieldArg = new Argument<string>("field") { Description = "字段名，如 InitialEconomy / ColorR / ArmorLevel" };
        var lsValueArg = new Argument<double>("value") { Description = "数值（按字段宽度自动钳位，浮点字段可带小数）" };
        lsCmd.Arguments.Add(lsIndexArg);
        lsCmd.Arguments.Add(lsFieldArg);
        lsCmd.Arguments.Add(lsValueArg);
        lsCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
            PrintOp(RecordLegionOp("修改军团字段", () => ObjectApi(mapData).SetLegionField(
                parseResult.GetValue(lsIndexArg), parseResult.GetValue(lsFieldArg) ?? string.Empty, parseResult.GetValue(lsValueArg))))));

        // 定位键（CountryId / ActionId）单独走带校验的命令：它们不在 set-field 的白名单里
        var sCountryCmd = new Command("set-country-at", "按列表索引设置国家ID（会校验唯一性）");
        var scIndexArg = new Argument<int>("index") { Description = "军团列表索引" };
        var scCountryArg = new Argument<int>("countryId") { Description = "国家ID（不能与其它军团重复）" };
        sCountryCmd.Arguments.Add(scIndexArg);
        sCountryCmd.Arguments.Add(scCountryArg);
        sCountryCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
            PrintOp(RecordLegionOp("设置军团国家ID", () => ObjectApi(mapData).SetLegionCountryIdAt(
                parseResult.GetValue(scIndexArg), parseResult.GetValue(scCountryArg))))));

        var sActionAtCmd = new Command("set-action-at", "按列表索引设置 ActionId（行动顺序）");
        var saIndexAtArg = new Argument<int>("index") { Description = "军团列表索引" };
        var saActionArg = new Argument<int>("actionId") { Description = "行动顺序" };
        sActionAtCmd.Arguments.Add(saIndexAtArg);
        sActionAtCmd.Arguments.Add(saActionArg);
        sActionAtCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
            PrintOp(RecordLegionOp("设置军团行动顺序", () => ObjectApi(mapData).SetLegionActionIdAt(
                parseResult.GetValue(saIndexAtArg), parseResult.GetValue(saActionArg))))));

        var moveCmd = new Command("move", "移动军团到新位置（同步改写归属与单位等的所属军团序号，默认按新顺序重排行动顺序）");
        var moveFromArg = new Argument<int>("from") { Description = "原列表索引（0 起）" };
        var moveToArg = new Argument<int>("to") { Description = "新列表索引（0 起）" };
        var keepActionOpt = new Option<bool>("--keep-action-ids") { Description = "保留原有行动顺序，不按新顺序重排" };
        moveCmd.Arguments.Add(moveFromArg);
        moveCmd.Arguments.Add(moveToArg);
        moveCmd.Options.Add(keepActionOpt);
        moveCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var legion = mgr.GetModifier<LegionModifier>()!;
            int from = parseResult.GetValue(moveFromArg);
            int to = parseResult.GetValue(moveToArg);
            bool renumber = !parseResult.GetValue(keepActionOpt);
            ModifierResult result = default;
            RecordEntityChange($"移动军团 {from} → {to}", () => result = legion.MoveLegion(from, to, renumber));
            _output.WriteLine(result.Success ? result.Message : $"失败：{result.Message}");
        }));

        var setPlayerAtCmd = new Command("set-player-at", "按列表索引设置玩家军团（-1 = 不设玩家军团，全部改为 AI）");
        var spIndexArg = new Argument<int>("index") { Description = "军团列表索引（0 起），-1 表示没有玩家军团" };
        setPlayerAtCmd.Arguments.Add(spIndexArg);
        setPlayerAtCmd.SetAction(parseResult => WithModifiers((mapData, mgr) =>
        {
            var legion = mgr.GetModifier<LegionModifier>()!;
            int index = parseResult.GetValue(spIndexArg);
            ModifierResult result = default;
            RecordEntityChange("设置玩家军团", () => result = legion.SetPlayerControlledLegionAt(Math.Max(-1, index)));
            _output.WriteLine(result.Success ? result.Message : $"失败：{result.Message}");
        }));

        cmd.Subcommands.Add(setEconomyCmd);
        cmd.Subcommands.Add(setIndustryCmd);
        cmd.Subcommands.Add(setTechCmd);
        cmd.Subcommands.Add(lfCmd);
        cmd.Subcommands.Add(lsaCmd);
        cmd.Subcommands.Add(lsCmd);
        cmd.Subcommands.Add(sCountryCmd);
        cmd.Subcommands.Add(sActionAtCmd);
        cmd.Subcommands.Add(moveCmd);
        cmd.Subcommands.Add(setPlayerAtCmd);

        return cmd;
    }

    /// <summary>对象读写类军团命令（走 Lua 同一实现）的撤销入口：结果原样返回给调用方打印。</summary>
    private WC4MapEditor.Scripting.ScriptOperationResult RecordLegionOp(string description,
        Func<WC4MapEditor.Scripting.ScriptOperationResult> operation)
    {
        WC4MapEditor.Scripting.ScriptOperationResult result = default;
        RecordEntityChange(description, () => result = operation());
        return result;
    }
}
