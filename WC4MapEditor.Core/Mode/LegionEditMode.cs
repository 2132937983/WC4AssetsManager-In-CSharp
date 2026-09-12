using WC4MapEditor.Core.Input;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.Mode;

public sealed class LegionEditMode : IModeHandler
{
    public EditMode Mode => EditMode.LegionEdit;
    public string DisplayName => "军团编辑";
    public ModifierKind PrimaryModifierKind => ModifierKind.Legion;
    public ModifierKind[] ModifierKinds => new[] { ModifierKind.Legion };
    public bool RequiresSelection => false;

    public string HelpText =>
        "=== 军团编辑模式 ===\n" +
        "左键 - 选择格子\n" +
        "右键 - 查看军团信息\n" +
        "P - 进行军团范围截图\n" +
        "Q - 打开军团编辑器\n" +
        "F - 打开军团列表窗口\n" +
        "C - 应用默认颜色到所有军团\n" +
        "U - 从setting.txt匹配颜色应用到所有军团\n" +
        "R - 随机化所有军团等级与经济\n" +
        "F6 - 更新征服国家设置\n" +
        "E - 打开头部数据编辑器\n" +
        "I - 修改所有军团的行动顺序和归属列表\n" +
        "X - 在当前选中格子添加/删除首都\n" +
        "ESC - 退出军团编辑模式";

    public IEnumerable<ModeKeyBinding> GetKeyBindings()
    {
        return new[]
        {
            new ModeKeyBinding("LE_Delete", KeyCodes.Delete, KeyModifiers.None, "remove", "清除军团领域"),
            new ModeKeyBinding("LE_Q", KeyCodes.Q, KeyModifiers.None, "prev_legion", "上一个军团"),
            new ModeKeyBinding("LE_E", KeyCodes.E, KeyModifiers.None, "next_legion", "下一个军团"),
        };
    }

    public Task<bool> HandleKeyAction(string action, int col, int row, ModeContext context)
    {
        var legion = context.GetModifier<LegionModifier>()!;
        bool modified = false;

        switch (action)
        {
            case "apply":
                context.RecordProvinceChange(col, row, $"设置军团领域 ({col},{row})", () => legion.Apply(col, row));
                modified = true;
                break;
            case "remove":
                context.RecordProvinceChange(col, row, $"清除军团领域 ({col},{row})", () => legion.Remove(col, row));
                modified = true;
                break;
            case "next_legion":
                legion.SelectedLegionId = (legion.SelectedLegionId % 8) + 1;
                context.RaiseStatusMessage?.Invoke($"选中军团: {legion.SelectedLegionId}");
                return Task.FromResult(true);
            case "prev_legion":
                legion.SelectedLegionId = ((legion.SelectedLegionId - 2 + 8) % 8) + 1;
                context.RaiseStatusMessage?.Invoke($"选中军团: {legion.SelectedLegionId}");
                return Task.FromResult(true);
        }

        if (modified) context.NotifyDataModified?.Invoke();
        return Task.FromResult(modified);
    }
}