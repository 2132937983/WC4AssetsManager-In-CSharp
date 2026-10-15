using System.Diagnostics;
using System.Text;

namespace WC4MapEditor.Core.Config;

/// <summary>
/// 字符串表解析器 - 负责解析和管理 stringtable_*.ini 文件
/// 类似于 SettingTxtParser，专门处理键值对格式的ini文件
/// </summary>
public sealed class StringTableParser
{
    private readonly Dictionary<string, string> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _filePath;
    private bool _isDirty;

    /// <summary>
    /// 文件路径
    /// </summary>
    public string FilePath => _filePath;

    /// <summary>
    /// 是否已修改但未保存
    /// </summary>
    public bool IsDirty => _isDirty;

    /// <summary>
    /// 条目数量
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// 所有条目的只读视图
    /// </summary>
    public IReadOnlyDictionary<string, string> Entries => _entries;

    public StringTableParser(string filePath)
    {
        _filePath = filePath;
        if (File.Exists(filePath))
        {
            var lines = File.ReadAllLines(filePath);
            Parse(lines);
        }
    }

    /// <summary>
    /// 从文本行解析
    /// </summary>
    public void Parse(string[] lines)
    {
        _entries.Clear();
        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[')) continue;

            int eqIdx = line.IndexOf('=');
            if (eqIdx < 0) continue;

            string key = line.Substring(0, eqIdx).Trim();
            string value = line.Substring(eqIdx + 1).Trim();
            _entries[key] = value;
        }
        _isDirty = false;
        Debug.WriteLine($"[StringTableParser] 已解析 {_entries.Count} 个条目");
    }

    /// <summary>
    /// 获取值
    /// </summary>
    public string GetValue(string key, string defaultValue = "")
    {
        return _entries.TryGetValue(key, out var value) ? value : defaultValue;
    }

    /// <summary>
    /// 设置值
    /// </summary>
    public void SetValue(string key, string value)
    {
        if (_entries.TryGetValue(key, out var existing) && existing == value)
            return;

        _entries[key] = value;
        _isDirty = true;
    }

    /// <summary>
    /// 删除条目
    /// </summary>
    public bool Remove(string key)
    {
        if (_entries.Remove(key))
        {
            _isDirty = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// 检查是否包含键
    /// </summary>
    public bool ContainsKey(string key) => _entries.ContainsKey(key);

    /// <summary>
    /// 查找以指定前缀开头的所有条目
    /// </summary>
    public Dictionary<string, string> FindByPrefix(string prefix)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _entries)
        {
            if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                result[kvp.Key] = kvp.Value;
        }
        return result;
    }

    /// <summary>
    /// 查找城市名称条目 (battle_cityname_xxx)
    /// </summary>
    public Dictionary<int, string> FindCityNames()
    {
        var result = new Dictionary<int, string>();
        foreach (var kvp in _entries)
        {
            if (kvp.Key.StartsWith("battle_cityname_", StringComparison.OrdinalIgnoreCase))
            {
                var numPart = kvp.Key["battle_cityname_".Length..];
                if (int.TryParse(numPart, out int cityId))
                    result[cityId] = kvp.Value;
            }
        }
        return result;
    }

    /// <summary>
    /// 根据城市名称查找ID
    /// </summary>
    public int? FindCityIdByName(string cityName)
    {
        foreach (var kvp in _entries)
        {
            if (kvp.Key.StartsWith("battle_cityname_", StringComparison.OrdinalIgnoreCase)
                && string.Equals(kvp.Value, cityName, StringComparison.Ordinal))
            {
                var numPart = kvp.Key["battle_cityname_".Length..];
                if (int.TryParse(numPart, out int cityId))
                    return cityId;
            }
        }
        return null;
    }

    /// <summary>
    /// 获取最大的城市名称ID
    /// </summary>
    public int GetMaxCityId()
    {
        int maxId = 0;
        foreach (var kvp in _entries)
        {
            if (kvp.Key.StartsWith("battle_cityname_", StringComparison.OrdinalIgnoreCase))
            {
                var numPart = kvp.Key["battle_cityname_".Length..];
                if (int.TryParse(numPart, out int cityId) && cityId > maxId)
                    maxId = cityId;
            }
        }
        return maxId;
    }

    /// <summary>
    /// 添加或更新城市名称
    /// </summary>
    /// <returns>城市ID</returns>
    public int AddOrUpdateCityName(string cityName)
    {
        // 先查找是否已存在
        var existingId = FindCityIdByName(cityName);
        if (existingId.HasValue)
            return existingId.Value;

        // 创建新ID
        int newId = GetMaxCityId() + 1;
        string key = $"battle_cityname_{newId:D3}";
        SetValue(key, cityName);

        Debug.WriteLine($"[StringTableParser] 添加新城市名称: {key}={cityName}");
        return newId;
    }

    /// <summary>
    /// 保存到文件
    /// </summary>
    public void Save()
    {
        if (!_isDirty) return;

        var sb = new StringBuilder();

        // 写入所有条目，保持原有顺序（如果有原始文件则保留注释和空行）
        if (File.Exists(_filePath))
        {
            var lines = File.ReadAllLines(_filePath);
            var processedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();

                // 保留注释和空行
                if (string.IsNullOrEmpty(line) || line.StartsWith(';') || line.StartsWith('#') || line.StartsWith('['))
                {
                    sb.AppendLine(rawLine);
                    continue;
                }

                int eqIdx = line.IndexOf('=');
                if (eqIdx < 0)
                {
                    sb.AppendLine(rawLine);
                    continue;
                }

                string key = line.Substring(0, eqIdx).Trim();

                // 如果该键在修改后的字典中存在，使用新值
                if (_entries.TryGetValue(key, out var newValue))
                {
                    sb.AppendLine($"{key}={newValue}");
                    processedKeys.Add(key);
                }
                else
                {
                    // 键已被删除，跳过
                }
            }

            // 添加新增的条目
            foreach (var kvp in _entries)
            {
                if (!processedKeys.Contains(kvp.Key))
                {
                    sb.AppendLine($"{kvp.Key}={kvp.Value}");
                }
            }
        }
        else
        {
            // 新文件，直接写入所有条目
            foreach (var kvp in _entries)
            {
                sb.AppendLine($"{kvp.Key}={kvp.Value}");
            }
        }

        File.WriteAllText(_filePath, sb.ToString(), Encoding.UTF8);
        _isDirty = false;

        Debug.WriteLine($"[StringTableParser] 已保存到 {_filePath}");
    }

    /// <summary>
    /// 重新加载文件
    /// </summary>
    public void Reload()
    {
        if (File.Exists(_filePath))
        {
            var lines = File.ReadAllLines(_filePath);
            Parse(lines);
        }
    }
}