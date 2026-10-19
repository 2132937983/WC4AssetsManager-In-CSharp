using Microsoft.Win32;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.Stage;

namespace WC4MapEditor.GUI.Views;

public class StageRenderScene : RenderSceneBase
{
    private readonly string? _filePath;
    private readonly bool _isNew;

    public StageRenderScene(MainWindow window, string? filePath = null, bool isNew = false) : base(window)
    {
        _filePath = filePath;
        _isNew = isNew;
    }

    protected override string SceneTitle => Loc.T("Render.SceneTitle.Stage");
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
                Filter = $"{Loc.T("Render.File.StageFilter")} (*.btl)|*.btl|{Loc.T("Common.AllFiles")} (*.*)|*.*",
                Title = Loc.T("Render.File.OpenStageTitle")
            };
            if (dlg.ShowDialog() != true) return null;
            path = dlg.FileName;
        }

        return ParseMapFileOrReport(path, StageParser.LoadToMapData);
    }

    protected override bool SaveMapData(MapData mapData, string outputPath)
        => StageParser.SaveFromMapData(mapData, outputPath);

    protected override MapData? ReloadMapData(string filePath)
        => StageParser.LoadToMapData(filePath);

    protected override void InitializeRenderers()
    {
        // 基础渲染层始终启用。其他渲染层由编辑模式控制（UpdateRenderLayersByMode），这里不去动它们：
        // 打开 / 新建 / 切换场景时也会调用本方法，而模式并没有变，以前在这里把它们统统关掉，
        // 换图后省份、建筑、单位层和选区高亮就一直消失，直到用户再切一次模式。
        RenderEngine.EnableBackgroundRender = true;
        RenderEngine.EnableTerrainsRender = true;
    }
}