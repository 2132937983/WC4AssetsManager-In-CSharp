using Microsoft.Win32;
using WC4MapEditor.Models;
using WC4MapEditor.Parsers.Stage;

namespace WC4MapEditor.Views;

public class StageRenderScene : RenderSceneBase
{
    private readonly string? _filePath;

    public StageRenderScene(MainWindow window, string? filePath = null) : base(window)
    {
        _filePath = filePath;
    }

    protected override string SceneTitle => "战役地图";

    protected override MapData? LoadMapData()
    {
        string? path = _filePath;
        if (string.IsNullOrEmpty(path))
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Stage Files|*.bin;*.dat|All Files|*.*",
                Title = "打开战役地图文件"
            };
            if (dlg.ShowDialog() != true) return null;
            path = dlg.FileName;
        }

        var parser = new StageParser(path);
        return BuildMapDataFromStageParser(parser);
    }

    private static MapData BuildMapDataFromStageParser(StageParser parser)
    {
        var mapData = new MapData();
        mapData.FilePath = parser.HexFilePath;
        mapData.Header = parser.Header;
        mapData.MapWidth = parser.Header.MapWidth;
        mapData.MapHeight = parser.Header.MapLength;

        mapData.InitializeTerrain(parser.Header.MapWidth, parser.Header.MapLength);

        var terrainData = parser.GetTerrainData();
        if (terrainData.Count > 0)
        {
            for (int i = 0; i < Math.Min(terrainData.Count, mapData.TerrainCount); i++)
            {
                mapData.SetTerrain(i, terrainData[i].ToTerrainData());
            }
        }

        return mapData;
    }

    protected override void InitializeRenderers()
    {
        RenderEngine.EnableBackgroundRender = true;
        RenderEngine.EnableTerrainsRender = true;
        RenderEngine.EnableProvinceRender = true;
        RenderEngine.EnableBuildingRender = true;
        RenderEngine.EnableArmyRender = true;
        RenderEngine.EnableTrapRender = true;
    }
}
