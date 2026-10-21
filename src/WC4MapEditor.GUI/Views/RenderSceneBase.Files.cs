using System.IO;
using Microsoft.Win32;
using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Parsers.Conquest;
using WC4MapEditor.Core.Parsers.Stage;
using WC4MapEditor.Core.Parsers.World;
using WC4MapEditor.Core.Services;
using WC4MapEditor.GUI.Services;
using WC4MapEditor.GUI.Views.Assist;
using WC4MapEditor.GUI.Views.Dialogs;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 渲染场景的文件与场景记录：打开 / 保存 / 新建 / 场景页签切换，以及原地换图。
/// <list type="bullet">
/// <item>.btl 按文件头区分战役 / 征服（<see cref="BtlTypeDetector"/>），不看文件名；</item>
/// <item>保存一律写场景自己的格式（<see cref="SaveMapData"/>）—— 征服按战役格式写会带上地形块、整个文件损坏；
/// 只有战役场景另存为 .bin 才是"导出地形为世界地图"；</item>
/// <item>每次换图都登记场景记录：当前地图（含未保存的编辑）缓存进场景列表，从场景页签切回时从缓存恢复。</item>
/// </list>
/// </summary>
public abstract partial class RenderSceneBase
{
    /// <summary>.btl 按文件头选择解析器（地图编号大于 0 = 征服布局，没有地形块）。</summary>
    private static MapData LoadBtlByHeader(string filePath)
        => BtlTypeDetector.Detect(filePath) == BTLFileType.Conquest
            ? ConquestParser.LoadToMapData(filePath)
            : StageParser.LoadToMapData(filePath);

    private static bool IsWorldMapExtension(string filePath)
        => IOPath.GetExtension(filePath).ToLowerInvariant() is ".bin" or ".dat";

    /// <summary>战役场景另存为 .bin = 把地形导出成世界地图；征服的地形本来就是只读的世界底图，不提供导出。</summary>
    private bool OffersTerrainExport => SceneType == "stage";

    private RenderSceneType CurrentRenderSceneType => RenderSceneManager.MapSceneTypeToRenderSceneType(SceneType);

