using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 战略建设编辑模式（对应 VB 的 StrategicConstruction 模式）：战略建设是与格子无关的全局列表，没有鼠标操作。
/// Q 打开战略建设列表窗口（鼠标所在格子属于某个军团时，窗口定位到该军团的第一条）。
/// 显示国家领域（对齐 VB）和右上角按军团分组的战略建设面板（C# 新增）。
/// </summary>
public sealed class StrategicConstructionEditMode : ModeBase
{
    public override EditMode Mode => EditMode.StrategicEdit;
    protected override string LocKey => "Strategic";
    protected override string DefaultDisplayName => "战略建设";
    public override ModifierKind PrimaryModifierKind => ModifierKind.StrategicConstruction;
    public override ModifierKind[] ModifierKinds => new[] { ModifierKind.StrategicConstruction };
    public override ModeRenderLayers RenderLayers => ModeRenderLayers.LegionDomain | ModeRenderLayers.StrategicConstruction;

    protected override void DefineActions(ModeActionTable t)
    {
        t.Command("SC_Q", KeyCodes.Q, "open_list", "打开战略建设列表",
                a => a.Context.OpenEditor(EditorKinds.StrategicConstructionList, a.Col, a.Row))
         .Note("focus", "打开列表时定位到鼠标所在格子所属军团的条目")
         .Note("panel", "右上角面板按军团列出战略建设（执行回合、建设代码）");
    }
}
