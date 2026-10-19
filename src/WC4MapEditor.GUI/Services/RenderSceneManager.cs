using System.Diagnostics;
using System.IO;
using WC4MapEditor.Core.Models;
// 注意：本文件位于 WPF 项目内，隐式全局 using 会引入 System.Windows.Shapes（含 Path 类），
// 与 System.IO.Path 冲突，因此统一使用 IOPath 别名（项目已全局定义）。
using WC4MapEditor.Core.Parsers.Conquest;
using WC4MapEditor.Core.Parsers.Stage;
using WC4MapEditor.Core.Parsers.World;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.GUI.Services;

public enum RenderSceneType
{
    Test,
    Campaign,
    Conquest,
    Stage
}

public sealed class RenderSceneInfo
{
    public int SceneId { get; init; }
    public string SceneName { get; set; } = "";
    public string MapFilePath { get; set; } = "";
    public MapData? MapData { get; set; }
    public RenderSceneType SceneType { get; init; }
    public DateTime CreatedTime { get; } = DateTime.Now;
    public DateTime LastAccessTime { get; set; } = DateTime.Now;
    public bool IsCachedToDisk { get; set; }
    public string CacheFilePath { get; set; } = "";

    /// <summary>
    /// 征服场景所用的世界底图（空 = 按数据窗口布局显示）。随 <see cref="RenderSceneManager.SetSceneMapData"/> 记录，
    /// 从磁盘缓存恢复时用它重新铺上同一张底图，否则切回来省份/建筑就不再对齐世界地形。
    /// </summary>
    public string WorldMapPath { get; set; } = "";
}

public sealed class SceneSwitchRequestEventArgs : EventArgs
{
    public int TargetSceneId { get; }
    public RenderSceneType TargetSceneType { get; }
    public string MapFilePath { get; }

    public SceneSwitchRequestEventArgs(int targetSceneId, RenderSceneType targetSceneType, string mapFilePath)
    {
        TargetSceneId = targetSceneId;
        TargetSceneType = targetSceneType;
        MapFilePath = mapFilePath;
    }
}

public sealed class RenderSceneManager
{
    private static readonly object _lock = new();
    private static RenderSceneManager? _instance;

    private readonly Dictionary<int, RenderSceneInfo> _scenes = new();
    private int _currentSceneId = -1;
    private int _sceneIdCounter;
    private readonly string _cacheDirectory;

    public static RenderSceneManager Instance
    {
        get
        {
            lock (_lock)
            {
                _instance ??= new RenderSceneManager();
                return _instance;
            }
        }
    }

    public int CurrentSceneId => _currentSceneId;
    public RenderSceneInfo? CurrentScene => _currentSceneId >= 0 && _scenes.TryGetValue(_currentSceneId, out var s) ? s : null;
    public IReadOnlyCollection<RenderSceneInfo> AllScenes => _scenes.Values;
    public int SceneCount => _scenes.Count;

    public event Action? SceneListChanged;
    public event Action<int>? ActiveSceneChanged;
    public event EventHandler<SceneSwitchRequestEventArgs>? SceneSwitchRequested;