    /// <summary>
    /// OnLoaded 用的初始加载。由场景切换创建的场景（已分配场景号）先读该场景的磁盘缓存 ——
    /// 切换前的未保存编辑都在缓存里，直接重读原文件会把它们丢掉；没有缓存时才走子类的常规加载。
    /// 加载结果登记回场景记录，否则下次缓存时会拿原文件覆盖缓存。
    /// </summary>
    private MapData? LoadInitialMapData()
    {
        MapData? mapData;
        try
        {
            if (_sceneId < 0)
            {
                mapData = LoadMapData();
            }
            else
            {
                var sceneManager = RenderSceneManager.Instance;
                mapData = (sceneManager.HasDiskCache(_sceneId) ? sceneManager.LoadSceneFromDisk(_sceneId) : null) ?? LoadMapData();
                if (mapData != null)
                    sceneManager.SetSceneMapData(_sceneId, mapData);
            }
        }
        catch (Exception ex)
        {
            // 损坏 / 截断的文件让解析器抛异常：提示原因后返回 null，OnLoaded 随即退回主界面，而不是让异常冲出加载流程
            MessageBox.Show(Loc.F("Render.File.OpenFailed", ex.Message), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }

        // 相机在 OnLoaded 里随后才建好并居中整张地图，等加载流程走完再对准数据窗口
        if (mapData is ConquestWorldMapData)
            Dispatcher.BeginInvoke(() => FocusCameraOnDataWindow(), DispatcherPriority.Loaded);
        return mapData;
    }

    /// <summary>
    /// 征服铺在世界底图上时，相机对准数据窗口中心：
    /// 很多征服布局的关卡（event501xx 等）只占世界地图的一小块，居中整张世界地图会看不到它。
    /// </summary>
    private void FocusCameraOnDataWindow()
    {
        if (_mapData is not ConquestWorldMapData mapData) return;

        var window = ConquestWindow.FromHeader(mapData.Header);
        if (window.CellCount == 0) return;

        var (col, row) = window.ToWorld(window.Height / 2 * window.Width + window.Width / 2, mapData.MapWidth);
        MoveCameraToHex(col, row);
    }

    /// <summary>写一行到调试控制台（子类加载时报告底图等信息用）。</summary>
    protected void WriteConsoleLine(string message) => _debugConsole.WriteLine(message);

    // ==================== 保存 ====================

    private void OnQuickSave()
    {
        if (_mapData == null) return;

        // 已有文件路径直接按场景格式保存；从未保存过（新建）则走另存为
        if (string.IsNullOrEmpty(_mapData.FilePath))
            SaveButton_Click(this, new RoutedEventArgs());
        else
            SaveSceneFile(_mapData.FilePath);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var mapData = _mapData;
        if (mapData == null) return;

        var dialog = new SaveFileDialog
        {
            Title = Loc.T("Render.File.SaveTitle"),
            Filter = BuildSaveFilter(),
            FilterIndex = 1,
            FileName = DefaultSaveFileName(mapData),
            InitialDirectory = DefaultFileDirectory(mapData)
        };

        if (dialog.ShowDialog() == true)
        {
            if (OffersTerrainExport && IsWorldMapExtension(dialog.FileName))
                ExportTerrain(mapData, dialog.FileName);
            else
                SaveSceneFile(dialog.FileName);
        }
        RestoreFocus();
    }

    /// <summary>
    /// 按场景自己的格式保存。成功后更新文件路径、场景记录（另存为后切走再切回仍指向新文件）与场景标签，
    /// 并作废磁盘缓存（否则切回来会加载到比文件更旧的缓存，看起来像"保存没生效"）。
    /// </summary>
    private bool SaveSceneFile(string filePath, bool notify = true)
    {
        var mapData = _mapData;
        if (mapData == null) return false;

        try
        {
            if (!SaveMapData(mapData, filePath))
            {
                if (notify)
                    MessageBox.Show(Loc.T("Render.Save.Failed"), Loc.T("Render.Save.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                _debugConsole.WriteLine(Loc.T("Render.Console.SaveFailed"));
                return false;
            }
        }
        catch (Exception ex)
        {
            // 解析器把"文件被占用 / 无写权限 / 磁盘已满"等真实原因原样抛出，这里展示给用户
            if (notify)
                MessageBox.Show(Loc.F("Render.Save.FailedWithReason", ex.Message), Loc.T("Render.Save.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            _debugConsole.WriteLine(Loc.F("Render.Console.SaveFailedWithReason", ex.Message));
            return false;
        }

        mapData.FilePath = filePath;
        _fileStateManager.MarkSaved();

        if (_sceneId >= 0)
        {
            var sceneManager = RenderSceneManager.Instance;
            sceneManager.UpdateSceneFile(_sceneId, filePath);
            sceneManager.InvalidateSceneCache(_sceneId);
            if (_sceneNameLabel != null && sceneManager.GetScene(_sceneId) is { } scene)
                _sceneNameLabel.Text = scene.SceneName;
        }

        if (notify)
            MessageBox.Show(Loc.F("Render.Save.SavedTo", filePath), Loc.T("Render.Save.SuccessTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        _debugConsole.WriteLine(Loc.F("Render.Console.SavedTo", filePath));
        return true;
    }

    /// <summary>战役地形导出为世界地图 .bin；场景仍对应原来的 .btl，文件路径与场景记录都不变。</summary>
    private void ExportTerrain(MapData mapData, string filePath)
    {
        try
        {
            WorldParser.SaveToFile(mapData, filePath);
            MessageBox.Show(Loc.F("SceneFile.TerrainExported", filePath), Loc.T("Render.Save.SuccessTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            _debugConsole.WriteLine(Loc.F("SceneFile.TerrainExported", filePath));
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Render.Save.FailedWithReason", ex.Message), Loc.T("Render.Save.ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            _debugConsole.WriteLine(Loc.F("Render.Console.SaveFailedWithReason", ex.Message));
        }
    }

    /// <summary>场景自己的格式排第一；世界地图只存 .bin，征服只存 .btl，战役另可导出地形 .bin。</summary>
    private string BuildSaveFilter()
    {
        string all = $"{Loc.T("Common.AllFiles")}|*.*";
        if (SceneType == "world")
            return $"{Loc.T("Render.File.BinOnly")}|*.bin|{all}";

        string btl = $"{Loc.T("Render.File.BtlOnly")}|*.btl";
        return OffersTerrainExport
            ? $"{btl}|{Loc.T("SceneFile.ExportTerrainFilter")}|*.bin|{all}"
            : $"{btl}|{all}";
    }

    private string DefaultSaveFileName(MapData mapData)
    {
        if (!string.IsNullOrEmpty(mapData.FilePath))
            return IOPath.GetFileName(mapData.FilePath);

        return SceneType switch
        {
            // 征服的网格是世界地图尺寸，文件名里用数据窗口尺寸（与 VB 新建征服的命名一致）
            "conquest" => $"conquest_{mapData.Header.MapNumber}_{mapData.Header.MapLength}x{mapData.Header.MapWidth}.btl",
            "stage" => $"stage_{mapData.MapWidth}x{mapData.MapHeight}.btl",
            _ => $"new_map_{mapData.MapWidth}x{mapData.MapHeight}.bin"
        };
    }

    /// <summary>文件对话框起始目录：当前地图所在目录，没有则用当前工作目录。</summary>
    private static string DefaultFileDirectory(MapData? mapData)
    {
        string filePath = mapData?.FilePath ?? "";
        string? dir = filePath.Length > 0 ? IOPath.GetDirectoryName(filePath) : null;
        return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : Directory.GetCurrentDirectory();
    }

    // ==================== 打开 ====================

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        string btl = $"{Loc.T("Render.File.BtlFilter")} (*.btl)|*.btl";
        string bin = $"{Loc.T("Render.File.MapFilter")} (*.bin)|*.bin";
        string all = $"{Loc.T("Common.AllFiles")} (*.*)|*.*";
        var dialog = new OpenFileDialog
        {
            Title = Loc.T("Render.File.OpenTitle"),
            // 战役 / 征服场景里 BTL 排第一
            Filter = SceneType == "world" ? $"{bin}|{btl}|{all}" : $"{btl}|{bin}|{all}",
            InitialDirectory = DefaultFileDirectory(_mapData)
        };

        if (dialog.ShowDialog() == true)
            OpenMapFile(dialog.FileName);
        RestoreFocus();
    }

    /// <summary>
    /// 打开文件：同类型 → 登记新场景并原地换图（当前地图留在场景列表里）；
    /// 其它类型 → 登记场景后交给主窗口切到对应场景（会先缓存当前场景）。
    /// </summary>
    private void OpenMapFile(string filePath)
    {
        try
        {
            var sceneManager = RenderSceneManager.Instance;
            var fileType = RenderSceneManager.GetSceneTypeByFilePath(filePath);
            string sceneName = Loc.F("Render.SceneNumbered", sceneManager.SceneCount + 1, IOPath.GetFileNameWithoutExtension(filePath));

            if (fileType != CurrentRenderSceneType)
            {
                sceneManager.RequestSceneSwitch(sceneManager.CreateScene(sceneName, filePath, fileType));
                return;
            }

            var mapData = ReloadMapData(filePath);
            if (mapData == null)
            {
                MessageBox.Show(Loc.T("Render.File.LoadFailed"), Loc.T("Common.Error"));
                return;
            }

            mapData.FilePath = filePath;
            InstallAsNewScene(mapData, sceneName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Render.File.OpenFailed", ex.Message), Loc.T("Common.Error"));
        }
    }

    // ==================== 新建 ====================

    private async void NewButton_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(Loc.T("SceneFile.NewMapConfirm"), Loc.T("Render.File.NewMapTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm == MessageBoxResult.Yes)
        {
            try
            {
                var mapData = await CreateNewMapDataAsync();
                if (mapData != null)
                {
                    string title = Loc.F("SceneFile.NewMapName", SceneTitle);
                    InstallAsNewScene(mapData, Loc.F("Render.SceneNumbered", RenderSceneManager.Instance.SceneCount + 1, title));
                    _debugConsole.WriteLine(Loc.F("SceneFile.NewMapCreated", title, mapData.MapWidth, mapData.MapHeight));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.F("SceneFile.NewMapFailed", ex.Message), Loc.T("Common.Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        RestoreFocus();
    }

    /// <summary>
    /// 新建地图的数据（New 按钮）。默认：世界地图询问宽、高（10-500，与 new_map 命令一致）；
    /// 战役询问宽、高（1-500）与军团数（对齐 VB StageRenderScene.CreateNewMap）。
    /// 征服场景重写为"选世界地图 → 地图编号 → 军团数"。
    /// 返回 null = 用户取消。
    /// </summary>
    protected virtual async Task<MapData?> CreateNewMapDataAsync()
    {
        if (SceneType == "world")
        {
            var worldSize = await AskMapSizeAsync(Loc.T("Begin.NewMapTitle"), 100, 10, 500);
            return worldSize == null ? null : WorldParser.CreateNew(worldSize.Value.Width, worldSize.Value.Height);
        }

        string title = Loc.T("Begin.NewCampaignTitle");
        var size = await AskMapSizeAsync(title, 10, 1, 500);
        if (size == null) return null;

        int? legions = await AskLegionCountAsync(title);
        if (legions == null) return null;

        // 与开始界面"新建战役"同一套生成规则（平原地形、空的第 2/3 层、按军团轮流分配归属、每军团一个首都），
        // 结果与保存后重新打开的文件完全一致
        var parser = new StageParser();
        if (!parser.CreateNew(size.Value.Width, size.Value.Height, legions.Value))
            throw new InvalidOperationException(parser.LastError ?? Loc.T("Begin.CreateCampaignFailed"));

        var mapData = new MapData();
        StageParser.PopulateMapData(parser, mapData);
        mapData.FilePath = string.Empty;
        return mapData;
    }

    /// <summary>询问地图宽、高（覆盖层双输入框，带范围校验）；取消或输入无效返回 null。</summary>
    protected async Task<(int Width, int Height)?> AskMapSizeAsync(string title, int defaultSize, int min, int max)
    {
        var input = await DoubleInputDialog.ShowDialog(Window, title,
            Loc.T("Begin.MapWidthPrompt"), Loc.T("Begin.MapHeightPrompt"),
            defaultSize.ToString(), defaultSize.ToString(),
            minValue1: min, maxValue1: max, minValue2: min, maxValue2: max);
        if (input == null) return null;

        if (int.TryParse(input.Value.Value1, out int width) && int.TryParse(input.Value.Value2, out int height) &&
            width >= min && width <= max && height >= min && height <= max)
            return (width, height);

        MessageBox.Show(Loc.F("Common.InputRange", min, max), Loc.T("Common.InputError"), MessageBoxButton.OK, MessageBoxImage.Warning);
        return null;
    }

    /// <summary>询问军团数（1-255：归属值是一个字节，0xFF 表示无归属）；取消或输入无效返回 null。</summary>
    protected Task<int?> AskLegionCountAsync(string title)
        => AskIntegerAsync(title, Loc.T("Begin.LegionCountPrompt"), 2, 1, 255);

    /// <summary>询问一个整数（覆盖层输入框，带范围校验）；取消或输入无效返回 null。</summary>
    protected async Task<int?> AskIntegerAsync(string title, string description, int defaultValue, int min, int max)
    {
        using var dialog = new SingleInputDialog(Window)
        {
            Title = title,
            Description = description,
            DefaultValue = defaultValue.ToString(),
            MinValue = min,
            MaxValue = max
        };
        string? text = await dialog.ShowAsync();
        if (text == null) return null;

        if (int.TryParse(text, out int value) && value >= min && value <= max)
            return value;

        MessageBox.Show(Loc.F("Common.InputRange", min, max), Loc.T("Common.InputError"), MessageBoxButton.OK, MessageBoxImage.Warning);
        return null;
    }

    // ==================== 场景页签切换 ====================

    private void SwitchToScene(int targetSceneId)
    {
        try
        {
            var sceneManager = RenderSceneManager.Instance;
            var targetScene = sceneManager.GetScene(targetSceneId);
            if (targetScene == null)
            {
                MessageBox.Show(Loc.T("Render.SceneNotFound"), Loc.T("Common.Error"));
                return;
            }

            // 类型不同：由主窗口换成对应的场景类，新场景从缓存恢复数据
            if (targetScene.SceneType != CurrentRenderSceneType)
            {
                sceneManager.RequestSceneSwitch(targetSceneId);
                return;
            }

            // 先读目标再缓存当前：读失败时当前场景保持原样，不会落到"已释放内存却还在编辑"的状态
            var mapData = sceneManager.LoadSceneFromDisk(targetSceneId);
            if (mapData == null)
            {
                MessageBox.Show(Loc.T("Render.SceneLoadFailed"), Loc.T("Common.Error"));
                return;
            }

            if (_sceneId >= 0)
            {
                sceneManager.CacheSceneToDisk(_sceneId);
                sceneManager.ReleaseSceneMemory(_sceneId);
            }

            ReplaceMapData(mapData, targetSceneId);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Render.SceneSwitchFailed", ex.Message), Loc.T("Common.Error"));
        }
    }

    // ==================== 原地换图 ====================

    /// <summary>当前地图（含未保存的编辑）缓存进场景列表，为新地图登记一个场景并原地换上。</summary>
    private void InstallAsNewScene(MapData mapData, string sceneName)
    {
        var sceneManager = RenderSceneManager.Instance;
        if (_sceneId >= 0)
        {
            sceneManager.CacheSceneToDisk(_sceneId);
            sceneManager.ReleaseSceneMemory(_sceneId);
        }

        int sceneId = sceneManager.CreateScene(sceneName, mapData.FilePath, CurrentRenderSceneType);
        ReplaceMapData(mapData, sceneId);
    }

    /// <summary>
    /// 原地换图（打开同类文件 / 新建 / 同类场景切换）。按顺序重建：相机、渲染缓存、模块与撤销、
    /// 控制台命令上下文、格子信息、选区、场景记录与标签，最后按当前模式重新应用渲染层 ——
    /// <see cref="InitializeRenderers"/> 会把省份/建筑/单位等层先关掉，不重新应用的话换图后这些层一直不显示。
    /// </summary>
    private void ReplaceMapData(MapData mapData, int sceneId)
    {
        // 编辑窗口绑定的是旧地图，继续开着会改到已经换下去的数据
        _assistEditors?.CloseAll();

        _mapData = mapData;
        _camera = new Camera
        {
            MapWidth = mapData.MapWidth,
            MapHeight = mapData.MapHeight,
            ViewportWidth = (int)_skElement.ActualWidth,
            ViewportHeight = (int)_skElement.ActualHeight
        };
        _camera.CenterOnMap();
        FocusCameraOnDataWindow();

        InitializeRenderers();
        _renderEngine.InvalidateTerrainCache();
        _renderEngine.InvalidateProvinceCache();
        _renderEngine.InvalidateLegionDomainCache();
        _renderEngine.InvalidateBelongFlagCache();
        _renderEngine.InvalidateViewLayerCache();
        _renderEngine.InvalidateCoastCacheFull(mapData);
        _renderEngine.PreloadBelongFlagAtlas(mapData);

        _editModeManager.Initialize(mapData);
        _editModeManager.SetSceneType(SceneType);
        _fileStateManager.OpenFile(mapData, mapData.FilePath, SceneType);
        _commandManager.SetContext(new CommandContext { MapData = mapData });
        HexInfoService.Instance.SetMapData(mapData);
        _hexSelector.ClearSelection();

        var sceneManager = RenderSceneManager.Instance;
        sceneManager.SetSceneMapData(sceneId, mapData);
        sceneManager.ActivateScene(sceneId);
        _sceneId = sceneId;
        if (_sceneNameLabel != null)
            _sceneNameLabel.Text = sceneManager.GetScene(sceneId)?.SceneName ?? SceneTitle;

        UpdateRenderLayersByMode(_editModeManager.CurrentMode);
        RefreshBoundaryOverlay();
        UpdateOverlayFromEditMode();
        _skElement.InvalidateVisual();
    }
}
