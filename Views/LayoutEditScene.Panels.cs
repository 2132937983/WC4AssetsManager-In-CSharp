using System.Globalization;
using System.Linq;
using System.Windows.Media.Imaging;
using WC4MapEditor.Core.Parsers.Layout;
using WC4MapEditor.Views.Assist;
using WC4MapEditor.Views.Dialogs;

namespace WC4MapEditor.Views;

/// <summary>布局编辑器的右侧面板：控件树 / 属性面板 / 精灵图库。</summary>
public partial class LayoutEditScene
{
    // ---------- 精灵图库状态 ----------
    private WrapPanel _spAtlasPanel = null!;
    private WrapPanel _spGrid = null!;
    private TextBox _spSearch = null!;
    private TextBlock _spCount = null!;
    private Button _spMore = null!;
    private StackPanel _spHits = null!;
    private int _spShown = 120;
    private string? _spAtlasFilter;
    private string? _spSelectedName;

    // ---------- 属性面板 geo 卡 ----------
    private TextBlock? _geoX, _geoY, _geoW, _geoH;

    // ---------- 图片使用位置反查 ----------
    private List<(string FormId, LayoutNode Form, LayoutNode Node, string Key, string Name)>? _usageMap;

    private static readonly string[] AlignHOptions = { "left", "center", "right" };
    private static readonly string[] AlignVOptions = { "top", "center", "bottom" };

    private static readonly Dictionary<string, string> PropLabels = new(StringComparer.Ordinal)
    {
        ["id"] = "标识",
        ["margin"] = "边距",
        ["halign"] = "水平对齐",
        ["valign"] = "垂直对齐",
        ["width"] = "宽度",
        ["height"] = "高度",
        ["minwidth"] = "最小宽",
        ["minheight"] = "最小高",
        ["maxwidth"] = "最大宽",
        ["maxheight"] = "最大高",
        ["drawmode"] = "填充方式",
        ["visible"] = "可见性",
        ["alpha"] = "不透明度",
        ["color"] = "颜色",
        ["scale"] = "缩放",
        ["clipped"] = "裁剪溢出",
        ["texture"] = "纹理",
        ["image"] = "图片",
        ["normal"] = "常态图",
        ["res"] = "资源",
        ["normalimage"] = "常态图片",
        ["backimage"] = "背景图",
        ["pressedimage"] = "按下图片",
        ["disabledimage"] = "禁用图片",
        ["checkedimage"] = "选中图片",
        ["uncheckedimage"] = "未选图片",
        ["barimage"] = "槽底图",
        ["thumbimage"] = "滑块图",
        ["text"] = "文本键",
        ["string"] = "固定文本",
        ["font"] = "字体",
        ["htextalign"] = "文字横排",
        ["vtextalign"] = "文字竖排",
        ["breakwords"] = "自动换行",
        ["gap"] = "间距",
        ["hlayoutalign"] = "横向排列",
        ["vlayoutalign"] = "纵向排列",
        ["templateid"] = "模板 ID",
        ["itemwidth"] = "单元宽",
        ["itemheight"] = "单元高",
        ["itemcount"] = "单元数量"
    };

    private static readonly string[] ImageTagList = { "Image", "Button", "CheckButton", "Label", "ProgressBar", "Slider", "Animation" };
    private static readonly string[] TextTagList = { "Label", "TextBox", "EditBox", "Button" };
    private static readonly string[] GapTagList = { "HGroup", "VGroup", "Repeater", "SlideList" };

    private static string OptLabel(string key, string v)
    {
        if (v == "") return "（默认）";
        if (key == "drawmode") return v switch { "hextend" => "横向铺满", "vextend" => "纵向铺满", "extend" => "双向铺满", "stretch" => "拉伸填充", "tile" => "平铺", _ => v };
        if (key.Contains("align")) return v switch { "left" => "左", "center" => "居中", "right" => "右", "top" => "上", "bottom" => "下", "middle" => "居中", _ => v };
        if (key == "visible") return v switch { "true" => "显示", "false" => "隐藏", _ => v };
        if (key == "clipped") return v switch { "true" => "裁剪子控件", "false" => "不裁剪", _ => v };
        if (key == "breakwords") return v switch { "true" => "自动换行", "false" => "不换行", _ => v };
        return v;
    }

