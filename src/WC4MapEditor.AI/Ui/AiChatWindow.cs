using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using WC4MapEditor.AI.Agent;
using WC4MapEditor.AI.Providers;
using WC4MapEditor.AI.Settings;

namespace WC4MapEditor.AI.Ui;

/// <summary>
/// AI 助手聊天窗口（非模态）。
/// 左侧对话（用户/AI 气泡），右侧工具调用日志（时间 + 工具名 + 参数 + 结果），
/// 顶部工具栏：新对话 / 设置 / 停止 / 压缩上下文。
/// 状态栏显示上下文 Token 估算 + 消息条数。
/// </summary>
public sealed class AiChatWindow : Window
{
    private readonly AiSettings _settings = AiSettings.Load();
    private readonly AgentLoop _agent;

    private ObservableCollection<ToolLogEntry> _toolLogs = new();

    private readonly List<string> _inputHistory = new();
    private int _historyIndex = -1;
    private string _draftText = "";

    private ScrollViewer _chatScroll = null!;
    private StackPanel _chatPanel = null!;
    private RichTextBox? _pendingRichText;      // 流式气泡的正文控件
    private Run? _pendingRun;                   // 流式期间只改这个 Run 的 Text，最省
    private string _pendingRawText = "";

    private ListBox _toolLogList = null!;
    private TextBox _inputBox = null!;
    private Button _sendButton = null!;
    private Button _stopButton = null!;
    private Button _compressButton = null!;
    private TextBlock _statusText = null!;
    private TextBlock _contextInfoText = null!;
    private FrameworkElement _settingsPanel = null!;
    private ComboBox _providerCombo = null!;
    private TextBox _baseUrlBox = null!;
    private TextBox _apiKeyBox = null!;
    private TextBox _modelBox = null!;
    private TextBox _temperatureBox = null!;
    private CheckBox _visionCheck = null!;
    private TextBlock _visionHint = null!;
    private CheckBox _streamCheck = null!;
    private CheckBox _fallbackCheck = null!;
    private ComboBox _effortCombo = null!;
    private Slider _reasoningHeightSlider = null!;
    private ComboBox _toolsModeCombo = null!;
    private ComboBox _reasoningModeCombo = null!;

    /// <summary>推理气泡的最大高度（可拖滑块调，记在设置里）</summary>
    private double _reasoningMaxHeight = 160;
    private Button _fetchModelsButton = null!;
    private TextBox _userAgentBox = null!;

    public AiChatWindow()
    {
        _agent = new AgentLoop(_settings)
        {
            OnAssistantText = t => AddBubble("assistant", t),
            OnTextDelta = t => AppendToPendingBubble(t),
            OnToolCall = (name, args) => { ClearPendingBubble(); AddToolLog(name, args); },
            OnToolResult = (name, output, ok) => MarkToolResult(output, ok),
            OnError = e => { ClearPendingBubble(); AddBubble("assistant", $"⚠ {e}"); SetStatus($"错误: {e}"); },
            OnStatus = s =>
            {
                SetStatus(s);
                // 模型拒绝过图片输入 → 同步把设置里的开关关掉，界面与实际行为保持一致
                if (_agent.VisionRejected && _visionCheck?.IsChecked == true)
                {
                    _visionCheck.IsChecked = false;
                    _settings.VisionEnabled = false;
                    UpdateVisionHint();
                }
            },
            OnReasoning = r => AddReasoningBubble(r),
            OnRound = msg => AddToolLog("轮次", msg),
        };

        _reasoningMaxHeight = _settings.ReasoningMaxHeight > 30 ? _settings.ReasoningMaxHeight : 160;

        Title = $"AI 助手 — {_settings.Model}";
        Width = 940;
        Height = 640;
        MinWidth = 720;
        MinHeight = 480;
        Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Content = BuildLayout();

        Loaded += (_, _) => _inputBox.Focus();
        Closed += (_, _) => SaveSettings();
    }

    // ============================================================= 布局 =============================================================
    private FrameworkElement BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(8) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(BuildToolBar());
        Grid.SetRow(_settingsPanel = BuildSettingsPanel(), 1);
        root.Children.Add(_settingsPanel);
        _settingsPanel.Visibility = Visibility.Collapsed;

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        Grid.SetRow(columns, 2);

        columns.Children.Add(BuildChatArea());

        var toolLog = BuildToolLogArea();
        Grid.SetColumn(toolLog, 2);
        columns.Children.Add(toolLog);

