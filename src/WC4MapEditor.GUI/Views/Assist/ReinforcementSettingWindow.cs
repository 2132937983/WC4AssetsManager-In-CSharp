using System.Collections.Specialized;
using WC4MapEditor.Core.Mode;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Selection;
using WC4MapEditor.GUI.Services;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 援军编辑器（对齐 VB ReinforceSetting / ReinforceSetting_v3）：援军模式按 Q 打开，编辑所选格子上叠放的整组援军。
/// <para>
/// 一个窗口支持两种版本：按地图 BtlVersion 选用 v1（80 字节）或 v3（104 字节）的字段表。
/// 列表下方 + 新建（按地形随机兵种、自动所属）、⊕ 复制选中项、− 删除；"随机将领"按所属军团的国家挑选将领。
/// 字段改动按 Enter / "应用" 提交：经援军模块写回，所属为 0 时自动取所在省份省会的军团，格子归属随第一个援军；
/// 每次提交都可以在地图上 Ctrl+Z 撤销。窗口打开时在另一个格子上再按 Q 会切换到那个格子。
/// </para>
/// </summary>
[AssistEditor(EditorKinds.ReinforcementSetting)]
public sealed partial class ReinforcementSettingWindow : ListEditorWindow
{
    private int _col = -1;
    private int _row = -1;
    private IRecordAccess _access = null!;

    /// <summary>本窗口自己在改数据（提交 / 增删）时为 true：这期间的集合变化不触发"外部变化"刷新。</summary>
    private bool _selfChange;
    private bool _refreshQueued;
    private INotifyCollectionChanged? _watched;

    public ReinforcementSettingWindow()
    {
        Width = 840;
        Height = 680;
        Closed += (_, _) => StopWatching();
    }

    protected override string TitleText => Loc.T("Assist.Reinforce.Title", "援军编辑器");

    protected override int ItemCount => _access.Count(_col, _row);

    protected override bool SupportsDuplicate => true;

    /// <summary>提交时自动归属会改格子归属，领域层与国旗层要一起刷新。</summary>
    protected override EditorDataScope ChangeScope => EditorDataScope.Entities | EditorDataScope.Belongs;

    protected override string DescribeItem(int index)
    {
        var slots = _access.Read(_col, _row, index);
        return slots == null ? "" : DescribeRecord(index, slots);
    }

    protected override void BuildFields(FieldPanel fields)
    {
        // 首次建表时 Context 已就绪、列表尚未填充：先定下版本与格子，ItemCount 才有意义
        _access = Map.Header.UsesV3Records
            ? new RecordAccess<Reinforcement_3>(Context.Editor.GetModifier<ReinforcementV3Modifier>()!, V3Rows())
            : new RecordAccess<Reinforcement>(Context.Editor.GetModifier<ReinforcementModifier>()!, V1Rows());
        (_col, _row) = InitialHex(Context.Request);
        BuildFieldControls(fields);
    }

    /// <summary>
    /// 打开时的目标格子：请求带了格子（援军模式 Q 键）就用它；没带（脚本 / AI / 启动参数打开）时取当前选中的格子，
    /// 再没有就定位到地图上第一个有援军的格子，免得打开一个空窗口。
    /// </summary>
    private (int Col, int Row) InitialHex(EditorRequest request)
    {
        if (request.Col >= 0 && request.Row >= 0) return (request.Col, request.Row);
        if (HexSelector.Instance.PrimarySelected is { } selected) return (selected.Col, selected.Row);
        var first = _access.Module.GetAllRecords().FirstOrDefault(r => r.Coordinate >= 0 && r.Coordinate < Map.MapWidth * Map.MapHeight);
        return first == null ? (-1, -1) : (first.Coordinate % Map.MapWidth, first.Coordinate / Map.MapWidth);
    }

    protected override void LoadFields(int index)
    {
        var slots = _access.Read(_col, _row, index);
        if (slots != null) ShowRecord(slots);
    }