    // ============================================================= 控件树 =============================================================
    private void RefreshTree()
    {
        _treePanel.Children.Clear();
        if (_form == null) return;

        _treePanel.Children.Add(MakeTreeNode(_form, 0, true));
        if (!_form.Collapsed)
            foreach (var c in _form.Children) _treePanel.Children.Add(MakeTreeNode(c, 1, false));
    }

    private UIElement MakeTreeNode(LayoutNode n, int depth, bool isRoot)
    {
        var row = new Border
        {
            Background = ReferenceEquals(n, _selected)
                ? new SolidColorBrush(Color.FromRgb(0x2B, 0x6C, 0xB0))
                : Brushes.Transparent,
            Padding = new Thickness(6 + depth * 12, 2, 6, 2),
            Cursor = Cursors.Hand
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        bool hasKids = n.Children.Count > 0;

        if (hasKids && !isRoot)
        {
            var glyph = new TextBlock
            {
                Text = n.Collapsed ? "▶" : "▼",
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 10,
                Width = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            glyph.MouseLeftButtonDown += (_, e) =>
            {
                n.Collapsed = !n.Collapsed;
                e.Handled = true;
                RefreshTree();
            };
            sp.Children.Add(glyph);
        }
        else
        {
            sp.Children.Add(new TextBlock { Text = " ", Width = 12, FontSize = 10 });
        }

        sp.Children.Add(new TextBlock
        {
            Text = TagCn(n.Tag),
            Foreground = new SolidColorBrush(Color.FromRgb(0x7F, 0xD1, 0xB9)),
            FontSize = 12
        });
        var idText = n.Get("id");
        if (!string.IsNullOrEmpty(idText))
        {
            sp.Children.Add(new TextBlock
            {
                Text = " #" + idText,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
                FontSize = 12
            });
        }

        // C2: eye 图标 — 切换可见性
        if (!isRoot && !n.IsVirtual)
        {
            bool hidden = LayoutDocument.IsFalseValue(n.Get("visible") ?? "true") && n.Get("visible") != null;
            var eye = new TextBlock
            {
                Text = hidden ? "○" : "●",
                Foreground = hidden ? new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)) : new SolidColorBrush(Color.FromRgb(0x7F, 0xD1, 0xB9)),
                FontSize = 10,
                Width = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = hidden ? "隐藏中（点击显示）" : "显示中（点击隐藏）"
            };
            eye.MouseLeftButtonDown += (_, e) =>
            {
                if (hidden) n.Set("visible", null);
                else n.Set("visible", "false");
                e.Handled = true;
                Repaint();
                RefreshTree();
            };
            sp.Children.Add(eye);
        }

        row.Child = sp;
        row.MouseLeftButtonDown += (_, _) => SelectNode(n);
        return row;
    }

