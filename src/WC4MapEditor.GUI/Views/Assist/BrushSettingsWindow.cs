using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WC4MapEditor.Core.Brush;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.GUI.Views.Assist;

public sealed class BrushSettingsEventArgs : EventArgs
{
    public int BrushSize { get; }
    public int TerrainType { get; }
    public int TerrainVariant { get; }
    public string BrushShape { get; }

    public BrushSettingsEventArgs(int brushSize, int terrainType, int terrainVariant, string brushShape)
    {
        BrushSize = brushSize;
        TerrainType = terrainType;
        TerrainVariant = terrainVariant;
        BrushShape = brushShape;
    }
}

/// <summary>
/// 画笔设置窗口（地形 / 省份 / 归属 / 单位画笔共用）：大小、形状、地形与变体（仅地形画笔）、蒙版地形。
/// <para>
/// 只有用户在窗口里的操作才发出 <see cref="BrushSettingsChanged"/>；代码设置属性（打开窗口时把画笔当前值灌进来、
/// [ ] 键改大小后同步显示）不发事件。以前初始化列表时的选中事件会把画笔重置成大小 0 / 地形 0 / 变体 0。
/// </para>
/// </summary>
public sealed class BrushSettingsWindow : Window
{
    private int _brushSize;
    private int _selectedTerrainType;
    private int _selectedTerrainVariant;
    private bool _maskIncludeMode = true;
    private string _brushShape = BrushEngine.ShapeCircle;
    private readonly HashSet<int> _maskedTerrainIds = [];
    private bool _isClosing;
    private bool _isExplicitlyShown;
    private bool _isTerrainMode = true;
    private int _suppressEvents;

    private TextBox? _sizeInput;
    private ListBox? _terrainList;
    private ListBox? _variantList;
    private ListBox? _maskTerrainList;
    private CheckBox? _maskModeCheckBox;
    private ComboBox? _shapeCombo;

    public event EventHandler<BrushSettingsEventArgs>? BrushSettingsChanged;

    public BrushSettingsWindow()
    {
        EditorTheme.Apply(this);
        Title = Loc.T("Assist.Brush.Title");
        Width = 300;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        // 无边框圆角卡片 + 投影需要窗口本身透明；主题底色放在内部卡片 Border 上
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;

        Content = CreateWindowContent();
        Loaded += (_, _) => PlayFadeInAnimation();
        Closing += (s, e) =>
        {
            e.Cancel = true;
            if (_isExplicitlyShown)
                PlayFadeOutAndHide();
        };
    }

    private void PlayFadeInAnimation()
    {
        var sb = new Storyboard();
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(fadeIn, this);
        Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
        sb.Children.Add(fadeIn);
        sb.Begin();
    }

