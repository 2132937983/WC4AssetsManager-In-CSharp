using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Models;

/// <summary>
/// 战略建设数据结构 (16字节) - 使用结构体减少内存开销
/// <para>
/// 0x00 军团序号（= <see cref="Legion.ActionId"/>，不是军团在列表里的位置），0x04 保留，0x08 执行回合，
/// 0x0C 建设代码（= 游戏 config/def_strategy.xml 里的策略 ID，见 <see cref="StrategyCatalog"/>）：
/// 从执行回合起，该军团的 AI 按这条策略分配生产。记录里没有坐标。
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct StrategicConstruction
{
    public int LegionId;
    public int Reserved1;

    /// <summary>执行回合（VB 与早先的 C# 叫 Reserved2，字段位置不变）。</summary>
    public int ExecuteRound;

    public int ConstructionCode;

    /// <summary>
    /// 从字节数组解析战略建筑数据
    /// </summary>
    public static StrategicConstruction FromBytes(ReadOnlySpan<byte> data, int offset) =>
        MemoryMarshal.Read<StrategicConstruction>(data[offset..]);

    /// <summary>
    /// 写入字节数组
    /// </summary>
    public void ToBytes(Span<byte> data, int offset) =>
        MemoryMarshal.Write(data[offset..], in this);

    /// <summary>
    /// 创建默认战略建筑
    /// </summary>
    public static StrategicConstruction CreateDefault() => new StrategicConstruction();
}

/// <summary>def_strategy.xml 里的一条策略：ID、名称、类别（produce / infantry / navy ……）。</summary>
public sealed record StrategyDefinition(int Id, string Name, string Category)
{
    /// <summary>类别的显示名（随界面语言）；不认识的类别显示原文。</summary>
    public string CategoryName => Loc.T($"Module.Strategic.Category.{Category}", Category);

    /// <summary>列表里显示的名称，例如 "步兵4、3（步兵）"。</summary>
    public string DisplayName => Loc.F("Module.Strategic.CodeDisplay", Name, CategoryName);
}

/// <summary>
/// 建设代码表：游戏 config/def_strategy.xml（只读，首次访问时加载一次）。
/// 文件不存在或读不了时为空表，此时建设代码只能按数字填写。
/// </summary>
public static class StrategyCatalog
{
    private sealed record Table(IReadOnlyList<StrategyDefinition> List, IReadOnlyDictionary<int, StrategyDefinition> ById);

    private static readonly Lazy<Table> Data = new(Load);

    /// <summary>全部策略（文件顺序）。</summary>
    public static IReadOnlyList<StrategyDefinition> All => Data.Value.List;

    /// <summary>按策略 ID 查找；不在表里时返回 null。</summary>
    public static StrategyDefinition? Find(int id) => Data.Value.ById.TryGetValue(id, out var d) ? d : null;

    private static Table Load()
    {
        var list = new List<StrategyDefinition>();
        var byId = new Dictionary<int, StrategyDefinition>();
        string path = Path.Combine(AssetManager.GetDefaultAssetsPath(), "config", "def_strategy.xml");
        try
        {
            var root = File.Exists(path) ? XDocument.Load(path).Root : null;
            foreach (var e in root?.Elements("strategy") ?? Enumerable.Empty<XElement>())
            {
                if (!int.TryParse((string?)e.Attribute("id"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) continue;
                var definition = new StrategyDefinition(id, (string?)e.Attribute("name") ?? "", (string?)e.Attribute("type") ?? "");
                if (byId.TryAdd(id, definition)) list.Add(definition);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            Debug.WriteLine($"[StrategyCatalog] 读取 {path} 失败: {ex.Message}");
        }
        return new Table(list, byId);
    }
}