    // ============================================================= 属性面板 =============================================================
    private void RenderProps()
    {
        if (_propsPanel == null) return;
        _propsPanel.Children.Clear();
        _geoX = _geoY = _geoW = _geoH = null;

        bool has = _selected != null;
        _btnAddChild.IsEnabled = has;
        _btnDup.IsEnabled = has && _selected?.Parent != null && !_selected.IsVirtual;
        _btnDel.IsEnabled = _btnDup.IsEnabled;

        if (_selected is not { } n)
        {
            _propsPanel.Children.Add(new TextBlock
            {
                Text = "未选中控件（点击画布或控件树选择）",
                Foreground = Brushes.Gray,
                FontSize = 12,
                Margin = new Thickness(4, 6, 4, 6)
            });
            return;
        }

        _propsPanel.Children.Add(BuildInfoCard(n));

        AddPropGroup(n, "标识", new[] { ("id", "text") });
        AddPropGroup(n, "布局", new[]
        {
            ("margin", "margin"), ("_align", "grid"), ("halign", "selH"), ("valign", "selV"),
            ("width", "num"), ("height", "num"), ("minwidth", "num"), ("minheight", "num"),
            ("maxwidth", "num"), ("maxheight", "num")
        });
        AddPropGroup(n, "外观", new[]
        {
            ("drawmode", "selDraw"), ("visible", "selVisible"), ("alpha", "num"),
            ("color", "color"), ("scale", "num"), ("clipped", "selBool")
        });
        AddPropGroup(n, "图片", new[]
        {
            ("texture", "text"), ("image", "text"), ("res", "text"), ("normalimage", "text"),
            ("normal", "text"), ("backimage", "text"), ("pressedimage", "text"), ("disabledimage", "text"),
            ("checkedimage", "text"), ("uncheckedimage", "text"), ("barimage", "text"), ("thumbimage", "text")
        });
        AddPropGroup(n, "文本", new[]
        {
            ("string", "text"), ("text", "text"), ("font", "text"), ("htextalign", "selHText"),
            ("vtextalign", "selVText"), ("breakwords", "selBool")
        });
        AddPropGroup(n, "组排列", new[] { ("gap", "num"), ("hlayoutalign", "selHText"), ("vlayoutalign", "selVText") });
        AddPropGroup(n, "模板 / 重复", new[] { ("templateid", "text"), ("itemwidth", "num"), ("itemheight", "num"), ("itemcount", "num") });

        AddRawAttrGroup(n);
    }

