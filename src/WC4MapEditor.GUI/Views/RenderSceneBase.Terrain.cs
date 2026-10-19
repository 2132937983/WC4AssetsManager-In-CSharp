using WC4MapEditor.Core.Brush;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.GUI.Views.Assist;
using WC4MapEditor.GUI.Views.Dialogs;

namespace WC4MapEditor.GUI.Views;

// 地形绘制模式的场景部分：地形画笔（H 键窗口、右键绘制）、离开模式时关掉画笔、背景图识别地形（P 键），
// 以及调试控制台的地形 / 经纬度命令（greening、random_*、create_coast、process_ocean_layer2、export_hd、scale_map、resize_map、geo_*）。
public abstract partial class RenderSceneBase
{
    #region 地形画笔

    private void ToggleTerrainBrush()
    {
        var terrain = _editModeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return;

        terrain.BrushActive = !terrain.BrushActive;

        if (terrain.BrushActive)
        {
            _brushSettingsWindow ??= CreateTerrainBrushWindow(terrain);
            _brushSettingsWindow.Show();
            _editModeManager.RaiseStatusMessage(Loc.T("Render.Status.BrushOnTerrain"));
        }
        else
        {
            _brushSettingsWindow?.PlayFadeOutAndHide();
            _renderEngine.HideBrushPreview();
            _skElement.InvalidateVisual();
            _editModeManager.RaiseStatusMessage(Loc.T("Render.Status.BrushOff"));
        }
    }

    /// <summary>
    /// 创建地形画笔窗口：先把画笔当前的大小 / 形状 / 地形 / 变体灌进窗口、再订阅设置变化，
    /// 窗口初始化列表时就不会把画笔重置成 0（以前每次进入地形模式按 H 都会这样）。
    /// </summary>
    private BrushSettingsWindow CreateTerrainBrushWindow(TerrainModifier terrain)
    {
        var window = new BrushSettingsWindow
        {
            BrushSize = terrain.BrushSize,
            BrushShape = terrain.BrushShape
        };
        window.LoadTerrainBrush(terrain.BrushTerrainType, terrain.BrushDecoration);
        window.InitializeMaskTerrainList();
        window.BrushSettingsChanged += OnBrushSettingsChanged;

        var mainWindow = Window;
        window.Owner = mainWindow;
        window.Left = mainWindow.Left + mainWindow.ActualWidth - window.Width - 20;
        window.Top = mainWindow.Top + 80;
        return window;
    }

    /// <summary>
    /// 画笔窗口里的改动写回地形画笔。刻意不碰编辑层：窗口里没有编辑层控件，编辑层统一由 Z 键（以及 CLI 的 --layer）控制。
    /// </summary>
    private void ApplyTerrainBrushSettings(BrushSettingsEventArgs e)
    {
        var terrain = _editModeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return;

        terrain.BrushSize = e.BrushSize;
        terrain.BrushTerrainType = e.TerrainType;
        terrain.BrushDecoration = e.TerrainVariant;
        terrain.BrushShape = e.BrushShape;
    }

    /// <summary>[ ] 键改了画笔大小：同步窗口里的数值并刷新画笔预览。</summary>
    private void RefreshTerrainBrushSize(int col, int row)
    {
        var terrain = _editModeManager.GetModifier<TerrainModifier>();
        if (terrain == null) return;

        if (_brushSettingsWindow != null && _brushSettingsWindow.IsVisible)
            _brushSettingsWindow.BrushSize = terrain.BrushSize;

        if (terrain.BrushActive)
        {
            _renderEngine.SetBrushPreview(
                col, row,
                terrain.BrushSize, terrain.BrushShape,
                _camera.ZoomLevel, _camera.OffsetX, _camera.OffsetY,
                true);
            _skElement.InvalidateVisual();
        }
    }

    /// <summary>
    /// 在 (<paramref name="col"/>, <paramref name="row"/>) 落一笔地形画笔。有多格选区时只画在选区里
    /// （对齐 VB；单击总会留下一格选中，所以只在多选时限制）。
    /// </summary>
    private void PaintTerrainBrushAt(int col, int row)
    {
        var terrain = _editModeManager.GetModifier<TerrainModifier>();
        if (terrain == null || _mapData == null) return;

        var maskIds = _brushSettingsWindow?.IsMaskEnabled == true ? _brushSettingsWindow.MaskedTerrainIds : null;
        bool maskInclude = _brushSettingsWindow?.MaskIncludeMode ?? true;
        var region = _hexSelector.SelectedHexes.Count > 1 ? _hexSelector.SelectedHexes : null;
        terrain.PaintWithBrushMasked(col, row, maskIds, maskInclude, region);

        _renderEngine.InvalidateTerrainCache();
        _renderEngine.InvalidateCoastCacheRegion(_mapData, col, row, terrain.BrushSize + 2);
        _skElement.InvalidateVisual();
    }

