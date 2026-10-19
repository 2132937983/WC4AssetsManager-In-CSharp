using System.Globalization;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 空袭 / 空中支援列表窗口的公共部分：兵种、弹药（下拉名录来自 setting.txt [空袭修改]）、军团（带"选择"按钮）、回合四个字段，
/// 列表项的描述（列表在窗口外被改动时的刷新由基类的 <see cref="ListEditorWindow.ObservedList"/> 负责）。
/// <para>VB 的两个窗口是整份复制、只差坐标 / 兵种字段；这里共用一份，子类只提供各自的列表与额外字段。</para>
/// </summary>
public abstract class AirListWindowBase : ListEditorWindow
{
    private ComboBox _typeBox = null!;
    private ComboBox _ammoBox = null!;
    private TextBox _legionBox = null!;
    private TextBlock _legionName = null!;
    private TextBox _roundBox = null!;

    /// <summary>空袭 / 空中支援模块（模式注册的同一个实例）。</summary>
    protected AirForceModifier Module
        => Context.Module<AirForceModifier>() ?? throw new InvalidOperationException("AirForceModifier is not registered in EditModeManager");

    /// <summary>兵种字段的标题（空袭叫"兵种"，空中支援叫"空军类型"，与 VB 一致）。</summary>
    protected abstract string TypeLabel { get; }

    private static string AmmoLabel => Loc.T("Assist.AirList.Field.Ammo", "弹药");
    private static string LegionLabel => Loc.T("Assist.AirList.Field.Legion", "军团");
    private static string RoundLabel => Loc.T("Assist.AirList.Field.Round", "回合");

    // ================================================================== 字段

    /// <summary>添加兵种 / 弹药 / 军团 / 回合四个字段与名录说明。</summary>
    protected void BuildCommonFields(FieldPanel f)
    {
        string choiceTip = Loc.T("Assist.AirList.TypeTip", "从列表选择，或直接输入数值");
        _typeBox = f.Choice(TypeLabel, AirForceCatalog.Types, choiceTip);
        _ammoBox = f.Choice(AmmoLabel, AirForceCatalog.Ammos, choiceTip);

        _legionBox = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };
        _legionName = EditorTheme.Muted("");
        _legionName.VerticalAlignment = VerticalAlignment.Center;
        _legionName.Margin = new Thickness(8, 0, 8, 0);
        _legionBox.TextChanged += (_, _) =>
        {
            f.Changed();
            UpdateLegionName();
        };
        var pick = EditorTheme.Button(Loc.T("Assist.AirList.PickLegion", "选择"), (_, _) => PickLegion());
        pick.ToolTip = Loc.T("Assist.AirList.PickLegionTip", "从军团列表中选择");
        f.Custom(LegionLabel, Row(_legionBox, _legionName, pick), Loc.T("Assist.AirList.LegionTip", "所属军团的 ActionId"));

        _roundBox = f.Text(RoundLabel, Loc.F("Assist.AirList.RoundTip", AirForceModifier.MaxRound));
        f.Hint(Loc.T("Assist.AirList.CatalogHint", "兵种与弹药的名称来自 setting.txt 的 [空袭修改]（AirForceList / AirForcePowerList）；列表里没有的值也可以直接输入数值。"));
    }

    /// <summary>把四个公共字段显示出来。</summary>
    protected void LoadCommonFields(int type, int ammo, int legion, int round)
    {
        SetChoice(_typeBox, type);
        SetChoice(_ammoBox, ammo);
        _legionBox.Text = legion.ToString(CultureInfo.InvariantCulture);
        _roundBox.Text = round.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>读取并校验四个公共字段（军团必须是地图上的军团）；有错误时写入 errors 并返回 false。</summary>
    protected bool TryReadCommonFields(List<string> errors, out int type, out int ammo, out int legion, out int round)
    {
        // 用 &= 而不是 &&：每个字段都要校验，错误一次列全
        bool ok = TryChoice(_typeBox, TypeLabel, 0, AirForceCatalog.MaxCode, errors, out type);
        ok &= TryChoice(_ammoBox, AmmoLabel, 0, AirForceCatalog.MaxCode, errors, out ammo);
        bool legionOk = TryInt(_legionBox, LegionLabel, int.MinValue, int.MaxValue, errors, out legion);
        if (legionOk && !Module.IsValidLegion(legion))
        {
            errors.Add(Loc.F("Assist.AirList.LegionNotFound", LegionLabel, legion));
            legionOk = false;
        }
        ok &= legionOk;
        ok &= TryInt(_roundBox, RoundLabel, 0, AirForceModifier.MaxRound, errors, out round);
        return ok;
    }

    /// <summary>
    /// 列表项第二行："兵种 · 弹药 · 军团"（VB 是"兵种 | 弹药 | 军团 | 回合"一行，在列表宽度里放不下，回合挪到第一行）。
    /// </summary>
    protected string DescribeDetail(int type, int ammo, int legion)
        => Loc.F("Assist.AirList.ItemDetail", AirForceCatalog.TypeName(type), AirForceCatalog.AmmoName(ammo), LegionText(legion));

    /// <summary>"军团9 美国"；地图上没有该军团时只显示编号。</summary>
    private string LegionText(int actionId)
        => TryFindLegion(actionId, out var legion)
            ? Loc.F("Assist.AirList.LegionShort", actionId, CountryNameProvider.Get(legion.CountryId))
            : Loc.F("Assist.AirList.LegionNumber", actionId);

    private bool TryFindLegion(int actionId, out Legion legion)
    {
        foreach (var l in Map.Legions)
        {
            if (l.ActionId != actionId) continue;
            legion = l;
            return true;
        }
        legion = default;
        return false;
    }

    /// <summary>横排一行控件：第一个占满剩余宽度，其余按内容宽度。</summary>
    protected static Grid Row(FrameworkElement stretch, params FrameworkElement[] rest)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(stretch);
        for (int i = 0; i < rest.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (rest[i] is Button b) b.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(rest[i], i + 1);
            grid.Children.Add(rest[i]);
        }
        return grid;
    }

    private void UpdateLegionName()
    {
        if (!int.TryParse(_legionBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int actionId))
        {
            _legionName.Text = "";
            return;
        }
        bool found = TryFindLegion(actionId, out var legion);
        _legionName.Text = found ? CountryNameProvider.Get(legion.CountryId) : Loc.T("Assist.AirList.LegionMissing", "无此军团");
        _legionName.Foreground = found ? EditorTheme.TextMuted : EditorTheme.Danger;
    }

    private void PickLegion()
    {
        var picker = new LegionBelongListWindow(Map.Legions.ToList()) { Owner = this };
        picker.ShowDialog();
        if (picker.IsConfirmed && picker.SelectedActionId is { } actionId)
            _legionBox.Text = actionId.ToString(CultureInfo.InvariantCulture);
    }
}
