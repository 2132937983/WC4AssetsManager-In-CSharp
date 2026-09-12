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

    public bool HasData => Items.Count > 0 && Items.Any(i => i.Value != "-");
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
    public HexInfoSection? ReinforcementSection { get; init; }
    public HexInfoSection? TrapSection { get; init; }
    public HexInfoSection? BelongSection { get; init; }

    public string GetModeTitle() => DisplayMode switch
    {
        HexInfoDisplayMode.Terrain => "地形信息",
        HexInfoDisplayMode.Province => "省份信息",
        HexInfoDisplayMode.Building => "建筑信息",
        HexInfoDisplayMode.Army => "单位信息",
        HexInfoDisplayMode.Legion => "军团信息",
        HexInfoDisplayMode.Belong => "归属信息",
        HexInfoDisplayMode.Reinforcement => "援军信息",
        HexInfoDisplayMode.Trap => "陷阱信息",
        _ => "格子信息"
    };

    public List<HexInfoSection> GetSectionsForMode()
    {
        var sections = new List<HexInfoSection> { CoordinateSection };

        switch (DisplayMode)
        {
            case HexInfoDisplayMode.Terrain:
                if (TerrainSection != null) sections.Add(TerrainSection);
                break;
            case HexInfoDisplayMode.Province:
                if (ProvinceSection != null) sections.Add(ProvinceSection);
                break;
            case HexInfoDisplayMode.Building:
                if (BuildingSection != null) sections.Add(BuildingSection);
                break;
            case HexInfoDisplayMode.Army:
                if (ArmySection != null) sections.Add(ArmySection);
                break;
            case HexInfoDisplayMode.Legion:
                if (LegionSection != null) sections.Add(LegionSection);
                break;
            case HexInfoDisplayMode.Belong:
                if (BelongSection != null) sections.Add(BelongSection);
                break;
            case HexInfoDisplayMode.Reinforcement:
                if (ReinforcementSection != null) sections.Add(ReinforcementSection);
                break;
            case HexInfoDisplayMode.Trap:
                if (TrapSection != null) sections.Add(TrapSection);
                break;
            default:
                if (TerrainSection != null) sections.Add(TerrainSection);
                if (ProvinceSection != null) sections.Add(ProvinceSection);
                if (BuildingSection != null) sections.Add(BuildingSection);
                if (ArmySection != null) sections.Add(ArmySection);
                if (LegionSection != null) sections.Add(LegionSection);
                break;
        }

        return sections;
    }

    public static string GetTerrainName(int tileType) => ConfigManager.Instance.GetTerrainTypeName(tileType);

    public static string GetKeyPointText(int keyPoint) => keyPoint switch
    {
        1 => "红圈",
        2 => "绿圈",
        _ => "否"
    };

    public static string FormatHexDump(byte[]? data)
    {
        if (data == null || data.Length == 0) return "无数据";

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