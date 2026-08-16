using Microsoft.Win32;
using WC4MapEditor.Models;
using WC4MapEditor.Parsers.Conquest;

namespace WC4MapEditor.Views;

public class ConquestRenderScene : RenderSceneBase
{
    private readonly string? _filePath;

    public ConquestRenderScene(MainWindow window, string? filePath = null) : base(window)
    {
        _filePath = filePath;
    }

    protected override string SceneTitle => "征服地图";
    protected override string SceneType => "conquest";

    protected override MapData? LoadMapData()
    {
        string? path = _filePath;
        if (string.IsNullOrEmpty(path))
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Conquest Files|*.bin;*.dat|All Files|*.*",
                Title = "打开征服地图文件"
            };
            if (dlg.ShowDialog() != true) return null;
            path = dlg.FileName;
        }

        var parser = new ConquestParser(path);
        return BuildMapDataFromConquestParser(parser);
    }

    private static MapData BuildMapDataFromConquestParser(ConquestParser parser)
    {
        var mapData = new MapData();
        mapData.FilePath = parser.HexFilePath;
        mapData.Header = parser.Header;
        mapData.MapWidth = parser.Header.MapWidth;
        mapData.MapHeight = parser.Header.MapLength;

        mapData.InitializeTerrain(parser.Header.MapWidth, parser.Header.MapLength);

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