    public void PlayFadeOutAndHide()
    {
        if (_isClosing) return;
        _isClosing = true;
        _isExplicitlyShown = false;

        var sb = new Storyboard();
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        Storyboard.SetTarget(fadeOut, this);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));
        sb.Children.Add(fadeOut);
        sb.Completed += (_, _) =>
        {
            _isClosing = false;
            // 淡出过程中若又被重新显示，就不要再把它收起来
            if (!_isExplicitlyShown)
                Visibility = Visibility.Collapsed;
        };
        sb.Begin();
    }

    public new void Show()
    {
        _isExplicitlyShown = true;
        if (Visibility != Visibility.Visible)
            Visibility = Visibility.Visible;

        // 本窗口是复用实例（关闭时只是隐藏），Loaded 只在首次显示时触发，
        // 因此每次显示都要主动停掉可能仍在跑的淡出动画并重新淡入。
        // 否则窗口会停留在上一次淡出后的 Opacity=0 状态：
        // 表现为第一次能打开、第二次能关闭，之后按 H 键就再也看不到窗口。
        BeginAnimation(OpacityProperty, null);
        _isClosing = false;
        Opacity = 0;
        PlayFadeInAnimation();

        base.Show();
    }

    private FrameworkElement CreateWindowContent()
    {
        var outerGrid = new Grid();

        var mainBorder = new Border
        {
            Width = 280,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Background = EditorTheme.Bg2,
            CornerRadius = new CornerRadius(8),
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(10),
            Effect = new DropShadowEffect
            {
                Color = EditorTheme.ColorOf(EditorTheme.Bg0),
                Direction = 270,
                ShadowDepth = 4,
                BlurRadius = 16,
                Opacity = 0.5
            }
        };

        var mainStack = new StackPanel();

        var titleText = EditorTheme.Muted(Loc.T("Assist.Brush.DragTitle"));
        titleText.HorizontalAlignment = HorizontalAlignment.Center;
        titleText.VerticalAlignment = VerticalAlignment.Center;

        var titleBar = new Border
        {
            Height = 30,
            Background = EditorTheme.Bg3,
            CornerRadius = new CornerRadius(8, 8, 0, 0),
            Cursor = Cursors.Hand,
            Child = titleText
        };
        titleBar.MouseLeftButtonDown += TitleBar_MouseLeftButtonDown;

        mainStack.Children.Add(titleBar);

        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
            MaxHeight = 470
        };

        var contentPanel = new StackPanel
        {
            Margin = new Thickness(15)
        };

        _terrainList = null;
        _variantList = null;
        CreateSizeAndShapeSection(contentPanel);
        if (_isTerrainMode)
        {
            CreateTerrainAndVariantSection(contentPanel);
        }
        CreateMaskTerrainSection(contentPanel);

        scrollViewer.Content = contentPanel;
        mainStack.Children.Add(scrollViewer);
        mainBorder.Child = mainStack;
        outerGrid.Children.Add(mainBorder);

        return outerGrid;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    /// <summary>分组标题：主题的 Section 样式自带上边距，面板里第一组不需要。</summary>
    private static TextBlock SectionTitle(string text, bool first = false)
    {
        var tb = EditorTheme.Section(text);
        tb.Margin = first ? new Thickness(0, 0, 0, 4) : new Thickness(0, 6, 0, 4);
        return tb;
    }

    /// <summary>主题里的 ListBox 本身无底无框，用一层面板画出列表区域。</summary>
    private static Border Boxed(ListBox list)
    {
        var host = EditorTheme.Panel(new Thickness(1));
        host.CornerRadius = new CornerRadius(4);
        host.Child = list;
        return host;
    }

    private void CreateSizeAndShapeSection(StackPanel parent)
    {
        var rowPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var leftPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 15, 0)
        };

        leftPanel.Children.Add(SectionTitle(Loc.T("Assist.Brush.Size"), first: true));

        _sizeInput = new TextBox
        {
            Text = _brushSize.ToString(),
            Width = 60,
            ToolTip = Loc.F("Assist.Brush.SizeRange", 0, BrushEngine.MaxRadius)
        };
        _sizeInput.TextChanged += SizeInput_TextChanged;
        _sizeInput.LostFocus += SizeInput_LostFocus;
        leftPanel.Children.Add(_sizeInput);

        var rightPanel = new StackPanel();

        rightPanel.Children.Add(SectionTitle(Loc.T("Assist.Brush.Shape"), first: true));

        // 下拉框只负责显示文字；形状值按 SelectedIndex 映射到 BrushEngine.ShapeCircle / ShapeSquare
        _shapeCombo = EditorTheme.ComboBox(100);
        _shapeCombo.Items.Add(Loc.T("Assist.Brush.ShapeCircle"));
        _shapeCombo.Items.Add(Loc.T("Assist.Brush.ShapeSquare"));
        _shapeCombo.SelectedIndex = _brushShape == BrushEngine.ShapeSquare ? 1 : 0;
        _shapeCombo.SelectionChanged += ShapeCombo_SelectionChanged;
        rightPanel.Children.Add(_shapeCombo);

        rowPanel.Children.Add(leftPanel);
        rowPanel.Children.Add(rightPanel);
        parent.Children.Add(rowPanel);
    }

    private void CreateTerrainAndVariantSection(StackPanel parent)
    {
        parent.Children.Add(SectionTitle(Loc.T("Assist.Brush.TerrainSection")));

        var horizontalPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var leftPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 5, 0)
        };

        var terrainLabel = EditorTheme.Muted(Loc.T("Assist.Brush.TerrainType"));
        terrainLabel.FontSize = 11;
        terrainLabel.Margin = new Thickness(0, 0, 0, 2);
        leftPanel.Children.Add(terrainLabel);

        _terrainList = new ListBox
        {
            Width = 110,
            Height = 125,
            FontSize = 11
        };
        _terrainList.SelectionChanged += TerrainList_SelectionChanged;
        leftPanel.Children.Add(Boxed(_terrainList));

        var rightPanel = new StackPanel();

        var variantLabel = EditorTheme.Muted(Loc.T("Assist.Brush.TerrainVariant"));
        variantLabel.FontSize = 11;
        variantLabel.Margin = new Thickness(0, 0, 0, 2);
        rightPanel.Children.Add(variantLabel);

        _variantList = new ListBox
        {
            Width = 110,
            Height = 125,
            FontSize = 11
        };
        _variantList.SelectionChanged += VariantList_SelectionChanged;
        rightPanel.Children.Add(Boxed(_variantList));

        horizontalPanel.Children.Add(leftPanel);
        horizontalPanel.Children.Add(rightPanel);
        parent.Children.Add(horizontalPanel);
    }

    private void CreateMaskTerrainSection(StackPanel parent)
    {
        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var titleLabel = SectionTitle(Loc.T("Assist.Brush.MaskSection"));
        titleLabel.Margin = new Thickness(0, 6, 0, 0);
        titleLabel.VerticalAlignment = VerticalAlignment.Center;
        headerPanel.Children.Add(titleLabel);

        _maskModeCheckBox = new CheckBox
        {
            Content = Loc.T("Assist.Brush.MaskOnlySelected"),
            FontSize = 11,
            IsChecked = _maskIncludeMode,
            Margin = new Thickness(10, 6, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _maskModeCheckBox.Checked += MaskModeCheckBox_Changed;
        _maskModeCheckBox.Unchecked += MaskModeCheckBox_Changed;
        headerPanel.Children.Add(_maskModeCheckBox);

        parent.Children.Add(headerPanel);

        var descLabel = EditorTheme.Muted(Loc.T("Assist.Brush.MaskHint"));
        descLabel.FontSize = 10;
        descLabel.Margin = new Thickness(0, 0, 0, 4);
        descLabel.TextWrapping = TextWrapping.Wrap;
        parent.Children.Add(descLabel);

        // 蒙版可以同时选多种地形（点击切换选中，对齐 VB 的 MultiSimple）
        _maskTerrainList = new ListBox
        {
            Height = 100,
            FontSize = 11,
            SelectionMode = SelectionMode.Multiple
        };
        _maskTerrainList.SelectionChanged += MaskTerrainList_SelectionChanged;
        parent.Children.Add(Boxed(_maskTerrainList));
    }

    /// <summary>
    /// 地形画笔：把画笔当前的地形与变体灌进窗口（填充并选中地形列表、刷新变体列表）。不发 <see cref="BrushSettingsChanged"/>。
    /// </summary>
    public void LoadTerrainBrush(int terrainType, int terrainVariant)
    {
        _selectedTerrainType = terrainType;
        _selectedTerrainVariant = terrainVariant;
        if (_terrainList == null) return;

        _suppressEvents++;
        try
        {
            _terrainList.Items.Clear();
            foreach (var terrain in ConfigManager.Instance.GetTerrainTypes())
            {
                _terrainList.Items.Add($"{terrain.Key}: {terrain.Value}");
                if (terrain.Key == _selectedTerrainType)
                    _terrainList.SelectedIndex = _terrainList.Items.Count - 1;
            }
            if (_terrainList.SelectedItem != null)
                _terrainList.ScrollIntoView(_terrainList.SelectedItem);
            UpdateVariantList();
        }
        finally
        {
            _suppressEvents--;
        }
    }

    private void UpdateVariantList()
    {
        if (_variantList == null) return;

        _suppressEvents++;
        try
        {
            _variantList.Items.Clear();

            int variantCount = ConfigManager.Instance.GetTerrainVariantCount(_selectedTerrainType);
            for (int i = 0; i < variantCount; i++)
                _variantList.Items.Add(Loc.F("Assist.Brush.VariantItem", i));

            if (_selectedTerrainVariant >= _variantList.Items.Count)
                _selectedTerrainVariant = 0;
            if (_variantList.Items.Count > 0)
                _variantList.SelectedIndex = _selectedTerrainVariant;
        }
        finally
        {
            _suppressEvents--;
        }
    }

    public void InitializeMaskTerrainList()
    {
        if (_maskTerrainList == null) return;

        _suppressEvents++;
        try
        {
            _maskTerrainList.Items.Clear();
            foreach (var terrain in ConfigManager.Instance.GetTerrainTypes())
            {
                int index = _maskTerrainList.Items.Add($"{terrain.Key}: {terrain.Value}");
                if (_maskedTerrainIds.Contains(terrain.Key))
                    _maskTerrainList.SelectedItems.Add(_maskTerrainList.Items[index]);
            }
        }
        finally
        {
            _suppressEvents--;
        }
    }

    private void SizeInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_sizeInput == null || _suppressEvents > 0) return;
        if (int.TryParse(_sizeInput.Text, out int size) && size >= 0)
        {
            // 超过上限按上限算，失去焦点时输入框改回实际值
            _brushSize = BrushEngine.ClampRadius(size);
            RaiseSettingsChanged();
        }
    }

    private void SizeInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_sizeInput == null) return;
        if (_sizeInput.Text != _brushSize.ToString())
            SetSizeText();
    }

    private void SetSizeText()
    {
        if (_sizeInput == null) return;
        _suppressEvents++;
        try { _sizeInput.Text = _brushSize.ToString(); }
        finally { _suppressEvents--; }
    }

    private void ShapeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shapeCombo == null || _suppressEvents > 0) return;
        _brushShape = _shapeCombo.SelectedIndex == 1 ? BrushEngine.ShapeSquare : BrushEngine.ShapeCircle;
        RaiseSettingsChanged();
    }

    private void TerrainList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_terrainList == null || _suppressEvents > 0 || _terrainList.SelectedIndex < 0) return;

        string selectedText = _terrainList.SelectedItem?.ToString() ?? "";
        int colonIndex = selectedText.IndexOf(':');
        if (colonIndex > 0 && int.TryParse(selectedText.AsSpan(0, colonIndex), out int terrainId))
        {
            _selectedTerrainType = terrainId;
            UpdateVariantList();
            RaiseSettingsChanged();
        }
    }

    private void VariantList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_variantList == null || _suppressEvents > 0 || _variantList.SelectedIndex < 0) return;
        _selectedTerrainVariant = _variantList.SelectedIndex;
        RaiseSettingsChanged();
    }

    private void MaskModeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_maskModeCheckBox == null) return;
        _maskIncludeMode = _maskModeCheckBox.IsChecked == true;
        if (_suppressEvents == 0) RaiseSettingsChanged();
    }

    private void MaskTerrainList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_maskTerrainList == null || _suppressEvents > 0) return;
        _maskedTerrainIds.Clear();
        foreach (var item in _maskTerrainList.SelectedItems)
        {
            string text = item?.ToString() ?? "";
            int colonIndex = text.IndexOf(':');
            if (colonIndex > 0 && int.TryParse(text.AsSpan(0, colonIndex), out int terrainId))
                _maskedTerrainIds.Add(terrainId);
        }
        RaiseSettingsChanged();
    }

    private void RaiseSettingsChanged()
    {
        BrushSettingsChanged?.Invoke(this, new BrushSettingsEventArgs(_brushSize, _selectedTerrainType, _selectedTerrainVariant, _brushShape));
    }

    /// <summary>画笔大小（0 ~ <see cref="BrushEngine.MaxRadius"/>）。代码设置时只刷新显示，不发事件。</summary>
    public int BrushSize
    {
        get => _brushSize;
        set
        {
            _brushSize = BrushEngine.ClampRadius(value);
            SetSizeText();
        }
    }

    /// <summary>画笔形状（<see cref="BrushEngine.ShapeCircle"/> / <see cref="BrushEngine.ShapeSquare"/>）。代码设置时不发事件。</summary>
    public string BrushShape
    {
        get => _brushShape;
        set
        {
            if (value != BrushEngine.ShapeCircle && value != BrushEngine.ShapeSquare) return;
            _brushShape = value;
            if (_shapeCombo == null) return;
            _suppressEvents++;
            try { _shapeCombo.SelectedIndex = value == BrushEngine.ShapeSquare ? 1 : 0; }
            finally { _suppressEvents--; }
        }
    }

    public bool IsMaskEnabled => _maskedTerrainIds.Count > 0;
    public bool MaskIncludeMode => _maskIncludeMode;
    public HashSet<int> MaskedTerrainIds => _maskedTerrainIds;

    /// <summary>
    /// 是否显示地形 / 变体列表（只有地形画笔用得上）。改动时重建窗口内容：调用方用对象初始化器设置，
    /// 那时构造函数已经按默认值建好了内容（以前设成 false 也照样显示地形列表）。
    /// </summary>
    public bool IsTerrainMode
    {
        get => _isTerrainMode;
        set
        {
            if (_isTerrainMode == value) return;
            _isTerrainMode = value;
            Content = CreateWindowContent();
        }
    }
}
