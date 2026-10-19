using System.CommandLine;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 Belong 命令组。
public sealed partial class CliCommandHost
{
    private Command BuildBelongCommand()
    {
        var cmd = new Command("belong", "归属修改操作");

        var clearCmd = new Command("clear", "清空所有归属");
        clearCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = 0;
                RecordBelongChange("清空所有归属", () => count = belong.ClearAllBelongs());
                _output.WriteLine($"已清空 {count} 个格子的归属");
            });
        });

        var setCmd = new Command("set", "设置指定格子归属");
        var setColArg = new Argument<int>("col") { Description = "列" };
        var setRowArg = new Argument<int>("row") { Description = "行" };
        var setBelongArg = new Argument<int>("belong") { Description = "归属值(0-255, 255=无归属)" };
        setCmd.Arguments.Add(setColArg);
        setCmd.Arguments.Add(setRowArg);
        setCmd.Arguments.Add(setBelongArg);
        setCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(setColArg);
                int row = parseResult.GetValue(setRowArg);
                int belongVal = Math.Clamp(parseResult.GetValue(setBelongArg), 0, 255);
                ModifierResult result = default;
                RecordSingleBelongChange(col, row, $"设置归属 ({col},{row})",
                    () => result = belong.SetBelongByCountryId(col, row, belongVal));
                _output.WriteLine(result.Message ?? $"已设置归属 ({col},{row}) = {belongVal}");
            });
        });

        var fillCmd = new Command("fill-from-legion", "按军团ID填充归属到建筑格子");
        var fillLegionIdArg = new Argument<int>("legionId") { Description = "军团ID" };
        fillCmd.Arguments.Add(fillLegionIdArg);
        fillCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int legionId = parseResult.GetValue(fillLegionIdArg);
                int count = 0;
                RecordBelongChange($"按军团填充归属 ({legionId})", () =>
                {
                    foreach (var b in mapData.Buildings)
                    {
                        int col = b.Coordinate % mapData.MapWidth;
                        int row = b.Coordinate / mapData.MapWidth;
                        if (mapData.GetBelongValue(col, row) != legionId && mapData.SetBelongValue(col, row, legionId))
                            count++;
                    }
                });
                _output.WriteLine($"已将 {count} 个建筑格子归属设为军团 {legionId}");
            });
        });

        var statsCmd = new Command("stats", "归属统计");
        statsCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belongCounts = new Dictionary<int, int>();
                for (int i = 0; i < mapData.Belongs.Count; i++)
                {
                    int val = mapData.GetBelongValueByIndex(i);
                    if (val != 0xFF)
                    {
                        if (!belongCounts.ContainsKey(val)) belongCounts[val] = 0;
                        belongCounts[val]++;
                    }
                }
                _output.WriteLine("归属值分布:");
                foreach (var kv in belongCounts.OrderBy(x => x.Key))
                {
                    string name = "";
                    var legionInfo = mapData.ResolveLegionByBelong(kv.Key);
                    if (legionInfo != null) name = $" ({legionInfo.Value.DisplayName})";
                    _output.WriteLine($"  归属={kv.Key}{name}: {kv.Value} 格");
                }
                int emptyCount = mapData.Belongs.Count - belongCounts.Values.Sum();
                _output.WriteLine($"  无归属(0xFF): {emptyCount} 格");
            }, readOnly: true);
        });

        var removeCmd = new Command("remove", "删除指定格子归属");
        var removeColArg = new Argument<int>("col") { Description = "列" };
        var removeRowArg = new Argument<int>("row") { Description = "行" };
        removeCmd.Arguments.Add(removeColArg);
        removeCmd.Arguments.Add(removeRowArg);
        removeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(removeColArg);
                int row = parseResult.GetValue(removeRowArg);
                ModifierResult result = default;
                RecordSingleBelongChange(col, row, $"删除归属 ({col},{row})",
                    () => result = belong.Remove(col, row));
                _output.WriteLine(result.Message ?? $"已删除归属 ({col},{row})");
            });
        });

        var copyCmd = new Command("copy", "复制指定格子归属值");
        var copyColArg = new Argument<int>("col") { Description = "列" };
        var copyRowArg = new Argument<int>("row") { Description = "行" };
        copyCmd.Arguments.Add(copyColArg);
        copyCmd.Arguments.Add(copyRowArg);
        copyCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(copyColArg);
                int row = parseResult.GetValue(copyRowArg);
                var result = belong.CopyBelongValue(col, row);
                _output.WriteLine(result.Message ?? $"已复制归属 ({col},{row})");
            }, readOnly: true);
        });

        var pasteCmd = new Command("paste", "粘贴归属值到指定格子");
        var pasteColArg = new Argument<int>("col") { Description = "列" };
        var pasteRowArg = new Argument<int>("row") { Description = "行" };
        pasteCmd.Arguments.Add(pasteColArg);
        pasteCmd.Arguments.Add(pasteRowArg);
        pasteCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(pasteColArg);
                int row = parseResult.GetValue(pasteRowArg);
                ModifierResult result = default;
                RecordSingleBelongChange(col, row, $"粘贴归属 ({col},{row})",
                    () => result = belong.PasteBelongValue(col, row));
                _output.WriteLine(result.Message ?? $"已粘贴归属 ({col},{row})");
            });
        });

        var cleanOrphanCmd = new Command("clean-orphan", "清理孤立归属(无建筑/单位的格子)");
        cleanOrphanCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = 0;
                RecordBelongChange("清理孤立归属", () => count = belong.CleanOrphanBelongs());
                _output.WriteLine($"已清理 {count} 个孤立归属");
            });
        });

        var randomizeCmd = new Command("randomize", "把所有已有归属的格子随机分给地图上的军团（无归属格子不动）");
        randomizeCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = 0;
                RecordBelongChange("随机化所有归属",
                    () => count = belong.RandomizeBelongs(new Random()));
                _output.WriteLine($"已随机化 {count} 个格子的归属");
            });
        });

        // 注意：这个命令改的是"已经有归属的格子"（0xFF 保持不动；归属 0 是第 1 个军团，同样会被改），
        // 并不是按省区操作 —— 需要按省区请用 belong set-province，
        // 需要连无归属格子一起刷请用 belong set-region。
        var batchSetCmd = new Command("batch-set", "把所有【已有归属】的格子统一改为目标ID（无归属格子不动）");
        var batchTargetArg = new Argument<int>("targetId") { Description = "目标国家ID(0-255)" };
        batchSetCmd.Arguments.Add(batchTargetArg);
        batchSetCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int targetId = Math.Clamp(parseResult.GetValue(batchTargetArg), 0, 255);
                int count = 0;
                RecordBelongChange($"批量设置归属 ({targetId})",
                    () => count = belong.BatchSetBelongByProvince(targetId));
                _output.WriteLine($"已将 {count} 个已有归属的格子改为国家ID {targetId}");
            });
        });

        var removeBorderEntitiesCmd = new Command("remove-border-entities", "删除地图最外圈格子上的建筑、单位与陷阱");
        removeBorderEntitiesCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                BelongModifier.BorderRemoval removal = default;
                RecordEntityChange("删除地图边缘的建筑、单位与陷阱",
                    () => removal = belong.RemoveBorderEntities());
                _output.WriteLine($"已删除地图边缘的 {removal.Buildings} 个建筑、{removal.Armies + removal.ArmiesV3} 个单位、{removal.Traps} 个陷阱");
            });
        });

        var removeBorderArmiesCmd = new Command("remove-border-armies", "删除地图最外圈格子上的单位");
        removeBorderArmiesCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = 0;
                RecordEntityChange("删除地图边缘的单位",
                    () => count = belong.RemoveBorderArmies());
                _output.WriteLine($"已删除地图边缘的 {count} 个单位");
            });
        });

        // ---- 补齐 Modifier 已有、但此前没有命令入口的归属能力 ----

        var getCmd = new Command("get", "查询指定格子的归属值");
        var getColArg = new Argument<int>("col") { Description = "列" };
        var getRowArg = new Argument<int>("row") { Description = "行" };
        getCmd.Arguments.Add(getColArg);
        getCmd.Arguments.Add(getRowArg);
        getCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int col = parseResult.GetValue(getColArg);
                int row = parseResult.GetValue(getRowArg);

                int? value = belong.GetBelongValue(col, row);
                if (!value.HasValue)
                {
                    _output.WriteLine($"坐标 ({col},{row}) 超出范围");
                    return;
                }

                if (value.Value == 0xFF)
                {
                    _output.WriteLine($"({col},{row}) 归属= 无归属(0xFF)");
                    return;
                }

                var legionInfo = mapData.ResolveLegionByBelong(value.Value);
                string name = legionInfo != null ? $" ({legionInfo.Value.DisplayName})" : "";
                _output.WriteLine($"({col},{row}) 归属= {value.Value}{name}");
            }, readOnly: true);
        });

        var replaceCmd = new Command("replace", "批量替换归属值（把 oldId 的格子全部改成 newId）");
        var replaceOldArg = new Argument<int>("oldId") { Description = "原归属值(0-255)" };
        var replaceNewArg = new Argument<int>("newId") { Description = "新归属值(0-255)" };
        replaceCmd.Arguments.Add(replaceOldArg);
        replaceCmd.Arguments.Add(replaceNewArg);
        replaceCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int oldId = Math.Clamp(parseResult.GetValue(replaceOldArg), 0, 255);
                int newId = Math.Clamp(parseResult.GetValue(replaceNewArg), 0, 255);

                int count = 0;
                RecordBelongChange($"批量替换归属 ({oldId} → {newId})",
                    () => count = belong.ReplaceBelong(oldId, newId));
                _output.WriteLine($"已将 {count} 个格子的归属从 {oldId} 改为 {newId}");
            });
        });

        var randomizeByCmd = new Command("randomize-by", "随机化指定归属值的格子");
        var randomizeByIdArg = new Argument<int>("belongId") { Description = "目标归属值(0-255)" };
        randomizeByCmd.Arguments.Add(randomizeByIdArg);
        randomizeByCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var belong = mgr.GetModifier<BelongModifier>()!;
                int targetId = Math.Clamp(parseResult.GetValue(randomizeByIdArg), 0, 255);

                int count = 0;
                RecordBelongChange($"随机化归属 ({targetId})",
                    () => count = belong.RandomizeBelongByTargetId(targetId, new Random()));
                _output.WriteLine($"已随机化 {count} 个归属为 {targetId} 的格子");
            });
        });

        var setByCityCmd = new Command("set-by-city", "按城市名称批量设置归属（匹配建筑名称）");
        var cityNameArg = new Argument<string>("cityName") { Description = "城市名称，如 北京" };
        var cityBelongArg = new Argument<int>("belongId") { Description = "归属值(0-255)" };
        setByCityCmd.Arguments.Add(cityNameArg);
        setByCityCmd.Arguments.Add(cityBelongArg);
        setByCityCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                string cityName = parseResult.GetValue(cityNameArg)!;
                int belongId = Math.Clamp(parseResult.GetValue(cityBelongArg), 0, 255);

                // 城市名 → 城市ID：名称必须已经在城市名称表里
                //（即此前用 building add -n 写入过的名字）
                int? cityId = ConfigManager.Instance.GetStringTableParser().FindCityIdByName(cityName);
                if (!cityId.HasValue)
                {
                    _output.WriteLine($"没有名为「{cityName}」的城市：该名称还没写入过城市名称表");
                    return;
                }

                var belong = mgr.GetModifier<BelongModifier>()!;
                int count = 0;
                RecordBelongChange($"按城市名设置归属 ({cityName} → {belongId})", () =>
                {
                    foreach (var building in mapData.Buildings)
                    {
                        if (building.Name != cityId.Value) continue;
                        int col = building.Coordinate % mapData.MapWidth;
                        int row = building.Coordinate / mapData.MapWidth;
                        belong.SetBelongByCountryId(col, row, belongId);
                        count++;
                    }
                });

                _output.WriteLine(count > 0
                    ? $"已将「{cityName}」所在的 {count} 个格子归属设为 {belongId}"
                    : $"地图上没有名为「{cityName}」的建筑");
            });
        });

        var setRegionCmd = new Command("set-region", "把矩形区域内的格子批量设为指定归属");
        var regionCol1Arg = new Argument<int>("col1") { Description = "角点1 列" };
        var regionRow1Arg = new Argument<int>("row1") { Description = "角点1 行" };
        var regionCol2Arg = new Argument<int>("col2") { Description = "角点2 列" };
        var regionRow2Arg = new Argument<int>("row2") { Description = "角点2 行" };
        var regionBelongArg = new Argument<int>("belongId") { Description = "归属值(0-255)" };
        setRegionCmd.Arguments.Add(regionCol1Arg);
        setRegionCmd.Arguments.Add(regionRow1Arg);
        setRegionCmd.Arguments.Add(regionCol2Arg);
        setRegionCmd.Arguments.Add(regionRow2Arg);
        setRegionCmd.Arguments.Add(regionBelongArg);
        setRegionCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int col1 = Math.Min(parseResult.GetValue(regionCol1Arg), parseResult.GetValue(regionCol2Arg));
                int col2 = Math.Max(parseResult.GetValue(regionCol1Arg), parseResult.GetValue(regionCol2Arg));
                int row1 = Math.Min(parseResult.GetValue(regionRow1Arg), parseResult.GetValue(regionRow2Arg));
                int row2 = Math.Max(parseResult.GetValue(regionRow1Arg), parseResult.GetValue(regionRow2Arg));
                int belongId = Math.Clamp(parseResult.GetValue(regionBelongArg), 0, 255);

                if (col1 < 0 || row1 < 0 || col2 >= mapData.MapWidth || row2 >= mapData.MapHeight)
                {
                    _output.WriteLine($"区域超出地图范围（地图 {mapData.MapWidth}x{mapData.MapHeight}）");
                    return;
                }

                int count = 0;
                RecordBelongChange($"区域设置归属 ({col1},{row1})-({col2},{row2}) → {belongId}", () =>
                {
                    for (int row = row1; row <= row2; row++)
                        for (int col = col1; col <= col2; col++)
                            if (mapData.SetBelongValue(col, row, belongId)) count++;
                });
                _output.WriteLine($"已将 ({col1},{row1})-({col2},{row2}) 共 {count} 个格子的归属设为 {belongId}");
            });
        });

        var setProvinceCmd = new Command("set-province", "按省会格子定位省区，把整片省区设为指定归属");
        var provinceColArg = new Argument<int>("capitalCol") { Description = "省会所在格子的列" };
        var provinceRowArg = new Argument<int>("capitalRow") { Description = "省会所在格子的行" };
        var provinceBelongArg = new Argument<int>("belongId") { Description = "归属值(0-255)" };
        setProvinceCmd.Arguments.Add(provinceColArg);
        setProvinceCmd.Arguments.Add(provinceRowArg);
        setProvinceCmd.Arguments.Add(provinceBelongArg);
        setProvinceCmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                int capitalCol = parseResult.GetValue(provinceColArg);
                int capitalRow = parseResult.GetValue(provinceRowArg);
                int belongId = Math.Clamp(parseResult.GetValue(provinceBelongArg), 0, 255);

                if (capitalCol < 0 || capitalRow < 0 ||
                    capitalCol >= mapData.MapWidth || capitalRow >= mapData.MapHeight)
                {
                    _output.WriteLine($"坐标 ({capitalCol},{capitalRow}) 超出范围");
                    return;
                }

                // 省区值约定为省会格子的线性索引（地图索引）；归属要按格子坐标写，
                // 征服地图的归属数组只覆盖裁剪区，地图索引不能直接当归属下标用
                int capitalIndex = capitalRow * mapData.MapWidth + capitalCol;
                int width = mapData.MapWidth;
                int total = width * mapData.MapHeight;
                int count = 0;

                RecordBelongChange($"省区设置归属 (省会 {capitalCol},{capitalRow} → {belongId})", () =>
                {
                    for (int i = 0; i < total; i++)
                    {
                        if (mapData.GetProvinceRef(i).ProvinceValue != capitalIndex) continue;
                        if (mapData.SetBelongValue(i % width, i / width, belongId)) count++;
                    }
                });

                _output.WriteLine(count > 0
                    ? $"已将该省区的 {count} 个格子归属设为 {belongId}"
                    : $"({capitalCol},{capitalRow}) 不是一个省会格子（该格省区值不等于自身索引）");
            });
        });

        cmd.Subcommands.Add(clearCmd);
        cmd.Subcommands.Add(setCmd);
        cmd.Subcommands.Add(removeCmd);
        cmd.Subcommands.Add(copyCmd);
        cmd.Subcommands.Add(pasteCmd);
        cmd.Subcommands.Add(fillCmd);
        cmd.Subcommands.Add(statsCmd);
        cmd.Subcommands.Add(cleanOrphanCmd);
        cmd.Subcommands.Add(randomizeCmd);
        cmd.Subcommands.Add(batchSetCmd);
        cmd.Subcommands.Add(removeBorderEntitiesCmd);
        cmd.Subcommands.Add(removeBorderArmiesCmd);
        cmd.Subcommands.Add(getCmd);
        cmd.Subcommands.Add(replaceCmd);
        cmd.Subcommands.Add(randomizeByCmd);
        cmd.Subcommands.Add(setByCityCmd);
        cmd.Subcommands.Add(setRegionCmd);
        cmd.Subcommands.Add(setProvinceCmd);

        return cmd;
    }

    /// <summary>
    /// 归属批量变更的统一入口：非脚本模式下记录撤销（按归属数组下标快照，裁剪地图上撤销不会错位，
    /// 见 <see cref="BelongModifier.RecordBulkChange"/>）。
    /// <para>
    /// 脚本批量执行（<c>run xxx.zme</c>）时直接执行、不记撤销 —— 一份脚本动辄几百条命令，
    /// 每条都存一份全图归属快照会撑爆撤销栈，而且脚本本身属于一次性操作。
    /// </para>
    /// </summary>
    private void RecordBelongChange(string description, Action applyChange)
    {
        var map = CommandManager.Instance.GetContext()?.MapData;
        if (_batchMode || map == null)
        {
            applyChange();
            return;
        }

        BelongModifier.RecordBulkChange(EditModeManager.Instance, map, description, applyChange);
    }

    /// <summary>
    /// 单格归属变更的撤销入口。与 <see cref="RecordBelongChange"/> 同理，
    /// 脚本批量执行时不记撤销。
    /// </summary>
    private void RecordSingleBelongChange(int col, int row, string description, Action applyChange)
    {
        if (_batchMode)
        {
            applyChange();
            return;
        }

        EditModeManager.Instance.RecordBelongChange(col, row, description, applyChange);
    }

    /// <summary>
    /// 实体（建筑/单位/陷阱/援军……）变更的撤销入口，走全实体快照
    /// （以前走建筑快照，只覆盖建筑/省份/归属，删单位、删陷阱撤销不回来）。
    /// 脚本批量执行时不记撤销。
    /// </summary>
    private void RecordEntityChange(string description, Action applyChange)
    {
        if (_batchMode)
        {
            applyChange();
            return;
        }

        EditModeManager.Instance.RecordEntitiesSnapshot(description, applyChange);
    }
}
