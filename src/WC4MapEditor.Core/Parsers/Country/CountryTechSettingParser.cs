using System.Text.Json.Serialization;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Parsers.Country;

/// <summary>
/// 国家科技树解析器（CountryTechSettings.json + stringtable 的 countrytech_{Type} / countrytech_desc_{Type}）。
/// <para>
/// 科技的名称与描述按 Type 索引（游戏里 116 组），同一 Type 的节点共用文本。
/// 旧实现按 skill_name_{Id} 读写，那是将领技能的键：列表几乎全显示“未命名”，新增科技还会覆盖技能名（例如 skill_name_1）。
/// </para>
/// </summary>
public sealed class CountryTechSettingParser : IGameSettingsSource
{
    private static readonly object _lock = new();
    private static CountryTechSettingParser? _instance;
    public static CountryTechSettingParser Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new CountryTechSettingParser();
                }
            }
            return _instance;
        }
    }

    private CountryTechSettingParser()
    {
        Table = new GameJsonTable<CountryTechData>(GameJson.AssetPath("json", "CountryTechSettings.json"));
        Strings = StringTableStore.Default;
        TypeTexts = new RecordTextKeys(Strings, this, "countrytech_{0}", "countrytech_desc_{0}");
        LoadAll();
    }

    public GameJsonTable<CountryTechData> Table { get; }
    public StringTableParser Strings { get; }

    /// <summary>按科技 Type 的文本键：0 = 名称，1 = 描述。</summary>
    public RecordTextKeys TypeTexts { get; }

    public IReadOnlyList<IEditableTable> Tables => [Table];
    StringTableParser? IGameSettingsSource.Strings => Strings;

    public List<CountryTechData> Items => Table.Items;
    public string ConfigPath => Table.FilePath;
    public string StringTablePath => Strings.FilePath;

    public void LoadAll() => Table.Load();

    public CountryTechData? GetById(int id) => Items.FirstOrDefault(t => t.Id == id);

    public string GetTechNameByType(int type) => TypeTexts.Get(0, type);
    public void SetTechNameByType(int type, string v) => TypeTexts.Set(0, type, v);
    public string GetTechDescByType(int type) => TypeTexts.Get(1, type);
    public void SetTechDescByType(int type, string v) => TypeTexts.Set(1, type, v);

    /// <summary>
    /// 指定分类下的新 Id：分类内最大 Id + 1；分类还没有节点时取 分类×100 + 1（与游戏数据的编号习惯一致）。
    /// 结果被别的分类占用时继续递增，保证全表唯一。
    /// </summary>
    public int GetNextIdInCategory(int categoryId)
    {
        var inCategory = Items.Where(t => t.CategoryId == categoryId).Select(t => t.Id).ToList();
        int id = inCategory.Count > 0 ? inCategory.Max() + 1 : categoryId * 100 + 1;
        var taken = Items.Select(t => t.Id).ToHashSet();
        while (taken.Contains(id)) id++;
        return id;
    }

    /// <summary>
    /// 删除科技节点，并从其它节点的 NeedId 中去掉对它的引用（对应的 Lines / Lines2 路径一并去掉），
    /// 避免留下指向不存在节点的前置。
    /// </summary>
    public void Remove(CountryTechData tech)
    {
        Items.Remove(tech);
        foreach (var other in Items)
        {
            int index;
            while ((index = other.NeedId.IndexOf(tech.Id)) >= 0)
            {
                other.NeedId.RemoveAt(index);
                if (index == 0)
                {
                    other.Lines = other.Lines2;
                    other.Lines2 = new List<double>();
                }
                else if (index == 1)
                {
                    other.Lines2 = new List<double>();
                }
            }
        }
    }

    /// <summary>改 Id：新 Id 已被占用时返回 false；其它节点 NeedId 里的引用跟着改。</summary>
    public bool ChangeId(CountryTechData tech, int newId)
    {
        if (tech.Id == newId) return true;
        if (Items.Any(t => t.Id == newId)) return false;
        foreach (var other in Items)
        {
            for (int i = 0; i < other.NeedId.Count; i++)
            {
                if (other.NeedId[i] == tech.Id) other.NeedId[i] = newId;
            }
        }
        tech.Id = newId;
        return true;
    }

    // ============================================================= 分组 / 新增 / 连线 =============================================================

    /// <summary>分类（科技组）下已经有节点。</summary>
    public bool HasCategory(int categoryId) => Items.Any(t => t.CategoryId == categoryId);

    /// <summary>该分类里已有节点使用了这个 Type（同一分类内 Type 应当唯一）。</summary>
    public bool IsTypeTaken(int categoryId, int type) => Items.Any(t => t.CategoryId == categoryId && t.Type == type);

    /// <summary>
    /// 在指定网格位置造一个新节点（尚未加入表）。有前置时 NeedId 指向它、Categorys 沿用它的值，并按网格位置生成 Lines。
    /// </summary>
    public CountryTechData CreateNode(int categoryId, int level, int position, int type, CountryTechData? parent)
    {
        var node = new CountryTechData
        {
            Id = GetNextIdInCategory(categoryId),
            NeedId = parent == null ? new List<int>() : new List<int> { parent.Id },
            CategoryId = categoryId,
            Categorys = parent?.Categorys ?? 0,
            Type = type,
            ResearchLv = level,
            Position = position,
            CostMoney = 100,
            CostGear = 50,
            CostAtomic = 25,
            CostMerit = 1,
            Chance = 100
        };
        if (parent != null) node.Lines = TechTreeGeometry.MakeOrthogonalPath(parent, node);
        return node;
    }

    /// <summary>删除整个分类的节点（其它分类对它们的 NeedId 引用一并去掉），返回删除的节点数。</summary>
    public int RemoveCategory(int categoryId)
    {
        var nodes = Items.Where(t => t.CategoryId == categoryId).ToList();
        foreach (var node in nodes) Remove(node);
        return nodes.Count;
    }

    /// <summary>
    /// 按 NeedId 重写一个分类里所有有前置的节点的 Lines / Lines2。
    /// 前置超过两个、前置不存在或在别的分类的节点跳过（连线只在组内有意义）。
    /// </summary>
    public TechRewriteResult RewriteCategoryLines(int categoryId)
    {
        int candidates = 0, changed = 0;
        var skipped = new List<int>();
        foreach (var node in Items.Where(t => t.CategoryId == categoryId && t.NeedId.Count > 0))
        {
            candidates++;
            var parents = node.NeedId.Select(GetById).ToList();
            if (node.NeedId.Count > 2 || parents.Any(p => p == null || p.CategoryId != categoryId))
            {
                skipped.Add(node.Id);
                continue;
            }
            var lines = TechTreeGeometry.MakeOrthogonalPath(parents[0]!, node);
            var lines2 = parents.Count == 2 ? TechTreeGeometry.MakeOrthogonalPath(parents[1]!, node) : new List<double>();
            if (node.Lines.SequenceEqual(lines) && node.Lines2.SequenceEqual(lines2)) continue;
            node.Lines = lines;
            node.Lines2 = lines2;
            changed++;
        }
        return new TechRewriteResult(candidates, changed, skipped);
    }

    // ============================================================= 校验 =============================================================

    /// <summary>
    /// 校验整张科技树。错误会让游戏读表出错（重复 Id、Categorys 越界、路径坐标成对、前置不存在），保存前必须修好；
    /// 提示不阻断保存（同分类 Type 重复、前置多于两个、消耗为负、斜线）。
    /// </summary>
    public IReadOnlyList<TechIssue> Validate()
    {
        var issues = new List<TechIssue>();
        var seenIds = new HashSet<int>();
        var seenTypes = new HashSet<(int Category, int Type)>();
        var ids = Items.Select(t => t.Id).ToHashSet();
        void Add(TechIssueSeverity severity, CountryTechData t, string text) => issues.Add(new TechIssue(severity, $"[Id={t.Id}] {text}"));

        foreach (var t in Items)
        {
            if (!seenIds.Add(t.Id)) Add(TechIssueSeverity.Error, t, Loc.T("Common.DuplicateId"));
            if (t.Categorys is < 0 or > 4) Add(TechIssueSeverity.Error, t, Loc.F("CountryTech.Validate.CategorysRange", t.Categorys));
            if (!seenTypes.Add((t.CategoryId, t.Type))) Add(TechIssueSeverity.Warning, t, Loc.F("CountryTech.Validate.DuplicateType", t.CategoryId, t.Type));
            if (t.NeedId.Count > 2) Add(TechIssueSeverity.Warning, t, Loc.F("CountryTech.Validate.TooManyNeedId", t.NeedId.Count));
            foreach (var need in t.NeedId.Where(n => !ids.Contains(n)))
                Add(TechIssueSeverity.Error, t, Loc.F("CountryTech.Validate.NeedIdMissing", need));
            if (t.CostMoney < 0 || t.CostGear < 0 || t.CostAtomic < 0) Add(TechIssueSeverity.Warning, t, Loc.T("CountryTech.Validate.NegativeCost"));
            ValidatePath(t, "Lines", t.Lines, issues);
            ValidatePath(t, "Lines2", t.Lines2, issues);
        }
        return issues;
    }

    /// <summary>路径坐标成对；游戏只画正交线段，相邻两点 x、y 都变化就是斜线。</summary>
    private static void ValidatePath(CountryTechData t, string name, List<double> path, List<TechIssue> issues)
    {
        if (path.Count % 2 != 0)
        {
            issues.Add(new TechIssue(TechIssueSeverity.Error, $"[Id={t.Id}] {Loc.F("CountryTech.Validate.OddCoords", name, path.Count)}"));
            return;
        }
        double previousX = 0, previousY = 0;
        for (int i = 0; i + 1 < path.Count; i += 2)
        {
            if (path[i] != previousX && path[i + 1] != previousY)
                issues.Add(new TechIssue(TechIssueSeverity.Warning, $"[Id={t.Id}] {Loc.F("CountryTech.Validate.Diagonal", name, i / 2 + 1)}"));
            previousX = path[i];
            previousY = path[i + 1];
        }
    }

    public static void ClearInstance()
    {
        lock (_lock) { _instance = null; }
    }
}