    /// <summary>
    /// 按文件判断应由哪种场景打开：.bin/.dat = 世界地图；.btl 按文件头 MapNumber 区分
    /// （大于 0 = 征服布局，否则战役），不再看文件名前缀 —— 大量征服布局的官方文件并不以 conquest 开头。
    /// </summary>
    public static RenderSceneType GetSceneTypeByFilePath(string filePath)
    {
        var ext = IOPath.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".btl")
            return BtlTypeDetector.Detect(filePath) == BTLFileType.Conquest ? RenderSceneType.Conquest : RenderSceneType.Stage;
        return RenderSceneType.Test;
    }

    public static string GetSceneTypeDisplayLabel(RenderSceneType sceneType)
    {
        return sceneType switch
        {
            RenderSceneType.Test => Loc.T("Scene.TypeWorld"),
            RenderSceneType.Stage => Loc.T("Scene.TypeCampaign"),
            RenderSceneType.Conquest => Loc.T("Scene.TypeConquest"),
            RenderSceneType.Campaign => Loc.T("Scene.TypeCampaign"),
            _ => ""
        };
    }

    public static RenderSceneType MapSceneTypeToRenderSceneType(string sceneType)
    {
        return sceneType switch
        {
            "world" => RenderSceneType.Test,
            "stage" => RenderSceneType.Stage,
            "conquest" => RenderSceneType.Conquest,
            _ => RenderSceneType.Test
        };
    }

    private RenderSceneManager()
    {
        _cacheDirectory = InitializeCacheDirectory();
    }

    private static string InitializeCacheDirectory()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var cachePath = IOPath.Combine(baseDir, "Cache", "Scenes");
        if (!Directory.Exists(cachePath))
        {
            Directory.CreateDirectory(cachePath);
            Debug.WriteLine($"[SceneManager] 创建缓存目录: {cachePath}");
        }
        return cachePath;
    }

    public void RequestSceneSwitch(int targetSceneId)
    {
        lock (_lock)
        {
            if (!_scenes.TryGetValue(targetSceneId, out var scene)) return;

            if (_currentSceneId >= 0 && _scenes.TryGetValue(_currentSceneId, out var currentScene))
            {
                CacheSceneToDisk(_currentSceneId);
                ReleaseSceneMemory(_currentSceneId);
            }

            SceneSwitchRequested?.Invoke(this, new SceneSwitchRequestEventArgs(
                targetSceneId, scene.SceneType, scene.MapFilePath));
        }
    }

    public int CreateScene(string sceneName, string mapFilePath, RenderSceneType sceneType = RenderSceneType.Test)
    {
        lock (_lock)
        {
            _sceneIdCounter++;
            var scene = new RenderSceneInfo
            {
                SceneId = _sceneIdCounter,
                SceneName = string.IsNullOrEmpty(sceneName) ? Loc.F("Scene.DefaultName", _sceneIdCounter) : sceneName,
                MapFilePath = mapFilePath,
                SceneType = sceneType,
                CacheFilePath = IOPath.Combine(_cacheDirectory, $"scene_{_sceneIdCounter}.cache")
            };
            _scenes[_sceneIdCounter] = scene;
            Debug.WriteLine($"[SceneManager] 创建场景 {scene.SceneId}: {scene.SceneName}");
            SceneListChanged?.Invoke();
            return scene.SceneId;
        }
    }

    public void SetSceneMapData(int sceneId, MapData mapData)
    {
        lock (_lock)
        {
            if (_scenes.TryGetValue(sceneId, out var scene))
            {
                scene.MapData = mapData;
                scene.WorldMapPath = mapData is ConquestWorldMapData world ? world.WorldMapPath : "";
                scene.LastAccessTime = DateTime.Now;
            }
        }
    }

    /// <summary>场景是否有可用的磁盘缓存（缓存里可能有尚未保存到原文件的编辑）。</summary>
    public bool HasDiskCache(int sceneId)
    {
        lock (_lock)
        {
            return _scenes.TryGetValue(sceneId, out var scene) && scene.IsCachedToDisk && File.Exists(scene.CacheFilePath);
        }
    }

    /// <summary>
    /// 另存为之后让场景记录指向新文件（并按新文件名改名）。
    /// 否则切走再切回时 <see cref="LoadSceneFromDisk"/> 会把路径还原成旧文件，下一次 Ctrl+S 覆盖的就是原文件。
    /// </summary>
    public void UpdateSceneFile(int sceneId, string filePath)
    {
        lock (_lock)
        {
            if (!_scenes.TryGetValue(sceneId, out var scene)) return;
            if (string.Equals(scene.MapFilePath, filePath, StringComparison.OrdinalIgnoreCase)) return;

            scene.MapFilePath = filePath;
            scene.SceneName = IOPath.GetFileNameWithoutExtension(filePath);
        }
        SceneListChanged?.Invoke();
    }

    public void ActivateScene(int sceneId)
    {
        lock (_lock)
        {
            if (_scenes.ContainsKey(sceneId))
            {
                _currentSceneId = sceneId;
                _scenes[sceneId].LastAccessTime = DateTime.Now;
                Debug.WriteLine($"[SceneManager] 激活场景 {sceneId}");
                ActiveSceneChanged?.Invoke(sceneId);
            }
        }
    }

    public RenderSceneInfo? GetScene(int sceneId)
    {
        lock (_lock)
        {
            return _scenes.TryGetValue(sceneId, out var s) ? s : null;
        }
    }

    public void CacheSceneToDisk(int sceneId)
    {
        lock (_lock)
        {
            if (!_scenes.TryGetValue(sceneId, out var scene)) return;

            try
            {
                var cacheDir = IOPath.GetDirectoryName(scene.CacheFilePath);
                if (!string.IsNullOrEmpty(cacheDir) && !Directory.Exists(cacheDir))
                    Directory.CreateDirectory(cacheDir);

                if (scene.MapData != null)
                {
                    bool saved = scene.SceneType switch
                    {
                        RenderSceneType.Test => SaveWorldMapData(scene.MapData, scene.CacheFilePath),
                        RenderSceneType.Stage => StageParser.SaveFromMapData(scene.MapData, scene.CacheFilePath),
                        RenderSceneType.Conquest => ConquestParser.SaveFromMapData(scene.MapData, scene.CacheFilePath),
                        RenderSceneType.Campaign => StageParser.SaveFromMapData(scene.MapData, scene.CacheFilePath),
                        _ => SaveWorldMapData(scene.MapData, scene.CacheFilePath)
                    };
                    if (saved)
                    {
                        scene.IsCachedToDisk = true;
                        Debug.WriteLine($"[SceneManager] 场景 {sceneId} 已缓存到磁盘: {scene.CacheFilePath}");
                    }
                }
                else if (scene.IsCachedToDisk && File.Exists(scene.CacheFilePath))
                {
                    // 内存已释放、缓存仍在：缓存就是最新数据（可能含未保存的编辑），不能再拿原文件覆盖它
                    Debug.WriteLine($"[SceneManager] 场景 {sceneId} 已有缓存，保持不变");
                }
                else if (File.Exists(scene.MapFilePath))
                {
                    File.Copy(scene.MapFilePath, scene.CacheFilePath, true);
                    scene.IsCachedToDisk = true;
                    Debug.WriteLine($"[SceneManager] 场景 {sceneId} 已缓存（复制原文件）");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SceneManager] 缓存场景 {sceneId} 失败: {ex.Message}");
            }
        }
    }

    public MapData? LoadSceneFromDisk(int sceneId)
    {
        lock (_lock)
        {
            if (!_scenes.TryGetValue(sceneId, out var scene)) return null;

            // 优先从磁盘缓存恢复（可能含尚未落盘的编辑），其次才是原始文件
            var filePath = scene.IsCachedToDisk && File.Exists(scene.CacheFilePath)
                ? scene.CacheFilePath
                : scene.MapFilePath;

            if (!File.Exists(filePath)) return null;

            try
            {
                MapData? mapData = scene.SceneType switch
                {
                    RenderSceneType.Test => WorldParser.LoadFromFile(filePath),
                    RenderSceneType.Stage => StageParser.LoadToMapData(filePath),
                    RenderSceneType.Conquest => LoadConquest(filePath, scene.WorldMapPath),
                    RenderSceneType.Campaign => StageParser.LoadToMapData(filePath),
                    _ => WorldParser.LoadFromFile(filePath)
                };
                if (mapData != null)
                {
                    // 各 Parser 都会把 FilePath 设成"传进去的那个路径"，从缓存恢复时那就是
                    // 缓存文件（scene_N.cache）。必须改回原始文件路径，否则：
                    //   1) 场景切回来之后按 Ctrl+S，地图会被存进缓存文件，而不是用户打开的那个文件；
                    //   2) 保存提示 / 标题里显示的文件名也变成缓存名。
                    // MapFilePath 为空 = 新建后从未保存过，此时保持为空，保存时照常弹对话框。
                    mapData.FilePath = scene.MapFilePath ?? string.Empty;

                    scene.MapData = mapData;
                    scene.LastAccessTime = DateTime.Now;
                    Debug.WriteLine($"[SceneManager] 场景 {sceneId} 从磁盘加载完成");
                }
                return mapData;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SceneManager] 加载场景 {sceneId} 失败: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// 让场景的磁盘缓存失效并删除缓存文件。
    /// <para>
    /// 地图被保存到原始文件后必须调用：否则下次切回该场景时，
    /// <see cref="LoadSceneFromDisk"/> 仍会优先加载比原文件更旧的缓存，
    /// 让刚才的保存看起来"没生效"。
    /// </para>
    /// </summary>
    public void InvalidateSceneCache(int sceneId)
    {
        lock (_lock)
        {
            if (!_scenes.TryGetValue(sceneId, out var scene)) return;

            scene.IsCachedToDisk = false;
            try
            {
                if (File.Exists(scene.CacheFilePath))
                    File.Delete(scene.CacheFilePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SceneManager] 删除场景缓存失败: {ex.Message}");
            }
        }
    }

    public void ReleaseSceneMemory(int sceneId)
    {
        lock (_lock)
        {
            if (!_scenes.TryGetValue(sceneId, out var scene)) return;

            if (!scene.IsCachedToDisk)
                CacheSceneToDisk(sceneId);

            scene.MapData = null;
            Debug.WriteLine($"[SceneManager] 场景 {sceneId} 内存已释放");
        }
    }

    public int SwitchToNewMap(string mapFilePath, string sceneName = "", RenderSceneType sceneType = RenderSceneType.Test)
    {
        if (_currentSceneId >= 0)
            ReleaseSceneMemory(_currentSceneId);

        if (string.IsNullOrEmpty(sceneName))
            sceneName = Loc.F("Scene.DefaultName", SceneCount + 1);

        var newSceneId = CreateScene(sceneName, mapFilePath, sceneType);
        ActivateScene(newSceneId);
        Debug.WriteLine($"[SceneManager] 切换到新地图: {mapFilePath}, 场景ID: {newSceneId}");
        return newSceneId;
    }

    public void RemoveScene(int sceneId)
    {
        lock (_lock)
        {
            if (!_scenes.TryGetValue(sceneId, out var scene)) return;

            ReleaseSceneMemory(sceneId);

            if (File.Exists(scene.CacheFilePath))
            {
                try { File.Delete(scene.CacheFilePath); }
                catch (Exception ex) { Debug.WriteLine($"[SceneManager] 删除缓存文件失败: {ex.Message}"); }
            }

            _scenes.Remove(sceneId);
            if (_currentSceneId == sceneId)
                _currentSceneId = -1;
            Debug.WriteLine($"[SceneManager] 场景 {sceneId} 已删除");
            SceneListChanged?.Invoke();
        }
    }

    public void ClearAllScenes()
    {
        lock (_lock)
        {
            foreach (var sceneId in _scenes.Keys.ToList())
            {
                if (_scenes.TryGetValue(sceneId, out var scene) && File.Exists(scene.CacheFilePath))
                {
                    try { File.Delete(scene.CacheFilePath); }
                    catch { }
                }
            }

            _scenes.Clear();
            _currentSceneId = -1;
            _sceneIdCounter = 0;
            SceneListChanged?.Invoke();
            Debug.WriteLine("[SceneManager] 所有场景已清理");
        }
    }

    /// <summary>
    /// 征服缓存/文件恢复：场景原来铺着世界底图就还铺同一张（缓存是标准征服 BTL，两种布局都能读）；
    /// 底图已不可用（文件被移走、文件头裁剪参数被改到放不进底图）时退回数据窗口布局，至少数据不丢。
    /// </summary>
    private static MapData LoadConquest(string filePath, string worldMapPath)
    {
        if (!string.IsNullOrEmpty(worldMapPath) && File.Exists(worldMapPath))
        {
            try
            {
                return ConquestParser.LoadOnWorldMap(filePath, worldMapPath);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"[SceneManager] 世界底图不可用，按数据窗口布局恢复: {ex.Message}");
            }
        }
        return ConquestParser.LoadToMapData(filePath);
    }

    private static bool SaveWorldMapData(MapData mapData, string filePath)
    {
        try
        {
            WorldParser.SaveToFile(mapData, filePath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}