    /// <summary>
    /// 离开模式时把该模式的画笔一起关掉（地形 / 省份 / 归属 / 单位画笔都适用）：画笔窗口随模式关闭了，
    /// 画笔若还开着，回到该模式时右键仍会直接绘制，按 H 反而是把它关掉。
    /// </summary>
    private void DeactivateBrushOfMode(EditMode mode)
    {
        var handler = _editModeManager.GetModeHandler(mode);
        if (handler == null) return;
        if (_editModeManager.GetModifier(handler.PrimaryModifierKind) is not IBrushTarget brush || !brush.BrushActive) return;

        brush.BrushActive = false;
        _renderEngine.HideBrushPreview();
    }

    #endregion

    #region 识别地形（P 键）

    /// <summary>
    /// 从视图层图片识别地形。识别在后台只做计算，进度窗口的取消按钮 / Esc 会真正中止识别（取消后地图不变）；
    /// 识别期间主窗口不接受输入。结果在 UI 线程写入并记一条撤销，状态栏显示结果或失败原因。
    /// </summary>
    private async Task<(bool success, int modifiedCount)> RecognizeTerrainFromViewLayer()
    {
        var terrain = _editModeManager.GetModifier<TerrainModifier>();
        if (terrain == null || _mapData == null) return (false, 0);

        var provider = _renderEngine.ViewLayerImageProvider;
        if (provider == null)
        {
            _editModeManager.RaiseStatusMessage(Loc.T("Render.Status.NoViewLayerProvider"));
            return (false, 0);
        }

        terrain.TerrainRecognizer.SetImageProvider(provider);

        using var cts = new CancellationTokenSource();
        var mainWindow = Window;
        var progressWindow = new ProgressWindow { Title = Loc.T("Mode.Terrain.Msg.RecognizeTitle"), Owner = mainWindow };

        // 进度窗口的取消按钮与 Esc 只会置 IsCancelled（窗口没有取消事件），这里轮询它来中止识别
        var cancelWatcher = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        cancelWatcher.Tick += (_, _) =>
        {
            if (progressWindow.IsCancelled) cts.Cancel();
        };

        var progress = new Progress<(int current, int total, int row, int col)>(p =>
            progressWindow.UpdateProgress(p.current, p.total, p.row, p.col, p.total > 0 ? (int)(p.current * 100L / p.total) : 0));

        TerrainRecognitionResult recognition;
        mainWindow.IsEnabled = false;
        try
        {
            progressWindow.Show();
            cancelWatcher.Start();
            recognition = await terrain.TerrainRecognizer.RecognizeTerrainAsync(progress, cts.Token);
        }
        finally
        {
            cancelWatcher.Stop();
            mainWindow.IsEnabled = true;
            progressWindow.PlayFadeOutAndClose();
            RestoreFocus();
        }

        if (!recognition.Success)
        {
            _editModeManager.RaiseStatusMessage(recognition.Message);
            return (false, 0);
        }

        ModifierResult applied = default;
        _editModeManager.RecordMultiCellChange(Loc.T("Module.Terrain.Undo.Recognize"), () => applied = terrain.ApplyRecognition(recognition));
        if (!string.IsNullOrEmpty(applied.Message))
            _editModeManager.RaiseStatusMessage(applied.Message);

        if (applied.AffectedCount > 0)
        {
            _renderEngine.InvalidateTerrainCache();
            _renderEngine.InvalidateCoastCacheFull(_mapData);
            _skElement.InvalidateVisual();
        }

        return (applied.Success, applied.AffectedCount);
    }

    #endregion

    #region 控制台地形命令

