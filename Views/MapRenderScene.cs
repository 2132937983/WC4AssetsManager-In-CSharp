using Microsoft.Win32;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.World;

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

    protected override bool SaveMapData(MapData mapData, string outputPath)
    {
        try
        {
            WorldParser.SaveToFile(mapData, outputPath);
            return true;
        }
        catch (Exception ex)
        {
            // 不要静默吞掉：保存失败的原因（文件被 Excel 等程序锁定、无写权限、磁盘满等）
            // 必须留下日志。用 Trace.WriteLine 而非 Debug.WriteLine：
            // .NET Core 下 Debug 输出不会进 Trace.Listeners，控制台就看不到。
            System.Diagnostics.Trace.WriteLine($"[MapRenderScene] 保存失败: {outputPath}{Environment.NewLine}" +
                                               $"  {ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    protected override MapData? ReloadMapData(string filePath)
    {
        var loaded = WorldParser.LoadFromFile(filePath);
        if (loaded != null) loaded.FilePath = filePath;
        return loaded;
    }

    protected override void InitializeRenderers()
    {
        RenderEngine.EnableBackgroundRender = true;
        RenderEngine.EnableTerrainsRender = true;
    }
}