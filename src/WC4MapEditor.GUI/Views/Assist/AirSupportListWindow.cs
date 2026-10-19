using System.Collections.Specialized;
using WC4MapEditor.Core.Mode;
using WC4MapEditor.GUI.Services;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 空中支援列表窗口（空袭编辑模式 E 键，对应 VB 的 AirSupportListSetting）：编辑空军类型 / 弹药 / 军团 / 回合。
/// 空中支援没有坐标；其余字段、校验与提交方式与 <see cref="AirForceListWindow"/> 共用 <see cref="AirListWindowBase"/>。
/// </summary>
[AssistEditor(EditorKinds.AirSupportList)]
public sealed class AirSupportListWindow : AirListWindowBase
{
    protected override string TitleText => Loc.T("Assist.AirSupportList.Title", "空中支援列表管理");
    protected override int ItemCount => Map.AirSupports.Count;
    protected override INotifyCollectionChanged? ObservedList => Map.AirSupports;
    protected override string TypeLabel => Loc.T("Assist.AirSupportList.Field.Type", "空军类型");
    protected override bool SupportsDuplicate => true;

    protected override string DescribeItem(int index)
    {
        var s = Map.AirSupports[index];
        return Loc.F("Assist.AirSupportList.ItemTitle", index + 1, s.TriggerRound) + "\n"
             + DescribeDetail(s.AirForceSequence, s.AmmoType, s.OwnerLegion);
    }

    protected override void BuildFields(FieldPanel f)
    {
        f.Section(Loc.T("Common.BasicInfo", "基本信息"));
        BuildCommonFields(f);
    }

    protected override void LoadFields(int index)
    {
        var s = Map.AirSupports[index];
        LoadCommonFields(s.AirForceSequence, s.AmmoType, s.OwnerLegion, s.TriggerRound);
    }

    protected override bool TryCommitFields(int index, List<string> errors)
    {
        if (!TryReadCommonFields(errors, out int type, out int ammo, out int legion, out int round)) return false;

        var result = Module.UpdateAirSupport(index, new AirSupport
        {
            AirForceSequence = type,
            AmmoType = ammo,
            OwnerLegion = legion,
            TriggerRound = round
        });
        if (!result.Success) errors.Add(result.Message ?? "");
        return result.Success;
    }

    protected override int AddItem()
        => Module.AddAirSupport(AirSupport.CreateDefault(Module.DefaultLegion())).Success ? Map.AirSupports.Count - 1 : -1;

    protected override void RemoveItem(int index) => Module.DeleteAirSupport(index);

    protected override int DuplicateItem(int index)
        => Module.DuplicateAirSupport(index).Success ? index + 1 : -1;

    protected override void RecordChange(string description, Action apply)
        => Context.Editor.RecordCollectionChange(description, Map.AirSupports, apply);
}
