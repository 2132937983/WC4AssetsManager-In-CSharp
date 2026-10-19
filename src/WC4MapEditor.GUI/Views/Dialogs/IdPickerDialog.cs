using System.Collections.ObjectModel;
using WC4MapEditor.GUI.Views.EditScenes;

namespace WC4MapEditor.GUI.Views.Dialogs;

/// <summary>选择器里的一个候选项：Id 与显示文字（文字里已包含 Id，搜索匹配整行文字）。</summary>
public sealed record PickerEntry(int Id, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// 可搜索的 Id 选择对话框（编辑主题、模态）：国家、将领、兵种、技能、Buff 等引用字段共用。
/// <see cref="PickOne"/> 选一个 Id；<see cref="PickMany"/> 选一组有顺序的 Id（左边候选池，右边已选列表，可上下调整顺序）。
/// 返回 null 表示取消。
/// </summary>
public static class IdPickerDialog
{
    /// <summary>单选。<paramref name="allowClear"/> 时多一个“清除”按钮，选定后返回 0（字段里 0 表示没有引用）。</summary>
    public static int? PickOne(Window? owner, string title, IReadOnlyList<PickerEntry> entries, int current, bool allowClear)
    {
        int? result = null;
        var window = EditorDialogs.CreateWindow(owner, title, 520);
        var root = new StackPanel { Margin = new Thickness(16) };

        var search = EditorTheme.SearchBox(Loc.T("Common.SearchPlaceholder"), out var searchBox);
        root.Children.Add(search);
        var list = new ListBox { Height = 340, Margin = new Thickness(0, 8, 0, 0) };
        root.Children.Add(list);
        var count = EditorTheme.Muted("");
        count.FontSize = 11;
        count.Margin = new Thickness(2, 6, 0, 0);
        root.Children.Add(count);

        void Fill()
        {
            var shown = Filter(entries, searchBox.Text.Trim());
            list.ItemsSource = shown;
            count.Text = Loc.F("Picker.Count", shown.Count, entries.Count);
            if (list.SelectedItem == null && shown.Count > 0) list.SelectedIndex = 0;
        }

        void Accept()
        {
            if (list.SelectedItem is not PickerEntry entry) return;
            result = entry.Id;
            window.Close();
        }

        searchBox.TextChanged += (_, _) => Fill();
        searchBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Accept();
                e.Handled = true;
            }
            else if (e.Key == Key.Down && list.Items.Count > 0)
            {
                list.Focus();
                e.Handled = true;
            }
        };
        list.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(list, source) != null) Accept();
        };
        list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            Accept();
            e.Handled = true;
        };

        var buttons = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        var ok = EditorTheme.PrimaryButton(Loc.T("Common.Confirm"), (_, _) => Accept());
        ok.MinWidth = 88;
        var cancel = EditorTheme.Button(Loc.T("Common.Cancel"), (_, _) => window.Close());
        cancel.MinWidth = 88;
        cancel.IsCancel = true;
        cancel.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(cancel, Dock.Right);
        DockPanel.SetDock(ok, Dock.Right);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        if (allowClear)
        {
            var clear = EditorTheme.Button(Loc.T("Picker.Clear"), (_, _) =>
            {
                result = 0;
                window.Close();
            });
            clear.MinWidth = 88;
            buttons.Children.Add(clear);
        }
        root.Children.Add(buttons);

        window.Content = root;
        Fill();
        if (entries.FirstOrDefault(e => e.Id == current) is { } preselect)
        {
            list.SelectedItem = preselect;
            list.ScrollIntoView(preselect);
        }
        window.Loaded += (_, _) => searchBox.Focus();
        window.ShowDialog();
        return result;
    }

    /// <summary>多选并保持顺序：已选的 Id 不在候选里时也保留（显示为“未知 Id”），避免确认时悄悄丢掉。</summary>
    public static List<int>? PickMany(Window? owner, string title, IReadOnlyList<PickerEntry> entries, IReadOnlyList<int> current)
    {
        List<int>? result = null;
        var window = EditorDialogs.CreateWindow(owner, title, 820);
        var byId = entries.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        var selected = new ObservableCollection<PickerEntry>(
            current.Select(id => byId.TryGetValue(id, out var known) ? known : new PickerEntry(id, Loc.F("Picker.UnknownId", id))));

        var body = new Grid { Margin = new Thickness(16) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 左：候选池
        var search = EditorTheme.SearchBox(Loc.T("Common.SearchPlaceholder"), out var searchBox);
        body.Children.Add(search);
        var pool = new ListBox { Height = 340, SelectionMode = SelectionMode.Extended, Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetRow(pool, 1);
        body.Children.Add(pool);
        var addRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var poolCount = EditorTheme.Muted("");
        poolCount.FontSize = 11;
        poolCount.VerticalAlignment = VerticalAlignment.Center;
        var add = EditorTheme.Button(Loc.T("Picker.Add"));
        DockPanel.SetDock(add, Dock.Right);
        addRow.Children.Add(add);
        addRow.Children.Add(poolCount);
        Grid.SetRow(addRow, 2);
        body.Children.Add(addRow);

        // 右：已选列表
        var selectedHeader = EditorTheme.Section("");
        selectedHeader.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetColumn(selectedHeader, 2);
        body.Children.Add(selectedHeader);
        var chosen = new ListBox { Height = 340, SelectionMode = SelectionMode.Extended, ItemsSource = selected, Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetColumn(chosen, 2);
        Grid.SetRow(chosen, 1);
        body.Children.Add(chosen);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetColumn(tools, 2);
        Grid.SetRow(tools, 2);
        body.Children.Add(tools);

        void UpdateCounts() => selectedHeader.Text = Loc.F("Picker.Selected", selected.Count);

        void Fill()
        {
            var shown = Filter(entries, searchBox.Text.Trim());
            pool.ItemsSource = shown;
            poolCount.Text = Loc.F("Picker.Count", shown.Count, entries.Count);
        }

        void AddEntries(IEnumerable<PickerEntry> toAdd)
        {
            foreach (var entry in toAdd.Where(entry => selected.All(s => s.Id != entry.Id))) selected.Add(entry);
            UpdateCounts();
        }

        void RemoveSelected()
        {
            foreach (var entry in chosen.SelectedItems.Cast<PickerEntry>().ToList()) selected.Remove(entry);
            UpdateCounts();
        }

        void Move(int delta)
        {
            var picked = chosen.SelectedItems.Cast<PickerEntry>().OrderBy(selected.IndexOf).ToList();
            if (delta > 0) picked.Reverse();
            foreach (var entry in picked)
            {
                int index = selected.IndexOf(entry);
                int target = index + delta;
                if (target < 0 || target >= selected.Count || picked.Contains(selected[target])) continue;
                selected.Move(index, target);
            }
        }

        Button Tool(string text, string tip, Action action)
        {
            var button = EditorTheme.ToolButton(text, (_, _) => action());
            button.ToolTip = tip;
            button.Margin = new Thickness(0, 0, 4, 0);
            return button;
        }

        tools.Children.Add(Tool("▲", Loc.T("JsonEditor.Tip.MoveUp"), () => Move(-1)));
        tools.Children.Add(Tool("▼", Loc.T("JsonEditor.Tip.MoveDown"), () => Move(1)));
        tools.Children.Add(Tool(Loc.T("Picker.Remove"), Loc.T("Picker.Remove"), RemoveSelected));
        tools.Children.Add(Tool(Loc.T("Picker.ClearAll"), Loc.T("Picker.ClearAll"), () =>
        {
            selected.Clear();
            UpdateCounts();
        }));

        searchBox.TextChanged += (_, _) => Fill();
        add.Click += (_, _) => AddEntries(pool.SelectedItems.Cast<PickerEntry>());
        pool.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(pool, source) is ListBoxItem { Content: PickerEntry entry })
                AddEntries([entry]);
        };
        pool.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            AddEntries(pool.SelectedItems.Cast<PickerEntry>());
            e.Handled = true;
        };
        chosen.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Delete) return;
            RemoveSelected();
            e.Handled = true;
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 0, 16, 16) };
        var ok = EditorTheme.PrimaryButton(Loc.T("Common.Confirm"), (_, _) =>
        {
            result = selected.Select(e => e.Id).ToList();
            window.Close();
        });
        ok.MinWidth = 88;
        ok.IsDefault = true;
        var cancel = EditorTheme.Button(Loc.T("Common.Cancel"), (_, _) => window.Close());
        cancel.MinWidth = 88;
        cancel.IsCancel = true;
        cancel.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel();
        root.Children.Add(body);
        root.Children.Add(buttons);
        window.Content = root;
        Fill();
        UpdateCounts();
        window.Loaded += (_, _) => searchBox.Focus();
        window.ShowDialog();
        return result;
    }

    private static List<PickerEntry> Filter(IReadOnlyList<PickerEntry> entries, string query)
        => query.Length == 0 ? entries.ToList() : entries.Where(e => e.Label.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
}
