using System.Reflection;
using WC4MapEditor.Core.Mode;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.GUI.Services;

/// <summary>
/// 标注一个窗口类负责哪种编辑窗口（<see cref="EditorKinds"/>）。
/// 标注后无需改动渲染场景：模式调用 <c>context.OpenEditor(EditorKinds.Xxx)</c> 时由 <see cref="AssistEditorHost"/> 找到并打开它。
/// 窗口类需要有公开的无参构造函数，并实现 <see cref="IAssistEditor"/> 接收上下文。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class AssistEditorAttribute : Attribute
{
    public AssistEditorAttribute(string kind) => Kind = kind;
    public string Kind { get; }
}

/// <summary>编辑窗口改动了哪类数据（场景据此失效对应的渲染缓存）。</summary>
[Flags]
public enum EditorDataScope
{
    /// <summary>实体列表（事件、天气、方案、空袭、援军、战略建设、单位、建筑……）</summary>
    Entities = 1,
    /// <summary>军团（颜色、国家 ID、增删）——领域层与国旗图集要重建</summary>
    Legions = 2,
    /// <summary>格子归属</summary>
    Belongs = 4,
    /// <summary>地形</summary>
    Terrain = 8,
    /// <summary>省份</summary>
    Provinces = 16,
    All = Entities | Legions | Belongs | Terrain | Provinces
}

/// <summary>编辑窗口拿到的上下文：地图、模块与撤销入口、以及回调场景的几个动作。</summary>
public sealed class AssistEditorContext
{
    public required MapData MapData { get; init; }

    /// <summary>编辑模式管理器：取模块（<see cref="Module{T}"/>）、记录撤销（RecordCollectionChange 等）、发状态消息。</summary>
    public required EditModeManager Editor { get; init; }

    /// <summary>首次打开时的请求（之后的同类请求通过 <see cref="IAssistEditor.Retarget"/> 送达）。</summary>
    public required EditorRequest Request { get; init; }

    public IDialogService? Dialogs { get; init; }

    /// <summary>通知场景数据已改：失效相应缓存、重绘、标记文件已修改。</summary>
    public Action<EditorDataScope>? DataChanged { get; init; }

    /// <summary>把相机移到某格（列、行）。</summary>
    public Action<int, int>? MoveCamera { get; init; }

    public T? Module<T>() where T : class, IModifier => Editor.GetModifier<T>();

    public void NotifyChanged(EditorDataScope scope = EditorDataScope.Entities) => DataChanged?.Invoke(scope);

    public void Status(string message) => Editor.RaiseStatusMessage(message);
}

/// <summary>由 <see cref="AssistEditorHost"/> 打开的编辑窗口实现的接口。</summary>
public interface IAssistEditor
{
    /// <summary>窗口显示前调用一次，传入上下文。</summary>
    void Attach(AssistEditorContext context);

    /// <summary>窗口已打开时又收到同类请求（例如在另一个格子上再按 Q）：默认什么都不做，宿主随后会激活窗口。</summary>
    void Retarget(EditorRequest request) { }

    /// <summary>切换编辑模式后是否保持打开。默认关闭（窗口跟着请求它的模式走，对齐 VB 离开模式时关窗）。</summary>
    bool KeepOpenOnModeChange(EditMode newMode) => false;
}

/// <summary>
/// 通用编辑窗口宿主：扫描 GUI 程序集里带 <see cref="AssistEditorAttribute"/> 的窗口类，
/// 按种类打开 / 激活（同一种类同时只开一个），切换模式或离开场景时统一关闭。
/// </summary>
public sealed class AssistEditorHost
{
    private static readonly Lazy<Dictionary<string, Type>> Registry = new(Discover);

    private readonly Func<EditorRequest, AssistEditorContext?> _contextFactory;
    private readonly Func<Window?> _owner;
    private readonly Dictionary<string, Window> _open = new(StringComparer.Ordinal);

    public AssistEditorHost(Func<EditorRequest, AssistEditorContext?> contextFactory, Func<Window?> owner)
    {
        _contextFactory = contextFactory;
        _owner = owner;
    }

    /// <summary>已实现的窗口种类。</summary>
    public static IReadOnlyCollection<string> AvailableKinds => Registry.Value.Keys;

    /// <summary>打开（或激活已打开的）编辑窗口；该种类没有实现时返回 false。</summary>
    public bool Open(EditorRequest request)
    {
        if (!Registry.Value.TryGetValue(request.Kind, out var type)) return false;

        if (_open.TryGetValue(request.Kind, out var existing))
        {
            if (existing is IAssistEditor ed) ed.Retarget(request);
            existing.Activate();
            return true;
        }

        var context = _contextFactory(request);
        if (context == null) return false;

        var window = (Window)Activator.CreateInstance(type)!;
        if (window is IAssistEditor editor) editor.Attach(context);
        var owner = _owner();
        if (owner != null && !ReferenceEquals(owner, window)) window.Owner = owner;

        string kind = request.Kind;
        window.Closed += (_, _) =>
        {
            if (_open.TryGetValue(kind, out var w) && ReferenceEquals(w, window)) _open.Remove(kind);
        };
        _open[kind] = window;
        window.Show();
        // 显式激活：键盘焦点落在子窗口上，Esc 才不会被主窗口的全局绑定接走
        window.Activate();
        return true;
    }

    /// <summary>模式切换：关闭不需要保留的窗口。</summary>
    public void OnModeChanged(EditMode newMode)
    {
        foreach (var w in _open.Values.ToList())
        {
            if (w is IAssistEditor e && e.KeepOpenOnModeChange(newMode)) continue;
            w.Close();
        }
    }

    /// <summary>关闭全部窗口（离开场景 / 重新加载地图）。</summary>
    public void CloseAll()
    {
        foreach (var w in _open.Values.ToList())
        {
            try { w.Close(); } catch { /* 窗口已在关闭中 */ }
        }
        _open.Clear();
    }

    private static Dictionary<string, Type> Discover()
    {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var t in typeof(AssistEditorHost).Assembly.GetTypes())
        {
            if (t.IsAbstract || !typeof(Window).IsAssignableFrom(t)) continue;
            foreach (var a in t.GetCustomAttributes<AssistEditorAttribute>())
                map[a.Kind] = t;
        }
        return map;
    }
}
