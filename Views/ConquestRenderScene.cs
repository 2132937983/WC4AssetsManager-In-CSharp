using Microsoft.Win32;
using WC4MapEditor.Models;
using WC4MapEditor.Parsers.Conquest;

namespace WC4MapEditor.Views;

public class ConquestRenderScene : RenderSceneBase
{
    private readonly string? _filePath;
    private readonly bool _isNew;

    public ConquestRenderScene(MainWindow window, string? filePath = null, bool isNew = false) : base(window)
    {
        _filePath = filePath;
        _isNew = isNew;
    }

    protected override string SceneTitle => "征服地图";
    protected override string SceneType => "conquest";

    protected override MapData? LoadMapData()
    {
        if (_isNew)
        {
            var parser = new ConquestParser();
            if (!parser.CreateNew(40, 30, 2))
                return null;
            return BuildMapDataFromConquestParser(parser);
        }

        string? path = _filePath;
        if (string.IsNullOrEmpty(path))
        {
            var dlg = new OpenFileDialog
            {
                Filter = "征服文件 (*.btl)|*.btl|所有文件 (*.*)|*.*",
                Title = "打开征服地图文件"
            };
            if (dlg.ShowDialog() != true) return null;
            path = dlg.FileName;
        }

        var fileParser = new ConquestParser(path);
        return BuildMapDataFromConquestParser(fileParser);
    }

    private static MapData BuildMapDataFromConquestParser(ConquestParser parser)
    {
        var mapData = new MapData();
        mapData.FilePath = parser.HexFilePath;
        mapData.Header = parser.Header;
        // 注意：与VB版本保持一致，MapLength存宽度，MapWidth存高度
        mapData.MapWidth = parser.Header.MapLength;
        mapData.MapHeight = parser.Header.MapWidth;

        mapData.InitializeTerrain(mapData.MapWidth, mapData.MapHeight);

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

        foreach (var army in parser.GetTroopData())
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