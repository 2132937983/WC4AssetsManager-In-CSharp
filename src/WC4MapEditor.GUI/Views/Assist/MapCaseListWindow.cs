using System.Collections.Specialized;
using System.Globalization;
using WC4MapEditor.Core.Mode;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.GUI.Services;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 方案列表管理窗口（VB MapCaseListSetting）：左边方案列表，右边编辑方针编号 / 方针类型 / 触发回合 / 目标值。
/// <para>
/// 方案编辑模式按 Q 打开。增删改都经由 <see cref="MapCaseModifier"/>，并记录撤销（Ctrl+Z 在地图上撤销）；
/// 输入先校验再写入（VB 每敲一个字就直接写回，且不做任何校验）。
/// 方针类型取自 setting.txt [方针修改] 的 CaseList；"走向地块"类型的目标值可以直接输入"列,行"。
/// </para>
/// </summary>
[AssistEditor(EditorKinds.MapCaseList)]
public sealed class MapCaseListWindow : ListEditorWindow
{
    private TextBox _policyBox = null!;
    private ComboBox _typeBox = null!;
    private TextBox _roundBox = null!;
    private TextBox _targetBox = null!;
    private TextBlock _targetInfo = null!;
    private TextBlock _unitInfo = null!;
    private Button _selectUnitsButton = null!;
    private Button _locateButton = null!;

    /// <summary>本窗口正在通过 <see cref="RecordChange"/> 改方案列表。</summary>
    private bool _ownChange;

    /// <summary>方案列表被窗口外改过（地图上撤销 / 重做、命令行……），下次激活窗口时重新载入。</summary>
    private bool _stale;

    /// <summary>订阅了变更通知的方案列表（关闭窗口时退订）。</summary>
    private INotifyCollectionChanged? _watchedCases;

    public MapCaseListWindow()
    {
        Activated += OnWindowActivated;
    }

    private MapCaseModifier Module => Context.Module<MapCaseModifier>()
        ?? throw new InvalidOperationException("MapCaseModifier 未在 EditModeManager 中注册");

    private static IReadOnlyList<ConfigKeyValue> CaseTypes => ConfigManager.Instance.GetCaseEditConfig().CaseList;

    private static string PolicyLabel => Loc.T("Assist.MapCase.Field.Policy", "方针编号");
    private static string TypeLabel => Loc.T("Assist.MapCase.Field.Type", "方针类型");
    private static string RoundLabel => Loc.T("Assist.MapCase.Field.Round", "触发回合");
    private static string TargetLabel => Loc.T("Assist.MapCase.Field.Target", "目标值");

    // ================================================================== ListEditorWindow

    protected override string TitleText => Loc.T("Assist.MapCase.Title", "方案列表管理");

    protected override int ItemCount => Map.Cases.Count;

    protected override string DescribeItem(int index)
    {
        var c = Map.Cases[index];
        return Loc.F("Assist.MapCase.Item", index + 1, c.PolicyNumber, c.TriggerRound, TypeName(c.CaseType));
    }

    protected override bool SupportsDuplicate => true;

