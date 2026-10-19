namespace WC4MapEditor.GUI.Views.EditScenes;

/// <summary>
/// 操作历史窗口（Ctrl+H）：列出打开场景以来的每一步，可以回退到任意一步，回退后仍可前进（重做）。
/// </summary>
public static class HistoryWindow
{
    public static void Show(Window? owner, JsonEditSession session, Action<string> status)
    {
        session.Flush();
        var window = EditorDialogs.CreateWindow(owner, Loc.T("JsonEditor.History.Title"), 560);
        var root = new DockPanel { Margin = new Thickness(16) };

        var hint = EditorTheme.Muted(Loc.F("JsonEditor.History.Hint", JsonEditSession.MaxHistory));
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(hint, Dock.Top);
        root.Children.Add(hint);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var list = new ListBox { Height = 380 };
        root.Children.Add(list);

        void Fill()
        {
            list.Items.Clear();
            for (int i = 0; i < session.History.Count; i++)
            {
                var entry = session.History[i];
                bool current = i == session.HistoryIndex;
                list.Items.Add(new ListBoxItem
                {
                    Content = $"{(current ? "▶" : "  ")} {i,2}.  [{entry.Time:HH:mm:ss}]  {entry.Label}",
                    Foreground = i > session.HistoryIndex ? EditorTheme.TextDim : EditorTheme.Text,
                    FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal,
                    Tag = i
                });
            }
            list.SelectedIndex = session.HistoryIndex;
            if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem);
        }

        void RevertSelected()
        {
            if (list.SelectedItem is not ListBoxItem { Tag: int index } || index == session.HistoryIndex) return;
            session.RevertTo(index);
            status(Loc.F("JsonEditor.Status.Reverted", session.History[index].Label));
            Fill();
        }

        void AddButton(Button b)
        {
            b.MinWidth = 88;
            b.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(b);
        }

        AddButton(EditorTheme.PrimaryButton(Loc.T("JsonEditor.History.Revert"), (_, _) => RevertSelected()));
        AddButton(EditorTheme.Button(Loc.T("Common.Undo"), (_, _) => { session.Undo(); Fill(); }));
        AddButton(EditorTheme.Button(Loc.T("Common.Redo"), (_, _) => { session.Redo(); Fill(); }));
        var close = EditorTheme.Button(Loc.T("Common.Close"), (_, _) => window.Close());
        close.IsCancel = true;
        AddButton(close);

        list.MouseDoubleClick += (_, _) => RevertSelected();
        Fill();
        window.Content = root;
        window.ShowDialog();
    }
}
