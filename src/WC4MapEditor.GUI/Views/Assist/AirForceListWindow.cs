using System.Collections.Specialized;
using System.Globalization;
using WC4MapEditor.Core.Mode;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.GUI.Services;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 空袭列表窗口（空袭编辑模式 Q 键，对应 VB 的 AirForceListSetting）：左边列出全部空袭，右边编辑坐标 / 兵种 / 弹药 / 军团 / 回合。
/// <para>
/// 与 VB 的差别：改的是字段副本，按 Enter / 应用 / 切换条目时才经模块提交并记撤销（VB 每敲一个字就写回、取消也撤不回）；
/// 坐标用"列,行"并检查在地图内（VB 是不检查的格子索引）；新增的空袭放在地图上选中的格子（VB 放在左上角 0 号格）；
/// 军团、回合不再截成一个字节（VB 输入 -1 或 300 会抛异常）。
/// </para>
/// </summary>
[AssistEditor(EditorKinds.AirForceList)]
public sealed class AirForceListWindow : AirListWindowBase
{
    private TextBox _coordBox = null!;
    private TextBlock _coordHint = null!;
    private Dictionary<int, string>? _cityNames;

    protected override string TitleText => Loc.T("Assist.AirForceList.Title", "空袭列表管理");
    protected override int ItemCount => Map.AirForces.Count;
    protected override INotifyCollectionChanged? ObservedList => Map.AirForces;
    protected override string TypeLabel => Loc.T("Assist.AirForceList.Field.Type", "兵种");
    protected override bool SupportsDuplicate => true;

    private static string CoordLabel => Loc.T("Assist.AirForceList.Field.Coordinate", "坐标");

    protected override string DescribeItem(int index)
    {
        var af = Map.AirForces[index];
        var (col, row) = ToCell(af.Coordinate);
        return Loc.F("Assist.AirForceList.ItemTitle", index + 1, col, row, af.TriggerRound) + "\n"
             + DescribeDetail(af.UnitType, af.AmmoType, af.OwnerLegion);
    }

    protected override void BuildFields(FieldPanel f)
    {
        f.Section(Loc.T("Common.BasicInfo", "基本信息"));

        _coordBox = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
        _coordBox.TextChanged += (_, _) =>
        {
            f.Changed();
            UpdateCoordHint();
        };
        var useSelection = EditorTheme.Button(Loc.T("Assist.AirForceList.UseSelection", "取选中格"), (_, _) => UseSelectedCell());
        useSelection.ToolTip = Loc.T("Assist.AirForceList.UseSelectionTip", "把坐标设为地图上当前选中的格子");
        var locate = EditorTheme.Button(Loc.T("Assist.AirForceList.Locate", "定位"), (_, _) => LocateOnMap());
        locate.ToolTip = Loc.T("Assist.AirForceList.LocateTip", "在地图上选中该格并移动视角过去");
        _coordHint = EditorTheme.Muted("");
        _coordHint.TextWrapping = TextWrapping.Wrap;
        _coordHint.Margin = new Thickness(0, 4, 0, 0);
        var coordPanel = new StackPanel();
        coordPanel.Children.Add(Row(_coordBox, useSelection, locate));
        coordPanel.Children.Add(_coordHint);
        f.Custom(CoordLabel, coordPanel, Loc.T("Assist.AirForceList.CoordinateTip", "机场所在格，写成“列,行”，例如 12,5"));

        BuildCommonFields(f);
    }

    protected override void LoadFields(int index)
    {
        var af = Map.AirForces[index];
        var (col, row) = ToCell(af.Coordinate);
        _coordBox.Text = FormatCell(col, row);
        LoadCommonFields(af.UnitType, af.AmmoType, af.OwnerLegion, af.TriggerRound);
    }

    protected override bool TryCommitFields(int index, List<string> errors)
    {
        bool ok = TryReadCell(errors, out int col, out int row);
        ok &= TryReadCommonFields(errors, out int type, out int ammo, out int legion, out int round);
        if (!ok) return false;

        var result = Module.UpdateAirForce(index, new AirForce
        {
            Coordinate = row * Map.MapWidth + col,
            UnitType = type,
            AmmoType = ammo,
            OwnerLegion = legion,
            TriggerRound = round
        });
        if (!result.Success) errors.Add(result.Message ?? "");
        return result.Success;
    }

    /// <summary>新增：放在地图上选中的格子（没有选中时用打开窗口时的焦点格），军团取该格归属。</summary>
    protected override int AddItem()
    {
        var (col, row) = TargetCell();
        var result = Module.Apply(col, row);
        return result.Success ? Map.AirForces.Count - 1 : -1;
    }

    protected override void RemoveItem(int index) => Module.DeleteAirForce(index);

    protected override int DuplicateItem(int index)
        => Module.DuplicateAirForce(index).Success ? index + 1 : -1;

    protected override void RecordChange(string description, Action apply)
        => Context.Editor.RecordCollectionChange(description, Map.AirForces, apply);

    protected override void OnAttached()
    {
        SelectFirstAt(Context.Request.Col, Context.Request.Row);
    }

