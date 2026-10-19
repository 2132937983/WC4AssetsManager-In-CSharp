using System.Collections.ObjectModel;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Parsers.ArmySetting;
using WC4MapEditor.Core.Parsers.General;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>随机将领的结果：将领 Id、名称、军衔与五个技能等级（按将领表的技能换算）。</summary>
public readonly record struct ReinforcementGeneralPick(int GeneralId, string Name, int Rank, IReadOnlyList<int> SkillLevels);

/// <summary>援军数据检查发现的一个问题：援军在列表中的下标、坐标与说明。</summary>
public readonly record struct ReinforcementIssue(int Index, int Coordinate, string Message);

/// <summary>
/// 援军模块的版本无关部分（模式、命令行只按这一层调用，不必区分 v1 / v3）。
/// <para>
/// 一个格子可以叠放多个援军（游戏地图里很常见），读写都以"格子上的援军组"为单位：
/// 组内顺序就是它们在援军列表里的先后顺序。
/// </para>
/// <para>
/// 自动归属：援军的所属值是军团的<b>行动顺序（ActionId）</b>（见 <see cref="IReinforcementData.Owner"/>）。
/// 放置、粘贴、提交时所属为 0（未设置）就取 <see cref="ResolveNewOwner"/>，
/// 随后格子归属设为组内第一个援军所属军团在列表中的索引（<see cref="ReinforcementModifierBase{T}.SyncHexBelong"/>）。
/// </para>
/// </summary>
public abstract class ReinforcementModifierBase : ModifierBase
{
    /// <summary>
    /// 新援军的所属军团：-1 = 自动（所在省份省会的军团），否则为军团在 <see cref="MapData.Legions"/> 中的索引。
    /// 援军模式的 [ / ] 键切换它。
    /// </summary>
    public int DefaultOwnerLegionIndex { get; set; } = -1;

    /// <summary>援军总数</summary>
    public abstract int GetReinforcementCount();

    /// <summary>格子上的援军数</summary>
    public abstract int CountAt(int col, int row);

    /// <summary>全部援军（按列表顺序的副本，供命令行 / 检查等不区分版本的只读用途）。</summary>
    public abstract IReadOnlyList<IReinforcementData> GetAllRecords();

    /// <summary>格子上的整组援军（副本）。</summary>
    public abstract IReadOnlyList<IReinforcementData> GetRecordsAt(int col, int row);

    /// <summary>
    /// 新建一个援军放到格子上：先取新援军的默认值（随机兵种、自动所属……），再由 <paramref name="customize"/> 改字段。
    /// 返回它在格子组内的下标，坐标无效返回 -1。
    /// </summary>
    public abstract int AddNew(int col, int row, Action<IReinforcementData>? customize = null);

    /// <summary>删除格子上的整组援军，返回删除的个数。</summary>
    public abstract int RemoveAllAt(int col, int row);

    /// <summary>复制格子上的整组援军到剪贴板。</summary>
    public abstract ModifierResult CopyGroup(int col, int row);

    /// <summary>剪贴板里的援军数（0 = 没有可粘贴的）。</summary>
    public abstract int ClipboardCount { get; }

    /// <summary>用剪贴板里的援军组替换格子上的整组援军，并自动设置所属与格子归属。</summary>
    public abstract ModifierResult PasteGroup(int col, int row);

    /// <summary>删除所有援军。</summary>
    public abstract ModifierResult DeleteAllReinforcements();

    /// <summary>
    /// 按援军设置格子归属（援军模式 G 键）：每个有援军的格子设为组内第一个援军所属军团的索引，
    /// 所属未设置或不是本图军团时取所在省份省会的归属。返回归属被改动的格子数。
    /// </summary>
    public abstract int SyncBelongsFromReinforcements();

    /// <summary>给指定归属的每个还没有援军的格子各放一个新援军（所属 = 该归属军团的行动顺序，兵种按地形随机）。</summary>
    public abstract ModifierResult AddReinforcementsToBelongCells(int belongValue);

