using System.Globalization;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Parsers.BTL;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 军团编辑模式 - 对齐 VB 版 Builder/LegionModifier。
/// <para>
/// 左键把当前军团（[ / ] 或 F 列表切换）的领域设到点中的格子，右键查看该格军团信息；
/// Q / F / E 打开军团编辑器、军团列表、头部数据窗口；C / U / R / P / I 批量改军团数据；X 切换首都。
/// 首都编辑是<b>独立模块</b>（<see cref="CapitalModifier"/>），本模式通过 <see cref="ModifierKinds"/> 引入，
/// X 直接对选中的格子添加 / 取消首都。所有数据修改都可撤销。
/// </para>
/// </summary>
public sealed class LegionEditMode : ModeBase
{
    public override EditMode Mode => EditMode.LegionEdit;
    protected override string LocKey => "Legion";
    protected override string DefaultDisplayName => "军团编辑";
    public override ModifierKind PrimaryModifierKind => ModifierKind.Legion;
    public override ModifierKind[] ModifierKinds => new[] { ModifierKind.Legion, ModifierKind.Capital };

    /// <summary>左键单击选格（Shift 加选 / Ctrl 减选），右键拖动框选多格；X / Delete 作用于整个选区。</summary>
    public override bool RequiresSelection => true;

    public override ModeRenderLayers RenderLayers =>
        ModeRenderLayers.CapitalFlag | ModeRenderLayers.LegionDomain | ModeRenderLayers.ClippedTiles;

    protected override void DefineActions(ModeActionTable t)
    {
        t.GestureEdit(ModeGesture.LeftClick, "apply", "选中格子并设为当前军团的领域", ApplyDomain)
         .GestureCommand(ModeGesture.RightClick, "show_info", "查看军团信息", ShowLegionInfo)
         .HelpLine("box_select", ModeGesture.RightDrag, "框选多格")
         .Note("select_only", "Shift+左键 加选 / Ctrl+左键 减选：只改选区，不改领域")
         .Edit("LE_Delete", KeyCodes.Delete, "remove", "清除当前选中格子的军团领域", RemoveDomain)
         .Edit("LE_ShiftDelete", KeyChord.Shift(KeyCodes.Delete), "remove_by_belong", "删除指定归属的所有单位（可输入归属ID）", RemoveArmiesByBelongAsync)
         .Command("LE_Q", KeyCodes.Q, "open_legion_setting", "打开军团编辑器",
             a => a.Context.OpenEditor(EditorKinds.LegionSetting, a.Col, a.Row))
         .Command("LE_F", KeyCodes.F, "open_legion_list", "打开军团列表窗口（选择当前军团、调整军团顺序）",
             a => a.Context.OpenEditor(EditorKinds.LegionList, a.Col, a.Row))
         .Edit("LE_C", KeyCodes.C, "apply_default_colors", "应用默认颜色到所有军团",
             a => EditLegions(a, "ApplyDefaultColors", m => m.ApplyDefaultColorsToAllLegions()))
         .Edit("LE_U", KeyCodes.U, "apply_settings_colors", "从setting.txt匹配颜色应用到所有军团",
             a => EditLegions(a, "ApplySettingsColors", m => m.ApplyAllLegionsColorFromSettings()))
         .Edit("LE_R", KeyCodes.R, "randomize_levels", "随机化所有军团等级与经济",
             a => EditLegions(a, "RandomizeLevels", m => m.RandomizeAllLegionLevels()))
         .Edit("LE_P", KeyCodes.P, "set_player_legion", "把当前军团设为玩家军团（控制值 0，其余改为 1）", SetPlayerLegionAsync)
         .Command("LE_F4", KeyCodes.F4, "capture_screenshot", "进行军团范围截图", a =>
         {
             a.Context.NotifyCaptureLegionScreenshot?.Invoke();
             return true;
         })
         .Command("LE_F6", KeyCodes.F6, "update_conquer_settings", "更新征服国家设置", UpdateConquerSettingsAsync)
         .Command("LE_E", KeyCodes.E, "open_header_setting", "打开头部数据编辑器",
             a => a.Context.OpenEditor(EditorKinds.HeaderSetting, a.Col, a.Row))
         .Edit("LE_I", KeyCodes.I, "rebuild_action_belong", "按列表顺序重排所有军团的行动顺序", RenumberActionIdsAsync)
         .Edit("LE_X", KeyCodes.X, "toggle_capital", "在当前选中格子添加/取消首都（不改归属）", ToggleCapital)
         .Command("LE_BracketOpen", KeyCodes.OemOpenBrackets, "prev_legion", "上一个军团", a => StepLegion(a, -1))
         .Command("LE_BracketClose", KeyCodes.OemCloseBrackets, "next_legion", "下一个军团", a => StepLegion(a, +1))
         .GestureCommand(ModeGesture.Escape, "cancel", "取消选区", BelongCellActions.CancelSelection)
         .Note("layers", "首都格显示国旗；红叉 = 数据范围外的格子，游戏不会读取");
    }

