namespace WC4MapEditor.GUI.Views.EditScenes;

/// <summary>
/// 列表上方的分组筛选下拉框：“全部” + 数据里出现过的分组值（事件组、征服、科技分类）。
/// 数据变化后调用 <see cref="Refresh"/>，原来选中的分组仍然存在时保持选中。
/// </summary>
public sealed class GroupFilter
{
    private readonly string _allText;
    private readonly Func<int, string> _name;
    private bool _updating;

    public GroupFilter(string allText, Func<int, string> name)
    {
        _allText = allText;
        _name = name;
        Combo = EditorTheme.ComboBox();
        Combo.Margin = new Thickness(8, 8, 8, 0);
        Combo.SelectionChanged += (_, _) =>
        {
            if (!_updating) Changed?.Invoke();
        };
    }

    public ComboBox Combo { get; }

    /// <summary>选中的分组；-1 表示全部。</summary>
    public int Selected => Combo.SelectedItem is Entry e ? e.Id : -1;

    public bool IsAll => Selected < 0;

    public event Action? Changed;

    /// <summary>重新填充分组；<paramref name="select"/> 指定要选中的分组（否则保持原选择）。</summary>
    public void Refresh(IEnumerable<int> groups, int? select = null)
    {
        int keep = select ?? Selected;
        _updating = true;
        try
        {
            Combo.Items.Clear();
            Combo.Items.Add(new Entry(-1, _allText));
            foreach (var g in groups.Distinct().OrderBy(x => x)) Combo.Items.Add(new Entry(g, _name(g)));
            Combo.SelectedItem = Combo.Items.Cast<Entry>().FirstOrDefault(e => e.Id == keep) ?? Combo.Items[0];
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>上一个 / 下一个分组（跳过“全部”）。</summary>
    public void Step(int delta)
    {
        int count = Combo.Items.Count;
        if (count <= 1) return;
        int index = Combo.SelectedIndex <= 0 ? (delta > 0 ? 0 : count) : Combo.SelectedIndex;
        index += delta;
        if (index < 1) index = count - 1;
        if (index >= count) index = 1;
        Combo.SelectedIndex = index;
    }

    public bool Matches(int group) => IsAll || group == Selected;

    private sealed record Entry(int Id, string Name)
    {
        public override string ToString() => Name;
    }
}
