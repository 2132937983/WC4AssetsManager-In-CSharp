using System.Diagnostics;

namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// 推荐窗口：交流群列表，点击群号复制到剪贴板（对齐 VB 版 Assist/RecommandationWindow，开始界面 👍 按钮打开）。
/// 群名称与群号是社区的专有名称（QQ 里搜索时就要用原文），作为数据保留原文，不做翻译。
/// </summary>
public sealed class RecommendationWindow : Window
{
    /// <summary>交流群（群名称 - 群主，群号）</summary>
    private static readonly (string Name, string Number)[] Groups =
    {
        ("世四：帝国主群 - 守词", "331509554"),
        ("世界征服者4帝绍三群 - 孤独一人", "668371262"),
        ("欧陆战争IV 大鏖战 - 和泉纱雾", "753634257"),
        ("世界征服者4帝王命令 - 祝_San", "787334300"),
    };

    private readonly TextBlock _statusText = new();

    public RecommendationWindow()
    {
        EditorTheme.Apply(this);
        Title = Loc.T("Assist.Recommend.Title");
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = EditorTheme.Bg1;

        Content = CreateUI();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            Close();
            e.Handled = true;
        };

        Opacity = 0;
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
    }

    private FrameworkElement CreateUI()
    {
        var root = new StackPanel();
        root.Children.Add(CreateTitleBar());

        var body = new StackPanel { Margin = new Thickness(18, 14, 18, 6) };
        var intro = EditorTheme.Text_(Loc.T("Assist.Recommend.Intro"), 14);
        intro.TextWrapping = TextWrapping.Wrap;
        intro.Margin = new Thickness(0, 0, 0, 12);
        body.Children.Add(intro);

        foreach (var (name, number) in Groups)
            body.Children.Add(CreateGroupRow(name, number));

        var tip = EditorTheme.Muted(Loc.T("Assist.Recommend.ClickToCopy"));
        tip.FontStyle = FontStyles.Italic;
        tip.HorizontalAlignment = HorizontalAlignment.Center;
        tip.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(tip);

        _statusText.Foreground = EditorTheme.Success;
        _statusText.HorizontalAlignment = HorizontalAlignment.Center;
        _statusText.Margin = new Thickness(0, 6, 0, 0);
        _statusText.MinHeight = 18;
        body.Children.Add(_statusText);
        root.Children.Add(body);

        var close = EditorTheme.Button(Loc.T("Common.Close"), (_, _) => Close());
        close.MinWidth = 96;
        close.HorizontalAlignment = HorizontalAlignment.Center;
        root.Children.Add(new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 8, 12, 8),
            Child = close
        });

        return new Border
        {
            BorderBrush = EditorTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            Child = root
        };
    }

    /// <summary>标题栏：按住可拖动窗口，右侧 ✕ 关闭。</summary>
    private Border CreateTitleBar()
    {
        var grid = new Grid { Height = 40 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = EditorTheme.Title(Loc.T("Assist.Recommend.Title"));
        title.Margin = new Thickness(14, 0, 0, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(title);

        var close = EditorTheme.GhostButton("✕", (_, _) => Close());
        close.Width = 32;
        close.Height = 28;
        close.Padding = new Thickness(0);
        close.Margin = new Thickness(0, 0, 6, 0);
        close.ToolTip = Loc.T("Common.Close");
        Grid.SetColumn(close, 1);
        grid.Children.Add(close);

        var bar = new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
        return bar;
    }

    /// <summary>一行：左边群名称，右边可点击复制的群号</summary>
    private Grid CreateGroupRow(string name, string number)
    {
        var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var nameText = EditorTheme.Text_(name, 14);
        nameText.TextTrimming = TextTrimming.CharacterEllipsis;
        nameText.ToolTip = name;
        row.Children.Add(nameText);

        var numberBox = new Border
        {
            Background = EditorTheme.Bg3,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(12, 0, 0, 0),
            Cursor = Cursors.Hand,
            ToolTip = Loc.T("Assist.Recommend.CopyTooltip"),
            Child = new TextBlock { Text = number, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = EditorTheme.Text }
        };
        numberBox.MouseEnter += (_, _) => numberBox.Background = EditorTheme.Hover;
        numberBox.MouseLeave += (_, _) => numberBox.Background = EditorTheme.Bg3;
        numberBox.MouseLeftButtonUp += (_, _) => CopyNumber(numberBox, number);
        Grid.SetColumn(numberBox, 1);
        row.Children.Add(numberBox);
        return row;
    }

    /// <summary>复制群号；成功时群号块闪一下成功色（200 ms），底部提示已复制</summary>
    private void CopyNumber(Border numberBox, string number)
    {
        try
        {
            Clipboard.SetText(number);
        }
        catch (Exception ex)
        {
            // 剪贴板被别的程序占用时 SetText 会抛 COMException
            Debug.WriteLine($"[Recommendation] 复制失败: {ex.Message}");
            MessageBox.Show(this, Loc.F("Assist.Recommend.CopyFailed", ex.Message), Loc.T("Common.Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _statusText.Text = Loc.F("Assist.Recommend.Copied", number);
        numberBox.Background = EditorTheme.Success;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            numberBox.Background = numberBox.IsMouseOver ? EditorTheme.Hover : EditorTheme.Bg3;
        };
        timer.Start();
    }
}