    /// <summary>
    /// 执行一条控制台地形命令：检查地图与场景，执行后输出模块消息（失败也给出原因），改了数据就刷新画面。
    /// 征服场景里地形只读（地形不随征服文件保存，缩放 / 扩缩还会破坏与世界地图的对齐），改地形的命令一律拒绝。
    /// </summary>
    private void RunTerrainConsoleCommand(Func<TerrainModifier, ModifierResult> command, bool modifiesTerrain = true)
    {
        if (_mapData == null) { _debugConsole.WriteLine(Loc.T("Render.MapNotLoaded")); return; }
        if (modifiesTerrain && SceneType == "conquest") { _debugConsole.WriteLine(Loc.T("Mode.Terrain.Msg.ConquestReadOnly")); return; }
        var terrain = _editModeManager.GetModifier<TerrainModifier>();
        if (terrain == null) { _debugConsole.WriteLine(Loc.T("Render.Console.TerrainModifierInactive")); return; }

        var result = command(terrain);
        _debugConsole.WriteLine(result.Success ? result.Message ?? "" : Loc.F("Mode.Terrain.Msg.ConsoleFailed", result.Message));
        if (result.Success && result.AffectedCount > 0)
        {
            _editModeManager_DataModifiedFromUndo();
            _skElement.InvalidateVisual();
        }
    }

    /// <summary>在一条地形撤销记录里执行控制台命令。</summary>
    private ModifierResult RecordConsoleTerrainChange(string description, Func<ModifierResult> operation)
    {
        ModifierResult result = default;
        _editModeManager.RecordMultiCellChange(description, () => result = operation());
        return result;
    }

    /// <summary>与按键一致：有多格选区时只处理选区，否则处理全图。</summary>
    private IReadOnlyList<HexCoord>? ConsoleTerrainRegion()
        => _hexSelector.SelectedHexes.Count > 1 ? _hexSelector.SelectedHexes.ToList() : null;

    private static int ParseConsolePercent(string[] args)
        => args.Length > 0 && int.TryParse(args[0], out int value) ? Math.Clamp(value, 0, 100) : 50;

