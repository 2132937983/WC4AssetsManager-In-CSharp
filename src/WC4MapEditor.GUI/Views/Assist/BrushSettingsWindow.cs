using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.GUI.Views.Assist;

public sealed class BrushSettingsEventArgs : EventArgs
{
    public int BrushSize { get; }
    public int TerrainType { get; }
    public int TerrainVariant { get; }
    public int EditLayer { get; }
    public string BrushShape { get; }

    public BrushSettingsEventArgs(int brushSize, int terrainType, int terrainVariant, int editLayer, string brushShape)
    {
        BrushSize = brushSize;
        TerrainType = terrainType;
        TerrainVariant = terrainVariant;
        EditLayer = editLayer;
        BrushShape = brushShape;
    }
}

public sealed class BrushSettingsWindow : Window
{
    // "圆形" / "方形" 是 Core（BrushEngine / TerrainModifier）与 Rendering 比较用的标识值，不是界面文字，不能翻译
    private const string ShapeCircle = "圆形";
    private const string ShapeSquare = "方形";

    private int _brushSize = 0;
    private int _selectedTerrainType = 0;
    private int _selectedTerrainVariant = 0;
    private int _editLayer = 1;
    private bool _maskIncludeMode = true;
    private string _brushShape = ShapeCircle;
    private readonly HashSet<int> _maskedTerrainIds = [];
    private bool _isClosing;
    private bool _isExplicitlyShown;
    private bool _isTerrainMode = true;

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
            Text = "0",
            Width = 60
        };
        _sizeInput.TextChanged += SizeInput_TextChanged;
        _sizeInput.LostFocus += SizeInput_LostFocus;
        leftPanel.Children.Add(_sizeInput);

        var rightPanel = new StackPanel();

        rightPanel.Children.Add(SectionTitle(Loc.T("Assist.Brush.Shape"), first: true));

        // 下拉框只负责显示文字；形状值按 SelectedIndex 映射到 ShapeCircle / ShapeSquare
        _shapeCombo = EditorTheme.ComboBox(100);
        _shapeCombo.Items.Add(Loc.T("Assist.Brush.ShapeCircle"));
        _shapeCombo.Items.Add(Loc.T("Assist.Brush.ShapeSquare"));
        _shapeCombo.SelectedIndex = 0;
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
            IsChecked = true,
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

        _maskTerrainList = new ListBox
        {
            Height = 100,
            FontSize = 11
        };
        _maskTerrainList.SelectionChanged += MaskTerrainList_SelectionChanged;
        parent.Children.Add(Boxed(_maskTerrainList));
    }

    public void InitializeTerrainList()
    {
        if (_terrainList == null) return;

        _terrainList.Items.Clear();

        var terrainTypes = ConfigManager.Instance.GetTerrainTypes();
        foreach (var terrain in terrainTypes)
        {
            _terrainList.Items.Add($"{terrain.Key}: {terrain.Value}");
            if (terrain.Key == _selectedTerrainType)
                _terrainList.SelectedIndex = _terrainList.Items.Count - 1;
        }
    }

    public void UpdateVariantList()
    {
        if (_variantList == null) return;

        _variantList.Items.Clear();

        int variantCount = ConfigManager.Instance.GetTerrainVariantCount(_selectedTerrainType);
        for (int i = 0; i < variantCount; i++)
            _variantList.Items.Add(Loc.F("Assist.Brush.VariantItem", i));

        if (_selectedTerrainVariant < _variantList.Items.Count)
            _variantList.SelectedIndex = _selectedTerrainVariant;
        else if (_variantList.Items.Count > 0)
        {
            _variantList.SelectedIndex = 0;
            _selectedTerrainVariant = 0;
        }
    }

    public void InitializeMaskTerrainList()
    {
        if (_maskTerrainList == null) return;

        _maskTerrainList.Items.Clear();

        var terrainTypes = ConfigManager.Instance.GetTerrainTypes();
        foreach (var terrain in terrainTypes)
            _maskTerrainList.Items.Add($"{terrain.Key}: {terrain.Value}");
    }

    private void SizeInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_sizeInput == null) return;
        if (int.TryParse(_sizeInput.Text, out int size) && size >= 0)
        {
            _brushSize = size;
            RaiseSettingsChanged();
        }
    }

    private void SizeInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_sizeInput == null) return;
        if (!int.TryParse(_sizeInput.Text, out int size) || size < 0)
            _sizeInput.Text = _brushSize.ToString();
    }

    private void ShapeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shapeCombo == null) return;
        _brushShape = _shapeCombo.SelectedIndex == 1 ? ShapeSquare : ShapeCircle;
        RaiseSettingsChanged();
    }

    private void TerrainList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_terrainList == null || _terrainList.SelectedIndex < 0) return;

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
        if (_variantList == null || _variantList.SelectedIndex < 0) return;
        _selectedTerrainVariant = _variantList.SelectedIndex;
        RaiseSettingsChanged();
    }

    private void MaskModeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_maskModeCheckBox == null) return;
        _maskIncludeMode = _maskModeCheckBox.IsChecked == true;
        RaiseSettingsChanged();
    }

    private void MaskTerrainList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_maskTerrainList == null) return;
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
        BrushSettingsChanged?.Invoke(this, new BrushSettingsEventArgs(_brushSize, _selectedTerrainType, _selectedTerrainVariant, _editLayer, _brushShape));
    }

    public int BrushSize
    {
        get => _brushSize;
        set
        {
            _brushSize = Math.Max(0, value);
            if (_sizeInput != null)
                _sizeInput.Text = _brushSize.ToString();
        }
    }

    public int SelectedTerrainType
    {
        get => _selectedTerrainType;
        set => _selectedTerrainType = value;
    }

    public int SelectedTerrainVariant
    {
        get => _selectedTerrainVariant;
        set => _selectedTerrainVariant = value;
    }

    public int EditLayer
    {
        get => _editLayer;
        set => _editLayer = value;
    }

    public string BrushShape
    {
        get => _brushShape;
        set
        {
            if (value == ShapeCircle || value == ShapeSquare)
            {
                _brushShape = value;
                if (_shapeCombo != null)
                    _shapeCombo.SelectedIndex = value == ShapeSquare ? 1 : 0;
            }
        }
    }

    public bool IsMaskEnabled => _maskedTerrainIds.Count > 0;
    public bool MaskIncludeMode => _maskIncludeMode;
    public HashSet<int> MaskedTerrainIds => _maskedTerrainIds;

    public bool IsTerrainMode
    {
        get => _isTerrainMode;
        set => _isTerrainMode = value;
    }
}