    /// <summary>随机化格子上整组援军的等级、编制（不超过兵种最大编制）、军衔与将领品质。</summary>
    public abstract ModifierResult RandomizeReinforcementAttributes(int col, int row);

    /// <summary>随机化所属为指定归属军团（-1 = 全部）的援军的等级、编制、军衔与将领品质。</summary>
    public abstract ModifierResult RandomizeReinforcementsByBelong(int belongValue);

    /// <summary>
    /// 按概率在指定归属（-1 = 任意有归属的格子）的空格子上生成援军：所属 = 格子归属军团的行动顺序，
    /// 兵种按地形随机，等级 1~5，编制不超过兵种最大编制。
    /// </summary>
    public abstract ModifierResult GenerateReinforcementsByProbability(int belongValue, int probability);

    /// <summary>该坐标的格子索引（援军的 Coordinate）。</summary>
    protected int CoordOf(int col, int row) => row * _mapData!.MapWidth + col;

    /// <summary>归属值是否表示一个军团（0xFF 与负数 = 无归属）。</summary>
    public static bool IsBelongValue(int belong) => belong >= 0 && belong < 0xFF;

    /// <summary>
    /// 援军所属值（军团行动顺序）对应的归属值，即该军团在 <see cref="MapData.Legions"/> 中的索引；
    /// 所属未设置或没有这个行动顺序的军团时返回 -1。
    /// </summary>
    public int BelongOfOwner(int owner)
    {
        if (owner <= 0 || _mapData == null) return -1;
        var legions = _mapData.Legions;
        for (int i = 0; i < legions.Count; i++)
        {
            if (legions[i].ActionId == owner) return i;
        }
        return -1;
    }

    /// <summary>
    /// 自动归属：所在省份省会格子的归属军团的行动顺序；无省份或省会无归属时退回本格归属；都没有返回 0（未设置）。
    /// </summary>
    public int ResolveAutoOwner(int col, int row)
    {
        if (!IsValidCoord(col, row)) return 0;
        var map = _mapData!;
        int belong = map.GetProvinceCapitalBelong(col, row);
        if (!IsBelongValue(belong)) belong = map.GetBelongValue(col, row);
        if (!IsBelongValue(belong)) return 0;
        return map.ResolveLegionByBelong(belong)?.ActionId ?? 0;
    }

    /// <summary>新援军（以及所属未设置的援军）应取的所属：[ / ] 选定的军团，未选定时为 <see cref="ResolveAutoOwner"/>。</summary>
    public int ResolveNewOwner(int col, int row)
    {
        var legions = _mapData?.Legions;
        if (legions != null && DefaultOwnerLegionIndex >= 0 && DefaultOwnerLegionIndex < legions.Count)
            return legions[DefaultOwnerLegionIndex].ActionId;
        return ResolveAutoOwner(col, row);
    }

    /// <summary>
    /// 为指定所属军团随机挑一名将领：优先取该军团所属国家的将领（排除已被分配的），
    /// 取不到时从全部将领中挑；将领表为空时返回 null。只给出建议，不改数据。
    /// </summary>
    public ReinforcementGeneralPick? PickRandomGeneral(int owner)
    {
        var parser = GeneralSettingParser.Instance;
        var config = ConfigManager.Instance;

        var candidates = new List<int>();
        int legionIndex = BelongOfOwner(owner);
        if (legionIndex >= 0)
        {
            int countryId = _mapData!.Legions[legionIndex].CountryId;
            foreach (int id in config.GetAvailableGenerals(config.GetGeneralsByCountryId(countryId)))
            {
                if (parser.GetById(id) != null) candidates.Add(id);
            }
        }
        if (candidates.Count == 0)
        {
            foreach (var g in parser.All) candidates.Add(g.Id);
        }
        if (candidates.Count == 0) return null;

        int generalId = candidates[Random.Next(candidates.Count)];
        var skills = parser.GetSkills(generalId).Take(5).Select(GeneralSettingParser.CalculateSkillLevel).ToList();
        return new ReinforcementGeneralPick(generalId, parser.GetById(generalId)?.Name ?? "", parser.GetMilitaryRank(generalId), skills);
    }