/// <summary>
/// 国家科技树节点（CountryTechSettings.json）。
/// 名称 / 描述不保存在 JSON 中，而是按 Type 写入 stringtable 的 countrytech_{Type} / countrytech_desc_{Type}。
/// </summary>
public class CountryTechData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("NeedId")] public List<int> NeedId { get; set; } = new();
    [JsonPropertyName("CategoryId")] public int CategoryId { get; set; }
    [JsonPropertyName("Categorys")] public int Categorys { get; set; }
    [JsonPropertyName("Type")] public int Type { get; set; }
    [JsonPropertyName("ResearchLv")] public int ResearchLv { get; set; }
    [JsonPropertyName("CostMoney")] public int CostMoney { get; set; }
    [JsonPropertyName("CostGear")] public int CostGear { get; set; }
    [JsonPropertyName("CostAtomic")] public int CostAtomic { get; set; }
    [JsonPropertyName("Position")] public int Position { get; set; }
    [JsonPropertyName("Chance")] public int Chance { get; set; }
    [JsonPropertyName("Value")] public int Value { get; set; }
    [JsonPropertyName("Lines")] public List<double> Lines { get; set; } = new();
    [JsonPropertyName("Lines2")] public List<double> Lines2 { get; set; } = new();
    [JsonPropertyName("CostMerit")] public int CostMerit { get; set; }
}

