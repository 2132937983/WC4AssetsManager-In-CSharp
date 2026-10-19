using System.CommandLine;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 reinforcement 命令组：援军的查询、增删、整组复制粘贴、按援军设置归属、批量生成与检查。
// 全部操作走援军模块（按地图版本自动选 v1 / v3），与援军模式按键的行为一致。
public sealed partial class CliCommandHost
{
    [CliSubcommand]
    private Command BuildReinforcementCommand()
    {
        var cmd = new Command("reinforcement", "援军修改操作（v1 / v3 按地图版本自动选择）");
        cmd.Aliases.Add("reinf");

        // 优先复用 EditModeManager 里注册的模块（保留复制的援军组）；管理器没对这份地图初始化时临时建一个
        ReinforcementModifierBase ModuleFor(MapData mapData, EditModeManager mgr)
        {
            bool v3 = mapData.Header.UsesV3Records;
            ReinforcementModifierBase? module = v3 ? mgr.GetModifier<ReinforcementV3Modifier>() : mgr.GetModifier<ReinforcementModifier>();
            if (module != null && module.IsInitializedFor(mapData)) return module;
            module = v3 ? new ReinforcementV3Modifier() : new ReinforcementModifier();
            module.Initialize(mapData);
            return module;
        }

        // 改数据的子命令走实体快照记录撤销（GUI 控制台里可 Ctrl+Z）；管理器不是对这份地图初始化的（临时模块）时
        // 快照会拍到别的地图上，只直接执行
        void Change(MapData mapData, EditModeManager mgr, string description, Action apply)
        {
            if (mgr.GetModifier<ReinforcementModifier>()?.IsInitializedFor(mapData) == true)
                RecordEntityChange(description, apply);
            else
                apply();
        }

        void PrintRecords(MapData mapData, ReinforcementModifierBase module, IReadOnlyList<IReinforcementData> records, bool withIndex)
        {
            var units = ReinforcementModifierBase.BuildUnitTypeNames();
            _output.WriteLine($"{"序号",-6} {"坐标",-10} {"兵种",-14} {"等级",-5} {"编制",-5} {"将领",-7} {"所属(军团)",-12} {"回合",-6}");
            _output.WriteLine(new string('-', 76));
            for (int i = 0; i < records.Count; i++)
            {
                var r = records[i];
                var hex = HexCoord.FromIndex(r.Coordinate, mapData.MapWidth);
                string unit = units.TryGetValue(r.UnitType, out var name) ? $"{r.UnitType} {name}" : r.UnitType.ToString();
                int belong = module.BelongOfOwner(r.Owner);
                string owner = belong >= 0 ? $"{r.Owner}(#{belong})" : $"{r.Owner}(?)";
                string index = withIndex ? i.ToString() : "-";
                _output.WriteLine($"{index,-6} ({hex.Col},{hex.Row}){"",-2} {unit,-14} {r.Level,-5} {r.Organization,-5} {r.General,-7} {owner,-12} {r.SpawnRound,-6}");
            }
        }

        var listCmd = new Command("list", "列出所有援军（所属 = 军团行动顺序，括号内为军团在列表中的索引）");
        listCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var module = ModuleFor(mapData, mgr);
                var records = module.GetAllRecords();
                _output.WriteLine($"援军总数: {records.Count} (v{mapData.Header.BtlVersion})");
                if (records.Count > 0)
                {
                    _output.WriteLine();
                    PrintRecords(mapData, module, records, withIndex: true);
                }
            }, readOnly: true);
        });

        var getCmd = new Command("get", "查看格子上的援军组");
        var getCol = new Argument<int>("col") { Description = "列" };
        var getRow = new Argument<int>("row") { Description = "行" };
        getCmd.Arguments.Add(getCol);
        getCmd.Arguments.Add(getRow);
        getCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int col = parseResult.GetValue(getCol), row = parseResult.GetValue(getRow);
                var module = ModuleFor(mapData, mgr);
                var records = module.GetRecordsAt(col, row);
                _output.WriteLine($"格子 ({col},{row}) 援军数: {records.Count}");
                if (records.Count > 0) PrintRecords(mapData, module, records, withIndex: false);
            }, readOnly: true);
        });

        var addCmd = new Command("add", "在格子上新增一个援军（未指定的字段：兵种按地形随机，所属自动取所在省份省会的军团）");
        var addCol = new Argument<int>("col") { Description = "列" };
        var addRow = new Argument<int>("row") { Description = "行" };
        var addUnit = new Option<int?>("--unit", "-u") { Description = "兵种" };
        var addLevel = new Option<int?>("--level", "-l") { Description = "等级" };
        var addOrg = new Option<int?>("--org", "-o") { Description = "编制" };
        var addRound = new Option<int?>("--round", "-r") { Description = "爆兵回合" };
        var addOwner = new Option<int?>("--owner") { Description = "所属军团的行动顺序（ActionId）" };
        var addGeneral = new Option<int?>("--general", "-g") { Description = "将领 Id" };
        addCmd.Arguments.Add(addCol);
        addCmd.Arguments.Add(addRow);
        addCmd.Options.Add(addUnit);
        addCmd.Options.Add(addLevel);
        addCmd.Options.Add(addOrg);
        addCmd.Options.Add(addRound);
        addCmd.Options.Add(addOwner);
        addCmd.Options.Add(addGeneral);
        addCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int col = parseResult.GetValue(addCol), row = parseResult.GetValue(addRow);
                var module = ModuleFor(mapData, mgr);
                int index = -1;
                Change(mapData, mgr, $"新增援军 ({col},{row})", () => index = module.AddNew(col, row, r =>
                {
                    if (parseResult.GetValue(addUnit) is int unit) r.UnitType = unit;
                    if (parseResult.GetValue(addLevel) is int level) r.Level = level;
                    if (parseResult.GetValue(addOrg) is int org) r.Organization = org;
                    if (parseResult.GetValue(addRound) is int round) r.SpawnRound = round;
                    if (parseResult.GetValue(addOwner) is int owner) r.Owner = owner;
                    if (parseResult.GetValue(addGeneral) is int general) r.General = general;
                }));
                _output.WriteLine(index >= 0
                    ? $"已在 ({col},{row}) 新增援军，该格共 {index + 1} 个"
                    : $"坐标超出范围: ({col},{row})");
            });
        });

        var removeCmd = new Command("remove", "删除格子上的全部援军");
        var rmCol = new Argument<int>("col") { Description = "列" };
        var rmRow = new Argument<int>("row") { Description = "行" };
        removeCmd.Arguments.Add(rmCol);
        removeCmd.Arguments.Add(rmRow);
        removeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int col = parseResult.GetValue(rmCol), row = parseResult.GetValue(rmRow);
                var module = ModuleFor(mapData, mgr);
                ModifierResult result = default;
                Change(mapData, mgr, $"删除援军 ({col},{row})", () => result = module.Remove(col, row));
                _output.WriteLine(result.Message);
            });
        });

        var removeAllCmd = new Command("remove-all", "删除所有援军");
        removeAllCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var module = ModuleFor(mapData, mgr);
                ModifierResult result = default;
                Change(mapData, mgr, "删除所有援军", () => result = module.DeleteAllReinforcements());
                _output.WriteLine(result.Message);
            });
        });

        var copyCmd = new Command("copy", "复制格子上的援军组（同一会话内可 paste）");
        var cpCol = new Argument<int>("col") { Description = "列" };
        var cpRow = new Argument<int>("row") { Description = "行" };
        copyCmd.Arguments.Add(cpCol);
        copyCmd.Arguments.Add(cpRow);
        copyCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
                _output.WriteLine(ModuleFor(mapData, mgr).CopyGroup(parseResult.GetValue(cpCol), parseResult.GetValue(cpRow)).Message),
                readOnly: true);
        });

        var pasteCmd = new Command("paste", "把复制的援军组粘贴到格子（替换原有援军，所属与格子归属自动设置）");
        var psCol = new Argument<int>("col") { Description = "列" };
        var psRow = new Argument<int>("row") { Description = "行" };
        pasteCmd.Arguments.Add(psCol);
        pasteCmd.Arguments.Add(psRow);
        pasteCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int col = parseResult.GetValue(psCol), row = parseResult.GetValue(psRow);
                var module = ModuleFor(mapData, mgr);
                ModifierResult result = default;
                Change(mapData, mgr, $"粘贴援军组 ({col},{row})", () => result = module.PasteGroup(col, row));
                _output.WriteLine(result.Message);
            });
        });

        var syncCmd = new Command("sync-belong", "按援军设置格子归属（每格取第一个援军的所属军团，所属无效时取省会归属）");
        syncCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var module = ModuleFor(mapData, mgr);
                int changed = 0;
                Change(mapData, mgr, "为援军格子添加归属值", () => changed = module.SyncBelongsFromReinforcements());
                _output.WriteLine($"已为 {changed} 个援军格子设置归属");
            });
        });

        var randomizeCmd = new Command("randomize", "随机化援军的等级、编制（不超过最大编制）、军衔与将领品质");
        var rndBelong = new Option<int>("--belong", "-b") { Description = "只处理所属为该归属军团的援军（-1 = 全部）", DefaultValueFactory = _ => -1 };
        randomizeCmd.Options.Add(rndBelong);
        randomizeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var module = ModuleFor(mapData, mgr);
                ModifierResult result = default;
                Change(mapData, mgr, "随机化援军属性", () => result = module.RandomizeReinforcementsByBelong(parseResult.GetValue(rndBelong)));
                _output.WriteLine(result.Message);
            });
        });

        var generateCmd = new Command("generate", "按概率在空格子上生成援军（所属 = 格子归属军团）");
        var genBelong = new Argument<int>("belong") { Description = "归属（-1 = 任意有归属的格子）" };
        var genProbability = new Argument<int>("probability") { Description = "概率 0-100" };
        generateCmd.Arguments.Add(genBelong);
        generateCmd.Arguments.Add(genProbability);
        generateCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int belong = parseResult.GetValue(genBelong), probability = parseResult.GetValue(genProbability);
                var module = ModuleFor(mapData, mgr);
                ModifierResult result = default;
                Change(mapData, mgr, $"按概率生成援军 ({belong}, {probability}%)",
                    () => result = module.GenerateReinforcementsByProbability(belong, probability));
                _output.WriteLine(result.Message);
            });
        });

        var fillCmd = new Command("fill-belong", "给指定归属的每个空格子各放一个援军（所属 = 该归属军团）");
        var fillBelong = new Argument<int>("belong") { Description = "归属（军团在列表中的索引）" };
        fillCmd.Arguments.Add(fillBelong);
        fillCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int belong = parseResult.GetValue(fillBelong);
                var module = ModuleFor(mapData, mgr);
                ModifierResult result = default;
                Change(mapData, mgr, $"按归属填充援军 ({belong})", () => result = module.AddReinforcementsToBelongCells(belong));
                _output.WriteLine(result.Message);
            });
        });

        var checkCmd = new Command("check", "检查援军数据（坐标、兵种、等级、编制、所属、归属、回合、将领），只报告不修改");
        checkCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var module = ModuleFor(mapData, mgr);
                var issues = module.ValidateReinforcements();
                _output.WriteLine($"援军 {module.GetReinforcementCount()} 个，发现问题 {issues.Count} 条");
                foreach (var issue in issues)
                {
                    var hex = HexCoord.FromIndex(issue.Coordinate, Math.Max(1, mapData.MapWidth));
                    _output.WriteLine($"  #{issue.Index} ({hex.Col},{hex.Row}) {issue.Message}");
                }
            }, readOnly: true);
        });

        cmd.Subcommands.Add(listCmd);
        cmd.Subcommands.Add(getCmd);
        cmd.Subcommands.Add(addCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(removeAllCmd);
        cmd.Subcommands.Add(copyCmd);
        cmd.Subcommands.Add(pasteCmd);
        cmd.Subcommands.Add(syncCmd);
        cmd.Subcommands.Add(randomizeCmd);
        cmd.Subcommands.Add(generateCmd);
        cmd.Subcommands.Add(fillCmd);
        cmd.Subcommands.Add(checkCmd);
        return cmd;
    }
}