    /// <summary>
    /// 兵种 ID → 显示名（ArmySettings.json 的 Army / Name）。同一兵种有多个条目时：先去掉精英等级后缀数字
    /// （医疗兵1…医疗兵8 → 医疗兵），再取最短的基础名（战斗机 / 战斗机编队 → 战斗机）或共同后缀
    /// （日本特种兵 / 中国特种兵 → 特种兵），都不行时取第一个名称加"等"。
    /// </summary>
    public static Dictionary<int, string> BuildUnitTypeNames()
    {
        var variants = new SortedDictionary<int, List<string>>();
        foreach (var unit in ArmySettingParser.Instance.Items)
        {
            if (unit.Army <= 0 || string.IsNullOrWhiteSpace(unit.Name)) continue;
            if (!variants.TryGetValue(unit.Army, out var names))
            {
                names = new List<string>();
                variants[unit.Army] = names;
            }
            if (!names.Contains(unit.Name)) names.Add(unit.Name);
        }

        var result = new Dictionary<int, string>(variants.Count);
        foreach (var (army, names) in variants)
            result[army] = names.Count == 1 ? names[0] : MergeVariantNames(names);
        return result;
    }

    private static string MergeVariantNames(List<string> names)
    {
        var bases = names.Select(n => n.TrimEnd("0123456789 ".ToCharArray())).Where(n => n.Length > 0).Distinct().ToList();
        if (bases.Count == 0) bases = names;
        if (bases.Count == 1) return bases[0];

        string shortest = bases.MinBy(n => n.Length)!;
        if (bases.All(n => n.StartsWith(shortest, StringComparison.Ordinal))) return shortest;

        string first = bases[0];
        int length = bases.Min(n => n.Length);
        int k = 0;
        while (k < length && bases.All(n => n[n.Length - 1 - k] == first[first.Length - 1 - k])) k++;
        return k >= 2 ? first[(first.Length - k)..] : Loc.F("Module.Reinforcement.UnitVariants", first);
    }

    /// <summary>格子上应放的随机兵种：海上取 ArmyOnSea、陆地取 ArmyOnLand（setting.txt），都没配置时为 1（轻型步兵）。</summary>
    protected int RandomUnitTypeFor(int col, int row)
    {
        var cfg = ConfigManager.Instance.GetArmyEditConfig();
        bool isSea = _mapData!.GetTerrainRef(col, row).TileType1 == 1;
        var candidates = isSea ? cfg.ArmyOnSea : cfg.ArmyOnLand;
        return candidates.Count > 0 ? candidates[Random.Next(candidates.Count)] : 1;
    }

    /// <summary>该兵种的随机编制：1 ~ 最大编制（兵种表没有最大编制时为 1）。</summary>
    protected int RandomOrganizationFor(int unitType)
    {
        int max = ConfigManager.Instance.GetMaxFormation(unitType);
        return max > 0 ? Random.Next(1, max + 1) : 1;
    }

    /// <summary>援军等级上限：v1 地图只用到 6 级，v3 地图的精英兵种用到 12 级。</summary>
    public const int MaxLevel = 12;

