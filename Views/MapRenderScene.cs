using Microsoft.Win32;
using WC4MapEditor.Models;
using WC4MapEditor.Parsers.World;

namespace WC4MapEditor.Views;

public class MapRenderScene : RenderSceneBase
{
    private readonly string? _filePath;
    private readonly bool _isNew;

    public MapRenderScene(MainWindow window, string? filePath = null, bool isNew = false) : base(window)
    {
        _filePath = filePath;
        _isNew = isNew;
    }

    protected override string SceneTitle => "地形地图";
    protected override string SceneType => "world";

    protected override MapData? LoadMapData()
    {
        if (_isNew)
        {
            var mapData = WorldParser.CreateNew(40, 30);
            return mapData;
        }

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

        var loadedData = WorldParser.LoadFromFile(path);
        if (loadedData == null) return null;

        loadedData.FilePath = path;
        return loadedData;
    }

    protected override void InitializeRenderers()
    {
        RenderEngine.EnableBackgroundRender = true;
        RenderEngine.EnableTerrainsRender = true;
    }
}