    protected override bool TryCommitFields(int index, List<string> errors)
    {
        var original = _access.Read(_col, _row, index);
        if (original == null)
        {
            errors.Add(Loc.T("Module.Reinforcement.NoSuchRecord", "该格子没有这个援军（列表可能已变化）"));
            return false;
        }

        if (!TryReadRecord(original, errors, out var slots)) return false;

        var result = _access.Write(_col, _row, index, slots);
        if (!result.Success)
        {
            errors.Add(result.Message ?? Loc.T("Assist.ListEditor.Invalid", "输入无效"));
            return false;
        }

        // 所属可能被自动填写：把结果显示回来（格子归属的变化由 RecordChange 刷新）
        var saved = _access.Read(_col, _row, index);
        if (saved != null) ShowAutoFilled(saved);
        return true;
    }

    protected override int AddItem()
    {
        int index = _access.Add(_col, _row);
        if (index < 0) SetStatus(Loc.T("Assist.Reinforce.NoHex", "请先在地图上选择一个格子，再按 Q 打开援军编辑器"), true);
        return index;
    }

    protected override void RemoveItem(int index) => _access.Remove(_col, _row, index);

    protected override int DuplicateItem(int index) => _access.Duplicate(_col, _row, index);

    protected override void RecordChange(string description, Action apply)
    {
        _selfChange = true;
        try
        {
            Context.Editor.RecordEntitiesSnapshot(description, apply);
        }
        finally
        {
            _selfChange = false;
        }
        // 提交 / 新增 / 复制 / 删除都可能改了格子归属（自动归属）
        UpdateHexInfo();
    }

    protected override void OnAttached()
    {
        UpdateHexInfo();
        StartWatching();
        ShowHexWarning();
        // 格子是自动定位的（请求没带格子）：把相机移过去，地图上能看到正在编辑的是哪一格
        if (IsValidHex && (Context.Request.Col < 0 || Context.Request.Row < 0)) Context.MoveCamera?.Invoke(_col, _row);
    }

    protected override void OnRetarget(EditorRequest request)
    {
        // 不带格子的请求（脚本 / AI 再次打开）只激活窗口，保持当前格子
        if (request.Col < 0 || request.Row < 0 || (request.Col == _col && request.Row == _row)) return;
        if (!CommitCurrent())
        {
            SetStatus(Loc.T("Assist.Reinforce.RetargetBlocked", "当前援军有无效输入：请先修正或按 Esc 撤回，再切换格子"), true);
            return;
        }
        _col = request.Col;
        _row = request.Row;
        UpdateHexInfo();
        RefreshList(0);
        ShowHexWarning();
    }

    private bool IsValidHex => _col >= 0 && _row >= 0 && _col < Map.MapWidth && _row < Map.MapHeight;

    /// <summary>没有目标格子、或格子在归属数据范围外（游戏不读那里的援军）时在状态行提示。</summary>
    private void ShowHexWarning()
    {
        if (!IsValidHex)
            SetStatus(Loc.T("Assist.Reinforce.NoHex", "请先在地图上选择一个格子，再按 Q 打开援军编辑器"), true);
        else if (!Map.IsInBelongRange(_col, _row))
            SetStatus(Loc.T("Assist.Reinforce.OutOfBelongRange", "该格子在归属数据范围外，游戏不会读取这里的援军"), true);
    }

    // ================================================================== 外部变化（地图上撤销 / 删除等）

    private void StartWatching()
    {
        _watched = Map.Header.UsesV3Records ? Map.ReinforcementsV3 : Map.Reinforcements;
        _watched.CollectionChanged += OnRecordsChanged;
    }

    private void StopWatching()
    {
        if (_watched != null) _watched.CollectionChanged -= OnRecordsChanged;
        _watched = null;
    }

    /// <summary>援军列表在窗口之外被改了（地图上 Ctrl+Z、X 删除……）：合并成一次刷新，避免对着过期的列表编辑。</summary>
    private void OnRecordsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_selfChange || _refreshQueued) return;
        _refreshQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            _refreshQueued = false;
            if (_watched == null) return;
            UpdateHexInfo();
            RefreshList(CurrentIndex);
        }, DispatcherPriority.Background);
    }
}
