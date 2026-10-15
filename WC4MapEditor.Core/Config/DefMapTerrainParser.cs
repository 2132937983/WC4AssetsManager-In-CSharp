using System.Diagnostics;
using System.Xml;

namespace WC4MapEditor.Core.Config;

/// <summary>
/// 地形定义解析器：解析 WC4DATA/assets/config/def_mapterrain.xml。
/// <para>
/// 该文件是游戏对地形的<b>权威定义</b>，与 Java 版 ResConfig.Config.TERRAINIDS
/// （即 DefDAO.getAllTerrainIds）同源 —— 校验地形组 ID 时应当以它为准，
/// 而不是 Texture/MapTerrian/manager.json（后者只是贴图文件的映射表）。
/// </para>
/// <para>
/// 本类只负责<b>解析</b>。文件内容的获取由调用方通过 <c>AssetManager</c> 完成 ——
/// AssetManager 的职责是定位并读取 WC4DATA 内的资源，不应理解 XML 的语义。
/// </para>
/// </summary>
public static class DefMapTerrainParser
{
    /// <summary>文件在 assets 根目录下的相对路径，供 AssetManager 定位</summary>
    public const string AssetPath = "config/def_mapterrain.xml";

    /// <summary>
    /// 解析 def_mapterrain.xml 的内容。
    /// </summary>
    /// <param name="xmlContent">文件的文本内容（由 AssetManager 读出）</param>
    /// <returns>地形定义列表；内容为空或解析失败时返回空列表，不抛异常</returns>
    public static List<MapTerrainEntry> Parse(string xmlContent)
    {
        var result = new List<MapTerrainEntry>();

        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            Debug.WriteLine("[DefMapTerrainParser] 内容为空");
            return result;
        }

        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xmlContent);

            var nodes = doc.SelectNodes("//terrain");
            if (nodes == null || nodes.Count == 0)
            {
                Debug.WriteLine("[DefMapTerrainParser] 未找到任何 <terrain> 节点");
                return result;
            }

            foreach (XmlNode node in nodes)
            {
                if (node.Attributes == null) continue;

                // terrain 属性就是组 ID（贴图资源的索引），缺失则无法参与合法性判定
                var terrainAttr = node.Attributes["terrain"];
                if (terrainAttr == null || !int.TryParse(terrainAttr.Value, out int terrain))
                    continue;

                int type = 0;
                if (node.Attributes["type"] != null)
                    int.TryParse(node.Attributes["type"]!.Value, out type);

                result.Add(new MapTerrainEntry
                {
                    Terrain = terrain,
                    Type = type,
                    Name = node.Attributes["name"]?.Value ?? "",
                    TileCount = node.SelectNodes("tile")?.Count ?? 0
                });
            }

            Debug.WriteLine($"[DefMapTerrainParser] 解析完成: {result.Count} 个地形定义");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DefMapTerrainParser] 解析失败: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// 从解析结果里取出「合法地形组 ID」集合。
    /// <para>
    /// 与 Java 版 TERRAINIDS 的含义一致：所有在 def_mapterrain.xml 中定义过的组 ID。
    /// 哨兵值 0 与 63 由调用方另行放行，不包含在这里（它们未必在表中）。
    /// </para>
    /// </summary>
    public static HashSet<int> ToGroupIdSet(IEnumerable<MapTerrainEntry> entries)
        => entries.Select(e => e.Terrain).ToHashSet();
}

/// <summary>def_mapterrain.xml 中的一条地形定义</summary>
public sealed class MapTerrainEntry
{
    /// <summary>组 ID（terrain 属性）—— 贴图资源的索引，也是校验时判断合法性的依据</summary>
    public int Terrain { get; init; }

    /// <summary>地形类型（type 属性），对应 def_terraintype.xml 里的类型</summary>
    public int Type { get; init; }

    /// <summary>地形名称</summary>
    public string Name { get; init; } = "";

    /// <summary>变体数量（子元素 tile 的个数），相当于 Java 版的 TERRAINIMGIDMAX</summary>
    public int TileCount { get; init; }
}
