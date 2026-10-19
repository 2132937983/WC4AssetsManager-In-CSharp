using System.Globalization;
using System.Xml.Linq;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Parsers.General;

/// <summary>
/// 将领解析器（GeneralSettings.json + config/def_portraitpos.xml）。
/// 名称直接保存在 JSON 里，不写 stringtable。
/// </summary>
public sealed class GeneralSettingParser : IGameSettingsSource
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

    /// <summary>
    /// Id → 将领 的惰性索引。渲染器每帧按 Id 查将领，线性扫描太慢。
    /// 记录被整体替换（加载 / 撤销）或条数变化时重建；命中后再核对 Id，外部改了 Id 时回退线性查找并丢弃索引。
    /// </summary>
    private Dictionary<int, GeneralSettingData>? _idIndex;
    private int _idIndexCount = -1;
    private int _idIndexVersion = -1;

    private GeneralSettingParser()
    {
        Table = new GameJsonTable<GeneralSettingData>(GameJson.AssetPath("json", "GeneralSettings.json"));
        PortraitTable = new XmlRowTable<PortraitPosEntry>(GameJson.AssetPath("config", "def_portraitpos.xml"), "Portraits", "general", ReadPortrait, WritePortrait);
        GeneralPhotoDir = GameJson.AssetPath("image", "generalphoto");
        HeadsDir = GameJson.AssetPath("image", "heads");
        LoadAll();
    }

    public GameJsonTable<GeneralSettingData> Table { get; }
    public XmlRowTable<PortraitPosEntry> PortraitTable { get; }

    public IReadOnlyList<IEditableTable> Tables => [Table, PortraitTable];
    StringTableParser? IGameSettingsSource.Strings => null;

    public List<GeneralSettingData> Items => Table.Items;
    public IReadOnlyList<GeneralSettingData> All => Table.Items;

    public string ConfigPath => Table.FilePath;
    public string PortraitPosPath => PortraitTable.FilePath;
    public string GeneralPhotoDir { get; }
    public string HeadsDir { get; }

    public void LoadAll()
    {
        _idIndex = null;
        Table.Load();
        PortraitTable.Load();
    }

    public void Reload() => LoadAll();

    // ============================================================= 查询 =============================================================

    public GeneralSettingData? GetById(int id)
    {
        var index = _idIndex;
        if (index == null || _idIndexCount != Items.Count || _idIndexVersion != Table.Version)
            index = RebuildIdIndex();

        if (index.TryGetValue(id, out var hit) && hit.Id == id)
            return hit;

        // 未命中、或命中项的 Id 已被外部改写：索引可能过期，回退线性查找并丢弃索引以便下次重建。
        var found = Items.FirstOrDefault(g => g.Id == id);
        if (found != null || hit != null)
            _idIndex = null;
        return found;
    }

    private Dictionary<int, GeneralSettingData> RebuildIdIndex()
    {
        var index = new Dictionary<int, GeneralSettingData>(Items.Count);
        foreach (var g in Items)
            index.TryAdd(g.Id, g); // 重复 Id 保留首个，与 FirstOrDefault 语义一致
        _idIndex = index;
        _idIndexCount = Items.Count;
        _idIndexVersion = Table.Version;
        return index;
    }

    public GeneralSettingData? GetByEName(string ename) => Items.FirstOrDefault(g => string.Equals(g.EName, ename, StringComparison.OrdinalIgnoreCase));

    public int GetNextId() => Items.Count == 0 ? 1001 : Items.Max(g => g.Id) + 1;

    // ============================================================= PortraitPos =============================================================

    public PortraitPosEntry? GetPortrait(string ename)
        => string.IsNullOrEmpty(ename) ? null : PortraitTable.Entries.FirstOrDefault(p => string.Equals(p.Name, ename, StringComparison.OrdinalIgnoreCase));

    public PortraitPosEntry EnsurePortraitDefault(string ename)
    {
        if (string.IsNullOrEmpty(ename)) throw new ArgumentNullException(nameof(ename));
        var p = GetPortrait(ename);
        if (p != null) return p;
        p = new PortraitPosEntry { Name = ename, PosX = -30, PosY = 40, Scale = 1.0 };
        PortraitTable.Entries.Add(p);
        return p;
    }

    public bool RemovePortrait(string ename)
        => !string.IsNullOrEmpty(ename) && PortraitTable.Entries.RemoveAll(p => string.Equals(p.Name, ename, StringComparison.OrdinalIgnoreCase)) > 0;

    /// <summary>将领 EName 改变时立绘位置跟着改名（新名字还没有位置时）。</summary>
    public void RenamePortrait(string oldName, string newName)
    {
        if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) || GetPortrait(newName) != null) return;
        var p = GetPortrait(oldName);
        if (p != null) p.Name = newName;
    }

    private static PortraitPosEntry? ReadPortrait(XElement el)
    {
        var name = (string?)el.Attribute("name");
        if (string.IsNullOrEmpty(name)) return null;
        return new PortraitPosEntry
        {
            Name = name,
            PosX = (int?)el.Attribute("posx") ?? -30,
            PosY = (int?)el.Attribute("posy") ?? 40,
            Scale = (double?)el.Attribute("scale") ?? 1.0
        };
    }

    private static IReadOnlyList<KeyValuePair<string, string>> WritePortrait(PortraitPosEntry p) =>
    [
        new("name", p.Name),
        new("posx", p.PosX.ToString(CultureInfo.InvariantCulture)),
        new("posy", p.PosY.ToString(CultureInfo.InvariantCulture)),
        new("scale", p.Scale.ToString("0.0######", CultureInfo.InvariantCulture))
    ];

    // ============================================================= 复合操作（新增/删除 同步两边） =============================================================

    public (GeneralSettingData g, PortraitPosEntry p) AddNewGeneral(string name, string ename, int? id = null)
    {
        if (string.IsNullOrWhiteSpace(ename)) throw new ArgumentException(Loc.T("JsonEditor.General.ENameRequired"));
        if (GetByEName(ename) != null) throw new InvalidOperationException(Loc.F("JsonEditor.General.ENameTaken", ename));

        int finalId = id ?? GetNextId();
        if (GetById(finalId) != null) throw new InvalidOperationException(Loc.F("JsonEditor.Error.IdTaken", finalId));

        var g = new GeneralSettingData
        {
            Id = finalId,
            Name = string.IsNullOrWhiteSpace(name) ? ename : name,
            EName = ename,
            Photo = ename,
            Skills = new List<int>(),
            Medals = new List<int>()
        };
        Items.Add(g);
        _idIndex = null;

        // 新增时 portraitpos = posx=-30, posy=40, scale=1.0
        var p = EnsurePortraitDefault(ename);
        return (g, p);
    }

    public bool DeleteGeneral(GeneralSettingData g)
    {
        if (!Items.Remove(g)) return false;
        _idIndex = null;
        if (!string.IsNullOrEmpty(g.EName)) RemovePortrait(g.EName);
        return true;
    }

    /// <summary>改 Id：新 Id 已被占用时返回 false。</summary>
    public bool ChangeId(GeneralSettingData general, int newId)
    {
        if (general.Id == newId) return true;
        if (Items.Any(g => g.Id == newId)) return false;
        general.Id = newId;
        _idIndex = null;
        return true;
    }

    /// <summary>复制将领时：给副本一个不重复的 EName，并复制原将领的立绘位置。</summary>
    public void PrepareCopy(GeneralSettingData copy, string sourceEName)
    {
        string baseName = string.IsNullOrEmpty(copy.EName) ? "general" : copy.EName;
        string ename = $"{baseName}_{copy.Id}";
        for (int n = 2; GetByEName(ename) != null; n++) ename = $"{baseName}_{copy.Id}_{n}";
        copy.EName = ename;
        _idIndex = null;
        var source = GetPortrait(sourceEName);
        if (source == null) return;
        var target = EnsurePortraitDefault(ename);
        target.PosX = source.PosX;
        target.PosY = source.PosY;
        target.Scale = source.Scale;
    }

    // ============================================================= 辅助：VB 版本的专长/军衔/技能等级算法 =============================================================

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

    // ============================================================= 图片路径解析 =============================================================

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