        var sep = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            Width = 1
        };
        Grid.SetColumn(sep, 1);
        columns.Children.Add(sep);
        root.Children.Add(columns);

        var statusBar = BuildStatusBar();
        Grid.SetRow(statusBar, 3);
        root.Children.Add(statusBar);

        var inputBar = BuildInputBar();
        Grid.SetRow(inputBar, 4);
        root.Children.Add(inputBar);

        return root;
    }

    private FrameworkElement BuildToolBar()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

        panel.Children.Add(MakeToolButton("新对话", (_, _) =>
        {
            _agent.Reset();
            _chatPanel.Children.Clear();
            _toolLogs.Clear();
            _pendingRichText = null;
            _pendingRun = null;
            _pendingRawText = "";
            UpdateContextInfo();
            SetStatus("已开始新对话");
        }));

        panel.Children.Add(MakeToolButton("设置", (_, _) =>
            _settingsPanel.Visibility = _settingsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible));

        _stopButton = MakeToolButton("停止", (_, _) => _agent.Stop());
        _stopButton.IsEnabled = false;
        panel.Children.Add(_stopButton);

        _compressButton = MakeToolButton("压缩上下文", async (_, _) => await CompressContextAsync());
        panel.Children.Add(_compressButton);

        panel.Children.Add(MakeToolButton("测试视觉", (_, _) => ProbeVision()));

        return panel;
    }

    private FrameworkElement BuildSettingsPanel()
    {
        _providerCombo = new ComboBox
        {
            Width = 140,
            Height = 24,
            Background = new SolidColorBrush(Color.FromRgb(70, 70, 73)),
            Foreground = Brushes.White
        };
        _providerCombo.Items.Add("openai");
        _providerCombo.Items.Add("claude");
        ApplyDarkComboBoxStyle(_providerCombo);

        _baseUrlBox = MakeDarkTextBox(_settings.BaseUrl, "https://api.deepseek.com 或 https://api.anthropic.com");
        _apiKeyBox = MakeDarkTextBox(_settings.ApiKey, "sk-...");
        _temperatureBox = MakeDarkTextBox(_settings.Temperature.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // 模型：TextBox 黑底白字（已验证有效），点「拉取模型」弹 Popup 选择
        _modelBox = MakeDarkTextBox(_settings.Model, "deepseek-chat / claude-sonnet-4-5 ...");

        _fetchModelsButton = MakeToolButton("拉取模型", async (_, _) => await FetchModelsAsync());
        _fetchModelsButton.Margin = new Thickness(0, 0, 8, 0);

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        void AddCell(int col, int row, FrameworkElement el)
        {
            Grid.SetColumn(el, col);
            Grid.SetRow(el, row);
            grid.Children.Add(el);
        }

        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddCell(0, 0, MakeLabel("协议:"));
        AddCell(1, 0, _providerCombo);
        AddCell(0, 1, MakeLabel("BaseURL:"));
        AddCell(1, 1, _baseUrlBox);
        AddCell(0, 2, MakeLabel("ApiKey:"));
        AddCell(1, 2, _apiKeyBox);
        AddCell(0, 3, MakeLabel("模型:"));
        AddCell(1, 3, _modelBox);
        Grid.SetColumn(_fetchModelsButton, 3);
        Grid.SetRow(_fetchModelsButton, 3);
        grid.Children.Add(_fetchModelsButton);
        AddCell(0, 4, MakeLabel("温度:"));
        AddCell(1, 4, _temperatureBox);

        // 视觉：模型不支持时开着它，带图的那一轮会整轮失败，所以要给个显式开关 + 提示
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _visionCheck = new CheckBox
        {
            Content = "允许发送图片（视觉）",
            IsChecked = _settings.VisionEnabled,
            Foreground = Brushes.White,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };
        _visionHint = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 6, 0, 0),
        };

        AddCell(0, 5, _visionCheck);
        AddCell(1, 5, _visionHint);

        // 流式输出开关：个别网关在流式下会吞掉 tool_calls（AI 只说"我去查"就没下文），关掉可绕开
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _streamCheck = new CheckBox
        {
            Content = "流式输出",
            IsChecked = _settings.StreamEnabled,
            Foreground = Brushes.White,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
            ToolTip = "边生成边显示。若遇到「AI 说要调用工具却没动静」，把它关掉改走一次性响应再试",
        };
        AddCell(0, 6, _streamCheck);

        _fallbackCheck = new CheckBox
        {
            Content = "文本协议回退（不会调工具的模型也能干活）",
            IsChecked = _settings.TextToolFallback,
            Foreground = Brushes.White,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 6, 0, 0),
            ToolTip = "模型不支持 function calling 时（推理模型大多如此），允许它把要执行的 Lua 写在 ```lua 代码块里，"
                    + "助手自动执行并把输出回灌 —— 否则它只会「推理+描述」，永远不会真正动手。",
        };
        AddCell(1, 6, _fallbackCheck);

        // 推理强度：只在非 default 时才写进请求，避免不认识的模型直接报 400
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _effortCombo = new ComboBox
        {
            Width = 140,
            Height = 24,
            Margin = new Thickness(0, 6, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(70, 70, 73)),
            Foreground = Brushes.White,
            ToolTip = "default = 不发送该参数（最安全）；\n"
                    + "low/medium/high：OpenAI 兼容协议发 reasoning_effort，Claude 发 thinking 预算。\n"
                    + "不认识该字段的模型/网关可能报 400，遇到就改回 default。",
        };
        _effortCombo.Items.Add("default");
        _effortCombo.Items.Add("low");
        _effortCombo.Items.Add("medium");
        _effortCombo.Items.Add("high");
        ApplyDarkComboBoxStyle(_effortCombo);
        _effortCombo.SelectedItem = _effortCombo.Items.Contains(_settings.ReasoningEffort)
            ? _settings.ReasoningEffort : "default";

        // 推理开关：明确"要 / 不要思考"，比只给强度直观
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _reasoningModeCombo = new ComboBox
        {
            Width = 240,
            Height = 24,
            Margin = new Thickness(0, 6, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(70, 70, 73)),
            Foreground = Brushes.White,
            ToolTip = "default = 不干预，按模型默认（最安全）。\n"
                    + "on = 要求思考；off = 要求不思考。\n"
                    + "发送形式：OpenAI 兼容协议发 enable_thinking，Claude 发 thinking.type=disabled/enabled。\n"
                    + "不认识的模型/网关可能对未知字段报 400，遇到就改回 default；\n"
                    + "deepseek-reasoner 这类模型没有关闭参数，只能换模型。",
        };
        _reasoningModeCombo.Items.Add("default");
        _reasoningModeCombo.Items.Add("on");
        _reasoningModeCombo.Items.Add("off");
        ApplyDarkComboBoxStyle(_reasoningModeCombo);
        _reasoningModeCombo.SelectedItem = _reasoningModeCombo.Items.Contains(_settings.ReasoningMode)
            ? _settings.ReasoningMode : "default";

        AddCell(0, 7, MakeLabel("推理:"));
        AddCell(1, 7, _reasoningModeCombo);

        AddCell(0, 8, MakeLabel("推理强度:"));
        AddCell(1, 8, _effortCombo);

        // 推理气泡高度：这里设的是"新气泡的默认高度"，气泡头上那个滑块可以再临时微调
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _reasoningHeightSlider = new Slider
        {
            Minimum = 40,
            Maximum = 400,
            Value = _reasoningMaxHeight,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
            ToolTip = "推理气泡的最大高度（超出的部分内部滚动）。觉得占地方就往左拉。",
        };
        _reasoningHeightSlider.ValueChanged += (_, e) =>
        {
            _reasoningMaxHeight = e.NewValue;
            _settings.ReasoningMaxHeight = e.NewValue;
        };

        AddCell(0, 9, MakeLabel("推理气泡高度:"));
        AddCell(1, 9, _reasoningHeightSlider);

        // 工具调用方式：模型"只会说要调用却不动手"时选 text —— 不发工具定义，它只能写代码块
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _toolsModeCombo = new ComboBox
        {
            Width = 240,
            Height = 24,
            Margin = new Thickness(0, 6, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(70, 70, 73)),
            Foreground = Brushes.White,
            ToolTip = "native = 正常发送工具定义，走原生 function calling（推荐）。\n"
                    + "text = 不发送工具定义，让模型把要执行的命令写进 ```cli 代码块、"
                    + "Lua 写进 ```lua 代码块，由助手代为执行。\n"
                    + "适合「只会说'我将调用某某'却从不真的调用」的模型（推理模型、路由型模型常见）。",
        };
        _toolsModeCombo.Items.Add("native");
        _toolsModeCombo.Items.Add("text");
        ApplyDarkComboBoxStyle(_toolsModeCombo);
        _toolsModeCombo.SelectedItem = _toolsModeCombo.Items.Contains(_settings.ToolsMode)
            ? _settings.ToolsMode : "native";

        AddCell(0, 10, MakeLabel("工具调用:"));
        AddCell(1, 10, _toolsModeCombo);

        // 客户端标识：.NET 默认不发 User-Agent，而有些网关靠它认客户端（认不出就 401「不支持的客户端」）
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _userAgentBox = MakeDarkTextBox(_settings.UserAgent,
            "留空 = 不发送。部分中转站按它辨客户端，例如 claude-cli/2.0.0 (external, cli)");
        _userAgentBox.ToolTip = "请求头里的 User-Agent。\n"
                              + "留空 = 完全不发送（.NET 的默认行为）。\n"
                              + "若拉模型或对话报 401「不支持的客户端」，多半是网关按这个头判客户端：\n"
                              + "填上网关要求的值再试，Claude Code 系中转常用 claude-cli/2.0.0 (external, cli)。";
        Grid.SetColumnSpan(_userAgentBox, 3);
        AddCell(0, 11, MakeLabel("客户端UA:"));
        AddCell(1, 11, _userAgentBox);

        _modelBox.TextChanged += (_, _) => UpdateVisionHint();
        UpdateVisionHint();

        _providerCombo.SelectedItem = string.Equals(_settings.Provider, "claude", StringComparison.OrdinalIgnoreCase)
            ? "claude" : "openai";

        var border = new Border
        {
            Child = grid,
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x42)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8)
        };
        return border;
    }

    private FrameworkElement BuildChatArea()
    {
        _chatPanel = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
        };

        _chatScroll = new ScrollViewer
        {
            Content = _chatPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            Padding = new Thickness(6),
        };
        _chatScroll.SizeChanged += (_, _) => RefreshBubbleMaxWidth();

        return _chatScroll;
    }

    private void RefreshBubbleMaxWidth()
    {
        double maxW = Math.Max(200, _chatScroll.ActualWidth - 24);
        foreach (var child in _chatPanel.Children)
        {
            if (child is StackPanel sp)
                sp.MaxWidth = maxW;
        }
    }

    private FrameworkElement CreateBubble(string role, string text)
    {
        bool isUser = role == "user";

        var container = new StackPanel
        {
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = Math.Max(200, _chatScroll.ActualWidth - 24),
            Margin = new Thickness(0, 8, 0, 0)
        };

        var label = new TextBlock
        {
            Text = isUser ? "你" : "AI",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)),
            Margin = new Thickness(isUser ? 0 : 2, 0, isUser ? 2 : 0, 2),
            HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left
        };
        container.Children.Add(label);

        var border = new Border
        {
            Background = isUser
                ? new SolidColorBrush(Color.FromRgb(0x2A, 0x4D, 0x69))
                : new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3E)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 10, 6),
        };

        // 气泡正文用只读 RichTextBox：WPF 的 TextBlock 没有选中能力，AI 的回答没法拖选、只能整条复制。
        var body = CreateSelectableText();
        if (isUser) SetPlainText(body, text);
        else SetMarkdown(body, text);

        border.Child = body;
        container.Children.Add(border);

        var menu = new ContextMenu { Style = MakeDarkContextMenuStyle() };
        var copyItem = new MenuItem
        {
            Header = "复制整条",
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30))
        };
        copyItem.Click += (_, _) =>
        {
            try
            {
                // 现从控件里取文本：流式气泡创建时闭包里的 text 还是空的，只有控件内容才准
                string current = GetDocumentText(body);
                if (current.Length > 0) Clipboard.SetText(current);
            }
            catch { }
        };
        menu.Items.Add(copyItem);
        border.ContextMenu = menu;

        return container;
    }

    /// <summary>
    /// 推理气泡：模型的思维链单独放一块。
    /// <para>
    /// 长思维链能把整个对话顶爆，所以这里**默认限高 + 内部滚动**，并在气泡头部给一个滑块随时调高度
    /// （拖动即生效，值记进设置），另有"收起/展开"。推理内容只显示、不回传给模型。
    /// </para>
    /// </summary>
    private void AddReasoningBubble(string reasoning)
    {
        if (string.IsNullOrWhiteSpace(reasoning)) return;

        Dispatcher.Invoke(() =>
        {
            var container = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = Math.Max(200, _chatScroll.ActualWidth - 24),
                Margin = new Thickness(0, 6, 0, 0),
            };

            var body = CreateSelectableText();
            body.FontSize = 11.5;
            body.Document = new FlowDocument(new Paragraph(new Run(reasoning)
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB6)),
                FontSize = 11.5,
            }))
            {
                PagePadding = new Thickness(0),
                LineHeight = 17,
            };

            var scroll = new ScrollViewer
            {
                Content = body,
                MaxHeight = _reasoningMaxHeight,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x4A)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 6, 10, 6),
                Child = scroll,
            };

            bool expanded = true;
            var toggle = new Button
            {
                Content = "收起",
                FontSize = 10,
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(6, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3E)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
            };
            toggle.Click += (_, _) =>
            {
                expanded = !expanded;
                scroll.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                toggle.Content = expanded ? "收起" : "展开";
            };

            var slider = new Slider
            {
                Minimum = 40,
                Maximum = 400,
                Value = _reasoningMaxHeight,
                Width = 96,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "拖动调整推理气泡的高度（觉得占地方就往左拉）",
            };
            slider.ValueChanged += (_, e) =>
            {
                _reasoningMaxHeight = e.NewValue;
                _settings.ReasoningMaxHeight = e.NewValue;   // 记进设置，下次开窗沿用
                scroll.MaxHeight = e.NewValue;
            };

            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 3) };
            header.Children.Add(new TextBlock
            {
                Text = "推理",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)),
                VerticalAlignment = VerticalAlignment.Center,
            });
            header.Children.Add(slider);
            header.Children.Add(toggle);

            container.Children.Add(header);
            container.Children.Add(border);

            _chatPanel.Children.Add(container);
            ScrollChatToEnd();
        });
    }

    /// <summary>
    /// 气泡正文：只读 <see cref="RichTextBox"/>。
    /// <para>
    /// 为什么不用 TextBlock：WPF 的 TextBlock <b>不支持选中文本</b>，AI 的回答只能靠右键整条复制。
    /// 只读 RichTextBox 依然支持鼠标拖选与 Ctrl+C，又不会被真的编辑，正是聊天记录需要的行为。
    /// </para>
    /// </summary>
    private static RichTextBox CreateSelectableText()
    {
        return new RichTextBox
        {
            IsReadOnly = true,
            IsReadOnlyCaretVisible = false,     // 只读时不显示插入符
            IsDocumentEnabled = true,           // 让代码块这类内嵌 UI 正常显示
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = Brushes.White,
            FontSize = 12.5,
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            IsTabStop = false,                  // Tab 不落进气泡
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,   // 高度自适应内容，不出滚动条
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            SelectionBrush = new SolidColorBrush(Color.FromRgb(0x46, 0x6E, 0xA0)),
            SelectionOpacity = 0.7,
        };
    }

    private static FlowDocument MakeBubbleDocument(Paragraph content)
        => new(content)
        {
            PagePadding = new Thickness(0),
            LineHeight = 20,
            Foreground = Brushes.White,
        };

    /// <summary>用单个 Run 填纯文本，返回该 Run —— 流式刷新直接改它的 Text，不必重建文档。</summary>
    private static Run SetPlainText(RichTextBox box, string text)
    {
        var run = new Run(text) { Foreground = Brushes.White };
        box.Document = MakeBubbleDocument(new Paragraph(run) { Margin = new Thickness(0) });
        return run;
    }

    /// <summary>按 Markdown 渲染进气泡（**粗体**、`行内代码`、```代码块```、- 列表、换行）</summary>
    private static void SetMarkdown(RichTextBox box, string markdown)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        RenderMarkdownInlines(paragraph.Inlines, markdown);
        box.Document = MakeBubbleDocument(paragraph);
    }

    private static string GetDocumentText(RichTextBox box)
    {
        var range = new TextRange(box.Document.ContentStart, box.Document.ContentEnd);
        return range.Text.TrimEnd('\r', '\n');
    }

    private void ScrollChatToEnd()
    {
        _chatScroll.Dispatcher.BeginInvoke(new Action(() =>
        {
            _chatScroll.ScrollToEnd();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private FrameworkElement BuildToolLogArea()
    {
        _toolLogList = new ListBox
        {
            ItemsSource = _toolLogs,
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        _toolLogList.ItemTemplate = MakeToolLogTemplate();

        var header = new TextBlock
        {
            Text = "工具调用日志",
            Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)),
            FontSize = 11,
            Margin = new Thickness(6, 2, 0, 4)
        };

        var scroll = new ScrollViewer
        {
            Content = _toolLogList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(scroll);
        return panel;
    }

    private DataTemplate MakeToolLogTemplate()
    {
        var factory = new FrameworkElementFactory(typeof(TextBox), "tb");
        factory.SetValue(TextBox.IsReadOnlyProperty, true);
        factory.SetValue(TextBox.TextWrappingProperty, TextWrapping.Wrap);
        factory.SetValue(TextBox.BorderThicknessProperty, new Thickness(0));
        factory.SetValue(TextBox.BackgroundProperty, Brushes.Transparent);
        factory.SetValue(TextBox.FontSizeProperty, 11.0);
        factory.SetValue(TextBox.PaddingProperty, new Thickness(0));
        factory.SetValue(TextBox.FocusableProperty, true);
        factory.SetValue(TextBox.IsTabStopProperty, false);
        factory.SetValue(TextBox.CursorProperty, Cursors.IBeam);
        factory.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("Display") { Mode = BindingMode.OneWay });
        factory.SetBinding(TextBox.ForegroundProperty, new System.Windows.Data.Binding("Foreground"));

        var borderFactory = new FrameworkElementFactory(typeof(Border), "bd");
        borderFactory.SetValue(Border.PaddingProperty, new Thickness(4, 2, 4, 2));
        borderFactory.AppendChild(factory);

        return new DataTemplate { VisualTree = borderFactory };
    }

    private FrameworkElement BuildStatusBar()
    {
        var panel = new DockPanel { Margin = new Thickness(2, 2, 0, 4) };

        _contextInfoText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0x6A, 0x9E, 0xD6)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(_contextInfoText, Dock.Right);
        panel.Children.Add(_contextInfoText);

        _statusText = new TextBlock
        {
            Text = "就绪（Enter 发送 · Shift+Enter 换行）",
            Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(_statusText);

        return panel;
    }

    private FrameworkElement BuildInputBar()
    {
        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _inputBox = new TextBox
        {
            MinHeight = 34,
            MaxHeight = 120,
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(6, 6, 6, 6),
            Background = new SolidColorBrush(Color.FromRgb(70, 70, 73)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
            BorderThickness = new Thickness(1),
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _inputBox.ToolTip = "Enter 发送　Shift+Enter 换行　消息气泡里的文字可以直接拖选复制";
        // 必须用 PreviewKeyDown（隧道）：AcceptsReturn = true 的 TextBox 会先在**自己的类处理器**里
        // 把 Enter 当成换行吃掉，挂在 KeyDown 上的处理器根本不会被执行 —— 表现就是"回车只换行、不发送"。
        // 规则：Enter 发送；Shift+Enter 换行（不拦，交给默认行为插入换行）。
        _inputBox.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;   // 兼容 IME / Alt 组合键

            if (key == Key.Enter)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) return;
                Send();
                e.Handled = true;
                return;
            }

            if (key == Key.Up && string.IsNullOrEmpty(_inputBox.Text))
            {
                NavigateHistory(-1);
                e.Handled = true;
            }
            else if (key == Key.Down && string.IsNullOrEmpty(_inputBox.Text))
            {
                NavigateHistory(1);
                e.Handled = true;
            }
        };
        panel.Children.Add(_inputBox);

        // 这个键是"发送/停止"二合一：Agent 跑着的时候它变成停止键（见 SetAgentRunning）
        _sendButton = MakeToolButton("发送", (_, _) =>
        {
            if (_agent.IsRunning) _agent.Stop();
            else Send();
        });
        _sendButton.Width = 64;
        _sendButton.Margin = new Thickness(6, 0, 0, 0);
        _sendButton.VerticalAlignment = VerticalAlignment.Bottom;
        _sendButton.ToolTip = "发送（Enter）。Agent 执行中会变成「停止」，再点一次即中止";
        Grid.SetColumn(_sendButton, 1);
        panel.Children.Add(_sendButton);

        return panel;
    }

    // ============================================================= 交互 =============================================================
    private async void Send()
    {
        string text = _inputBox.Text.Trim();
        if (text.Length == 0 || _agent.IsRunning) return;

        ApplySettingsFromUi();

        if (_inputHistory.Count == 0 || _inputHistory[^1] != text)
            _inputHistory.Add(text);
        if (_inputHistory.Count > 100) _inputHistory.RemoveAt(0);
        _historyIndex = -1;
        _draftText = "";

        _inputBox.Clear();
        AddBubble("user", text);

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            AddBubble("assistant", "请先点击顶部「设置」，填入 API Key 后再发送。");
            return;
        }

        SetStatus("思考中…");
        SetAgentRunning(true);

        string final = "";
        try
        {
            final = await _agent.RunUserTurnAsync(text);
        }
        finally
        {
            // 正常结束、出错、被停止 —— 三种情况都要把发送键恢复回来
            SetAgentRunning(false);
        }

        ClearPendingBubble();

        if (!string.IsNullOrEmpty(final) && final.StartsWith("（"))
            AddBubble("assistant", final);

        UpdateContextInfo();
        SetStatus(final.StartsWith("（已停止）") ? "已停止" : "完成");
    }

    private void AddBubble(string role, string text)
    {
        Dispatcher.Invoke(() =>
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _chatPanel.Children.Add(CreateBubble(role, text));
            ScrollChatToEnd();
        });
    }

    /// <summary>流式文本增量追加到 pending 气泡；无 pending 时自动创建。</summary>
    private void AppendToPendingBubble(string delta)
    {
        if (string.IsNullOrEmpty(delta)) return;
        Dispatcher.Invoke(() =>
        {
            if (_pendingRichText == null)
            {
                var bubble = CreateBubble("assistant", "");
                _chatPanel.Children.Add(bubble);

                if (bubble is StackPanel sp && sp.Children.Count >= 2 && sp.Children[1] is Border b && b.Child is RichTextBox rtb)
                {
                    _pendingRichText = rtb;
                    _pendingRun = SetPlainText(rtb, "");   // 流式期间走纯文本，结束后再按 Markdown 重排
                }
                _pendingRawText = "";
            }
            if (_pendingRun != null)
            {
                _pendingRawText += delta;
                _pendingRun.Text = _pendingRawText;
            }
            ScrollChatToEnd();
        });
    }

    /// <summary>关闭 pending 气泡：若为空则移除，否则用 Markdown 重新渲染。</summary>
    private void ClearPendingBubble()
    {
        Dispatcher.Invoke(() =>
        {
            if (_pendingRichText != null)
            {
                if (string.IsNullOrEmpty(_pendingRawText))
                {
                    if (_pendingRichText.Parent is Border b && b.Parent is StackPanel sp)
                        _chatPanel.Children.Remove(sp);
                }
                else
                {
                    SetMarkdown(_pendingRichText, _pendingRawText);
                }
            }
            _pendingRichText = null;
            _pendingRun = null;
            _pendingRawText = "";
        });
    }

    private void AddToolLog(string toolName, string argsJson)
    {
        Dispatcher.Invoke(() =>
        {
            var entry = new ToolLogEntry(toolName, argsJson, null);
            _toolLogs.Add(entry);
            if (_toolLogs.Count > 500) _toolLogs.RemoveAt(0);
            _toolLogList.ScrollIntoView(entry);
        });
    }

    /// <summary>OnToolResult 无法直接拿到刚创建的 entry，这里以结果文本补一条并着色。</summary>
    private void MarkToolResult(string output, bool ok)
    {
        Dispatcher.Invoke(() =>
        {
            var entry = new ToolLogEntry("→ 结果", output, ok ? new SolidColorBrush(Color.FromRgb(0x9C, 0xD3, 0x9C)) : new SolidColorBrush(Color.FromRgb(0xE5, 0x8C, 0x8C)));
            _toolLogs.Add(entry);
            if (_toolLogs.Count > 500) _toolLogs.RemoveAt(0);
            _toolLogList.ScrollIntoView(entry);
        });
    }

    private void SetStatus(string s) => Dispatcher.Invoke(() => _statusText.Text = s);

    /// <summary>
    /// 切换"Agent 正在执行"的界面状态：发送键 ↔ 停止键（同一个键）。
    /// <para>
    /// 以前是把发送键禁用、另用工具栏那个「停止」—— 可最顺手的键就在手边，禁用它会让人以为程序卡住了。
    /// 注意这里**不能**把发送键设为不可用，否则就没法用它来停止了。
    /// </para>
    /// </summary>
    private void SetAgentRunning(bool running)
    {
        Dispatcher.Invoke(() =>
        {
            _sendButton.Content = running ? "停止" : "发送";
            _sendButton.Background = new SolidColorBrush(running
                ? Color.FromRgb(0x8B, 0x3A, 0x3A)     // 停止态：暗红，一眼看出是"中止"
                : Color.FromRgb(0x45, 0x45, 0x48));    // 常态：与其它工具按钮一致
            _sendButton.IsEnabled = true;
            _sendButton.ToolTip = running
                ? "停止当前这一轮（在途请求会被取消）"
                : "发送（Enter）。Agent 执行中会变成「停止」";

            // 工具栏那个「停止」保留并保持同步（两处都能停）
            _stopButton.IsEnabled = running;
            _compressButton.IsEnabled = !running;
        });
    }

    /// <summary>
    /// 按当前模型名给一句视觉能力提示。
    /// 名字只是线索（走中转网关时名字千奇百怪），权威判断是"真发一张图试试"——见 <see cref="ProbeVision"/>。
    /// </summary>
    private void UpdateVisionHint()
    {
        if (_visionHint == null) return;

        string model = _modelBox?.Text?.Trim() ?? "";
        bool likely = AiSettings.LikelySupportsVision(model);

        _visionHint.Text = likely
            ? "模型名看起来支持视觉 → AI 可自己截图看图"
            : "模型名看起来是纯文本（或判不出）→ 开着它带图的请求会失败，可点「测试视觉」实测";
        _visionHint.Foreground = new SolidColorBrush(
            likely ? Color.FromRgb(0x6A, 0xC8, 0x8A) : Color.FromRgb(0xD6, 0xA0, 0x6A));
    }

    /// <summary>
    /// 实测视觉能力：让 AI 自己截一张图并描述它。
    /// 模型真有视觉就会报出图上的坐标标注范围；没有视觉的话那一轮会直接报错 —— 报错本身就是答案。
    /// </summary>
    private void ProbeVision()
    {
        if (_agent.IsRunning) { SetStatus("上一轮还在跑，稍等再测"); return; }
        if (_visionCheck?.IsChecked != true)
        {
            SetStatus("「允许发送图片（视觉）」没打开，先在设置里勾上再测");
            return;
        }
        if (!string.IsNullOrWhiteSpace(_inputBox.Text)) { SetStatus("输入框里还有内容，先清空再测"); return; }

        _inputBox.Text = "请调用 capture_screenshot（mode=region，中心取 (100,80)，40×30 格）截一张图，"
                       + "然后只回答一件事：图上的坐标标注范围是多少？如果你看不到图片，就直接说看不到。";
        Send();
    }

    private void UpdateContextInfo()
    {
        Dispatcher.Invoke(() =>
        {
            int tokens = _agent.EstimateTokenCount();
            int msgs = _agent.MessageCount;
            string tokenStr = tokens >= 1000 ? $"{tokens / 1000.0:F1}k" : tokens.ToString();
            _contextInfoText.Text = $"上下文: ~{tokenStr} tokens | {msgs} 条消息";
        });
    }

    private async Task CompressContextAsync()
    {
        if (_agent.IsRunning) return;
        if (_agent.MessageCount <= 4)
        {
            SetStatus("消息太少，无需压缩");
            return;
        }

        _compressButton.IsEnabled = false;
        SetStatus("正在压缩上下文…");

        try
        {
            var (oldTokens, newTokens) = await _agent.CompressHistoryAsync();
            if (oldTokens == 0)
            {
                SetStatus("无需压缩");
            }
            else
            {
                int saved = oldTokens - newTokens;
                SetStatus($"压缩完成：{oldTokens} → {newTokens} tokens（节省 {saved}）");

                AddBubble("assistant", $"[上下文已压缩] {oldTokens} → {newTokens} tokens（节省 {saved}）");
            }
            UpdateContextInfo();
        }
        catch (Exception ex)
        {
            SetStatus($"压缩失败: {ex.Message}");
        }
        finally
        {
            _compressButton.IsEnabled = true;
        }
    }

    // ============================================================= Markdown 渲染 =============================================================

    /// <summary>简易 Markdown → TextBlock Inlines 渲染：支持 **粗体**、`行内代码`、```代码块```、- 列表项、换行。</summary>
    private static void RenderMarkdownInlines(InlineCollection target, string markdown)
    {
        target.Clear();
        if (string.IsNullOrEmpty(markdown)) return;

        var codeBlockBg = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        var codeFg = new SolidColorBrush(Color.FromRgb(0xCE, 0x91, 0x6B));
        var monoFamily = new FontFamily("Consolas, Courier New");

        var lines = markdown.Split('\n');
        bool inCodeBlock = false;
        var codeBlockLines = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            if (line.TrimStart().StartsWith("```"))
            {
                if (inCodeBlock)
                {
                    inCodeBlock = false;
                    var codeBorder = new Border
                    {
                        Background = codeBlockBg,
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 4, 8, 4),
                        Margin = new Thickness(0, 4, 0, 4)
                    };
                    var codeText = new TextBlock
                    {
                        Text = string.Join("\n", codeBlockLines),
                        Foreground = codeFg,
                        FontFamily = monoFamily,
                        FontSize = 11.5,
                        TextWrapping = TextWrapping.Wrap
                    };
                    codeBorder.Child = codeText;

                    var inlineUI = new InlineUIContainer { Child = codeBorder };
                    target.Add(inlineUI);
                    target.Add(new LineBreak());
                    codeBlockLines.Clear();
                }
                else
                {
                    inCodeBlock = true;
                    codeBlockLines.Clear();
                }
                continue;
            }

            if (inCodeBlock)
            {
                codeBlockLines.Add(line);
                continue;
            }

            if (i > 0) target.Add(new LineBreak());

            if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* "))
            {
                int indent = line.Length - line.TrimStart().Length;
                if (indent > 0) target.Add(new Run(new string(' ', indent / 2)));
                target.Add(new Run("• ") { Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)) });
                RenderInlineMarkdown(target, line.TrimStart()[2..]);
            }
            else if (line.TrimStart().StartsWith("# "))
            {
                RenderInlineMarkdown(target, line.TrimStart()[2..], bold: true, fontSize: 14);
            }
            else if (line.TrimStart().StartsWith("## "))
            {
                RenderInlineMarkdown(target, line.TrimStart()[3..], bold: true, fontSize: 13);
            }
            else if (line.TrimStart().StartsWith("> "))
            {
                RenderInlineMarkdown(target, line.TrimStart()[2..], italic: true,
                    fg: new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)));
            }
            else
            {
                RenderInlineMarkdown(target, line);
            }
        }
    }

    private static void RenderInlineMarkdown(InlineCollection target, string text, bool bold = false,
        bool italic = false, Brush? fg = null, double fontSize = 12.5)
    {
        var monoFamily = new FontFamily("Consolas, Courier New");
        var codeBg = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30));
        var codeFg = new SolidColorBrush(Color.FromRgb(0xCE, 0x91, 0x6B));

        int pos = 0;
        while (pos < text.Length)
        {
            if (text[pos] == '`' && pos + 1 < text.Length)
            {
                int end = text.IndexOf('`', pos + 1);
                if (end > pos)
                {
                    string code = text[(pos + 1)..end];
                    var border = new Border
                    {
                        Background = codeBg,
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(4, 1, 4, 1),
                        Margin = new Thickness(1, 0, 1, 0)
                    };
                    var codeRun = new Run(code) { Foreground = codeFg, FontFamily = monoFamily, FontSize = 11.5 };
                    border.Child = new TextBlock { Inlines = { codeRun }, VerticalAlignment = VerticalAlignment.Center };
                    target.Add(new InlineUIContainer { Child = border });
                    pos = end + 1;
                    continue;
                }
            }

            if (text[pos] == '*' && pos + 1 < text.Length && text[pos + 1] == '*')
            {
                int end = text.IndexOf("**", pos + 2);
                if (end > pos)
                {
                    string boldText = text[(pos + 2)..end];
                    target.Add(new Run(boldText)
                    {
                        FontWeight = FontWeights.Bold,
                        Foreground = fg ?? Brushes.White,
                        FontSize = fontSize
                    });
                    pos = end + 2;
                    continue;
                }
            }

            int nextSpecial = text.Length;
            for (int k = pos; k < text.Length; k++)
            {
                if (text[k] == '`' || (text[k] == '*' && k + 1 < text.Length && text[k + 1] == '*'))
                {
                    nextSpecial = k;
                    break;
                }
            }

            string plain = text[pos..nextSpecial];
            target.Add(new Run(plain)
            {
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
                Foreground = fg ?? Brushes.White,
                FontSize = fontSize
            });
            pos = nextSpecial;
        }
    }

    private static Style MakeDarkContextMenuStyle()
    {
        var style = new Style(typeof(ContextMenu));
        style.Setters.Add(new Setter(ContextMenu.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30))));
        style.Setters.Add(new Setter(ContextMenu.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(ContextMenu.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5E))));
        style.Setters.Add(new Setter(ContextMenu.BorderThicknessProperty, new Thickness(1)));
        return style;
    }

    /// <summary>Up/Down 浏览历史输入：-1 为旧一条，+1 为新一条，越界回草稿。</summary>
    private void NavigateHistory(int direction)
    {
        if (_inputHistory.Count == 0) return;

        if (direction < 0)
        {
            if (_historyIndex == -1)
            {
                _draftText = _inputBox.Text;
                _historyIndex = _inputHistory.Count - 1;
            }
            else if (_historyIndex > 0)
            {
                _historyIndex--;
            }
        }
        else
        {
            if (_historyIndex == -1) return;
            if (_historyIndex < _inputHistory.Count - 1) _historyIndex++;
            else { _historyIndex = -1; _inputBox.Text = _draftText; _inputBox.CaretIndex = _inputBox.Text.Length; return; }
        }

        _inputBox.Text = _inputHistory[_historyIndex];
        _inputBox.CaretIndex = _inputBox.Text.Length;
    }

    private void ApplySettingsFromUi()
    {
        _settings.Provider = _providerCombo.SelectedItem as string ?? "openai";
        _settings.BaseUrl = _baseUrlBox.Text.Trim();
        _settings.ApiKey = _apiKeyBox.Text.Trim();
        _settings.UserAgent = _userAgentBox.Text.Trim();
        _settings.Model = _modelBox.Text.Trim();
        if (double.TryParse(_temperatureBox.Text, System.Globalization.CultureInfo.InvariantCulture, out var t))
            _settings.Temperature = Math.Clamp(t, 0, 2);
        _settings.VisionEnabled = _visionCheck.IsChecked == true;
        _settings.StreamEnabled = _streamCheck.IsChecked == true;
        _settings.TextToolFallback = _fallbackCheck.IsChecked == true;
        _settings.ReasoningEffort = _effortCombo.SelectedItem as string ?? "default";
        _settings.ReasoningMode = _reasoningModeCombo.SelectedItem as string ?? "default";
        _settings.ToolsMode = _toolsModeCombo.SelectedItem as string ?? "native";
        Title = $"AI 助手 — {_settings.Model}";
    }

    /// <summary>从当前配置的 API 拉取可用模型列表，弹 Popup 让用户选择回填。</summary>
    private async Task FetchModelsAsync()
    {
        ApplySettingsFromUi();

        if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            SetStatus("请先填写 API 地址");
            return;
        }

        _fetchModelsButton.IsEnabled = false;
        SetStatus("正在拉取模型列表…");

        try
        {
            IAiProvider provider = string.Equals(_settings.Provider, "claude", StringComparison.OrdinalIgnoreCase)
                ? new ClaudeProvider(_settings)
                : new OpenAiCompatibleProvider(_settings);

            var models = await provider.ListModelsAsync();
            if (models.Count == 0)
            {
                SetStatus("API 未返回任何模型");
                return;
            }

            ShowModelPickerPopup(models);
            SetStatus($"已拉取 {models.Count} 个模型，请在弹出列表中选择");
        }
        catch (Exception ex)
        {
            SetStatus("拉取模型失败（详情见下方气泡）");

            // 只在小状态栏里显示会一闪而过、也复制不出来 —— 贴成气泡，好照着排查。
            AddBubble("assistant",
                "⚠ 拉取模型列表失败：" + ex.Message + "\n\n"
                + "排查建议：\n"
                + "1) 401「不支持的客户端」几乎都出在中转网关：先在「设置 → 客户端UA」填上网关要求的标识"
                + "（Claude Code 系中转常用 claude-cli/2.0.0 (external, cli)）再试。\n"
                + "2) 有些网关压根没实现 GET /v1/models（只代理 chat/completions 或 /v1/messages），"
                + "这种情况怎么调都会失败 —— 直接在「模型」框手填模型名即可，不影响对话。\n"
                + "3) 换成官方地址（如 https://api.deepseek.com）试一次，能立刻区分是网关问题还是配置问题。");
        }
        finally
        {
            _fetchModelsButton.IsEnabled = true;
        }
    }

    /// <summary>弹出模型选择 Popup（ListBox 黑底白字），选中回填到模型输入框。</summary>
    private void ShowModelPickerPopup(List<string> models)
    {
        var listBox = new ListBox
        {
            MaxHeight = 320,
            MinWidth = 260,
            Background = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            ItemContainerStyle = MakeDarkListItemStyle()
        };
        foreach (var m in models) listBox.Items.Add(m);

        var popup = new Popup
        {
            PlacementTarget = _modelBox,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            Child = listBox
        };

        listBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) popup.IsOpen = false;
            if (e.Key == Key.Enter && listBox.SelectedItem is string s)
            {
                _modelBox.Text = s;
                popup.IsOpen = false;
            }
        };
        listBox.MouseDoubleClick += (_, _) =>
        {
            if (listBox.SelectedItem is string s)
            {
                _modelBox.Text = s;
                popup.IsOpen = false;
            }
        };

        popup.IsOpen = true;
        listBox.Focus();
    }

    /// <summary>黑底白字 ListBoxItem 样式（选中蓝高亮、悬停深灰）。</summary>
    private static Style MakeDarkListItemStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0, 0, 0))));
        style.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, Brushes.White));
        style.Setters.Add(new Setter(ListBoxItem.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(6, 3, 6, 3)));
        var sel = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        sel.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0, 122, 204))));
        style.Triggers.Add(sel);
        var hover = new Trigger { Property = ListBoxItem.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, new SolidColorBrush(Color.FromRgb(40, 40, 40))));
        style.Triggers.Add(hover);
        return style;
    }

    private void SaveSettings()
    {
        try
        {
            ApplySettingsFromUi();
            _settings.Save();
            Debug.WriteLine("[AiChatWindow] 设置已保存");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AiChatWindow] 保存设置失败: {ex.Message}");
        }
    }

    // ============================================================= 小部件工厂 =============================================================
    private static Button MakeToolButton(string text, RoutedEventHandler onClick)
    {
        var b = new Button
        {
            Content = text,
            Height = 26,
            Padding = new Thickness(10, 0, 10, 0),
            Margin = new Thickness(0, 0, 6, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x45, 0x45, 0x48)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5E)),
            BorderThickness = new Thickness(1),
            FontSize = 12
        };
        b.Click += onClick;
        return b;
    }

    private static TextBlock MakeLabel(string text)
        => new()
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 6, 2),
            Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8)),
            FontSize = 12
        };

    private static TextBox MakeDarkTextBox(string text, string? placeholder = null)
        => new()
        {
            Text = text,
            Height = 24,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 8, 2),
            Background = new SolidColorBrush(Color.FromRgb(70, 70, 73)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 1, 4, 1),
            FontSize = 12,
            ToolTip = placeholder
        };

    /// <summary>
    /// 把 ComboBox 改成黑底白字（Aero2 默认模板不读 Background，须覆盖 ItemContainerStyle / Popup ListBox / 编辑区 TextBox）。
    /// </summary>
    private static void ApplyDarkComboBoxStyle(ComboBox cb)
    {
        var black = new SolidColorBrush(Color.FromRgb(0, 0, 0));
        var hoverBg = new SolidColorBrush(Color.FromRgb(40, 40, 40));
        var selectedBg = new SolidColorBrush(Color.FromRgb(0, 122, 204));

        cb.Background = black;
        cb.Foreground = Brushes.White;
        cb.BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));
        cb.BorderThickness = new Thickness(1);

        // 下拉项
        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, black));
        itemStyle.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, Brushes.White));
        itemStyle.Setters.Add(new Setter(ListBoxItem.BorderThicknessProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(4, 2, 4, 2)));
        var selTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selTrigger.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, selectedBg));
        itemStyle.Triggers.Add(selTrigger);
        var hoverTrigger = new Trigger { Property = ListBoxItem.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, hoverBg));
        itemStyle.Triggers.Add(hoverTrigger);
        cb.ItemContainerStyle = itemStyle;

        // 下拉 Popup 里的 ListBox
        var listBoxStyle = new Style(typeof(ListBox));
        listBoxStyle.Setters.Add(new Setter(ListBox.BackgroundProperty, black));
        listBoxStyle.Setters.Add(new Setter(ListBox.ForegroundProperty, Brushes.White));
        listBoxStyle.Setters.Add(new Setter(ListBox.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(100, 100, 100))));
        listBoxStyle.Setters.Add(new Setter(ListBox.BorderThicknessProperty, new Thickness(1)));
        cb.Resources[typeof(ListBox)] = listBoxStyle;

        // IsEditable=true 时编辑区 TextBox
        cb.Loaded += (_, _) =>
        {
            if (cb.Template?.FindName("PART_EditableTextBox", cb) is TextBox tb)
            {
                tb.Background = black;
                tb.Foreground = Brushes.White;
                tb.BorderThickness = new Thickness(0);
                tb.Padding = new Thickness(2, 0, 2, 0);
                tb.CaretBrush = Brushes.White;
            }
        };
    }

    // ============================================================= 数据模型 =============================================================
    public sealed class ToolLogEntry
    {
        public string Display { get; }
        public Brush Foreground { get; }

        public ToolLogEntry(string title, string body, Brush? fg)
        {
            string trimmed = body ?? "";
            if (trimmed.Length > 2000) trimmed = trimmed[..2000] + "…";
            Display = $"[{DateTime.Now:HH:mm:ss}] {title}\n{trimmed}";
            Foreground = fg ?? new SolidColorBrush(Color.FromRgb(0x8A, 0xB4, 0xE0));
        }
    }
}