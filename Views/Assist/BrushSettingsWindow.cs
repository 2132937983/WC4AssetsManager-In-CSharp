using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Views.Assist;

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
    private int _brushSize = 0;
    private int _selectedTerrainType = 0;
    private int _selectedTerrainVariant = 0;
    private int _editLayer = 1;
    private bool _maskIncludeMode = true;
    private string _brushShape = "圆形";
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

    private readonly SolidColorBrush _textBrush = Brushes.White;
    private readonly SolidColorBrush _borderBrush = new(Color.FromArgb(100, 255, 255, 255));
    private readonly Color _themeColor = Color.FromRgb(0x2D, 0x2D, 0x30);

    public event EventHandler<BrushSettingsEventArgs>? BrushSettingsChanged;

    public BrushSettingsWindow()
    {
        Title = "画笔设置";
        Width = 300;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        Opacity = 0;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        FontFamily = new FontFamily("Microsoft YaHei");

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
            Visibility = Visibility.Collapsed;
        };
        sb.Begin();
    }

    public new void Show()
    {
        _isExplicitlyShown = true;
        if (Visibility != Visibility.Visible)
            Visibility = Visibility.Visible;
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
            Background = new SolidColorBrush(Color.FromArgb(230, _themeColor.R, _themeColor.G, _themeColor.B)),
            CornerRadius = new CornerRadius(10),
            BorderBrush = _borderBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(10)
        };

        var mainStack = new StackPanel();

        var titleBar = new Border
        {
            Height = 30,
            Background = new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)),
            CornerRadius = new CornerRadius(10, 10, 0, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "画笔设置 (按住拖动)",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
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

        var sizeLabel = new TextBlock
        {
            Text = "画笔大小",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = _textBrush,
            Margin = new Thickness(0, 0, 0, 4)
        };
        leftPanel.Children.Add(sizeLabel);

        _sizeInput = new TextBox
        {
            Text = "0",
            Width = 60,
            FontSize = 12,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(50, 50, 54)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80))
        };
        _sizeInput.TextChanged += SizeInput_TextChanged;
        _sizeInput.LostFocus += SizeInput_LostFocus;
        leftPanel.Children.Add(_sizeInput);

        var rightPanel = new StackPanel();

        var shapeLabel = new TextBlock
        {
            Text = "画笔形状",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = _textBrush,
            Margin = new Thickness(0, 0, 0, 4)
        };
        rightPanel.Children.Add(shapeLabel);

        _shapeCombo = new ComboBox
        {
            Width = 80,
            FontSize = 12,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(50, 50, 54)),
            SelectedIndex = 0
        };
        _shapeCombo.Items.Add("圆形");
        _shapeCombo.Items.Add("方形");
        _shapeCombo.SelectionChanged += ShapeCombo_SelectionChanged;
        rightPanel.Children.Add(_shapeCombo);

        rowPanel.Children.Add(leftPanel);
        rowPanel.Children.Add(rightPanel);
        parent.Children.Add(rowPanel);
    }

    private void CreateTerrainAndVariantSection(StackPanel parent)
    {
        var titleLabel = new TextBlock
        {
            Text = "地形类型和变体",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = _textBrush,
            Margin = new Thickness(0, 0, 0, 4)
        };
        parent.Children.Add(titleLabel);

        var horizontalPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var leftPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 5, 0)
        };

        var terrainLabel = new TextBlock
        {
            Text = "地形类型",
            FontSize = 11,
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 0, 0, 2)
        };
        leftPanel.Children.Add(terrainLabel);

        _terrainList = new ListBox
        {
            Width = 110,
            Height = 125,
            FontSize = 11,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(50, 50, 54)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80))
        };
        _terrainList.SelectionChanged += TerrainList_SelectionChanged;
        leftPanel.Children.Add(_terrainList);

        var rightPanel = new StackPanel();

        var variantLabel = new TextBlock
        {
            Text = "地形变体",
            FontSize = 11,
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 0, 0, 2)
        };
        rightPanel.Children.Add(variantLabel);

        _variantList = new ListBox
        {
            Width = 110,
            Height = 125,
            FontSize = 11,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(50, 50, 54)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80))
        };
        _variantList.SelectionChanged += VariantList_SelectionChanged;
        rightPanel.Children.Add(_variantList);

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

        var titleLabel = new TextBlock
        {
            Text = "蒙版地形",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = _textBrush
        };
        headerPanel.Children.Add(titleLabel);

        _maskModeCheckBox = new CheckBox
        {
            Content = "只绘制选中地形",
            FontSize = 11,
            Foreground = Brushes.White,
            IsChecked = true,
            Margin = new Thickness(10, 0, 0, 0)
        };
        _maskModeCheckBox.Checked += MaskModeCheckBox_Changed;
        _maskModeCheckBox.Unchecked += MaskModeCheckBox_Changed;
        headerPanel.Children.Add(_maskModeCheckBox);

        parent.Children.Add(headerPanel);

        var descLabel = new TextBlock
        {
            Text = "勾选：只在选中地形上绘制 | 未勾选：跳过选中地形",
            FontSize = 9,
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 0, 0, 4),
            TextWrapping = TextWrapping.Wrap
        };
        parent.Children.Add(descLabel);

        _maskTerrainList = new ListBox
        {
            Height = 100,
            FontSize = 11,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(50, 50, 54)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80))
        };
        _maskTerrainList.SelectionChanged += MaskTerrainList_SelectionChanged;
        parent.Children.Add(_maskTerrainList);
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
            _variantList.Items.Add($"变体 {i}");

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
        _brushShape = _shapeCombo.SelectedIndex == 1 ? "方形" : "圆形";
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
            if (value == "圆形" || value == "方形")
            {
                _brushShape = value;
                if (_shapeCombo != null)
                    _shapeCombo.SelectedIndex = value == "方形" ? 1 : 0;
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