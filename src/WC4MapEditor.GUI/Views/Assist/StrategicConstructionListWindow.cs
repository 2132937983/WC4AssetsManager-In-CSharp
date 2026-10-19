using System.Collections.Specialized;
using System.Globalization;
using WC4MapEditor.Core.Mode;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.GUI.Services;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 战略建设列表窗口（战略建设模式 Q 键，对应 VB StrategicConstructionListSetting）：
/// 左侧列表，右侧军团序号 / 保留1 / 执行回合 / 建设代码。
/// <para>
/// 与 VB 的区别：军团从当前地图的军团里选（显示国家名），建设代码从游戏 config/def_strategy.xml 的策略里选，两者都允许直接输入数值；
/// 军团序号必须对应现有军团、执行回合 ≥ 1（VB 对无效输入静默忽略、不查范围）；
/// 修改按 Enter /"应用"/ 切换条目时提交、Esc 撤回，每次增删改都是一步撤销（VB 直接改实时列表，取消也撤不回）。
/// 打开时（及窗口已开时再按 Q）定位到鼠标所在格子所属军团的第一条；列表在窗口外被改动时自动刷新。
/// </para>
/// </summary>
[AssistEditor(EditorKinds.StrategicConstructionList)]
public sealed class StrategicConstructionListWindow : ListEditorWindow
{
    private ComboBox _legion = null!;
    private TextBox _reserved = null!;
    private TextBox _round = null!;
    private ComboBox _code = null!;

    private StrategicConstructionModifier Constructions => Context.Module<StrategicConstructionModifier>()
        ?? throw new InvalidOperationException("StrategicConstructionModifier is not registered in EditModeManager");

    protected override string TitleText => Loc.T("Assist.Strategic.Title", "战略建设列表管理");

    protected override int ItemCount => Map.StrategyConstructions.Count;

    protected override bool SupportsDuplicate => true;

    /// <summary>两行（对齐 VB 的两行列表项）：序号与军团；执行回合与建设代码。列表栏宽度固定，一行放不下。</summary>
    protected override string DescribeItem(int index)
    {
        var sc = Map.StrategyConstructions[index];
        return Loc.F("Assist.Strategic.ItemLine1", index + 1, Constructions.DescribeLegion(sc.LegionId)) + "\n"
             + Loc.F("Assist.Strategic.ItemLine2", sc.ExecuteRound, StrategicConstructionModifier.DescribeCode(sc.ConstructionCode));
    }

    protected override void BuildFields(FieldPanel fields)
    {
        fields.Section(Loc.T("Assist.Strategic.Section", "基本信息"));
        _legion = fields.Choice(LegionLabel, Constructions.GetLegionOptions(),
            Loc.T("Assist.Strategic.Field.LegionTip", "军团的 ActionId（军团设置里的序号，不是军团在列表里的位置）"));
        _reserved = fields.Text(ReservedLabel, Loc.T("Assist.Strategic.Field.ReservedTip", "0x04 保留字段，按原值保存"));
        _round = fields.Text(RoundLabel, Loc.T("Assist.Strategic.Field.RoundTip", "从第几回合起执行（≥ 1）"));
        _code = fields.Choice(CodeLabel, StrategicConstructionModifier.GetCodeOptions(),
            Loc.T("Assist.Strategic.Field.CodeTip", "游戏 config/def_strategy.xml 里的策略 ID"));
        fields.Hint(Loc.T("Assist.Strategic.Hint", "建设代码是 AI 的生产倾向（产出 / 设施 / 陆军 / 步兵 / 坦克 / 火炮 / 海军 / 空军 / 导弹 / 要塞 / 防空）：从执行回合起，该军团按这条策略分配生产。"));
    }

    protected override void LoadFields(int index)
    {
        var sc = Map.StrategyConstructions[index];
        SetChoice(_legion, sc.LegionId);
        _reserved.Text = sc.Reserved1.ToString(CultureInfo.InvariantCulture);
        _round.Text = sc.ExecuteRound.ToString(CultureInfo.InvariantCulture);
        SetChoice(_code, sc.ConstructionCode);
    }

    protected override bool TryCommitFields(int index, List<string> errors)
    {
        bool ok = TryChoice(_legion, LegionLabel, int.MinValue, int.MaxValue, errors, out int legion);
        ok &= TryInt(_reserved, ReservedLabel, int.MinValue, int.MaxValue, errors, out int reserved);
        ok &= TryInt(_round, RoundLabel, 1, StrategicConstructionModifier.MaxRound, errors, out int round);
        ok &= TryChoice(_code, CodeLabel, 0, int.MaxValue, errors, out int code);
        if (!ok) return false;

        var construction = new StrategicConstruction { LegionId = legion, Reserved1 = reserved, ExecuteRound = round, ConstructionCode = code };
        var result = Constructions.UpdateConstruction(index, construction);
        if (!result.Success)
        {
            errors.Add(result.Message ?? Loc.T("Assist.ListEditor.Invalid", "输入无效"));
            return false;
        }

        // 代码表外的值照样保存，但要提醒
        if (result.Message != null && StrategicConstructionModifier.UnknownCodeWarning(code) != null)
            ShowErrorLater(result.Message);
        return true;
    }

    protected override int AddItem()
    {
        var result = Constructions.AddConstruction(Constructions.CreateDefaultConstruction());
        if (result.Success) return Map.StrategyConstructions.Count - 1;

        // 没有军团时新增必然失败
        ShowErrorLater(result.Message ?? Loc.T("Assist.ListEditor.Invalid", "输入无效"));
        return -1;
    }

    /// <summary>基类在提交 / 新增之后会刷新列表并改写状态行：提示排到那之后再显示，免得被清掉。</summary>
    private void ShowErrorLater(string message)
        => Dispatcher.InvokeAsync(() => SetStatus(message, true), DispatcherPriority.Background);

    protected override void RemoveItem(int index) => Constructions.DeleteConstruction(index);

    protected override int DuplicateItem(int index) => Constructions.DuplicateConstruction(index).Success ? index + 1 : -1;

    protected override void RecordChange(string description, Action apply)
        => Context.Editor.RecordCollectionChange(description, Map.StrategyConstructions, apply);

    protected override INotifyCollectionChanged? ObservedList => Map.StrategyConstructions;

    protected override string? ExternalChangeMessage => Loc.T("Assist.Strategic.Refreshed", "战略建设列表已在窗口外被修改，已刷新");

    protected override void OnAttached() => SelectLegionAt(Context.Request);

    protected override void OnRetarget(EditorRequest request) => SelectLegionAt(request);

    /// <summary>定位到请求格子（按 Q 时的鼠标格）所属军团的第一条战略建设；格子没有归属或该军团没有条目时不动。</summary>
    private void SelectLegionAt(EditorRequest request)
    {
        if (request.Col < 0 || request.Row < 0) return;
        int belong = Map.GetBelongValue(request.Col, request.Row);
        if (belong == 0xFF || Map.ResolveLegionByBelong(belong) is not { } legion) return;
        int index = Constructions.FirstIndexOfLegion(legion.ActionId);
        if (index >= 0) SelectItem(index);
    }

    private static string LegionLabel => Loc.T("Assist.Strategic.Field.Legion", "军团序号");
    private static string ReservedLabel => Loc.T("Assist.Strategic.Field.Reserved", "保留1");
    private static string RoundLabel => Loc.T("Assist.Strategic.Field.Round", "执行回合");
    private static string CodeLabel => Loc.T("Assist.Strategic.Field.Code", "建设代码");
}