    private UIElement BuildInfoCard(LayoutNode n)
    {
        var card = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x47, 0x56)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(2, 2, 2, 8),
            Padding = new Thickness(8)
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = $"{TagCn(n.Tag)}  ({n.Tag})",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0xE0, 0xC2)),
            FontWeight = FontWeights.Bold,
            FontSize = 12
        });
        sp.Children.Add(new TextBlock
        {
            Text = $"标识：{(n.Get("id") != null ? "#" + n.Get("id") : "（无）")}",
            Foreground = new SolidColorBrush(Color.FromRgb(0xAE, 0xB9, 0xC5)),
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0)
        });
        var parent = n.Parent;
        int depth = 0;
        for (var p = n.Parent; p != null; p = p.Parent) depth++;
        sp.Children.Add(new TextBlock
        {
            Text = $"层级 / 父：第 {depth} 层 · {(parent != null ? TagCn(parent.Tag) + (parent.Get("id") != null ? "#" + parent.Get("id") : "") : "—")}",
            Foreground = new SolidColorBrush(Color.FromRgb(0xAE, 0xB9, 0xC5)),
            FontSize = 11
        });
        var kidCount = LayoutDocument.RealKids(n).Count;
        string kidSuffix = "";
        if (n.Items is { Count: > 0 } items) kidSuffix = $"（重复 {items.Count} 单元）";
        else if (n.Expanded is { Count: > 0 } expanded) kidSuffix = $"（模板 {expanded.Count}）";
        sp.Children.Add(new TextBlock
        {
            Text = $"子控件：{kidCount}{kidSuffix}",
            Foreground = new SolidColorBrush(Color.FromRgb(0xAE, 0xB9, 0xC5)),
            FontSize = 11
        });

        var geo = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        for (int i = 0; i < 4; i++) geo.ColumnDefinitions.Add(new ColumnDefinition());
        UIElement Cell(string label, out TextBlock value)
        {
            var st = new StackPanel();
            st.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0x8A, 0x97)), FontSize = 10 });
            value = new TextBlock { Text = "–", Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xED, 0xF2)), FontSize = 12 };
            st.Children.Add(value);
            return st;
        }
        geo.Children.Add(Cell("X", out _geoX));
        geo.Children.Add(Cell("Y", out _geoY));
        geo.Children.Add(Cell("宽", out _geoW));
        geo.Children.Add(Cell("高", out _geoH));
        int idx = 0;
        foreach (var child in geo.Children.Cast<UIElement>().ToList()) Grid.SetColumn(child, idx++);
        sp.Children.Add(geo);

        card.Child = sp;
        return card;
    }

    private void UpdateGeoCard()
    {
        var n = _selected;
        if (_geoX == null) return;
        if (n is { HasRect: true })
        {
            _geoX.Text = Math.Round(n.X).ToString(CultureInfo.InvariantCulture);
            _geoY.Text = Math.Round(n.Y).ToString(CultureInfo.InvariantCulture);
            _geoW.Text = Math.Round(n.W).ToString(CultureInfo.InvariantCulture);
            _geoH.Text = Math.Round(n.H).ToString(CultureInfo.InvariantCulture);
        }
    }

    private void AddPropGroup(LayoutNode n, string name, (string Key, string Type)[] rows)
    {
        bool anyAttr = rows.Any(r => n.Has(r.Key));
        bool forceShow = name is "标识" or "布局";
        if (!anyAttr && !forceShow) return;

        var visibleRows = new List<(string, string)>();
        foreach (var (key, type) in rows)
        {
            bool exists = n.Has(key);
            if (name == "图片" && !ImageTagList.Contains(n.Tag) && !exists) continue;
            if (name == "文本" && !TextTagList.Contains(n.Tag) && !exists) continue;
            if (name == "组排列" && !GapTagList.Contains(n.Tag) && !exists) continue;
            if (name == "模板 / 重复" && n.Tag is not ("PlaceHolder" or "Repeater") && !exists) continue;
            visibleRows.Add((key, type));
        }
        if (visibleRows.Count == 0) return;

        AddGroupHeader(name);
        foreach (var (key, type) in visibleRows) AddPropRow(n, key, type);
    }

    private void AddGroupHeader(string title)
    {
        _propsPanel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 6, 0, 2),
            Child = new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9F, 0xD8, 0xC6)),
                FontWeight = FontWeights.Bold,
                FontSize = 12
            }
        });
    }

    private void AddPropRow(LayoutNode n, string key, string type)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = PropLabels.TryGetValue(key, out var l) ? l : key,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            ToolTip = key + "（XML 属性名）"
        };
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        UIElement content;
        switch (type)
        {
            case "margin":
                content = BuildMarginEditor(n);
                break;
            case "grid":
                content = BuildAlignGrid(n);
                break;
            case "num":
                {
                    var tb = MakeTextBox("");
                    tb.Text = n.Get(key) ?? "";
                    tb.LostFocus += (_, _) => SetAttr(n, key, tb.Text);
                    content = tb;
                    break;
                }
            case "color":
                {
                    var sp = new StackPanel { Orientation = Orientation.Horizontal };
                    var initial = ParseColor(n.Get(key));
                    var sw = new Border
                    {
                        Width = 16,
                        Height = 16,
                        Margin = new Thickness(0, 0, 6, 0),
                        Background = new SolidColorBrush(initial is { } c0 ? ToWpfColor(c0) : Colors.Transparent),
                        BorderBrush = Brushes.Gray,
                        BorderThickness = new Thickness(1)
                    };
                    var tb = MakeTextBox("");
                    tb.Text = n.Get(key) ?? "";
                    tb.LostFocus += (_, _) =>
                    {
                        SetAttr(n, key, tb.Text);
                        var parsed = ParseColor(tb.Text);
                        sw.Background = new SolidColorBrush(parsed is { } c1 ? ToWpfColor(c1) : Colors.Transparent);
                    };
                    sp.Children.Add(sw);
                    sp.Children.Add(tb);
                    content = sp;
                    break;
                }
            case "selH":
                content = MakeComboLabeled("halign", new[] { "", "left", "center", "right" }, n.Get(key), v => SetAttr(n, key, v));
                break;
            case "selV":
                content = MakeComboLabeled("valign", new[] { "", "top", "center", "bottom" }, n.Get(key), v => SetAttr(n, key, v));
                break;
            case "selHText":
                content = MakeComboLabeled("htextalign", new[] { "", "left", "center", "right" }, n.Get(key), v => SetAttr(n, key, v));
                break;
            case "selVText":
                content = MakeComboLabeled("vtextalign", new[] { "", "top", "center", "bottom" }, n.Get(key), v => SetAttr(n, key, v));
                break;
            case "selBool":
                content = MakeComboLabeled("clipped", new[] { "", "true", "false" }, n.Get(key), v => SetAttr(n, key, v));
                break;
            case "selDraw":
                content = MakeComboLabeled("drawmode", new[] { "", "hextend", "vextend", "extend", "stretch", "tile" }, n.Get(key), v => SetAttr(n, key, v));
                break;
            case "selVisible":
                content = MakeComboLabeled("visible", new[] { "", "true", "false" }, n.Get(key), v => SetAttr(n, key, v));
                break;
            default:
                {
                    var tb = MakeTextBox("");
                    tb.Text = n.Get(key) ?? "";
                    tb.LostFocus += (_, _) => SetAttr(n, key, tb.Text);
                    content = tb;
                    break;
                }
        }

        Grid.SetColumn(content, 1);
        row.Children.Add(content);
        _propsPanel.Children.Add(row);
    }

    private UIElement BuildMarginEditor(LayoutNode n)
    {
        var m = n.Margins();
        var grid = new Grid();
        for (int i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        void AddCell(int col, string tag, double value)
        {
            var tb = MakeTextBox(tag);
            tb.Text = value.ToString(CultureInfo.InvariantCulture);
            tb.FontSize = 11;
            tb.LostFocus += (_, _) =>
            {
                var mm = n.Margins();
                double v = double.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
                switch (tag)
                {
                    case "L": mm.L = v; break;
                    case "T": mm.T = v; break;
                    case "R": mm.R = v; break;
                    case "B": mm.B = v; break;
                }
                SetAttr(n, "margin", $"{mm.L},{mm.T},{mm.R},{mm.B}");
            };
            Grid.SetColumn(tb, col);
            grid.Children.Add(tb);
        }
        AddCell(0, "L", m.L);
        AddCell(1, "T", m.T);
        AddCell(2, "R", m.R);
        AddCell(3, "B", m.B);
        return grid;
    }

    private UIElement BuildAlignGrid(LayoutNode n)
    {
        var grid = new Grid();
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });

        int ha = AlignOf(AlignHMap, n.Get("halign"), false);
        int va = AlignOf(AlignVMap, n.Get("valign"), false);
        string[] names = { "左上", "中上", "右上", "左中", "居中", "右中", "左下", "中下", "右下" };

        for (int i = 0; i < 9; i++)
        {
            int r = i / 3, c = i % 3;
            string h = AlignHOptions[c], v = AlignVOptions[r];
            var b = new Button
            {
                Content = names[i],
                FontSize = 10,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0),
                Margin = new Thickness(1),
                Cursor = Cursors.Hand,
                Background = (c == ha && r == va)
                    ? new SolidColorBrush(Color.FromRgb(0x16, 0x36, 0x4A))
                    : new SolidColorBrush(Color.FromRgb(0x17, 0x1D, 0x24)),
                Foreground = (c == ha && r == va) ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x88, 0x95, 0xA3))
            };
            b.Click += (_, _) =>
            {
                SetAttr(n, "halign", c == 1 ? null : h);
                SetAttr(n, "valign", r == 1 ? null : v);
                RenderProps();
            };
            Grid.SetRow(b, r);
            Grid.SetColumn(b, c);
            grid.Children.Add(b);
        }
        return grid;
    }

    private static ComboBox MakeCombo(string[] items, string? current, Action<string> changed)
    {
        var combo = new ComboBox
        {
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C))
        };
        foreach (var i in items) combo.Items.Add(i);
        combo.SelectedItem = current ?? "";
        if (combo.SelectedIndex < 0) combo.SelectedIndex = 0;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string s) changed(s);
        };
        return combo;
    }

    private static ComboBox MakeComboLabeled(string key, string[] items, string? current, Action<string> changed)
    {
        var combo = new ComboBox
        {
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C))
        };
        foreach (var i in items) combo.Items.Add(OptLabel(key, i));
        int idx = Array.IndexOf(items, current ?? "");
        combo.SelectedIndex = idx >= 0 ? idx : 0;
        combo.SelectionChanged += (_, _) =>
        {
            int si = combo.SelectedIndex;
            if (si >= 0 && si < items.Length) changed(items[si]);
        };
        return combo;
    }

    private void AddRawAttrGroup(LayoutNode n)
    {
        AddGroupHeader($"原始属性（{n.Attributes.Count}）");
        foreach (var kv in n.Attributes.ToList())
        {
            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = kv.Key,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var tb = MakeTextBox("");
            tb.Text = kv.Value;
            tb.LostFocus += (_, _) => SetAttr(n, kv.Key, tb.Text);
            Grid.SetColumn(tb, 1);
            row.Children.Add(tb);

            var del = new Button
            {
                Content = "×",
                Width = 22,
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            string key = kv.Key;
            del.Click += (_, _) =>
            {
                SetAttr(n, key, null);
                RenderProps();
            };
            Grid.SetColumn(del, 2);
            row.Children.Add(del);

            _propsPanel.Children.Add(row);
        }

        var addBtn = MakeSmallBtn("+ 添加属性", async (_, _) =>
        {
            var owner = Window.GetWindow(this) ?? _window;
            using var dlg = new SingleInputDialog(owner)
            {
                Title = "添加属性",
                Description = "属性名（如 normalimage / text / font …）",
                DefaultValue = ""
            };
            var key = await dlg.ShowAsync();
            if (string.IsNullOrWhiteSpace(key)) return;
            if (!n.Has(key)) SetAttr(n, key, "");
            RenderProps();
        });
        addBtn.HorizontalAlignment = HorizontalAlignment.Left;
        _propsPanel.Children.Add(addBtn);
    }

    private static System.Windows.Media.Color ToWpfColor(SKColor c)
        => System.Windows.Media.Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);

    // ============================================================= 精灵图库 =============================================================
    private UIElement BuildSpritesPane()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 6, 6, 3) };
        _spSearch = MakeTextBox("搜索图片名…");
        _spSearch.Width = 180;
        _spSearch.TextChanged += (_, _) =>
        {
            _spSearchDebounce?.Stop();
            _spSearchDebounce = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Normal, (_, _) => { _spShown = 120; RefreshSprites(); }, Dispatcher.CurrentDispatcher);
            _spSearchDebounce.Start();
        };
        tools.Children.Add(_spSearch);
        _spCount = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        tools.Children.Add(_spCount);
        Grid.SetRow(tools, 0);
        root.Children.Add(tools);

        _spAtlasPanel = new WrapPanel { Margin = new Thickness(6, 0, 6, 4) };
        Grid.SetRow(_spAtlasPanel, 1);
        root.Children.Add(_spAtlasPanel);

        _spGrid = new WrapPanel { Margin = new Thickness(6) };
        var gridScroll = new ScrollViewer { Content = _spGrid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(gridScroll, 2);
        root.Children.Add(gridScroll);

        var bottom = new StackPanel();
        _spMore = MakeSmallBtn("显示更多…", (_, _) => { _spShown += 120; RefreshSprites(); });
        _spMore.Visibility = Visibility.Collapsed;
        bottom.Children.Add(_spMore);
        _spHits = new StackPanel { Margin = new Thickness(6, 0, 6, 6) };
        bottom.Children.Add(_spHits);
        Grid.SetRow(bottom, 3);
        root.Children.Add(bottom);

        return root;
    }

    private void RefreshSprites()
    {
        if (_spAtlasPanel == null) return;
        _spAtlasPanel.Children.Clear();

        var all = LayoutImageProvider.AtlasNames;
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in all) counts[a] = 0;

        // 建立索引（首次访问会扫描图集）
        var query = (_spSearch.Text ?? "").Trim().ToLowerInvariant();
        var names = new List<(string Name, string Atlas)>();
        foreach (var atlas in all)
        {
            if (_spAtlasFilter != null && !string.Equals(atlas, _spAtlasFilter, StringComparison.OrdinalIgnoreCase)) continue;
            var parser = WC4MapEditor.Core.Parsers.HdAtlas.HdAtlasParser.Get(atlas);
            foreach (var def in parser.ImageDefinitions)
            {
                if (query.Length > 0 && !def.Name.ToLowerInvariant().Contains(query)) continue;
                names.Add((def.Name, atlas));
            }
        }

        // 图集 chips — B10: "全部" chip
        var allChip = new Button
        {
            Content = $"全部 ({names.Count})",
            FontSize = 11,
            Margin = new Thickness(2),
            Padding = new Thickness(6, 1, 6, 1),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            Foreground = _spAtlasFilter == null ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x9F, 0xB0, 0xC0)),
            Background = _spAtlasFilter == null
                ? new SolidColorBrush(Color.FromRgb(0x16, 0x36, 0x4A))
                : new SolidColorBrush(Color.FromRgb(0x17, 0x1D, 0x24))
        };
        allChip.Click += (_, _) => { _spAtlasFilter = null; _spShown = 120; RefreshSprites(); };
        _spAtlasPanel.Children.Add(allChip);

        foreach (var atlas in all)
        {
            var parser = WC4MapEditor.Core.Parsers.HdAtlas.HdAtlasParser.Get(atlas);
            var chip = new Button
            {
                Content = $"{atlas} ({parser.ImageDefinitions.Count})",
                FontSize = 11,
                Margin = new Thickness(2),
                Padding = new Thickness(6, 1, 6, 1),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Foreground = string.Equals(atlas, _spAtlasFilter, StringComparison.OrdinalIgnoreCase) ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x9F, 0xB0, 0xC0)),
                Background = string.Equals(atlas, _spAtlasFilter, StringComparison.OrdinalIgnoreCase)
                    ? new SolidColorBrush(Color.FromRgb(0x16, 0x36, 0x4A))
                    : new SolidColorBrush(Color.FromRgb(0x17, 0x1D, 0x24))
            };
            string a = atlas;
            chip.Click += (_, _) =>
            {
                _spAtlasFilter = string.Equals(_spAtlasFilter, a, StringComparison.OrdinalIgnoreCase) ? null : a;
                _spShown = 120;
                RefreshSprites();
            };
            _spAtlasPanel.Children.Add(chip);
        }

        _spCount.Text = $"{names.Count} 张";

        _spGrid.Children.Clear();
        var usage = UsageMap();
        var usageByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in usage)
        {
            var key = u.Name;
            usageByName[key] = (usageByName.TryGetValue(key, out var c) ? c : 0) + 1;
        }

        foreach (var (name, atlas) in names.Take(_spShown))
        {
            var isSel = string.Equals(name, _spSelectedName, StringComparison.OrdinalIgnoreCase);
            var cell = new StackPanel
            {
                Width = 78,
                Margin = new Thickness(3),
                Cursor = Cursors.Hand,
                Background = isSel ? new SolidColorBrush(Color.FromRgb(0x16, 0x36, 0x4A)) : Brushes.Transparent
            };
            var thumb = HdAtlasImageLoader.Load(atlas, name);
            var img = new Image
            {
                Width = 68,
                Height = 68,
                Stretch = Stretch.Uniform,
                Source = thumb
            };
            cell.Children.Add(img);
            cell.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xA3, 0xB5)),
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0)
            });
            // B11: 引用徽章
            if (usageByName.TryGetValue(name, out var refCount) && refCount > 0)
            {
                cell.Children.Add(new TextBlock
                {
                    Text = refCount.ToString(),
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0xE0, 0xC2)),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, -2, 0, 0),
                    ToolTip = $"当前布局有 {refCount} 处引用"
                });
            }
            string n = name;
            cell.MouseLeftButtonDown += (_, _) => SelectSprite(n);
            _spGrid.Children.Add(cell);
        }

        _spMore.Visibility = names.Count > _spShown ? Visibility.Visible : Visibility.Collapsed;

        if (_spSelectedName != null) RenderSpriteHits(_spSelectedName);
    }

    private void SelectSprite(string name)
    {
        _spSelectedName = name;
        RefreshSprites();
        RenderSpriteHits(name);

        // 跳到第一个引用它的控件
        var hits = UsageMap().Where(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (hits.Count > 0)
        {
            var first = hits[0];
            if (!ReferenceEquals(first.Form, _form))
            {
                _form = first.Form;
                RefreshFormList();
            }
            SelectNode(first.Node);
            CenterOnNode(first.Node);
            SetStatus($"「{name}」被 {hits.Count} 处引用 → {first.FormId} · {first.Node}（{first.Key}）");
        }
        else
        {
            SetStatus($"「{name}」当前布局中没有控件引用");
        }
    }

    private void RenderSpriteHits(string name)
    {
        _spHits.Children.Clear();
        var hits = UsageMap().Where(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();

        // B13: 带关闭按钮的标题行
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new TextBlock
        {
            Text = $"「{name}」{(hits.Count > 0 ? $"被 {hits.Count} 处引用" : "没有控件引用")}",
            Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0xE0, 0xC2)),
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 2),
            VerticalAlignment = VerticalAlignment.Center
        });
        var closeBtn = new Button
        {
            Content = "×",
            FontSize = 11,
            Width = 20,
            Height = 18,
            Margin = new Thickness(6, 2, 0, 0),
            Padding = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = "收起",
            Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C)),
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White
        };
        closeBtn.Click += (_, _) => _spHits.Children.Clear();
        header.Children.Add(closeBtn);
        _spHits.Children.Add(header);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        int shown = 0;
        foreach (var h in hits)
        {
            string label = $"{h.FormId} · {TagCn(h.Node.Tag)}{(h.Node.Get("id") != null ? "#" + h.Node.Get("id") : "")} · {h.Key}";
            if (!seen.Add(label)) continue;
            if (shown++ >= 60) break;

            var item = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Color.FromRgb(0xB9, 0xC6, 0xD3)),
                FontSize = 11,
                Padding = new Thickness(2),
                Cursor = Cursors.Hand
            };
            var node = h.Node;
            var form = h.Form;
            item.MouseLeftButtonDown += (_, _) =>
            {
                if (!ReferenceEquals(form, _form))
                {
                    _form = form;
                    RefreshFormList();
                }
                SelectNode(node);
                CenterOnNode(node);
            };
            _spHits.Children.Add(item);
        }
    }

    private void InvalidateUsage() => _usageMap = null;

    private List<(string FormId, LayoutNode Form, LayoutNode Node, string Key, string Name)> UsageMap()
    {
        if (_usageMap != null) return _usageMap;

        var map = new List<(string, LayoutNode, LayoutNode, string, string)>();
        foreach (var form in _doc.Forms)
        {
            string fid = form.Get("id") ?? "";

            void Walk(LayoutNode n)
            {
                foreach (var kv in n.Attributes)
                {
                    if (!ImageKeys.Contains(kv.Key)) continue;
                    var key = kv.Value.Replace('\\', '/').Split('/')[^1].ToLowerInvariant();
                    if (key.Length == 0) continue;
                    map.Add((fid, form, n, kv.Key, key));
                }
                foreach (var c in n.Children) Walk(c);
                if (n.Expanded != null) foreach (var c in n.Expanded) Walk(c);
                if (n.Items != null) foreach (var cell in n.Items) foreach (var c in cell.Children) Walk(c);
            }

            Walk(form);
        }

        _usageMap = map;
        return map;
    }
}
