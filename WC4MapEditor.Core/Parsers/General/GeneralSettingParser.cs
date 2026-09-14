using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Xml;
using System.Xml.Linq;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Parsers.General;

public class GeneralSettingParser
{
    private static readonly object _lock = new();
    private static GeneralSettingParser? _instance;
    public static GeneralSettingParser Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new GeneralSettingParser();
                }
            }
            return _instance;
        }
    }

    private readonly AssetManager _manager = AssetManager.Default;

    private List<GeneralSettingData> _data = new();
    public IReadOnlyList<GeneralSettingData> All => _data;

    public string ConfigPath { get; private set; }
    public string PortraitPosPath { get; private set; }
    public string GeneralPhotoDir { get; private set; }
    public string HeadsDir { get; private set; }

    private readonly Dictionary<string, PortraitPosEntry> _portraits = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, PortraitPosEntry> Portraits => _portraits;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        // 数值字段容错：允许 "1019" / null / 1019.0 等非标准写法
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new SafeInt32Converter(), new SafeIntListConverter() }
    };

    private GeneralSettingParser()
    {
        ResolvePaths();
        LoadAll();
    }

    private void ResolvePaths()
    {
        // 如果 AssetManager 未扫描，先尝试默认路径扫描
        if (!_manager.IsLoaded)
        {
            try { _manager.ScanDefault(); } catch { /* 静默处理 */ }
        }

        var root = _manager.AssetsRoot;
        if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
        {
            ConfigPath = Path.Combine(root, "json", "GeneralSettings.json");
            PortraitPosPath = Path.Combine(root, "config", "def_portraitpos.xml");
            GeneralPhotoDir = Path.Combine(root, "image", "generalphoto");
            HeadsDir = Path.Combine(root, "image", "heads");
        }
        else
        {
            // 兜底：从执行目录向上搜索 Resource/WC4DATA/assets
            var baseDir = AppContext.BaseDirectory;
            string? candidate = null;
            string? current = baseDir;
            for (int i = 0; i < 5 && !string.IsNullOrEmpty(current); i++)
            {
                var test = Path.Combine(current, "Resource", "WC4DATA", "assets");
                if (Directory.Exists(test)) { candidate = test; break; }
                current = Path.GetDirectoryName(current);
            }
            candidate ??= Path.GetFullPath(Path.Combine(baseDir ?? "", "..", "..", "..", "..", "Resource", "WC4DATA", "assets"));

            ConfigPath = Path.Combine(candidate, "json", "GeneralSettings.json");
            PortraitPosPath = Path.Combine(candidate, "config", "def_portraitpos.xml");
            GeneralPhotoDir = Path.Combine(candidate, "image", "generalphoto");
            HeadsDir = Path.Combine(candidate, "image", "heads");
        }

        Debug.WriteLine($"[GeneralSettingParser] 解析路径:");
        Debug.WriteLine($"  ConfigPath      = {ConfigPath}");
        Debug.WriteLine($"  PortraitPosPath = {PortraitPosPath}");
        Debug.WriteLine($"  GeneralPhotoDir = {GeneralPhotoDir}");
        Debug.WriteLine($"  HeadsDir        = {HeadsDir}");
    }

    public void LoadAll()
    {
        LoadGeneralSettings();
        LoadPortraitPos();
    }

    public void Reload() => LoadAll();

    // ============================== GeneralSettings.json ==============================
    public void LoadGeneralSettings()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                Debug.WriteLine($"[GeneralSettingParser] 配置文件不存在: {ConfigPath}");
                _data = new List<GeneralSettingData>();
                return;
            }
            var json = File.ReadAllText(ConfigPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                _data = new List<GeneralSettingData>();
                return;
            }

            try
            {
                _data = JsonSerializer.Deserialize<List<GeneralSettingData>>(json, JsonOpts) ?? new List<GeneralSettingData>();
            }
            catch (Exception ex)
            {
                // 整体解析失败时降级为逐条解析，只丢弃损坏的条目，避免"一条坏数据清空全部"
                Debug.WriteLine($"[GeneralSettingParser] 整体解析失败，降级为逐条解析: {ex.Message}");
                _data = DeserializeTolerant(json);
            }
            Debug.WriteLine($"[GeneralSettingParser] 已加载 {_data.Count} 个将领配置");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GeneralSettingParser] 加载 GeneralSettings.json 失败: {ex.Message}");
            _data = new List<GeneralSettingData>();
        }
    }

    /// <summary>
    /// 逐条反序列化：损坏的条目单独跳过，其余条目正常加载
    /// </summary>
    private List<GeneralSettingData> DeserializeTolerant(string json)
    {
        var result = new List<GeneralSettingData>();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                Debug.WriteLine("[GeneralSettingParser] 根节点不是数组，无法解析");
                return result;
            }

            int index = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                try
                {
                    var item = el.Deserialize<GeneralSettingData>(JsonOpts);
                    if (item != null) result.Add(item);
                }
                catch (Exception ex)
                {
                    var raw = string.Empty;
                    try { raw = el.GetRawText(); } catch { /* 忽略 */ }
                    if (raw.Length > 200) raw = raw.Substring(0, 200) + "...";
                    Debug.WriteLine($"[GeneralSettingParser] 第 {index} 条将领数据损坏已跳过: {ex.Message} | 内容: {raw}");
                }
                index++;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GeneralSettingParser] 逐条解析也失败: {ex.Message}");
        }
        return result;
    }

    public GeneralSettingData? GetById(int id) => _data.FirstOrDefault(g => g.Id == id);
    public GeneralSettingData? GetByEName(string ename) => _data.FirstOrDefault(g => string.Equals(g.EName, ename, StringComparison.OrdinalIgnoreCase));

    public int GetNextId() => _data.Count == 0 ? 1001 : _data.Max(g => g.Id) + 1;

    public bool SaveGeneralSettings(string? outputPath = null)
    {
        try
        {
            var path = outputPath ?? ConfigPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(_data, JsonOpts);
            File.WriteAllText(path, json, System.Text.Encoding.UTF8);
            Debug.WriteLine($"[GeneralSettingParser] 已保存 {_data.Count} 个将领配置 -> {path}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GeneralSettingParser] 保存失败: {ex.Message}");
            return false;
        }
    }

    // ============================== PortraitPos ==============================
    public void LoadPortraitPos()
    {
        _portraits.Clear();
        try
        {
            if (!File.Exists(PortraitPosPath))
            {
                Debug.WriteLine($"[GeneralSettingParser] portraitpos不存在: {PortraitPosPath}");
                return;
            }
            var doc = XDocument.Load(PortraitPosPath);
            var root = doc.Element("Portraits");
            if (root == null) return;
            foreach (var el in root.Elements("general"))
            {
                var name = (string?)el.Attribute("name") ?? string.Empty;
                if (string.IsNullOrEmpty(name)) continue;
                var entry = new PortraitPosEntry
                {
                    Name = name,
                    PosX = (int?)el.Attribute("posx") ?? -30,
                    PosY = (int?)el.Attribute("posy") ?? 40,
                    Scale = (double?)el.Attribute("scale") ?? 1.0
                };
                _portraits[name] = entry;
            }
            Debug.WriteLine($"[GeneralSettingParser] 已加载 {_portraits.Count} 个 PortraitPos");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GeneralSettingParser] 加载 PortraitPos 失败: {ex.Message}");
        }
    }

    public PortraitPosEntry? GetPortrait(string ename)
        => string.IsNullOrEmpty(ename) ? null : _portraits.GetValueOrDefault(ename);

    public PortraitPosEntry EnsurePortraitDefault(string ename)
    {
        if (string.IsNullOrEmpty(ename)) throw new ArgumentNullException(nameof(ename));
        if (!_portraits.TryGetValue(ename, out var p))
        {
            p = new PortraitPosEntry { Name = ename, PosX = -30, PosY = 40, Scale = 1.0 };
            _portraits[ename] = p;
        }
        return p;
    }

    public bool RemovePortrait(string ename)
        => !string.IsNullOrEmpty(ename) && _portraits.Remove(ename);

    public bool SavePortraitPos(string? outputPath = null)
    {
        try
        {
            var path = outputPath ?? PortraitPosPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = false,
                Encoding = System.Text.Encoding.UTF8
            };
            using var writer = XmlWriter.Create(path, settings);
            writer.WriteStartElement("Portraits");
            foreach (var kv in _portraits.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                writer.WriteStartElement("general");
                writer.WriteAttributeString("name", kv.Name);
                writer.WriteAttributeString("posx", kv.PosX.ToString());
                writer.WriteAttributeString("posy", kv.PosY.ToString());
                writer.WriteAttributeString("scale", kv.Scale.ToString("0.0######", System.Globalization.CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
            Debug.WriteLine($"[GeneralSettingParser] 已保存 PortraitPos {_portraits.Count} 项 -> {path}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GeneralSettingParser] 保存 PortraitPos 失败: {ex.Message}");
            return false;
        }
    }

    // ============================== 复合操作（新增/删除 同步两边） ==============================
    public (GeneralSettingData g, PortraitPosEntry p) AddNewGeneral(string name, string ename, int? id = null)
    {
        if (string.IsNullOrWhiteSpace(ename)) throw new ArgumentException("EName 不能为空");
        if (GetByEName(ename) != null) throw new InvalidOperationException($"已存在同名将领 EName={ename}");

        int finalId = id ?? GetNextId();
        if (GetById(finalId) != null) throw new InvalidOperationException($"ID={finalId} 已被占用");

        var g = new GeneralSettingData
        {
            Id = finalId,
            Name = string.IsNullOrWhiteSpace(name) ? ename : name,
            EName = ename,
            Photo = ename,
            Skills = new List<int>(),
            Medals = new List<int>()
        };
        _data.Add(g);

        // 新增时 portraitpos = posx=-30, posy=40, scale=1.0
        var p = EnsurePortraitDefault(ename);
        return (g, p);
    }

    public bool DeleteGeneral(int id)
    {
        var g = GetById(id);
        if (g == null) return false;
        _data.Remove(g);
        if (!string.IsNullOrEmpty(g.EName)) RemovePortrait(g.EName);
        return true;
    }

    public bool SaveAll()
    {
        var ok1 = SaveGeneralSettings();
        var ok2 = SavePortraitPos();
        return ok1 && ok2;
    }

    // ============================== 辅助：VB 版本的专长/军衔/技能等级算法 ==============================
    public string GetSpecialty(int generalId)
    {
        var g = GetById(generalId);
        if (g == null) return "Infantry";
        int[] vals = { g.Infantry, g.Armor, g.Artillery, g.Navy, g.AirForce };
        string[] names = { "Infantry", "Armor", "Artillery", "Navy", "AirForce" };
        int max = vals.Max();
        var candidates = new List<string>();
        for (int i = 0; i < 5; i++)
            if (vals[i] == max && vals[i] > 0) candidates.Add(names[i]);
        if (candidates.Count == 0) return names[Random.Shared.Next(5)];
        if (candidates.Count == 1) return candidates[0];
        return candidates[Random.Shared.Next(candidates.Count)];
    }

    public int GetMilitaryRank(int id) => GetById(id)?.MilitaryRank ?? 0;
    public int GetHp(int id) => GetById(id)?.Hp ?? 0;
    public List<int> GetSkills(int id) => GetById(id)?.Skills?.ToList() ?? new List<int>();
    public string? GetEname(int id) => GetById(id)?.EName;
    public static int CalculateSkillLevel(int skillId) => Math.Max(1, skillId % 10);

    // ============================== 图片路径解析 ==============================
    public string? GetGeneralPhotoPath(string ename)
    {
        if (string.IsNullOrEmpty(ename)) return null;
        var f1 = Path.Combine(GeneralPhotoDir, $"general_{ename}.webp");
        if (File.Exists(f1)) return f1;
        var f2 = Path.Combine(GeneralPhotoDir, $"general_{ename}.png");
        return File.Exists(f2) ? f2 : null;
    }

    public string? GetHeadPath(string ename)
    {
        if (string.IsNullOrEmpty(ename)) return null;
        var f1 = Path.Combine(HeadsDir, $"general_circle_{ename}.webp");
        if (File.Exists(f1)) return f1;
        var f2 = Path.Combine(HeadsDir, $"general_circle_{ename}.png");
        return File.Exists(f2) ? f2 : null;
    }
}

/// <summary>
/// 宽松的 int 读取转换器：容忍 null、字符串数字、浮点数、布尔等非常规写法
/// </summary>
public sealed class SafeInt32Converter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var i)) return i;
                if (reader.TryGetDouble(out var d)) return Clamp(d);
                return 0;

            case JsonTokenType.String:
                var s = reader.GetString();
                if (string.IsNullOrWhiteSpace(s)) return 0;
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var si)) return si;
                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var sd)) return Clamp(sd);
                return 0;

            case JsonTokenType.True:
                return 1;
            case JsonTokenType.False:
                return 0;
            default:
                return 0;
        }
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        => writer.WriteNumberValue(value);

    private static int Clamp(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return 0;
        if (d >= int.MaxValue) return int.MaxValue;
        if (d <= int.MinValue) return int.MinValue;
        return (int)Math.Round(d);
    }
}

/// <summary>
/// 宽松的 List&lt;int&gt; 读取转换器：容忍 null、单个数字、逗号分隔字符串等写法
/// </summary>
public sealed class SafeIntListConverter : JsonConverter<List<int>>
{
    private static readonly SafeInt32Converter IntReader = new();

    public override List<int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<int>();
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                list.Add(IntReader.Read(ref reader, typeof(int), options));
                return list;

            case JsonTokenType.String:
                var s = reader.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    foreach (var part in s.Split(',', ';', '|', ' ', '\t'))
                    {
                        if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                            list.Add(v);
                    }
                }
                return list;

            case JsonTokenType.StartArray:
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    list.Add(IntReader.Read(ref reader, typeof(int), options));
                }
                return list;

            default:
                return list;
        }
    }

    public override void Write(Utf8JsonWriter writer, List<int> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var v in value) writer.WriteNumberValue(v);
        writer.WriteEndArray();
    }
}