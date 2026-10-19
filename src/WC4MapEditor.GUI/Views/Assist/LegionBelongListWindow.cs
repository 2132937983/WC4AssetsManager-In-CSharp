using System.Collections.Specialized;
using WC4MapEditor.Core.Mode;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.GUI.Services;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 军团列表窗口（军团编辑模式 F 键，<see cref="EditorKinds.LegionList"/>）—— 对齐 VB 版 Assist/LegionBelongList。
/// <para>
/// 列出全部军团（国旗、序号、国家名、行动顺序）。Enter / 双击 / "确认" 把选中的军团设为当前军团
/// （军团模式左键设置领域、P 键默认值都用它；没选时取第一个）。拖动列表项或用 ▲ / ▼ 调整军团顺序：
/// 格子归属与单位、陷阱等的所属军团序号同步改写，领地跟着军团走；可选按新顺序重排行动顺序（VB 拖动排序的本意，
/// VB 原版只改了列表副本的行动顺序、从未写回地图）。每次移动都可撤销。
/// </para>
/// <para>
/// 另有一个只读的模态选择用法（<see cref="LegionBelongListWindow(List{Legion})"/>，供
/// IDialogService.ShowLegionBelongListDialogAsync 使用）：只能选择，不能排序，结果是选中军团的行动顺序。
/// </para>
/// </summary>
[AssistEditor(EditorKinds.LegionList)]
public sealed class LegionBelongListWindow : Window, IAssistEditor
{
    private readonly ListBox _listBox = new() { SelectionMode = SelectionMode.Single };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _orderBar = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    private readonly CheckBox _renumberBox;

    /// <summary>编辑窗口用法的上下文；为 null 时是只读的模态选择框。</summary>
    private AssistEditorContext? _context;
    private List<Legion> _pickerLegions = new();

    private bool _isApplying;
    private bool _reloadQueued;
    private int _dragIndex = -1;
    private Point _dragStart;

    /// <summary>模态选择用法的结果：选中军团的行动顺序（取消时为 null）。</summary>
    public int? SelectedActionId { get; private set; }

    /// <summary>模态选择用法：是否确认了选择。</summary>
    public bool IsConfirmed { get; private set; }