    private void RegisterTerrainCliCommands()
    {
        var cm = _commandManager;

        cm.RegisterCommand("greening", args => RunTerrainConsoleCommand(terrain =>
        {
            int prob = ParseConsolePercent(args);
            var region = ConsoleTerrainRegion();
            return RecordConsoleTerrainChange(Loc.T("Module.Terrain.Undo.Greening"), () => terrain.ApplyGreening(prob, region));
        }), "绿化平地（有多格选区时只处理选区）", "<概率0-100>");

        cm.RegisterCommand("random_flat", args => RunTerrainConsoleCommand(terrain =>
        {
            int prob = ParseConsolePercent(args);
            var region = ConsoleTerrainRegion();
            return RecordConsoleTerrainChange(Loc.T("Module.Terrain.Undo.RandomFlat"), () => terrain.RandomizeFlatTerrain(prob, region));
        }), "随机平地地形变体（有多格选区时只处理选区）", "<概率0-100>");

        cm.RegisterCommand("random_variant", args => RunTerrainConsoleCommand(terrain =>
        {
            int prob = ParseConsolePercent(args);
            var region = ConsoleTerrainRegion();
            return RecordConsoleTerrainChange(Loc.T("Module.Terrain.Undo.RandomVariant"), () => terrain.RandomizeVariant(prob, region));
        }), "随机当前层地形变体（有多格选区时只处理选区）", "<概率0-100>");

        cm.RegisterCommand("create_coast", _ => RunTerrainConsoleCommand(terrain =>
        {
            var region = ConsoleTerrainRegion();
            return RecordConsoleTerrainChange(Loc.T("Module.Terrain.Undo.Coast"), () => terrain.CreateCoast(region));
        }), "创建海岸线（有多格选区时只处理选区）", "");

        cm.RegisterCommand("process_ocean_layer2", _ => RunTerrainConsoleCommand(terrain =>
        {
            var region = ConsoleTerrainRegion();
            return RecordConsoleTerrainChange(Loc.T("Module.Terrain.Undo.OceanLayer2"), () => terrain.ProcessOceanSecondLayer(region));
        }), "处理海洋第二层（有多格选区时只处理选区）", "");

        cm.RegisterCommand("export_hd", _ => RunTerrainConsoleCommand(terrain => terrain.ExportHdFile(), modifiesTerrain: false),
            "导出HD文件", "");

        cm.RegisterCommand("scale_map", args => RunTerrainConsoleCommand(terrain =>
        {
            double scale = args.Length > 0 && double.TryParse(args[0], out double s) ? s : 1.0;
            return TerrainModifier.RecordResize(_mapData!, _fileStateManager.UndoManager.Record,
                Loc.F("Module.Terrain.Undo.Scale", scale), () => terrain.ScaleMap(scale));
        }), "按比例缩放地图", "<比例0.1-10.0>");

        cm.RegisterCommand("resize_map", args =>
        {
            if (args.Length < 2 || !TerrainModifier.TryParseDirection(args[0], out var direction))
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.ResizeUsage.Line1"));
                _debugConsole.WriteLine("  direction: up, down, left, right");
                _debugConsole.WriteLine(Loc.T("Render.Console.ResizeUsage.Line3"));
                _debugConsole.WriteLine(Loc.T("Render.Console.ResizeUsage.Line4"));
                return;
            }

            if (!int.TryParse(args[1], out int amount) || amount == 0)
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.InvalidAmount"));
                return;
            }

            // 第三个参数写 ocean 或 --ocean 都算（以前地形模式发的是 --ocean，命令却只认 ocean，扩展总是填平地）
            bool useOcean = args.Skip(2).Any(a => a.TrimStart('-').Equals("ocean", StringComparison.OrdinalIgnoreCase));
            RunTerrainConsoleCommand(terrain => TerrainModifier.RecordResize(_mapData!, _fileStateManager.UndoManager.Record,
                Loc.F("Module.Terrain.Undo.Resize", TerrainModifier.DirectionName(direction), amount),
                () => terrain.ResizeMap(direction, amount, useOcean)));
        }, "调整地图大小", "<direction> <amount> [ocean]");

        cm.RegisterCommand("geo_add_ref", args =>
        {
            if (args.Length < 4)
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoAddRefUsage.Line1"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoAddRefUsage.Line2"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoAddRefUsage.Line3"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoAddRefUsage.Line4"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoAddRefUsage.Line5"));
                return;
            }

            if (!int.TryParse(args[0], out int row) || !int.TryParse(args[1], out int col) ||
                !double.TryParse(args[2], out double lat) || !double.TryParse(args[3], out double lon))
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.ArgFormatError"));
                return;
            }

            var rp = _geoCalculator.ReferencePoints;
            rp.Points.Add(new Core.Geo.GeoReferencePoint { Row = row, Col = col, Latitude = lat, Longitude = lon });

            var ruler = _renderEngine.GeoRuler;
            if (ruler != null)
            {
                ruler.AddMarker(row, col, lat, lon);
            }

            _debugConsole.WriteLine(Loc.F("Render.Console.RefAdded", row, col, lat, lon));
            _skElement.InvalidateVisual();
        }, "添加经纬度参考点", "<row> <col> <lat> <lon>");

        cm.RegisterCommand("geo_remove_ref", args =>
        {
            if (args.Length < 1 || !int.TryParse(args[0], out int index) || index < 1)
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoRemoveRefUsage.Line1"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoRemoveRefUsage.Line2"));
                return;
            }

            var rp = _geoCalculator.ReferencePoints;
            if (index > rp.Points.Count)
            {
                _debugConsole.WriteLine(Loc.F("Render.Console.RefIndexOutOfRange", index, rp.Points.Count));
                return;
            }

            var removed = rp.Points[index - 1];
            rp.Points.RemoveAt(index - 1);

            var ruler = _renderEngine.GeoRuler;
            if (ruler != null && ruler.Markers.Count >= index)
            {
                var marker = ruler.Markers[index - 1];
                ruler.RemoveMarker(marker.Id);
            }

            _debugConsole.WriteLine(Loc.F("Render.Console.RefRemoved", index, removed.Row, removed.Col));
            _skElement.InvalidateVisual();
        }, "删除经纬度参考点", "<index>");

        cm.RegisterCommand("geo_calc", args =>
        {
            if (_mapData == null) { _debugConsole.WriteLine(Loc.T("Render.MapNotLoaded")); return; }

            var rp = _geoCalculator.ReferencePoints;
            if (!rp.IsValid())
            {
                _debugConsole.WriteLine(Loc.F("Render.Console.RefIncomplete", rp.Points.Count));
                return;
            }

            string outputPath = args.Length > 0 ? args[0] : $"geo_coords_{DateTime.Now:yyyyMMddHHmmss}.json";

            try
            {
                _geoCalculator.CalculateAndExport(_mapData, outputPath);
                _debugConsole.WriteLine(Loc.F("Render.Console.GeoCalcDone", outputPath));
            }
            catch (Exception ex)
            {
                _debugConsole.WriteLine(Loc.F("Render.Geo.CalcFailed", ex.Message));
            }
        }, "计算并导出经纬度", "[输出文件路径]");

        cm.RegisterCommand("geo_show_ref", _ =>
        {
            var rp = _geoCalculator.ReferencePoints;
            var ruler = _renderEngine.GeoRuler;
            _debugConsole.WriteLine(Loc.F("Render.Console.RefCount", rp.Points.Count));
            for (int i = 0; i < rp.Points.Count; i++)
            {
                var p = rp.Points[i];
                _debugConsole.WriteLine(Loc.F("Render.Console.RefRow", i + 1, p.Row, p.Col, p.Latitude, p.Longitude));
            }
            _debugConsole.WriteLine(Loc.F("Render.Console.RefValid", rp.IsValid()));
            if (ruler != null)
            {
                _debugConsole.WriteLine(Loc.F("Render.Console.RulerMarkerCount", ruler.Markers.Count));
                foreach (var m in ruler.Markers)
                {
                    _debugConsole.WriteLine(Loc.F("Render.Console.MarkerRow", m.Id, m.Row, m.Col, m.Latitude, m.Longitude));
                }
            }
        }, "显示当前经纬度参考点", "");

        cm.RegisterCommand("geo_set_lat", args =>
        {
            if (args.Length < 1 || !double.TryParse(args[0], out double lat))
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoSetLatUsage.Line1"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoSetLatUsage.Line2"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoSetLatUsage.Line3"));
                _debugConsole.WriteLine(Loc.F("Render.Console.GeoSetLatUsage.Current",
                    _geoCalculator.CentralLatitude?.ToString("F4") ?? Loc.T("Render.Console.AutoEstimate")));
                return;
            }

            _geoCalculator.CentralLatitude = lat;
            _debugConsole.WriteLine(Loc.F("Render.Console.CentralLatSet", lat));
            _debugConsole.WriteLine(Loc.T("Render.Console.SphericalNote"));
        }, "设置地图中心纬度（用于球面修正）", "<latitude>");

        cm.RegisterCommand("geo_export_ref", args =>
        {
            string fileName = args.Length > 0 ? args[0] : $"geo_ref_{DateTime.Now:yyyyMMddHHmmss}.json";
            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            try
            {
                _geoCalculator.ExportReferencePointsOnly(path);
                _debugConsole.WriteLine(Loc.F("Render.Console.RefExported", path));
                _debugConsole.WriteLine(Loc.F("Render.Console.RefCount", _geoCalculator.ReferencePoints.Points.Count));
            }
            catch (Exception ex)
            {
                _debugConsole.WriteLine(Loc.F("Common.ExportFailed", ex.Message));
            }
        }, "导出参考点配置到 JSON", "[filename]");

        cm.RegisterCommand("geo_import_ref", args =>
        {
            if (args.Length < 1)
            {
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoImportRefUsage.Line1"));
                _debugConsole.WriteLine(Loc.T("Render.Console.GeoImportRefUsage.Line2"));
                return;
            }

            string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, args[0]);
            if (!System.IO.File.Exists(path))
            {
                _debugConsole.WriteLine(Loc.F("Render.Console.FileNotExist", path));
                return;
            }

            try
            {
                _geoCalculator.ImportReferencePoints(path);
                var rp = _geoCalculator.ReferencePoints;
                _debugConsole.WriteLine(Loc.F("Render.Console.RefImported", path));
                _debugConsole.WriteLine(Loc.F("Render.Console.RefCount", rp.Points.Count));

                // 同步到标尺
                var ruler = _renderEngine.GeoRuler;
                if (ruler != null)
                {
                    ruler.ClearMarkers();
                    foreach (var p in rp.Points)
                    {
                        ruler.AddMarker(p.Row, p.Col, p.Latitude, p.Longitude);
                    }
                    _debugConsole.WriteLine(Loc.T("Render.Console.RulerSynced"));
                }
            }
            catch (Exception ex)
            {
                _debugConsole.WriteLine(Loc.F("Render.ImportFailed", ex.Message));
            }
        }, "从 JSON 导入参考点配置", "<filename>");

        cm.RegisterCommand("geo_ruler", _ =>
        {
            _debugConsole.WriteLine(Loc.T("Render.Console.RulerAlwaysOn"));
        }, "经纬度标尺状态（始终显示）", "");
    }

    #endregion
}
