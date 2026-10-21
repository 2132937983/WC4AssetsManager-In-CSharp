using System.CommandLine;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 Fix 命令组（从原单文件按命令组拆出，内容未改）。
public sealed partial class CliCommandHost
{
    /// <summary>
    /// fix 命令：按玩法规则修复 btl（对齐 Java 版纠错）。
    /// <para>
    /// 与 verify 不同，这个命令<b>会修改文件</b>，所以写回前先备份为 .bak。
    /// </para>
    /// </summary>
    private Command BuildFixCommand()
    {
        var cmd = new Command("fix", "按玩法规则修复 btl 文件（会写回文件，自动备份为 .bak）");

        var fileArg = new Argument<string?>("file")
        {
            Description = "要修复的 btl 文件路径；省略则修复当前打开的地图文件",
            Arity = ArgumentArity.ZeroOrOne
        };
        cmd.Arguments.Add(fileArg);

        cmd.SetAction(parseResult =>
        {
            string? path = parseResult.GetValue(fileArg);

            // 省略路径时先取当前地图的文件路径，走与「fix <路径>」完全一致的字节层修复；
            // 之后再做只有 MapData 层能做的：首都（需要 Capitals 列表）、
            // 港口方向（需要四邻地形，复用 BuildingModifier 的智能设置）。
            bool alsoFixMapData = false;

            if (string.IsNullOrWhiteSpace(path))
            {
                string current = "";
                WithModifiers((mapData, _) => current = mapData.FilePath, readOnly: true);

                if (string.IsNullOrWhiteSpace(current))
                {
                    _output.WriteLine("当前地图还没有对应的文件（可能尚未保存或来自新建）。");
                    _output.WriteLine("用法: fix <btl文件路径>");
                    return;
                }

                path = current;
                alsoFixMapData = true;
            }

            if (!File.Exists(path))
            {
                _output.WriteLine($"文件不存在: {path}");
                return;
            }

            try
            {
                byte[] data = File.ReadAllBytes(path);

                // 规则层：建筑设施 / 单位参数 / 军团 / 归属 / 区划 等
                int count = BTLRuleChecker.Fix(data);

                // 地形组 ID 纠错（对应 Java 版 E 键 checkMapTerrainIds）。
                // 取不到合法 ID 表时返回 -1，表示未做任何修改，不要计入修复数。
                int terrainFixed = BTLFormatChecker.FixTerrainGroups(data);
                if (terrainFixed > 0) count += terrainFixed;

                File.WriteAllBytes(path, data);

                _output.WriteLine($"已修复 {count} 处，结果已写回：{path}");
                if (terrainFixed < 0)
                    _output.WriteLine("（地形组 ID 检查已跳过：未取到合法地形表）");

                if (alsoFixMapData)
                {
                    WithModifiers((mapData, mgr) =>
                    {
                        int capitals = BTLRuleChecker.FixCapitals(mapData);
                        var building = mgr.GetModifier<BuildingModifier>()!;
                        int appearances = building.SmartSetBuildingAppearance(mapData);

                        _output.WriteLine($"另外修复首都 {capitals} 处、港口方向 {appearances} 处");
                    });
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"修复失败: {ex.Message}");
            }
        });

        return cmd;
    }
}
