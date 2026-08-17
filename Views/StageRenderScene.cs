using Microsoft.Win32;
using WC4MapEditor.Models;
using WC4MapEditor.Parsers.Stage;

namespace WC4MapEditor.Views;

public class StageRenderScene : RenderSceneBase
{
    private readonly string? _filePath;
    private readonly bool _isNew;

    public StageRenderScene(MainWindow window, string? filePath = null, bool isNew = false) : base(window)
    {
        _filePath = filePath;
        _isNew = isNew;
    }

    protected override string SceneTitle => "战役地图";
    protected override string SceneType => "stage";

    protected override MapData? LoadMapData()
    {
        if (_isNew)
        {
            var parser = new StageParser();
            if (!parser.CreateNew(40, 30, 2))
                return null;
            return BuildMapDataFromStageParser(parser);
        }

        string? path = _filePath;
        if (string.IsNullOrEmpty(path))
        {
            var dlg = new OpenFileDialog
            {
                Filter = "战役文件 (*.btl)|*.btl|所有文件 (*.*)|*.*",
                Title = "打开战役地图文件"
            };
            if (dlg.ShowDialog() != true) return null;
            path = dlg.FileName;
        }

        var fileParser = new StageParser(path);
        return BuildMapDataFromStageParser(fileParser);
    }

    private static MapData BuildMapDataFromStageParser(StageParser parser)
    {
        var mapData = new MapData();
        mapData.FilePath = parser.HexFilePath;
        mapData.Header = parser.Header;
        // 注意：与VB版本保持一致，MapLength存宽度，MapWidth存高度
        mapData.MapWidth = parser.Header.MapLength;
        mapData.MapHeight = parser.Header.MapWidth;

        mapData.InitializeTerrain(mapData.MapWidth, mapData.MapHeight);

        var terrainData = parser.GetTerrainData();
        if (terrainData.Count > 0)
        {
            for (int i = 0; i < Math.Min(terrainData.Count, mapData.TerrainCount); i++)
            {
                mapData.SetTerrain(i, terrainData[i].ToTerrainData());
            }
        }

        var provinceData = parser.GetProvinceData();
        if (provinceData.Count > 0)
        {
            for (int i = 0; i < Math.Min(provinceData.Count, mapData.TerrainCount); i++)
            {
                mapData.SetProvince(i, provinceData[i]);
            }
        }

        foreach (var building in parser.GetBuildingData())
            mapData.Buildings.Add(building);

        foreach (var army in parser.GetArmyData())
            mapData.Armies.Add(army);

        foreach (var legion in parser.GetLegionData())
            mapData.Legions.Add(legion);

        foreach (var trap in parser.GetTrapData())
            mapData.Traps.Add(trap);

        foreach (var reinforcement in parser.GetReinforcementData())
            mapData.Reinforcements.Add(reinforcement);

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