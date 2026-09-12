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
            return StageParser.CreateNewMapData(40, 30, 2);

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

        return StageParser.LoadToMapData(path);
    }

    protected override bool SaveMapData(MapData mapData, string outputPath)
        => StageParser.SaveFromMapData(mapData, outputPath);

    protected override MapData? ReloadMapData(string filePath)
        => StageParser.LoadToMapData(filePath);

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