    private static string SelectFirst => Loc.T("Mode.Legion.Msg.SelectFirst", "请先左键选中一个格子");
    private static string NoLegions => Loc.T("Mode.Legion.Msg.NoLegions", "当前地图没有军团");

    // ------------------------------------------------------------------ 鼠标

    /// <summary>
    /// 左键：场景已选中该格，这里把当前军团的领域设上去（对齐 VB"左键设置军团"的提示）。
    /// 归属本来就是当前军团时不算修改。
    /// </summary>
    private static bool ApplyDomain(ModeActionArgs a)
    {
        var legion = a.Module<LegionModifier>();
        ModifierResult result = default;
        a.Context.RecordBelongChange(a.Col, a.Row, Loc.F("Mode.Legion.Undo.Apply", a.Col, a.Row),
            () => result = legion.Apply(a.Col, a.Row));
        BelongCellActions.Report(a, result);
        return result.Success && result.AffectedCount > 0;
    }

    /// <summary>右键：在状态栏显示该格所属军团的信息（对齐 VB HandleMouseDown 右键分支）。</summary>
    private static bool ShowLegionInfo(ModeActionArgs a)
    {
        var map = a.Map!;
        int belong = map.GetBelongValue(a.Col, a.Row);
        if (belong == BelongModifier.NoOwner)
        {
            a.Status(Loc.F("Mode.Legion.Msg.NoLegionInfo", a.Col, a.Row));
            return true;
        }

        var legion = map.ResolveLegionByBelong(belong);
        if (legion == null)
        {
            a.Status(Loc.F("Mode.Legion.Msg.BelongNoLegion", a.Col, a.Row, belong));
            return true;
        }

        var l = legion.Value;
        a.Status(Loc.F("Mode.Legion.Msg.Info", l.ActionId, l.CountryId, l.ColorR, l.ColorG, l.ColorB, l.InitialEconomy, l.Camp));
        return true;
    }

    // ------------------------------------------------------------------ 选区

    /// <summary>Delete：清除选区内所有格子的军团领域（归属设为 0xFF）。</summary>
    private static bool RemoveDomain(ModeActionArgs a)
    {
        if (!BelongCellActions.TryGetTargets(a, SelectFirst, out var cells)) return false;
        var legion = a.Module<LegionModifier>();
        string undo = cells.Count == 1
            ? Loc.F("Mode.Legion.Undo.Remove", cells[0].Col, cells[0].Row)
            : Loc.F("Mode.Legion.Undo.RemoveMany", cells.Count);
        return BelongCellActions.WriteCells(a, cells, undo, c => legion.Remove(c.Col, c.Row),
            changed => Loc.F("Mode.Legion.Msg.DomainsCleared", changed, cells.Count));
    }

    /// <summary>
    /// X：对选区内每个格子添加 / 取消首都，整批一条撤销记录（按首都列表快照还原，顺序不变）。
    /// 新加的首都不在等级城（建筑类型 15-19）上时附带提示：游戏只认等级城上的首都。
    /// </summary>
    private static bool ToggleCapital(ModeActionArgs a)
    {
        if (!BelongCellActions.TryGetTargets(a, SelectFirst, out var cells)) return false;
        var map = a.Map!;
        var capital = a.Module<CapitalModifier>();
        bool hadCapital = cells.Count == 1 && capital.IndexOfCapital(cells[0].ToIndex(map.MapWidth)) >= 0;

        string undo = cells.Count > 1
            ? Loc.F("Mode.Legion.Undo.ToggleCapitalMany", cells.Count)
            : Loc.F(hadCapital ? "Mode.Legion.Undo.RemoveCapital" : "Mode.Legion.Undo.AddCapital", cells[0].Col, cells[0].Row);

        int added = 0, removed = 0, notOnCity = 0;
        a.Context.RecordCollectionChange(undo, map.Capitals, () =>
        {
            foreach (var cell in cells)
            {
                int hexIndex = cell.ToIndex(map.MapWidth);
                switch (capital.ToggleCapital(hexIndex))
                {
                    case CapitalModifier.ToggleResult.Added:
                        added++;
                        if (!capital.IsOnLevelCity(hexIndex)) notOnCity++;
                        break;
                    case CapitalModifier.ToggleResult.Removed:
                        removed++;
                        break;
                }
            }
        });

        string message = cells.Count > 1
            ? Loc.F("Mode.Legion.Msg.CapitalsToggled", added, removed)
            : Loc.F(hadCapital ? "Mode.Legion.Msg.CapitalRemoved" : "Mode.Legion.Msg.CapitalAdded", cells[0].Col, cells[0].Row);
        if (notOnCity > 0)
            message += Loc.F("Mode.Legion.Msg.CapitalNotOnCity", notOnCity,
                BTLRuleChecker.MinCapitalBuildingType, BTLRuleChecker.MaxCapitalBuildingType);
        a.Status(message);
        return added + removed > 0;
    }

