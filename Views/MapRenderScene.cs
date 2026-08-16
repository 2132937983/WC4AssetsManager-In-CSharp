using Microsoft.Win32;
using WC4MapEditor.Models;
using WC4MapEditor.Parsers.World;

namespace WC4MapEditor.Views;

public class MapRenderScene : RenderSceneBase
{
    private readonly string? _filePath;

    public MapRenderScene(MainWindow window, string? filePath = null) : base(window)
    {
        _filePath = filePath;
    }

    protected override string SceneTitle => "地形地图";
    protected override string SceneType => "world";

    protected override MapData? LoadMapData()
    {
        string? path = _filePath;
        if (string.IsNullOrEmpty(path))
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Map Files|*.bin;*.dat|All Files|*.*",
                Title = "打开地图文件"
            };
            if (dlg.ShowDialog() != true) return null;
            path = dlg.FileName;
        }

        var mapData = WorldParser.LoadFromFile(path);
        if (mapData == null) return null;

        mapData.FilePath = path;
        return mapData;
    }

    protected override void InitializeRenderers()
    {
        RenderEngine.EnableBackgroundRender = true;
        RenderEngine.EnableTerrainsRender = true;
    }
}