/// <summary>校验问题的严重程度：错误阻断保存，提示不阻断。</summary>
public enum TechIssueSeverity
{
    Error,
    Warning
}

/// <summary>一条科技树校验结果。</summary>
public readonly record struct TechIssue(TechIssueSeverity Severity, string Text);

/// <summary>按 NeedId 重写分类连线的结果：有前置的节点数、实际改变的节点数、被跳过的节点 Id。</summary>
public readonly record struct TechRewriteResult(int Candidates, int Changed, IReadOnlyList<int> Skipped);

/// <summary>科技树画布的网格与连线几何（画布绘制、新增节点、连线重写共用，与 Python 工具一致）。</summary>
public static class TechTreeGeometry
{
    public const double NodeStepX = 110, NodeStepY = 92, LineScaleX = 1.1, LineScaleY = 0.92;

    /// <summary>网格（等级 / 位置）→ 世界坐标。</summary>
    public static (double X, double Y) GridToWorld(double level, double position) => (position * NodeStepX, (level - 1) * NodeStepY);

    /// <summary>
    /// 正交连线：Lines 存的是相对 item 自身的偏移（绘制时从节点自身出发），
    /// 因此从 item 出发反向指向 parent，终点才能落在 parent 上。
    /// </summary>
    public static List<double> MakeOrthogonalPath(CountryTechData parent, CountryTechData item)
    {
        var source = GridToWorld(parent.ResearchLv, parent.Position);
        var target = GridToWorld(item.ResearchLv, item.Position);
        int dx = (int)Math.Round((source.X - target.X) / LineScaleX);
        int dy = (int)Math.Round((source.Y - target.Y) / LineScaleY);
        if (dx == 0) return dy == 0 ? new List<double>() : new List<double> { 0, dy };
        if (dy == 0) return new List<double> { dx, 0 };
        int first = dy / 2;
        return new List<double> { 0, first, dx, first, dx, dy };
    }
}