    /// <summary>窗口已打开时在另一个格子上又按了 Q：选中该格上的第一条空袭。</summary>
    protected override void OnRetarget(EditorRequest request) => SelectFirstAt(request.Col, request.Row);

    // ================================================================== 坐标

    /// <summary>选中该格上的第一条空袭并滚动到可见（先提交当前项；输入无效时留在当前项）。</summary>
    private void SelectFirstAt(int col, int row)
    {
        if (!IsInMap(col, row)) return;
        int coordinate = row * Map.MapWidth + col;
        for (int i = 0; i < Map.AirForces.Count; i++)
        {
            if (Map.AirForces[i].Coordinate != coordinate) continue;
            if (CommitCurrent()) RefreshList(i);
            return;
        }
    }

    /// <summary>新增条目的位置：地图上选中的格子 → 打开窗口时的焦点格 → 左上角。</summary>
    private (int Col, int Row) TargetCell()
    {
        if (HexSelector.Instance.PrimarySelected is { } selected && IsInMap(selected.Col, selected.Row))
            return (selected.Col, selected.Row);
        if (IsInMap(Context.Request.Col, Context.Request.Row))
            return (Context.Request.Col, Context.Request.Row);
        return (0, 0);
    }

    private void UseSelectedCell()
    {
        if (HexSelector.Instance.PrimarySelected is { } selected && IsInMap(selected.Col, selected.Row))
        {
            _coordBox.Text = FormatCell(selected.Col, selected.Row);
            return;
        }
        SetStatus(Loc.T("Assist.AirForceList.NoSelection", "请先在地图上选中一个格子"), true);
    }

    private void LocateOnMap()
    {
        if (!TryParseCell(_coordBox.Text, out int col, out int row) || !IsInMap(col, row))
        {
            SetStatus(Loc.F("Assist.AirForceList.BadCoordinate", CoordLabel), true);
            return;
        }
        HexSelector.Instance.Select(col, row, Map.MapWidth, Map.MapHeight);
        Context.MoveCamera?.Invoke(col, row);
    }

    /// <summary>坐标下方的提示：该格的机场（名称、航空等级），或"没有建筑 / 没有机场"的提醒。</summary>
    private void UpdateCoordHint()
    {
        if (!TryParseCell(_coordBox.Text, out int col, out int row) || !IsInMap(col, row))
        {
            _coordHint.Text = "";
            return;
        }

        int index = Map.FindBuildingIndex(col, row);
        if (index < 0)
        {
            ShowCoordHint(Loc.T("Assist.AirForceList.HintNoBuilding", "该格没有建筑：空袭应放在机场上"), warn: true);
            return;
        }

        var building = Map.Buildings[index];
        if (building.AviationLevel <= 0)
        {
            ShowCoordHint(Loc.T("Assist.AirForceList.HintNoAirport", "该格的建筑没有机场（航空等级为 0）"), warn: true);
            return;
        }
        ShowCoordHint(Loc.F("Assist.AirForceList.HintAirport", BuildingLabel(building), building.AviationLevel), warn: false);
    }

    private void ShowCoordHint(string text, bool warn)
    {
        _coordHint.Text = text;
        _coordHint.Foreground = warn ? EditorTheme.Warning : EditorTheme.TextMuted;
    }

    private string BuildingLabel(Building building)
    {
        if (building.Name != 0 && building.Name != 0xFFFF)
        {
            _cityNames ??= LoadCityNames();
            if (_cityNames.TryGetValue(building.Name, out var name)) return name;
        }
        return Loc.T("Assist.AirForceList.UnnamedBuilding", "无名建筑");
    }

    private static Dictionary<int, string> LoadCityNames()
    {
        try
        {
            return ConfigManager.Instance.GetStringTableParser().FindCityNames();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AirForceListWindow] 读取城市名称表失败: {ex.Message}");
            return new Dictionary<int, string>();
        }
    }

    private bool TryReadCell(List<string> errors, out int col, out int row)
    {
        if (!TryParseCell(_coordBox.Text, out col, out row))
        {
            errors.Add(Loc.F("Assist.AirForceList.BadCoordinate", CoordLabel));
            return false;
        }
        if (!IsInMap(col, row))
        {
            errors.Add(Loc.F("Assist.AirForceList.CoordinateOutOfMap", CoordLabel, col, row, Map.MapWidth - 1, Map.MapHeight - 1));
            return false;
        }
        return true;
    }

    /// <summary>解析"列,行"（也接受全角逗号与空格分隔）。</summary>
    private static bool TryParseCell(string text, out int col, out int row)
    {
        col = row = 0;
        var parts = text.Split(new[] { ',', '，', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out col)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out row);
    }

    private static string FormatCell(int col, int row)
        => col.ToString(CultureInfo.InvariantCulture) + "," + row.ToString(CultureInfo.InvariantCulture);

    private (int Col, int Row) ToCell(int coordinate)
        => Map.MapWidth > 0 ? (coordinate % Map.MapWidth, coordinate / Map.MapWidth) : (0, 0);

    private bool IsInMap(int col, int row) => col >= 0 && row >= 0 && col < Map.MapWidth && row < Map.MapHeight;
}