    public LegionBelongListWindow()
    {
        EditorTheme.Apply(this);
        _renumberBox = EditorTheme.CheckBox(Loc.T("Assist.LegionList.Renumber", "按新顺序重排行动顺序"), initial: true);
        _renumberBox.ToolTip = Loc.T("Assist.LegionList.RenumberTooltip", "移动后把所有军团的行动顺序改为 1、2、3……（与列表顺序一致）");

        Title = Loc.T("Assist.LegionList.Title");
        Width = 440;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        // 无边框圆角卡片 + 投影需要窗口本身透明；主题底色放在内部卡片 Border 上
        Background = Brushes.Transparent;
        AllowsTransparency = true;
        ShowInTaskbar = false;

        Content = CreateUI();

        Opacity = 0;
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>只读的模态选择框（IDialogService.ShowLegionBelongListDialogAsync）。</summary>
    public LegionBelongListWindow(List<Legion> legions) : this()
    {
        _pickerLegions = legions ?? new List<Legion>();
        RefreshList(0);
    }

    private IReadOnlyList<Legion> Legions => _context != null ? _context.MapData.Legions : _pickerLegions;

    private LegionModifier Module => _context?.Module<LegionModifier>()
        ?? throw new InvalidOperationException("LegionModifier is not available");

    // ------------------------------------------------------------------ IAssistEditor

    void IAssistEditor.Attach(AssistEditorContext context)
    {
        _context = context;
        _orderBar.Visibility = Visibility.Visible;
        _hint.Visibility = Visibility.Visible;
        _listBox.AllowDrop = true;
        context.MapData.Legions.CollectionChanged += OnLegionsCollectionChanged;
        RefreshList(Module.SelectedLegionId - 1);
    }

    // ------------------------------------------------------------------ UI

    private FrameworkElement CreateUI()
    {
        var card = new Border
        {
            Background = EditorTheme.Bg2,
            CornerRadius = new CornerRadius(8),
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(10),
            Effect = new DropShadowEffect
            {
                Color = EditorTheme.ColorOf(EditorTheme.Bg0),
                Direction = 270,
                ShadowDepth = 6,
                BlurRadius = 20,
                Opacity = 0.6
            }
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titlePanel = CreateTitlePanel();
        Grid.SetRow(titlePanel, 0);
        grid.Children.Add(titlePanel);

        var listPanel = CreateListPanel();
        Grid.SetRow(listPanel, 1);
        grid.Children.Add(listPanel);

        var orderPanel = CreateOrderBar();
        Grid.SetRow(orderPanel, 2);
        grid.Children.Add(orderPanel);

        var buttonPanel = CreateButtonPanel();
        Grid.SetRow(buttonPanel, 3);
        grid.Children.Add(buttonPanel);

        card.Child = grid;
        return card;
    }

    private StackPanel CreateTitlePanel()
    {
        var header = new DockPanel();
        var closeButton = EditorTheme.GhostButton("✕", (_, _) => Cancel());
        closeButton.Width = 28;
        closeButton.Height = 28;
        closeButton.Padding = new Thickness(0);
        closeButton.ToolTip = Loc.T("Assist.Common.CloseEsc");
        closeButton.VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(closeButton, Dock.Right);
        header.Children.Add(closeButton);

        var titleLabel = EditorTheme.Title(Loc.T("Assist.LegionList.Title"));
        DockPanel.SetDock(titleLabel, Dock.Left);
        header.Children.Add(titleLabel);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };

        _hint.Text = Loc.T("Assist.LegionList.Hint", "Enter / 双击：设为当前军团。拖动或 ▲ ▼：调整军团顺序（领地、单位跟着军团走，可撤销）");
        _hint.Foreground = EditorTheme.TextMuted;
        _hint.FontSize = 11;
        _hint.Margin = new Thickness(0, 4, 0, 0);

        var panel = new StackPanel { Margin = new Thickness(15, 10, 15, 5) };
        panel.Children.Add(header);
        panel.Children.Add(_hint);
        return panel;
    }

    private Border CreateListPanel()
    {
        // 列表项是直接塞进 Items 的 UIElement，容器回收模式下复用容器会把同一个元素挂到两个父级上，改回标准虚拟化
        VirtualizingPanel.SetVirtualizationMode(_listBox, VirtualizationMode.Standard);
        _listBox.MouseDoubleClick += (_, _) => { if (_listBox.SelectedIndex >= 0) Confirm(); };
        _listBox.PreviewMouseLeftButtonDown += OnListMouseDown;
        _listBox.PreviewMouseMove += OnListMouseMove;
        _listBox.DragOver += OnListDragOver;
        _listBox.DragLeave += (_, _) => HighlightDropTarget(-1);
        _listBox.Drop += OnListDrop;

        // 主题里的 ListBox 本身无底无框，用一层面板画出列表区域
        var host = EditorTheme.Panel(new Thickness(1));
        host.CornerRadius = new CornerRadius(4);
        host.Margin = new Thickness(15, 5, 15, 5);
        host.Child = _listBox;
        return host;
    }

    private StackPanel CreateOrderBar()
    {
        var up = EditorTheme.Button("▲", (_, _) => MoveSelected(-1));
        up.Width = 36;
        up.ToolTip = Loc.T("Assist.LegionList.MoveUp", "上移选中的军团");
        var down = EditorTheme.Button("▼", (_, _) => MoveSelected(+1));
        down.Width = 36;
        down.Margin = new Thickness(6, 0, 12, 0);
        down.ToolTip = Loc.T("Assist.LegionList.MoveDown", "下移选中的军团");
        _renumberBox.VerticalAlignment = VerticalAlignment.Center;

        _orderBar.Margin = new Thickness(15, 4, 15, 0);
        _orderBar.Children.Add(up);
        _orderBar.Children.Add(down);
        _orderBar.Children.Add(_renumberBox);

        var panel = new StackPanel();
        panel.Children.Add(_orderBar);
        _status.Margin = new Thickness(15, 4, 15, 0);
        _status.FontSize = 11;
        panel.Children.Add(_status);
        return panel;
    }

    private StackPanel CreateButtonPanel()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(15, 10, 15, 15)
        };

