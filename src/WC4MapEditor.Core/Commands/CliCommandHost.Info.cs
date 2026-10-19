using System.CommandLine;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Parsers.BTL;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的 Info 命令组（从原单文件按命令组拆出，内容未改）。
public sealed partial class CliCommandHost
{
    private Command BuildInfoCommand()
    {
        var cmd = new Command("info", "显示地图信息");

        cmd.SetAction(parseResult =>
        {
            WithModifiers((mapData, mgr) =>
            {
                var header = mapData.Header;
                _output.WriteLine("========== 地图信息 ==========");
                _output.WriteLine($"  地图尺寸:    {header.MapWidth} x {header.MapLength}");
                _output.WriteLine($"  总格子数:    {header.TotalTiles}");
                _output.WriteLine($"  军团数:      {mapData.Legions.Count}");
                _output.WriteLine($"  建筑数:      {mapData.Buildings.Count}");
                int cellCount = mapData.MapWidth * mapData.MapHeight;
                _output.WriteLine($"  省份数据:    {cellCount} 格");
                _output.WriteLine($"  归属数据:    {mapData.Belongs.Count} 格");
                _output.WriteLine($"  地形数据:    {cellCount} 格");

                int provinceCount = 0;
                for (int i = 0; i < cellCount; i++)
                    if (mapData.GetProvinceRef(i).ProvinceValue != 0) provinceCount++;
                int belongCount = 0;
                for (int i = 0; i < mapData.Belongs.Count; i++)
                    if (mapData.GetBelongValueByIndex(i) != 0xFF) belongCount++;
                _output.WriteLine($"  有效省份:    {provinceCount} 格");
                _output.WriteLine($"  有效归属:    {belongCount} 格");
                _output.WriteLine("==============================");
            }, readOnly: true);
        });

        return cmd;
    }

    private bool _batchMode;
    private int _batchCommandCount;

    public void BeginBatchMode()
    {
        _batchMode = true;
        _batchCommandCount = 0;
        var ctx = CommandManager.Instance.GetContext();
        ctx?.MapData?.RebuildBuildingCoordIndex();
    }

    public void EndBatchMode()
    {
        _batchMode = false;
        var ctx = CommandManager.Instance.GetContext();
        ctx?.MapData?.InvalidateBuildingCoordIndex();
        if (_batchCommandCount > 0)
            DataModified?.Invoke();
        _batchCommandCount = 0;
    }

    /// <summary>
    /// 对象属性读写的统一入口：直接复用 Lua 那套实现
    /// （<c>MapScriptApi</c>：字段白名单 + 按类型钳位 + 转发 Modifier）。
    /// <para>
    /// CLI 和 Lua 走同一实现，就不会出现"命令能改、脚本不能改"或两边校验口径不一致的问题；
    /// 新增字段时也只改一处白名单。
    /// </para>
    /// </summary>
    private static WC4MapEditor.Core.Scripting.MapScriptApi ObjectApi(MapData mapData)
        => new(mapData, EditModeManager.Instance);

    /// <summary>把对象操作结果打到输出：成功给消息，失败必须给出原因（不能静默）。</summary>
    private void PrintOp(WC4MapEditor.Scripting.ScriptOperationResult result)
        => _output.WriteLine(result.Success ? result.Message : $"失败：{result.Message}");

    private void WithModifiers(Action<MapData, EditModeManager> action, bool readOnly = false)
    {
        var ctx = CommandManager.Instance.GetContext();
        if (ctx?.MapData == null)
        {
            _output.WriteLine("地图数据未加载");
            return;
        }

        var mapData = ctx.MapData;
        var mgr = EditModeManager.Instance;

        if (!mgr.IsInitialized)
            mgr.Initialize(mapData);

        action(mapData, mgr);

        if (!readOnly)
        {
            if (_batchMode)
                _batchCommandCount++;
            else
                DataModified?.Invoke();
        }
    }

    public event Action? DataModified;
}
