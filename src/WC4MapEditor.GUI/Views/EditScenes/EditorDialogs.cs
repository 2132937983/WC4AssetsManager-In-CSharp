namespace WC4MapEditor.GUI.Views.EditScenes;

/// <summary>未保存修改对话框的选择。</summary>
public enum UnsavedChoice
{
    Save,
    Discard,
    Cancel
}

/// <summary>
/// JSON 编辑场景用的模态对话框（编辑主题、同步返回）。
/// 同步返回让“离开场景 / 关闭窗口 / 重新加载”前的保存询问可以直接在调用处决定是否继续。
/// </summary>
public static class EditorDialogs
{
    public static void Info(Window? owner, string title, string message) => Show(owner, title, message, EditorTheme.Info, (Loc.T("Common.Ok"), true, false));

    public static void Warning(Window? owner, string title, string message) => Show(owner, title, message, EditorTheme.Warning, (Loc.T("Common.Ok"), true, false));

    public static void Error(Window? owner, string title, string message) => Show(owner, title, message, EditorTheme.Danger, (Loc.T("Common.Ok"), true, false));

    /// <summary>确认 / 取消；<paramref name="danger"/> 时确认按钮用危险色（删除、覆盖这类操作）。</summary>
    public static bool Confirm(Window? owner, string title, string message, string? confirmText = null, bool danger = false)
        => Show(owner, title, message, danger ? EditorTheme.Danger : EditorTheme.Accent,
               (confirmText ?? Loc.T("Common.Confirm"), true, danger),
               (Loc.T("Common.Cancel"), false, false)) == 0;

    /// <summary>保存 / 放弃 / 取消（离开场景、关闭窗口、重新加载前）。</summary>
    public static UnsavedChoice AskUnsaved(Window? owner, string message)
    {
        int choice = Show(owner, Loc.T("JsonEditor.Unsaved.Title"), message, EditorTheme.Warning,
            (Loc.T("JsonEditor.Unsaved.Save"), true, false),
            (Loc.T("JsonEditor.Unsaved.Discard"), false, true),
            (Loc.T("Common.Cancel"), false, false));
        return choice switch
        {
            0 => UnsavedChoice.Save,
            1 => UnsavedChoice.Discard,
            _ => UnsavedChoice.Cancel
        };
    }

    /// <summary>
    /// 通用模态对话框：返回被点按钮的序号，关闭窗口 / Esc 返回最后一个按钮（取消）的序号。
    /// buttons：(文字, 是否主按钮（Enter）, 是否危险色)。
    /// </summary>
    public static int Show(Window? owner, string title, string message, Brush accent, params (string Text, bool IsDefault, bool Danger)[] buttons)
    {
        int result = buttons.Length - 1;
        var window = CreateWindow(owner, title, 460);

        var root = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new Border { Width = 4, Height = 18, Background = accent, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 10, 0) });
        header.Children.Add(EditorTheme.Title(title));
        root.Children.Add(header);

        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = EditorTheme.Text, FontSize = 13, LineHeight = 21 };
        root.Children.Add(new ScrollViewer
        {
            Content = text,
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 18)
        });

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            var (label, isDefault, danger) = buttons[i];
            var button = danger ? EditorTheme.DangerButton(label) : isDefault ? EditorTheme.PrimaryButton(label) : EditorTheme.Button(label);
            button.MinWidth = 88;
            button.Margin = new Thickness(8, 0, 0, 0);
            button.IsDefault = isDefault;
            button.IsCancel = i == buttons.Length - 1;
            button.Click += (_, _) => { result = index; window.Close(); };
            buttonRow.Children.Add(button);
        }
        root.Children.Add(buttonRow);

        window.Content = root;
        window.ShowDialog();
        return result;
    }

    /// <summary>长文本（帮助等）：宽一些、可滚动、Esc 关闭。</summary>
    public static void ShowText(Window? owner, string title, string text, double width = 720)
    {
        var window = CreateWindow(owner, title, width);
        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new Border { Width = 4, Height = 18, Background = EditorTheme.Info, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 10, 0) });
        header.Children.Add(EditorTheme.Title(title));
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var close = EditorTheme.PrimaryButton(Loc.T("Common.Close"), (_, _) => window.Close());
        close.MinWidth = 88;
        close.IsDefault = true;
        close.IsCancel = true;
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 14, 0, 0);
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);

        root.Children.Add(new ScrollViewer
        {
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = EditorTheme.Text, FontSize = 13, LineHeight = 21 },
            MaxHeight = 560,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        window.Content = root;
        window.ShowDialog();
    }

    /// <summary>编辑主题的模态窗口外壳（标题栏 + 主题底色，居中于主窗口）。</summary>
    public static Window CreateWindow(Window? owner, string title, double width)
    {
        var window = new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
        };
        if (owner != null && owner.IsLoaded) window.Owner = owner;
        EditorTheme.Apply(window);
        return window;
    }
}
