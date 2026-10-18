using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Services;

/// <summary>
/// 跨场景地图剪贴板：保存地形 / 省份的复制内容，使不同场景之间可以互相粘贴。
/// <para>
/// 纯数据，无任何 UI 依赖。原先这些状态由 <c>RenderSceneManager</c> 承载，
/// 但"渲染场景"属于 UI 概念，不应让 Core 的数据编辑逻辑依赖它，
/// 因此把剪贴板状态独立到本类。
/// </para>
/// </summary>
public sealed class MapClipboard
{
    private static readonly object _lock = new();
    private static MapClipboard? _instance;

    public static MapClipboard Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new MapClipboard();
                }
            }
            return _instance;
        }
    }

    private MapClipboard() { }

    #region 地形剪贴板

    public TerrainData? GlobalCopiedTerrain { get; set; }
    public int GlobalCopiedFromCol { get; set; } = -1;
    public int GlobalCopiedFromRow { get; set; } = -1;
    public Dictionary<(int, int), TerrainData> GlobalCopiedHexes { get; } = new();
    public int GlobalCopiedRegionMinCol { get; set; }
    public int GlobalCopiedRegionMinRow { get; set; }

    #endregion

    #region 省份剪贴板

    public Province? GlobalCopiedProvince { get; set; }
    public int GlobalCopiedProvinceFromCol { get; set; } = -1;
    public int GlobalCopiedProvinceFromRow { get; set; } = -1;
    public Dictionary<(int, int), Province> GlobalCopiedProvinceHexes { get; } = new();
    public int GlobalCopiedProvinceRegionMinCol { get; set; }
    public int GlobalCopiedProvinceRegionMinRow { get; set; }

    #endregion

    /// <summary>清空全部剪贴板内容</summary>
    public void Clear()
    {
        GlobalCopiedTerrain = null;
        GlobalCopiedFromCol = -1;
        GlobalCopiedFromRow = -1;
        GlobalCopiedHexes.Clear();

        GlobalCopiedProvince = null;
        GlobalCopiedProvinceFromCol = -1;
        GlobalCopiedProvinceFromRow = -1;
        GlobalCopiedProvinceHexes.Clear();
    }
}