        var confirmButton = EditorTheme.PrimaryButton(Loc.T("Common.Confirm"), (_, _) => Confirm());
        confirmButton.MinWidth = 80;
        confirmButton.Margin = new Thickness(0, 0, 10, 0);
        confirmButton.ToolTip = Loc.T("Assist.Common.ConfirmEnter");
        panel.Children.Add(confirmButton);

        var cancelButton = EditorTheme.Button(Loc.T("Common.Cancel"), (_, _) => Cancel());
        cancelButton.MinWidth = 80;
        cancelButton.ToolTip = Loc.T("Assist.Common.CancelEsc");
        panel.Children.Add(cancelButton);

        return panel;
    }

    // ------------------------------------------------------------------ 列表

    private void RefreshList(int select)
    {
        _listBox.Items.Clear();
        int current = _context != null ? Module.SelectedLegionId - 1 : -1;
        for (int i = 0; i < Legions.Count; i++)
            _listBox.Items.Add(CreateLegionListItem(Legions[i], i, i == current));

        if (_listBox.Items.Count == 0) return;
        _listBox.SelectedIndex = Math.Clamp(select, 0, _listBox.Items.Count - 1);
        _listBox.ScrollIntoView(_listBox.SelectedItem);
    }

    private Border CreateLegionListItem(Legion legion, int index, bool isCurrent)
    {
        var border = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0, 2, 0, 0)
        };

        var panel = new DockPanel { Margin = new Thickness(5, 3, 5, 3) };

        var flagImage = new Image
        {
            Width = 30,
            Height = 30,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(0, 0, 10, 0),
            Source = FlagImageLoader.Load(legion.CountryId)
        };
        DockPanel.SetDock(flagImage, Dock.Left);
        panel.Children.Add(flagImage);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        string name = Loc.F("Assist.LegionList.Item", index + 1, WC4MapEditor.Core.Config.CountryNameProvider.Get(legion.CountryId));
        if (isCurrent) name += Loc.T("Assist.LegionList.CurrentMark", "  ● 当前");
        info.Children.Add(new TextBlock { Text = name, FontSize = 14 });

        string detail = Loc.F("Assist.LegionList.Detail", legion.ActionId, legion.CountryId, legion.Camp);
        if (legion.IsPlayer) detail += Loc.T("Assist.LegionList.PlayerMark", "  · 玩家");
        info.Children.Add(new TextBlock { Text = detail, FontSize = 11, Foreground = EditorTheme.TextMuted });

        panel.Children.Add(info);
        border.Child = panel;
        return border;
    }

    private void SetStatus(string? message, bool isError = false)
    {
        _status.Text = message ?? string.Empty;
        _status.Foreground = isError ? EditorTheme.Danger : EditorTheme.TextMuted;
    }

    /// <summary>别处改了军团列表（军团编辑器、撤销、C/U/R/P 键……）：合并成一次刷新，保住选中位置。</summary>
    private void OnLegionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isApplying || _reloadQueued) return;
        _reloadQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _reloadQueued = false;
            RefreshList(_listBox.SelectedIndex);
        }, DispatcherPriority.Background);
    }

    // ------------------------------------------------------------------ 选择

    /// <summary>
    /// 确认选择（没选时取第一个）。编辑窗口用法：设为当前军团并关闭；模态选择用法：记下行动顺序后关闭。
    /// </summary>
    private void Confirm()
    {
        if (Legions.Count == 0)
        {
            Cancel();
            return;
        }

        int index = Math.Max(0, _listBox.SelectedIndex);
        if (_context == null)
        {
            SelectedActionId = Legions[index].ActionId;
            IsConfirmed = true;
            DialogResult = true;
            return;
        }

        Module.SelectedLegionId = index + 1;
        _context.Status(Loc.F("Assist.LegionList.Selected", index + 1,
            WC4MapEditor.Core.Config.CountryNameProvider.Get(Legions[index].CountryId)));
        Close();
    }

    private void Cancel()
    {
        if (_context == null)
        {
            IsConfirmed = false;
            SelectedActionId = null;
            DialogResult = false;
            return;
        }
        Close();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Confirm();
                e.Handled = true;
                break;
            case Key.Escape:
                Cancel();
                e.Handled = true;
                break;
            case Key.Tab when Legions.Count > 0:
                _listBox.SelectedIndex = (_listBox.SelectedIndex + 1) % Legions.Count;
                _listBox.ScrollIntoView(_listBox.SelectedItem);
                e.Handled = true;
                break;
        }
    }

    // ------------------------------------------------------------------ 排序

    private void MoveSelected(int step)
    {
        int from = _listBox.SelectedIndex;
        MoveLegion(from, from + step);
    }

    /// <summary>经军团模块移动军团（同步改写所有序号引用），整体快照记录撤销。</summary>
    private void MoveLegion(int from, int to)
    {
        if (_context == null || from < 0 || to < 0 || from >= Legions.Count || to >= Legions.Count || from == to) return;

        bool renumber = _renumberBox.IsChecked == true;
        ModifierResult result = default;
        _isApplying = true;
        try
        {
            _context.Editor.RecordEntitiesSnapshot(Loc.F("Assist.LegionList.UndoMove", from + 1, to + 1),
                () => result = Module.MoveLegion(from, to, renumber));
        }
        finally
        {
            _isApplying = false;
        }

        if (!result.Success)
        {
            SetStatus(result.Message, isError: true);
            return;
        }

        _context.NotifyChanged(EditorDataScope.Legions | EditorDataScope.Belongs | EditorDataScope.Entities);
        RefreshList(to);
        SetStatus(result.Message);
    }

    private int IndexAt(DependencyObject? source)
    {
        if (source == null) return -1;
        return ItemsControl.ContainerFromElement(_listBox, source) is ListBoxItem item
            ? _listBox.ItemContainerGenerator.IndexFromContainer(item)
            : -1;
    }

    private void OnListMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragIndex = _context != null ? IndexAt(e.OriginalSource as DependencyObject) : -1;
        _dragStart = e.GetPosition(_listBox);
    }

    private void OnListMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragIndex < 0 || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(_listBox) - _dragStart;
        if (Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        int from = _dragIndex;
        _dragIndex = -1;
        DragDrop.DoDragDrop(_listBox, from, DragDropEffects.Move);
        HighlightDropTarget(-1);
    }

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        bool valid = e.Data.GetDataPresent(typeof(int));
        e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
        HighlightDropTarget(valid ? IndexAt(e.OriginalSource as DependencyObject) : -1);
        e.Handled = true;
    }

    private void OnListDrop(object sender, DragEventArgs e)
    {
        HighlightDropTarget(-1);
        if (e.Data.GetData(typeof(int)) is not int from) return;
        int to = IndexAt(e.OriginalSource as DependencyObject);
        if (to < 0) to = Legions.Count - 1;
        MoveLegion(from, to);
    }

    /// <summary>拖动时给目标位置画一条强调色上边线（-1 清除）。</summary>
    private void HighlightDropTarget(int index)
    {
        for (int i = 0; i < _listBox.Items.Count; i++)
        {
            if (_listBox.Items[i] is Border border)
                border.BorderBrush = i == index ? EditorTheme.Accent : Brushes.Transparent;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_context != null) _context.MapData.Legions.CollectionChanged -= OnLegionsCollectionChanged;
        base.OnClosed(e);
    }
}
