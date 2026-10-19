using System.IO;
using Microsoft.Win32;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.Conquest;
using WC4MapEditor.Core.Parsers.World;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 征服场景：征服 BTL 画在它的世界地图 world{MapNumber}.bin 上（对齐 VB ConquestRenderScene），
/// 世界地形只读、不写回 BTL。自动找不到世界地图时让用户选；取消或选错则退回数据窗口布局（空白地形）并提示。
/// </summary>
public class ConquestRenderScene : RenderSceneBase
{
    private readonly string? _filePath;
    private readonly bool _isNew;

    public ConquestRenderScene(MainWindow window, string? filePath = null, bool isNew = false) : base(window)
    {
        _filePath = filePath;
        _isNew = isNew;
    }

    protected override string SceneTitle => Loc.T("Render.SceneTitle.Conquest");
    protected override string SceneType => "conquest";

    private static string WorldMapFilter
        => $"{Loc.T("Begin.WorldMapFileFilter")} (*.bin)|*.bin|{Loc.T("Common.AllFiles")} (*.*)|*.*";

    protected override MapData? LoadMapData()
    {
        if (_isNew)
            return ConquestParser.CreateNewMapData(40, 30, 2);

        string? path = _filePath;
        if (string.IsNullOrEmpty(path))
        {
            var dlg = new OpenFileDialog
            {
                Filter = $"{Loc.T("Render.File.ConquestFilter")} (*.btl)|*.btl|{Loc.T("Common.AllFiles")} (*.*)|*.*",
                Title = Loc.T("Render.File.OpenConquestTitle")
            };
            if (dlg.ShowDialog() != true) return null;
            path = dlg.FileName;
        }

        return LoadConquest(path);
    }

    protected override bool SaveMapData(MapData mapData, string outputPath)
        => ConquestParser.SaveFromMapData(mapData, outputPath);

    protected override MapData? ReloadMapData(string filePath)
        => LoadConquest(filePath);

    /// <summary>
    /// 新建征服（对齐 VB ConquestRenderScene.CreateNewMap）：选世界地图 → 地图编号
    /// （标准命名 world{N}.bin 直接取编号，否则询问 1-255）→ 军团数。数据窗口由世界地图尺寸决定。
    /// </summary>
    protected override async Task<MapData?> CreateNewMapDataAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("Conquest.New.PickWorldTitle"),
            Filter = WorldMapFilter,
            InitialDirectory = WorldMapLocator.SuggestPickerDirectory(null) ?? ""
        };
        if (dlg.ShowDialog() != true) return null;

        string worldPath = dlg.FileName;
        if (!WorldParser.TryReadHeader(worldPath, out _, out _))
        {
            MessageBox.Show(Loc.F("Conquest.World.NotWorldMap", worldPath), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }

        string title = Loc.T("Begin.NewConquestTitle");
        int mapNumber = WorldMapLocator.ExtractMapNumber(worldPath);
        if (mapNumber <= 0)
        {
            int? input = await AskIntegerAsync(title, Loc.T("Begin.MapNumberPrompt"), 1, 1, 255);
            if (input == null) return null;
            mapNumber = input.Value;
        }

        int? legions = await AskLegionCountAsync(title);
        if (legions == null) return null;

        var mapData = ConquestParser.CreateNewOnWorldMap(worldPath, legions.Value, mapNumber);
        WorldMapLocator.Remember(mapNumber, worldPath);
        return mapData;
    }

    /// <summary>
    /// 读征服文件并铺上它的世界地图。地图编号为 0 的文件没有对应的世界地图，按数据窗口布局显示。
    /// </summary>
    private MapData LoadConquest(string filePath)
    {
        int mapNumber = BtlTypeDetector.ReadMapNumber(filePath) ?? 0;
        if (mapNumber <= 0)
            return ConquestParser.LoadToMapData(filePath);

        string? worldPath = WorldMapLocator.Find(mapNumber, filePath) ?? PickWorldMap(mapNumber, filePath);
        if (worldPath == null)
        {
            WarnBlankTerrain(Loc.F("Conquest.World.NotFound", mapNumber));
            return ConquestParser.LoadToMapData(filePath);
        }

        try
        {
            var mapData = ConquestParser.LoadOnWorldMap(filePath, worldPath);
            WorldMapLocator.Remember(mapNumber, worldPath);
            var window = ConquestWindow.FromHeader(mapData.Header);
            WriteConsoleLine(Loc.F("Conquest.World.Loaded", IOPath.GetFileName(worldPath), mapData.MapWidth, mapData.MapHeight,
                window.Width, window.Height, window.ClipX, window.ClipY));
            return mapData;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            WarnBlankTerrain(Loc.F("Conquest.World.LoadFailed", worldPath, ex.Message));
            return ConquestParser.LoadToMapData(filePath);
        }
    }

    /// <summary>自动查找失败时让用户手动选择世界地图；取消返回 null。</summary>
    private static string? PickWorldMap(int mapNumber, string btlPath)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.F("Conquest.World.PickTitle", mapNumber),
            Filter = WorldMapFilter,
            FileName = $"world{mapNumber}.bin",
            InitialDirectory = WorldMapLocator.SuggestPickerDirectory(btlPath) ?? ""
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private void WarnBlankTerrain(string reason)
    {
        WriteConsoleLine(reason);
        MessageBox.Show(reason + Environment.NewLine + Environment.NewLine + Loc.T("Conquest.World.FallbackNote"),
            Loc.T("Common.Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    protected override void InitializeRenderers()
    {
        // 基础渲染层始终启用
        RenderEngine.EnableBackgroundRender = true;
        RenderEngine.EnableTerrainsRender = true;
        // 其他渲染层由编辑模式控制，不在此处默认启用
        RenderEngine.EnableProvinceRender = false;
        RenderEngine.EnableBuildingRender = false;
        RenderEngine.EnableArmyRender = false;
        RenderEngine.EnableTrapRender = false;
        RenderEngine.EnableSelectionRender = false;
    }
}