    /// <summary>
    /// 检查援军数据（只读，不修改）：坐标越界或在归属数据范围外、兵种不在兵种表、等级不在 1~12、编制超过最大编制、
    /// 所属未设置或不是本图军团、格子归属与所属军团不一致、爆兵回合小于 1、将领不在将领表。
    /// </summary>
    public IReadOnlyList<ReinforcementIssue> ValidateReinforcements()
    {
        var issues = new List<ReinforcementIssue>();
        var map = _mapData;
        if (map == null) return issues;

        var config = ConfigManager.Instance;
        var unitTypes = config.GetArmyTypeIds();
        var generals = GeneralSettingParser.Instance;
        int total = map.MapWidth * map.MapHeight;
        var records = GetAllRecords();

        for (int i = 0; i < records.Count; i++)
        {
            var r = records[i];
            void Report(string message) => issues.Add(new ReinforcementIssue(i, r.Coordinate, message));

            if (r.Coordinate < 0 || r.Coordinate >= total)
            {
                Report(Loc.F("Module.Reinforcement.Check.Coordinate", r.Coordinate));
                continue;
            }
            var hex = HexCoord.FromIndex(r.Coordinate, map.MapWidth);
            if (!map.IsInBelongRange(hex.Col, hex.Row))
                Report(Loc.T("Module.Reinforcement.Check.OutOfBelongRange", "格子在归属数据范围外，游戏不会读取"));
            if (unitTypes.Count > 0 && !unitTypes.Contains(r.UnitType))
                Report(Loc.F("Module.Reinforcement.Check.UnitType", r.UnitType));
            // 等级、编制只看低字节：游戏地图（尤其 v3）里这两个字段的高字节常是 0xCC 之类的填充
            int level = r.Level & 0xFF;
            if (level < 1 || level > MaxLevel)
                Report(Loc.F("Module.Reinforcement.Check.Level", r.Level, MaxLevel));
            int maxFormation = config.GetMaxFormation(r.UnitType);
            if (maxFormation > 0 && (r.Organization & 0xFF) > maxFormation)
                Report(Loc.F("Module.Reinforcement.Check.Organization", r.Organization, maxFormation));

            int belong = BelongOfOwner(r.Owner);
            if (r.Owner <= 0)
                Report(Loc.T("Module.Reinforcement.Check.OwnerUnset", "所属军团未设置"));
            else if (belong < 0)
                Report(Loc.F("Module.Reinforcement.Check.OwnerNotLegion", r.Owner));
            else if (map.GetBelongValue(hex.Col, hex.Row) != belong)
                Report(Loc.F("Module.Reinforcement.Check.BelongMismatch", map.GetBelongValue(hex.Col, hex.Row), belong));

            if (r.SpawnRound <= 0)
                Report(Loc.F("Module.Reinforcement.Check.SpawnRound", r.SpawnRound));
            if (r.General > 0 && generals.GetById(r.General) == null)
                Report(Loc.F("Module.Reinforcement.Check.General", r.General));
        }
        return issues;
    }

    protected Random Random { get; } = new();

    protected static ModifierResult InvalidCoord() => ModifierResult.Fail(Loc.T("Module.Reinforcement.InvalidCoord", "坐标超出范围"));

    protected static ModifierResult NotLoaded() => ModifierResult.Fail(Loc.T("Module.Reinforcement.NotLoaded", "地图数据未初始化"));
}

/// <summary>
/// 援军模块的通用实现（v1 <see cref="ReinforcementModifier"/> 与 v3 <see cref="ReinforcementV3Modifier"/> 共用），
/// <typeparamref name="T"/> 为本版本的援军记录。
/// </summary>
public abstract class ReinforcementModifierBase<T> : ReinforcementModifierBase where T : struct, IReinforcementData
{
    private readonly List<T> _clipboard = new();

    /// <summary>本版本的援军列表（v1 = Reinforcements，v3 = ReinforcementsV3）。</summary>
    protected abstract ObservableCollection<T> RecordsOf(MapData map);

    /// <summary>本版本的默认援军记录。</summary>
    protected abstract T CreateDefaultRecord();

    private ObservableCollection<T> Records => RecordsOf(_mapData!);

    #region 查询

    /// <summary>格子上的整组援军（按列表顺序的副本）。</summary>
    public IReadOnlyList<T> GetReinforcementsAt(int col, int row)
    {
        var result = new List<T>();
        if (!IsValidCoord(col, row)) return result;
        int coord = CoordOf(col, row);
        foreach (var r in Records)
        {
            if (r.Coordinate == coord) result.Add(r);
        }
        return result;
    }

