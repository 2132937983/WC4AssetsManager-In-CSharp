using System.Diagnostics;
using System.IO;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.Conquest;
using WC4MapEditor.Core.Parsers.Stage;
using WC4MapEditor.Core.Parsers.World;

namespace WC4MapEditor.Core.SceneManagement;

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

    public static RenderSceneType GetSceneTypeByFilePath(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is ".bin" or ".dat") return RenderSceneType.Test;
        if (ext == ".btl")
        {
            var name = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();
            if (name.StartsWith("conquest")) return RenderSceneType.Conquest;
            return RenderSceneType.Stage;
        }
        return RenderSceneType.Test;
    }

    public static string GetSceneTypeDisplayLabel(RenderSceneType sceneType)
    {
        return sceneType switch
        {
            RenderSceneType.Test => "[地图]",
            RenderSceneType.Stage => "[战役]",
            RenderSceneType.Conquest => "[征服]",
            RenderSceneType.Campaign => "[战役]",
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
        var cachePath = Path.Combine(baseDir, "Cache", "Scenes");
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
                SceneName = string.IsNullOrEmpty(sceneName) ? $"场景 {_sceneIdCounter}" : sceneName,
                MapFilePath = mapFilePath,
                SceneType = sceneType,
                CacheFilePath = Path.Combine(_cacheDirectory, $"scene_{_sceneIdCounter}.cache")
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
                scene.LastAccessTime = DateTime.Now;
            }
        }
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
                var cacheDir = Path.GetDirectoryName(scene.CacheFilePath);
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
                    RenderSceneType.Conquest => ConquestParser.LoadToMapData(filePath),
                    RenderSceneType.Campaign => StageParser.LoadToMapData(filePath),
                    _ => WorldParser.LoadFromFile(filePath)
                };
                if (mapData != null)
                {
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
            sceneName = $"场景 {SceneCount + 1}";

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

    #region 全局复制数据（跨场景共享）

    public TerrainData? GlobalCopiedTerrain { get; set; }
    public int GlobalCopiedFromCol { get; set; } = -1;
    public int GlobalCopiedFromRow { get; set; } = -1;
    public int GlobalCopiedFromSceneId { get; set; } = -1;

    public Dictionary<(int, int), TerrainData> GlobalCopiedHexes { get; } = new();
    public int GlobalCopiedRegionMinCol { get; set; }
    public int GlobalCopiedRegionMinRow { get; set; }

    public Province? GlobalCopiedProvince { get; set; }
    public int GlobalCopiedProvinceFromCol { get; set; } = -1;
    public int GlobalCopiedProvinceFromRow { get; set; } = -1;
    public int GlobalCopiedProvinceFromSceneId { get; set; } = -1;

    public Dictionary<(int, int), Province> GlobalCopiedProvinceHexes { get; } = new();
    public int GlobalCopiedProvinceRegionMinCol { get; set; }
    public int GlobalCopiedProvinceRegionMinRow { get; set; }

    public void ClearGlobalCopiedData()
    {
        GlobalCopiedTerrain = null;
        GlobalCopiedFromCol = -1;
        GlobalCopiedFromRow = -1;
        GlobalCopiedFromSceneId = -1;
        GlobalCopiedHexes.Clear();
        GlobalCopiedProvince = null;
        GlobalCopiedProvinceFromCol = -1;
        GlobalCopiedProvinceFromRow = -1;
        GlobalCopiedProvinceFromSceneId = -1;
        GlobalCopiedProvinceHexes.Clear();
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

    #endregion
}