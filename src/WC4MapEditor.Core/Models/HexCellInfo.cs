using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Models;

public enum HexInfoDisplayMode
{
    Default = 0,
    Terrain = 1,
    Province = 2,
    Building = 3,
    Army = 4,
    Legion = 5,
    Belong = 6,
    Reinforcement = 7,
    Trap = 8
}

public class HexInfoSection
{
    public string Title { get; init; } = "";
    public List<(string Label, string Value)> Items { get; init; } = new();
    public byte[]? RawData { get; init; }

    /// <summary>该格没有这类数据（分区里只有一行"无"占位）。默认视图据此省略空分区。</summary>
    public bool IsEmpty { get; init; }

    public bool HasData => !IsEmpty && Items.Count > 0;
}

public class HexCellInfo
{
    public int Col { get; init; }
    public int Row { get; init; }
    public int CoordinateIndex => MapWidth > 0 ? Row * MapWidth + Col : 0;
    public int MapWidth { get; init; }

    public HexInfoDisplayMode DisplayMode { get; init; } = HexInfoDisplayMode.Default;

    public HexInfoSection CoordinateSection { get; init; } = new();
    public HexInfoSection? TerrainSection { get; init; }
    public HexInfoSection? ProvinceSection { get; init; }
    public HexInfoSection? BuildingSection { get; init; }
    public HexInfoSection? ArmySection { get; init; }
    public HexInfoSection? LegionSection { get; init; }
    public HexInfoSection? TrapSection { get; init; }
    public HexInfoSection? BelongSection { get; init; }

    /// <summary>该格上的全部援军（一格可以有多条援军记录，每条一个分区；没有援军时是一个空分区）。</summary>
    public IReadOnlyList<HexInfoSection> ReinforcementSections { get; init; } = Array.Empty<HexInfoSection>();

    public string GetModeTitle() => DisplayMode switch
    {
        HexInfoDisplayMode.Terrain => Loc.T("Assist.HexInfo.Mode.Terrain", "地形信息"),
        HexInfoDisplayMode.Province => Loc.T("Assist.HexInfo.Mode.Province", "省份信息"),
        HexInfoDisplayMode.Building => Loc.T("Assist.HexInfo.Mode.Building", "建筑信息"),
        HexInfoDisplayMode.Army => Loc.T("Assist.HexInfo.Mode.Army", "单位信息"),
        HexInfoDisplayMode.Legion => Loc.T("Assist.HexInfo.Mode.Legion", "军团信息"),
        HexInfoDisplayMode.Belong => Loc.T("Assist.HexInfo.Mode.Belong", "归属信息"),
        HexInfoDisplayMode.Reinforcement => Loc.T("Assist.HexInfo.Mode.Reinforcement", "援军信息"),
        HexInfoDisplayMode.Trap => Loc.T("Assist.HexInfo.Mode.Trap", "陷阱信息"),
        _ => Loc.T("Assist.HexInfo.Title", "格子信息")
    };

    /// <summary>
    /// 当前显示模式下要展示的分区（坐标分区总在最前）。
    /// <para>
    /// 各编辑模式只看与自己相关的数据：省份模式附带归属（省会归属决定省份颜色），
    /// 单位模式附带陷阱（陷阱操作在单位部署模式里），军团 / 归属模式同时给出归属值与所属军团。
    /// 默认视图（浏览模式与没有专属视图的模式）列出地形、省份、归属，其余分区只在该格确有数据时出现。
    /// </para>
    /// </summary>
    public List<HexInfoSection> GetSectionsForMode()
    {
        var sections = new List<HexInfoSection> { CoordinateSection };

        switch (DisplayMode)
        {
            case HexInfoDisplayMode.Terrain:
                Add(sections, TerrainSection);
                break;
            case HexInfoDisplayMode.Province:
                Add(sections, ProvinceSection);
                Add(sections, BelongSection);
                break;
            case HexInfoDisplayMode.Building:
                Add(sections, BuildingSection);
                break;
            case HexInfoDisplayMode.Army:
                Add(sections, ArmySection);
                Add(sections, TrapSection);
                break;
            case HexInfoDisplayMode.Legion:
            case HexInfoDisplayMode.Belong:
                Add(sections, BelongSection);
                Add(sections, LegionSection);
                break;
            case HexInfoDisplayMode.Reinforcement:
                sections.AddRange(ReinforcementSections);
                break;
            case HexInfoDisplayMode.Trap:
                Add(sections, TrapSection);
                break;
            default:
                Add(sections, TerrainSection);
                Add(sections, ProvinceSection);
                Add(sections, BelongSection);
                AddIfData(sections, BuildingSection);
                AddIfData(sections, ArmySection);
                AddIfData(sections, LegionSection);
                AddIfData(sections, TrapSection);
                foreach (var s in ReinforcementSections) AddIfData(sections, s);
                break;
        }

        return sections;
    }

    private static void Add(List<HexInfoSection> list, HexInfoSection? section)
    {
        if (section != null) list.Add(section);
    }

    private static void AddIfData(List<HexInfoSection> list, HexInfoSection? section)
    {
        if (section is { HasData: true }) list.Add(section);
    }

    public static string GetKeyPointText(int keyPoint) => keyPoint switch
    {
        0 => Loc.T("Assist.HexInfo.Value.No", "否"),
        1 => Loc.T("Assist.HexInfo.Value.RedCircle", "红圈"),
        2 => Loc.T("Assist.HexInfo.Value.GreenCircle", "绿圈"),
        _ => Loc.F("Assist.HexInfo.Value.Unknown", keyPoint)
    };

    public static string FormatHexDump(byte[]? data)
    {
        if (data == null || data.Length == 0) return Loc.T("Assist.HexInfo.Value.NoData", "无数据");

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < data.Length; i += 4)
        {
            sb.Append($"{i:X4}: ");
            for (int j = 0; j < 4; j++)
            {
                if (i + j < data.Length)
                    sb.Append($"{data[i + j]:X2} ");
                else
                    sb.Append("   ");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
