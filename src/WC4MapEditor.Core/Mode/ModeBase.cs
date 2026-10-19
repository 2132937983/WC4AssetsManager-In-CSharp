using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Modifiers;

namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 编辑模式的公共基类：模式 = 若干模块 + 一张动作表。
/// <para>
/// 子类只需声明身份（<see cref="Mode"/>、名称、用到的模块）、要看到的渲染层，
/// 并在 <see cref="DefineActions"/> 里写"哪个键 → 哪个动作 → 调哪个模块的哪个函数"。
/// 按键绑定、动作分发与帮助文本都由动作表统一产出，不会再出现帮助文本与实际按键对不上的情况。
/// </para>
/// <para>
/// 翻译键：模式名 = <c>Mode.{LocKey}.Name</c>；动作说明 = <c>Mode.{LocKey}.Action.{动作名}</c>；
/// 帮助行 = <c>Mode.{LocKey}.Help.{id}</c>；备注 = <c>Mode.{LocKey}.Note.{id}</c>。
/// 键缺失时显示声明里给的简体中文。
/// </para>
/// </summary>
public abstract class ModeBase : IModeHandler
{
    private ModeActionTable? _actions;

    public abstract EditMode Mode { get; }

    /// <summary>翻译键里的模式名段，例如 "Belong" → "Mode.Belong.Name"。</summary>
    protected abstract string LocKey { get; }

    /// <summary>翻译缺失时的模式显示名（简体中文）。</summary>
    protected abstract string DefaultDisplayName { get; }

    public virtual string DisplayName => Loc.T(NameKey, DefaultDisplayName);

    /// <summary>模式名的翻译键（<c>Mode.{LocKey}.Name</c>）。</summary>
    public string NameKey => $"Mode.{LocKey}.Name";

    /// <summary>帮助文本：由动作表按声明顺序生成，标题为 "{模式名}模式:"（随当前语言）。</summary>
    public virtual string HelpText => Actions.BuildHelpText(Loc.F("Mode.HelpTitle", DisplayName));

    public abstract ModifierKind PrimaryModifierKind { get; }
    public abstract ModifierKind[] ModifierKinds { get; }
    public virtual bool RequiresSelection => false;
    public virtual ModeRenderLayers RenderLayers => ModeRenderLayers.None;
    public virtual Func<HexCoord, bool>? CreateSelectionFilter(MapData map) => null;

    /// <summary>本模式的动作表（首次访问时建表并自检）。</summary>
    public ModeActionTable Actions => _actions ??= BuildTable();

    public IEnumerable<ModeKeyBinding> GetKeyBindings() => Actions.KeyBindings;

    public string? GetGestureAction(ModeGesture gesture) => Actions.GetGestureAction(gesture);

    public virtual Task<bool> HandleKeyAction(string action, int col, int row, ModeContext context)
        => Actions.DispatchAsync(action, col, row, context);

    /// <summary>在这里声明本模式的全部动作。</summary>
    protected abstract void DefineActions(ModeActionTable t);

    private ModeActionTable BuildTable()
    {
        var t = new ModeActionTable($"Mode.{LocKey}");
        DefineActions(t);
        t.Validate();
        return t;
    }
}