    // ------------------------------------------------------------------ 军团数据

    /// <summary>C / U / R：对整个军团列表执行一个模块操作，按列表快照记录撤销。</summary>
    private static bool EditLegions(ModeActionArgs a, string undoName, Func<LegionModifier, ModifierResult> operation)
    {
        if (a.Map == null) return false;
        var legion = a.Module<LegionModifier>();
        ModifierResult result = default;
        a.Context.RecordCollectionChange(Loc.T($"Mode.Legion.Undo.{undoName}"), a.Map.Legions, () => result = operation(legion));
        BelongCellActions.Report(a, result);
        return result.Success && result.AffectedCount > 0;
    }

    /// <summary>
    /// P：输入军团序号（默认当前军团），把它设为玩家军团（控制值 0），其余改为 AI（控制值 1）；
    /// 输入 0 表示不设玩家军团（征服地图由开局选择决定玩家，对齐 Java 版 updatePlayer(-1)）。
    /// </summary>
    private static async Task<bool> SetPlayerLegionAsync(ModeActionArgs a)
    {
        var map = a.Map;
        if (map == null || map.Legions.Count == 0)
        {
            a.Status(NoLegions);
            return false;
        }
        if (a.Dialogs == null) return false;

        var legion = a.Module<LegionModifier>();
        int count = map.Legions.Count;
        var input = await a.Dialogs.ShowInputDialogAsync(
            Loc.T("Mode.Legion.Dialog.PlayerTitle", "设置玩家军团"),
            Loc.F("Mode.Legion.Dialog.PlayerPrompt", count),
            legion.SelectedLegionId.ToString(CultureInfo.InvariantCulture), 0, count);
        if (input == null) return false;

        if (!int.TryParse(input.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
        {
            a.Status(Loc.T("Mode.Legion.Msg.PlayerNumberInvalid", "输入的军团序号无效"));
            return false;
        }
        if (number < 0 || number > count)
        {
            a.Status(Loc.F("Mode.Legion.Msg.PlayerNumberOutOfRange", number, count));
            return false;
        }

        ModifierResult result = default;
        a.Context.RecordCollectionChange(Loc.T("Mode.Legion.Undo.SetPlayer", "设置玩家军团"), map.Legions,
            () => result = legion.SetPlayerControlledLegionAt(number - 1));
        BelongCellActions.Report(a, result);
        return result.Success && result.AffectedCount > 0;
    }

    /// <summary>
    /// I：按列表顺序把行动顺序重排为 1、2、3……（会覆盖原有的行动顺序，先确认；无界面时直接执行）。
    /// 归属值是军团序号，与行动顺序无关，不需要改动。
    /// </summary>
    private static async Task<bool> RenumberActionIdsAsync(ModeActionArgs a)
    {
        var map = a.Map;
        if (map == null || map.Legions.Count == 0)
        {
            a.Status(NoLegions);
            return false;
        }

        if (a.Dialogs != null && !await a.Dialogs.ShowConfirmDialogAsync(
                Loc.T("Mode.Legion.Dialog.RenumberTitle", "重排行动顺序"),
                Loc.F("Mode.Legion.Dialog.RenumberConfirm", map.Legions.Count, LegionModifier.FirstActionId),
                Loc.T("Common.Ok", "确定"), Loc.T("Common.Cancel", "取消")))
            return false;

        ModifierResult result = default;
        a.Context.RecordCollectionChange(Loc.T("Mode.Legion.Undo.Renumber", "重排行动顺序"), map.Legions,
            () => result = a.Module<LegionModifier>().UpdateAllLegionsActionIdAndBelong());
        BelongCellActions.Report(a, result);
        return result.Success && result.AffectedCount > 0;
    }

    /// <summary>
    /// F6：输入征服参数后按当前军团列表更新 ConquerCountrySettings.json（只写 json，不改地图）。
    /// 配置文件为空或不存在时从空列表开始（对齐 VB）。
    /// </summary>
    private static async Task<bool> UpdateConquerSettingsAsync(ModeActionArgs a)
    {
        var map = a.Map;
        if (map == null || map.Legions.Count == 0)
        {
            a.Status(NoLegions);
            return false;
        }
        if (a.Dialogs == null)
        {
            a.Status(Loc.T("Mode.Legion.Msg.NeedDialog", "需要界面输入征服参数，当前不可用"));
            return false;
        }

        string title = Loc.T("Mode.Legion.Dialog.ConquerTitle", "更新征服国家设置");
        var input = await a.Dialogs.ShowInputDialogAsync(title,
            Loc.T("Mode.Legion.Dialog.ConquerPrompt", "请输入征服参数 (ConquerId)："), "1", 1, 999);
        if (input == null
            || !int.TryParse(input.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int conquerId)
            || conquerId < 1 || conquerId > 999)
            return false;

        var settings = ConfigManager.Instance.GetConquerCountrySettingsData();
        var result = a.Module<LegionModifier>().UpdateConquerCountrySettings(conquerId, settings);
        BelongCellActions.Report(a, result);
        await a.Dialogs.ShowNotificationDialogAsync(title, result.Message ?? string.Empty, Loc.T("Common.Ok", "确定"));
        return result.Success;
    }

    /// <summary>[ / ]：切换当前军团，按地图军团数循环。</summary>
    private static bool StepLegion(ModeActionArgs a, int step)
    {
        var map = a.Map;
        if (map == null || map.Legions.Count == 0)
        {
            a.Status(NoLegions);
            return false;
        }

        var legion = a.Module<LegionModifier>();
        int count = map.Legions.Count;
        int index = ((legion.SelectedLegionId - 1 + step) % count + count) % count;
        legion.SelectedLegionId = index + 1;
        a.Status(Loc.F("Mode.Legion.Msg.LegionSelected", index + 1, CountryNameProvider.Get(map.Legions[index].CountryId)));
        return true;
    }

    // ------------------------------------------------------------------ 按归属删除单位（Shift+Delete）

    /// <summary>
    /// Shift+Delete：按归属批量删除单位（对齐单位部署模式的同名功能），整体快照记录撤销。
    /// 底层按<b>单位自身的所属军团</b>精确匹配，<c>-1</c> 不对应任何单位，所以默认值兜到 0。
    /// </summary>
    private static async Task<bool> RemoveArmiesByBelongAsync(ModeActionArgs a)
    {
        var map = a.Map;
        if (map == null || a.Dialogs == null) return false;

        int suggested = map.GetProvinceCapitalBelong(a.Col, a.Row);
        if (suggested < 0) suggested = map.GetBelongValue(a.Col, a.Row);
        if (suggested < 0) suggested = 0;

        string title = Loc.T("Mode.Legion.Dialog.RemoveByBelongTitle", "删除指定归属的所有单位");
        var input = await a.Dialogs.ShowInputDialogAsync(title,
            Loc.T("Mode.Legion.Dialog.RemoveByBelongPrompt", "归属ID（按单位自身的军团归属精确匹配）："),
            suggested.ToString(CultureInfo.InvariantCulture));
        if (input == null) return false;

        if (!int.TryParse(input.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int targetBelong))
        {
            a.Status(Loc.T("Mode.Legion.Msg.BelongIdInvalid", "输入的归属ID无效"));
            return false;
        }

        if (!await a.Dialogs.ShowConfirmDialogAsync(title,
                Loc.F("Mode.Legion.Dialog.RemoveByBelongConfirm", targetBelong),
                Loc.T("Common.Delete", "删除"), Loc.T("Common.Cancel", "取消")))
            return false;

        ModifierResult result = default;
        a.Context.Owner.RecordEntitiesSnapshot(Loc.F("Mode.Legion.Undo.RemoveByBelong", targetBelong), () =>
            result = map.Header.UsesV3Records
                ? a.Module<ArmyV3Modifier>().DeleteArmiesByBelong(targetBelong)
                : a.Module<ArmyModifier>().DeleteArmiesByBelong(targetBelong));
        BelongCellActions.Report(a, result);
        return result.Success;
    }
}