    public override int CountAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return 0;
        int coord = CoordOf(col, row);
        int count = 0;
        foreach (var r in Records)
        {
            if (r.Coordinate == coord) count++;
        }
        return count;
    }

    public IReadOnlyList<T> GetAllReinforcements() => _mapData != null ? Records : Array.Empty<T>();

    public override IReadOnlyList<IReinforcementData> GetAllRecords()
        => _mapData != null ? Records.Select(r => (IReinforcementData)r).ToList() : Array.Empty<IReinforcementData>();

    public override IReadOnlyList<IReinforcementData> GetRecordsAt(int col, int row)
        => GetReinforcementsAt(col, row).Select(r => (IReinforcementData)r).ToList();

    public T? GetReinforcement(int index)
    {
        if (_mapData == null || index < 0 || index >= Records.Count) return null;
        return Records[index];
    }

    public override int GetReinforcementCount() => _mapData != null ? Records.Count : 0;

    /// <summary>格子组内第 <paramref name="indexInHex"/> 个援军在整个列表里的下标，不存在返回 -1。</summary>
    private int ListIndexOf(int col, int row, int indexInHex)
    {
        if (indexInHex < 0 || !IsValidCoord(col, row)) return -1;
        int coord = CoordOf(col, row);
        var list = Records;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Coordinate != coord) continue;
            if (indexInHex-- == 0) return i;
        }
        return -1;
    }

    #endregion

    #region 新建 / 增删改（按格子组）

    /// <summary>
    /// 新建一个放在该格的援军（编辑窗口的 + 与右键放置）：按地形随机兵种（海上 / 陆地）、按兵种随机编制、
    /// 1 级、第 1 回合，所属取 <see cref="ReinforcementModifierBase.ResolveNewOwner"/>。只生成记录，不写入地图。
    /// </summary>
    public T CreateNewReinforcement(int col, int row)
    {
        var r = CreateDefaultRecord();
        if (!IsValidCoord(col, row)) return r;
        r.Coordinate = CoordOf(col, row);
        r.UnitType = RandomUnitTypeFor(col, row);
        r.Organization = RandomOrganizationFor(r.UnitType);
        r.Owner = ResolveNewOwner(col, row);
        return r;
    }

    public override int AddNew(int col, int row, Action<IReinforcementData>? customize = null)
    {
        if (!IsValidCoord(col, row)) return -1;
        var record = CreateNewReinforcement(col, row);
        if (customize != null)
        {
            IReinforcementData boxed = record;
            customize(boxed);
            record = (T)boxed;
        }
        return AddReinforcement(col, row, record);
    }

    /// <summary>在格子上追加一个援军（坐标按格子改写、所属为 0 时自动填写），并同步格子归属。返回它在组内的下标，失败返回 -1。</summary>
    public int AddReinforcement(int col, int row, T record)
    {
        if (!IsValidCoord(col, row)) return -1;
        int countBefore = CountAt(col, row);
        Records.Add(Normalize(col, row, record));
        SyncHexBelong(col, row);
        MarkModified();
        return countBefore;
    }

    /// <summary>用 <paramref name="record"/> 替换格子组内第 <paramref name="indexInHex"/> 个援军（所属为 0 时自动填写），并同步格子归属。</summary>
    public ModifierResult UpdateReinforcementAt(int col, int row, int indexInHex, T record)
    {
        if (!IsValidCoord(col, row)) return InvalidCoord();
        int listIndex = ListIndexOf(col, row, indexInHex);
        if (listIndex < 0) return ModifierResult.Fail(Loc.T("Module.Reinforcement.NoSuchRecord", "该格子没有这个援军（列表可能已变化）"));
        Records[listIndex] = Normalize(col, row, record);
        SyncHexBelong(col, row);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Reinforcement.Updated", col, row));
    }

    /// <summary>复制格子组内第 <paramref name="indexInHex"/> 个援军并追加到该格，返回新援军在组内的下标，失败返回 -1。</summary>
    public int DuplicateReinforcementAt(int col, int row, int indexInHex)
    {
        int listIndex = ListIndexOf(col, row, indexInHex);
        return listIndex < 0 ? -1 : AddReinforcement(col, row, Records[listIndex]);
    }

    /// <summary>删除格子组内第 <paramref name="indexInHex"/> 个援军。</summary>
    public ModifierResult RemoveReinforcementAt(int col, int row, int indexInHex)
    {
        int listIndex = ListIndexOf(col, row, indexInHex);
        if (listIndex < 0) return ModifierResult.Fail(Loc.T("Module.Reinforcement.NoSuchRecord", "该格子没有这个援军（列表可能已变化）"));
        Records.RemoveAt(listIndex);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Reinforcement.Removed", 1, col, row));
    }

    /// <summary>
    /// 用 <paramref name="records"/> 替换格子上的整组援军：新组插回原来第一个援军的位置（文件顺序尽量不变），
    /// 坐标按格子改写、所属为 0 的自动填写，然后同步格子归属。<paramref name="records"/> 为空等于删除整组。
    /// </summary>
    public ModifierResult ReplaceReinforcementsAt(int col, int row, IReadOnlyList<T> records)
    {
        if (!IsValidCoord(col, row)) return InvalidCoord();
        var list = Records;
        int coord = CoordOf(col, row);
        int insertAt = -1;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].Coordinate != coord) continue;
            list.RemoveAt(i);
            insertAt = i;
        }
        if (insertAt < 0) insertAt = list.Count;

        foreach (var record in records)
            list.Insert(insertAt++, Normalize(col, row, record));

        if (records.Count > 0) SyncHexBelong(col, row);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Reinforcement.Replaced", col, row, records.Count), records.Count);
    }

    public override int RemoveAllAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return 0;
        var list = Records;
        int coord = CoordOf(col, row);
        int removed = 0;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].Coordinate != coord) continue;
            list.RemoveAt(i);
            removed++;
        }
        if (removed > 0) MarkModified();
        return removed;
    }

    public override ModifierResult DeleteAllReinforcements()
    {
        if (_mapData == null) return NotLoaded();
        int count = Records.Count;
        Records.Clear();
        if (count > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Reinforcement.DeletedAll", count), count);
    }

    /// <summary>删除列表中第 <paramref name="index"/> 个援军（按整个列表的下标）。</summary>
    public ModifierResult DeleteReinforcement(int index)
    {
        if (_mapData == null) return NotLoaded();
        if (index < 0 || index >= Records.Count)
            return ModifierResult.Fail(Loc.T("Module.Reinforcement.IndexOutOfRange", "援军索引超出范围"));
        var coord = HexCoord.FromIndex(Records[index].Coordinate, _mapData.MapWidth);
        Records.RemoveAt(index);
        MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Reinforcement.Removed", 1, coord.Col, coord.Row));
    }

    /// <summary>坐标按格子改写，所属未设置时自动填写。</summary>
    private T Normalize(int col, int row, T record)
    {
        record.Coordinate = CoordOf(col, row);
        if (record.Owner <= 0) record.Owner = ResolveNewOwner(col, row);
        return record;
    }

    #endregion

    #region 复制 / 粘贴（整组）

    public override ModifierResult CopyGroup(int col, int row)
    {
        if (!IsValidCoord(col, row)) return InvalidCoord();
        var group = GetReinforcementsAt(col, row);
        if (group.Count == 0) return ModifierResult.Fail(Loc.T("Module.Reinforcement.NoneAtHex", "该格子没有援军"));
        _clipboard.Clear();
        _clipboard.AddRange(group);
        return ModifierResult.Ok(Loc.F("Module.Reinforcement.Copied", group.Count), group.Count);
    }

    public override int ClipboardCount => _clipboard.Count;

    public override ModifierResult PasteGroup(int col, int row)
    {
        if (!IsValidCoord(col, row)) return InvalidCoord();
        if (_clipboard.Count == 0)
            return ModifierResult.Fail(Loc.T("Module.Reinforcement.ClipboardEmpty", "没有可粘贴的援军组，请先按 C 复制"));
        ReplaceReinforcementsAt(col, row, _clipboard);
        return ModifierResult.Ok(Loc.F("Module.Reinforcement.Pasted", _clipboard.Count), _clipboard.Count);
    }

    #endregion

    #region 自动归属

    /// <summary>
    /// 把格子归属设为组内第一个援军所属军团的索引；所属未设置或不是本图军团时取所在省份省会的归属。
    /// 归属有变化返回 true（格子不在归属数据范围内时不改）。
    /// </summary>
    public bool SyncHexBelong(int col, int row)
    {
        if (!IsValidCoord(col, row)) return false;
        int coord = CoordOf(col, row);
        int belong = -1;
        bool found = false;
        foreach (var r in Records)
        {
            if (r.Coordinate != coord) continue;
            belong = BelongOfOwner(r.Owner);
            found = true;
            break;
        }
        if (!found) return false;

        var map = _mapData!;
        if (belong < 0)
        {
            int capital = map.GetProvinceCapitalBelong(col, row);
            if (!IsBelongValue(capital)) return false;
            belong = capital;
        }
        if (map.GetBelongValue(col, row) == belong) return false;
        if (!map.SetBelongValue(col, row, belong)) return false;
        MarkModified();
        return true;
    }

    public override int SyncBelongsFromReinforcements()
    {
        if (_mapData == null) return 0;
        var visited = new HashSet<int>();
        int changed = 0;
        // SyncHexBelong 只改归属不改援军列表，可以边遍历边调用
        foreach (var r in Records)
        {
            if (r.Coordinate < 0 || !visited.Add(r.Coordinate)) continue;
            var hex = HexCoord.FromIndex(r.Coordinate, _mapData.MapWidth);
            if (SyncHexBelong(hex.Col, hex.Row)) changed++;
        }
        return changed;
    }

    #endregion

    #region IModifier

    /// <summary>在格子上追加一个援军：<paramref name="parameter"/> 为 <typeparamref name="T"/> 时用它，否则新建（<see cref="CreateNewReinforcement"/>）。</summary>
    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (_mapData == null) return NotLoaded();
        if (!IsValidCoord(col, row)) return InvalidCoord();
        var record = parameter is T given ? given : CreateNewReinforcement(col, row);
        int index = AddReinforcement(col, row, record);
        return ModifierResult.Ok(Loc.F("Module.Reinforcement.Placed", col, row, index + 1));
    }

    /// <summary>删除格子上的整组援军。</summary>
    public override ModifierResult Remove(int col, int row)
    {
        if (_mapData == null) return NotLoaded();
        if (!IsValidCoord(col, row)) return InvalidCoord();
        int removed = RemoveAllAt(col, row);
        return removed > 0
            ? ModifierResult.Ok(Loc.F("Module.Reinforcement.Removed", removed, col, row), removed)
            : ModifierResult.Fail(Loc.T("Module.Reinforcement.NoneAtHex", "该格子没有援军"));
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);

    public override bool CanRemove(int col, int row) => CountAt(col, row) > 0;

    /// <summary>格子上第一个援军（没有则为 null）。整组请用 <see cref="GetReinforcementsAt"/>。</summary>
    public override object? GetDataAt(int col, int row)
    {
        var group = GetReinforcementsAt(col, row);
        return group.Count > 0 ? group[0] : null;
    }

    /// <summary><paramref name="data"/> 为整组（<see cref="IReadOnlyList{T}"/>）时替换整组；为单个记录时替换组内第一个（没有则追加）。</summary>
    public override bool SetDataAt(int col, int row, object data)
    {
        if (!IsValidCoord(col, row)) return false;
        switch (data)
        {
            case IReadOnlyList<T> group:
                return ReplaceReinforcementsAt(col, row, group).Success;
            case T record:
                if (CountAt(col, row) == 0) return AddReinforcement(col, row, record) >= 0;
                return UpdateReinforcementAt(col, row, 0, record).Success;
            default:
                return false;
        }
    }

    #endregion

    #region 批量生成 / 随机化

    public override ModifierResult AddReinforcementsToBelongCells(int belongValue)
    {
        if (_mapData == null) return NotLoaded();
        int owner = _mapData.ResolveLegionByBelong(belongValue)?.ActionId ?? 0;
        if (!IsBelongValue(belongValue) || owner <= 0)
            return ModifierResult.Fail(Loc.F("Module.Reinforcement.NoSuchLegion", belongValue));

        var occupied = new HashSet<int>(Records.Select(r => r.Coordinate));
        int added = 0;
        for (int row = 0; row < _mapData.MapHeight; row++)
        {
            for (int col = 0; col < _mapData.MapWidth; col++)
            {
                if (_mapData.GetBelongValue(col, row) != belongValue || occupied.Contains(CoordOf(col, row))) continue;
                var r = CreateNewReinforcement(col, row);
                r.Owner = owner;
                Records.Add(r);
                added++;
            }
        }
        if (added > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Reinforcement.FilledBelong", belongValue, added), added);
    }

    public override ModifierResult RandomizeReinforcementAttributes(int col, int row)
    {
        if (_mapData == null) return NotLoaded();
        if (!IsValidCoord(col, row)) return InvalidCoord();
        int coord = CoordOf(col, row);
        int count = RandomizeWhere(r => r.Coordinate == coord);
        return count > 0
            ? ModifierResult.Ok(Loc.F("Module.Reinforcement.Randomized", count), count)
            : ModifierResult.Fail(Loc.T("Module.Reinforcement.NoneAtHex", "该格子没有援军"));
    }

    public override ModifierResult RandomizeReinforcementsByBelong(int belongValue)
    {
        if (_mapData == null) return NotLoaded();
        int count = belongValue == -1
            ? RandomizeWhere(_ => true)
            : RandomizeWhere(r => BelongOfOwner(r.Owner) == belongValue);
        return ModifierResult.Ok(Loc.F("Module.Reinforcement.Randomized", count), count);
    }

    private int RandomizeWhere(Func<T, bool> predicate)
    {
        var list = Records;
        int count = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            if (!predicate(r)) continue;
            r.Level = Random.Next(1, 6);
            r.Organization = RandomOrganizationFor(r.UnitType);
            r.Rank = Random.Next(0, 6);
            r.Quality = Random.Next(0, 5);
            list[i] = r;
            count++;
        }
        if (count > 0) MarkModified();
        return count;
    }

    public override ModifierResult GenerateReinforcementsByProbability(int belongValue, int probability)
    {
        if (_mapData == null) return NotLoaded();
        if (probability < 0 || probability > 100)
            return ModifierResult.Fail(Loc.T("Module.Reinforcement.BadProbability", "概率必须在0-100之间"));

        var occupied = new HashSet<int>(Records.Select(r => r.Coordinate));
        int generated = 0;
        for (int row = 0; row < _mapData.MapHeight; row++)
        {
            for (int col = 0; col < _mapData.MapWidth; col++)
            {
                if (Random.Next(0, 100) >= probability) continue;
                int cellBelong = _mapData.GetBelongValue(col, row);
                if (!IsBelongValue(cellBelong) || (belongValue != -1 && cellBelong != belongValue)) continue;
                if (occupied.Contains(CoordOf(col, row))) continue;
                int owner = _mapData.ResolveLegionByBelong(cellBelong)?.ActionId ?? 0;
                if (owner <= 0) continue;

                var r = CreateNewReinforcement(col, row);
                r.Owner = owner;
                r.Level = Random.Next(1, 6);
                Records.Add(r);
                occupied.Add(r.Coordinate);
                generated++;
            }
        }
        if (generated > 0) MarkModified();
        return ModifierResult.Ok(Loc.F("Module.Reinforcement.Generated", generated), generated);
    }

    #endregion
}

/// <summary>v1 / v2 地图（BtlVersion &lt; 3）的援军模块，数据在 <see cref="MapData.Reinforcements"/>。</summary>
public sealed class ReinforcementModifier : ReinforcementModifierBase<Reinforcement>
{
    public override string Name => "reinforcement";
    public override string DisplayName => Loc.T("Module.Reinforcement.Name", "援军修改器");

    protected override ObservableCollection<Reinforcement> RecordsOf(MapData map) => map.Reinforcements;

    protected override Reinforcement CreateDefaultRecord() => Reinforcement.CreateDefault();
}