    protected override void BuildFields(FieldPanel f)
    {
        f.Section(Loc.T("Assist.MapCase.Section.Case", "基本信息"));
        _policyBox = f.Text(PolicyLabel, Loc.F("Assist.MapCase.Tip.Policy", MapCaseModifier.MaxPolicy));
        _typeBox = f.Choice(TypeLabel, CaseTypes, Loc.T("Assist.MapCase.Tip.Type", "取值表来自 setting.txt [方针修改] 的 CaseList，也可以直接输入数值"));
        _roundBox = f.Text(RoundLabel);
        _targetBox = f.Text(TargetLabel, Loc.T("Assist.MapCase.Tip.Target", "含义随方针类型而定；\"使单位走向目标值序号地块\"时为地块序号（行 × 地图宽 + 列），也可以输入\"列,行\""));

        _targetInfo = EditorTheme.Muted("");
        _targetInfo.TextWrapping = TextWrapping.Wrap;
        _locateButton = EditorTheme.Button(Loc.T("Assist.MapCase.Locate", "定位目标地块"), (_, _) => LocateTarget());
        _locateButton.Margin = new Thickness(8, 0, 0, 0);
        f.Custom("", TextWithButton(_targetInfo, _locateButton));

        _typeBox.SelectionChanged += (_, _) => UpdateTargetInfo();
        _typeBox.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) => UpdateTargetInfo()));
        _targetBox.TextChanged += (_, _) => UpdateTargetInfo();

        f.Section(Loc.T("Assist.MapCase.Section.Units", "使用该方针的单位"));
        _unitInfo = EditorTheme.Muted("");
        _unitInfo.TextWrapping = TextWrapping.Wrap;
        _selectUnitsButton = EditorTheme.Button(Loc.T("Assist.MapCase.SelectUnits", "在地图上选中"), (_, _) => SelectPolicyUnits());
        _selectUnitsButton.Margin = new Thickness(8, 0, 0, 0);
        f.Custom("", TextWithButton(_unitInfo, _selectUnitsButton));

        f.Hint(Loc.T("Assist.MapCase.Hint", "方针值等于方针编号的单位执行该方案；同一方针编号可以有多条方案（例如先改类型、下一回合再走向某个地块）。在地图上选中单位后按 Ctrl+Q 设置方针值。"));
    }

    protected override void LoadFields(int index)
    {
        var c = Map.Cases[index];
        _policyBox.Text = Format(c.PolicyNumber);
        SetChoice(_typeBox, c.CaseType);
        _roundBox.Text = Format(c.TriggerRound);
        _targetBox.Text = Format(c.TargetTile);
        UpdateTargetInfo();
        UpdateUnitInfo();
    }

    /// <summary>
    /// 解析字段后交给 <see cref="MapCaseModifier.UpdateCase"/>：范围规则在模块里（与命令行共用），
    /// 未改动的字段不检查范围，游戏自带地图里超出编辑范围的旧数据不会卡住对其他字段的修改。
    /// </summary>
    protected override bool TryCommitFields(int index, List<string> errors)
    {
        if (_stale)
        {
            // 列表在窗口外被改过（例如地图上撤销），当前下标可能已指向别的方案：不写回，重新载入后再编辑
            errors.Add(Loc.T("Assist.MapCase.StaleEdit", "方案列表已在窗口外修改，本次修改未保存"));
            Dispatcher.BeginInvoke(() => ReloadIfStale());
            return false;
        }

        bool ok = TryInt(_policyBox, PolicyLabel, int.MinValue, int.MaxValue, errors, out int policy);
        ok &= TryChoice(_typeBox, TypeLabel, int.MinValue, int.MaxValue, errors, out int type);
        ok &= TryInt(_roundBox, RoundLabel, int.MinValue, int.MaxValue, errors, out int round);
        if (!TryParseTarget(_targetBox.Text, type == MapCase.MoveToTileType, out int target))
        {
            errors.Add(Loc.F("Assist.ListEditor.NotInteger", TargetLabel));
            ok = false;
        }
        if (!ok) return false;

        var result = Module.UpdateCase(index, new MapCase
        {
            PolicyNumber = policy,
            CaseType = type,
            TriggerRound = round,
            TargetTile = target,
        });
        if (!result.Success)
        {
            errors.Add(result.Message ?? Loc.T("Assist.ListEditor.Invalid", "输入无效"));
            return false;
        }

        UpdateUnitInfo();
        return true;
    }

    protected override int AddItem()
        => Module.AddCase(Module.CreateCase()).Success ? Map.Cases.Count - 1 : -1;

    protected override void RemoveItem(int index) => Module.DeleteCase(index);

    protected override int DuplicateItem(int index) => Module.DuplicateCase(index);

    protected override void RecordChange(string description, Action apply)
        => Context.Editor.RecordCollectionChange(description, Map.Cases, () =>
        {
            _ownChange = true;
            try
            {
                apply();
            }
            finally
            {
                _ownChange = false;
            }
        });

    protected override void OnAttached()
    {
        _watchedCases = Map.Cases;
        _watchedCases.CollectionChanged += OnCasesChanged;
        SelectCaseFor(Context.Request);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_watchedCases != null) _watchedCases.CollectionChanged -= OnCasesChanged;
        _watchedCases = null;
        base.OnClosed(e);
    }

    protected override void OnRetarget(EditorRequest request)
    {
        ReloadIfStale();
        SelectCaseFor(request);
    }

    // ================================================================== 外部修改

    private void OnCasesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_ownChange) _stale = true;
    }

    /// <summary>回到窗口时：列表在外面被改过就重新载入；否则只刷新单位统计（Ctrl+Q 可能改了单位的方针值）。</summary>
    private void OnWindowActivated(object? sender, EventArgs e)
    {
        if (!ReloadIfStale() && CurrentIndex >= 0) UpdateUnitInfo();
    }

    private bool ReloadIfStale()
    {
        if (!_stale || _watchedCases == null) return false;
        _stale = false;
        RefreshList(CurrentIndex);
        SetStatus(Loc.T("Assist.MapCase.Reloaded", "方案列表已在窗口外修改，已重新载入"));
        return true;
    }

    /// <summary>选中与请求格子相关的方案（单位的方针对应的方案，或以该格为目标地块的方案）。</summary>
    private void SelectCaseFor(EditorRequest request)
    {
        int index = Module.FindCaseIndexFor(request.Col, request.Row);
        if (index >= 0) SelectItem(index);
    }

    // ================================================================== 目标地块 / 单位

    private void UpdateTargetInfo()
    {
        _locateButton.IsEnabled = false;
        if (!TryReadType(out int type) || type != MapCase.MoveToTileType)
        {
            _targetInfo.Text = Loc.T("Assist.MapCase.TargetNotTile", "该类型的目标值不是地块");
            return;
        }

        if (!TryParseTarget(_targetBox.Text, allowCell: true, out int tile))
        {
            _targetInfo.Text = Loc.T("Assist.MapCase.TargetFormat", "请输入地块序号，或\"列,行\"");
            return;
        }

        int width = Map.MapWidth;
        if (tile < 0 || tile >= width * Map.MapHeight)
        {
            _targetInfo.Text = Loc.F("Assist.MapCase.TargetOutside", width * Map.MapHeight - 1);
            return;
        }

        _targetInfo.Text = Loc.F("Assist.MapCase.TargetCell", tile % width, tile / width);
        _locateButton.IsEnabled = true;
    }

    private void LocateTarget()
    {
        if (!TryParseTarget(_targetBox.Text, allowCell: true, out int tile) || tile < 0 || tile >= Map.MapWidth * Map.MapHeight) return;
        int col = tile % Map.MapWidth, row = tile / Map.MapWidth;
        Context.MoveCamera?.Invoke(col, row);
        SetStatus(Loc.F("Assist.MapCase.Located", col, row));
    }

    private void UpdateUnitInfo()
    {
        int index = CurrentIndex;
        if (index < 0 || index >= Map.Cases.Count) return;

        int policy = Map.Cases[index].PolicyNumber;
        if (policy < 0 || policy > MapCaseModifier.MaxPolicy)
        {
            _unitInfo.Text = Loc.F("Assist.MapCase.PolicyUnreachable", MapCaseModifier.MaxPolicy);
            _selectUnitsButton.IsEnabled = false;
            return;
        }

        int count = Module.GetUnitCellsWithPolicy(policy).Count;
        _unitInfo.Text = Loc.F("Assist.MapCase.UnitCount", policy, count);
        _selectUnitsButton.IsEnabled = count > 0;
    }

    /// <summary>在地图上选中方针值等于当前方案方针编号的单位（Ctrl+Q 的反向查看）。</summary>
    private void SelectPolicyUnits()
    {
        int index = CurrentIndex;
        if (index < 0 || index >= Map.Cases.Count) return;

        int policy = Map.Cases[index].PolicyNumber;
        var cells = Module.GetUnitCellsWithPolicy(policy);
        HexSelector.Instance.SetSelection(cells);
        SetStatus(Loc.F("Assist.MapCase.UnitsSelected", cells.Count, policy));
    }

    // ================================================================== 解析

    private bool TryReadType(out int type)
        => TryChoice(_typeBox, TypeLabel, int.MinValue, int.MaxValue, new List<string>(), out type);

    /// <summary>
    /// 目标值：整数；<paramref name="allowCell"/> 时也接受"列,行"（逗号可以是全角），换算成地块序号，
    /// 超出地图的"列,行"得到 -1，交给范围检查报错。
    /// </summary>
    private bool TryParseTarget(string text, bool allowCell, out int value)
    {
        string s = text.Trim().Replace('，', ',');
        int comma = s.IndexOf(',');
        if (comma < 0)
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        value = -1;
        if (!allowCell
            || !int.TryParse(s[..comma].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int col)
            || !int.TryParse(s[(comma + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int row))
            return false;
        if (col >= 0 && col < Map.MapWidth && row >= 0 && row < Map.MapHeight)
            value = row * Map.MapWidth + col;
        return true;
    }

    // ================================================================== 小工具

    private static string TypeName(int type)
        => ConfigKeyValue.NameOf(CaseTypes, type) ?? Loc.F("Assist.MapCase.UnknownType", type);

    private static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>说明文字 + 右侧按钮的一行。</summary>
    private static Grid TextWithButton(TextBlock text, Button button)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return grid;
    }
}
