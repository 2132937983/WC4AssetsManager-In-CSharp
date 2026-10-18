using System.Globalization;
using System.Linq;
using WC4MapEditor.Core.Parsers.Layout;
using WC4MapEditor.GUI.Views.Assist;
using WC4MapEditor.GUI.Views.Dialogs;

namespace WC4MapEditor.GUI.Views;

/// <summary>布局编辑器的右侧面板：控件树 / 属性面板（按 LayoutSchema 生成）/ 精灵图库。</summary>
public partial class LayoutEditScene
{
    // ---------- 精灵图库状态 ----------
    private WrapPanel _spAtlasPanel = null!;
    private WrapPanel _spGrid = null!;
    private TextBox _spSearch = null!;
    private TextBlock _spCount = null!;
    private TextBlock _spAssignHint = null!;
    private Button _spMore = null!;
    private StackPanel _spHits = null!;
    private int _spShown = 120;
    private string? _spAtlasFilter;
    private string? _spSelectedName;

    /// <summary>图库处于"为某属性选图"模式时的目标（节点, 属性名）。</summary>
    private (LayoutNode Node, string Key)? _assignTarget;

    // ---------- 属性面板 geo 卡 ----------
    private TextBlock? _geoX, _geoY, _geoW, _geoH;

    // ---------- 图片使用位置反查 ----------
    private List<(string FormId, LayoutNode Form, LayoutNode Node, string Key, string Name)>? _usageMap;

    private static string AttrLabel(string key)
    {
        string k = "Layout.Attr." + key;
        return Loc.Has(k) ? Loc.T(k) : key;
    }

    private static string GroupLabel(string group) => Loc.T("Layout.Group." + group, group);

    private static string OptLabel(string v)
    {
        if (v == "") return Loc.T("Layout.Opt.Default");
        string k = "Layout.Opt." + v;
        return Loc.Has(k) ? Loc.T(k) : v;
    }

    // ============================================================= 控件树 =============================================================
    private void RefreshTree()
    {
        _treePanel.Children.Clear();
        if (_form == null) return;
        AppendTreeNode(_form, 0, true);
        UpdateNodeButtons();
    }

    private void AppendTreeNode(LayoutNode n, int depth, bool isRoot)
    {
        _treePanel.Children.Add(MakeTreeRow(n, depth, isRoot));
        if (n.Collapsed && !isRoot) return;
        foreach (var c in n.Children) AppendTreeNode(c, depth + 1, false);
    }

    private UIElement MakeTreeRow(LayoutNode n, int depth, bool isRoot)
    {
        bool selected = ReferenceEquals(n, _selected);
        bool hidden = LayoutDocument.IsHidden(n);
        var row = new Border
        {
            Background = selected ? EditorTheme.Selection : Brushes.Transparent,
            Padding = new Thickness(6 + depth * 12, 2, 6, 2),
            Cursor = Cursors.Hand,
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(4, 0, 4, 0)
        };
        row.MouseEnter += (_, _) => { if (!ReferenceEquals(n, _selected)) row.Background = EditorTheme.Hover; };
        row.MouseLeave += (_, _) => { if (!ReferenceEquals(n, _selected)) row.Background = Brushes.Transparent; };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        bool hasKids = n.Children.Count > 0;
        if (hasKids && !isRoot)
        {
            var glyph = new TextBlock
            {
                Text = n.Collapsed ? "▶" : "▼",
                Foreground = EditorTheme.TextDim,
                FontSize = 9,
                VerticalAlignment = VerticalAlignment.Center
            };
            glyph.MouseLeftButtonDown += (_, e) => { n.Collapsed = !n.Collapsed; e.Handled = true; RefreshTree(); };
            grid.Children.Add(glyph);
        }

        var label = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = hidden ? 0.55 : 1 };
        label.Inlines.Add(new System.Windows.Documents.Run(TagName(n.Tag)) { Foreground = selected ? Brushes.White : EditorTheme.Accent });
        if (n.Get("id") is { Length: > 0 } id)
            label.Inlines.Add(new System.Windows.Documents.Run(" #" + id) { Foreground = selected ? Brushes.White : EditorTheme.Text });
        if (n.Tag == "Label")
        {
            var t = LayoutEngine.TextOf(n, _ctx);
            if (t.Length > 0) label.Inlines.Add(new System.Windows.Documents.Run("  “" + (t.Length > 14 ? t[..14] + "…" : t) + "”") { Foreground = EditorTheme.TextMuted });
        }
        else if (LayoutDocument.ImageRefOf(n) is { } ir)
        {
            label.Inlines.Add(new System.Windows.Documents.Run("  " + ir.Ref) { Foreground = EditorTheme.TextDim });
        }
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        if (!isRoot && !n.IsVirtual)
        {
            var eye = new TextBlock
            {
                Text = hidden ? "◌" : "●",
                Foreground = hidden ? EditorTheme.TextDim : EditorTheme.Success,
                FontSize = 10,
                Width = 14,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = hidden ? Loc.T("Layout.Tree.HiddenTip") : Loc.T("Layout.Tree.VisibleTip")
            };
            eye.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                SetAttr(n, "visible", hidden ? null : "false");
                RefreshTree();
                if (ReferenceEquals(n, _selected)) RenderProps();
            };
            Grid.SetColumn(eye, 2);
            grid.Children.Add(eye);
        }

        row.Child = grid;
        row.MouseLeftButtonDown += (_, _) => { SelectNode(n); if (n.HasRect) CenterOnNodeIfOffscreen(n); };
        return row;
    }

    private void CenterOnNodeIfOffscreen(LayoutNode n)
    {
        double sx = _panX + n.X * _zoom, sy = _panY + n.Y * _zoom;
        double ex = sx + n.W * _zoom, ey = sy + n.H * _zoom;
        if (ex < 0 || ey < 0 || sx > _canvas.ActualWidth || sy > _canvas.ActualHeight) CenterOnNode(n);
    }

    private void UpdateNodeButtons()
    {
        bool has = _selected != null;
        bool real = has && _selected!.Parent != null && !_selected.IsVirtual;
        _btnAddChild.IsEnabled = _form != null;
        _btnDup.IsEnabled = real;
        _btnDel.IsEnabled = real;
        _btnUp.IsEnabled = real && _selected!.Parent!.Children.IndexOf(_selected) > 0;
        _btnDown.IsEnabled = real && _selected!.Parent!.Children.IndexOf(_selected) < _selected.Parent.Children.Count - 1;
    }

    // ============================================================= 属性面板 =============================================================
    private void RenderProps()
    {
        if (_propsPanel == null) return;
        _propsPanel.Children.Clear();
        _geoX = _geoY = _geoW = _geoH = null;
        UpdateNodeButtons();

        if (_selected is not { } n)
        {
            _propsPanel.Children.Add(EditorTheme.EmptyHint(Loc.T("Layout.NoSelection")));
            return;
        }

        _propsPanel.Children.Add(BuildInfoCard(n));

        if (n.IsVirtual)
        {
            _propsPanel.Children.Add(EditorTheme.EmptyHint(Loc.T("Layout.VirtualHint")));
            return;
        }

        var def = LayoutSchema.Get(n.Tag);
        var groups = def.AllAttributes.GroupBy(a => a.Group).ToList();
        var known = new HashSet<string>(def.AllAttributes.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var g in groups)
        {
            var rows = g.ToList();
            // 只在"布局 / 标识"组总是显示；其它组只有写了属性或是该标签的核心属性时才展开，避免面板过长
            bool force = g.Key is LayoutSchema.GroupIdentity or LayoutSchema.GroupLayout or LayoutSchema.GroupAppearance
                         || rows.Any(r => n.Has(r.Name)) || IsCoreGroup(n.Tag, g.Key);
            if (!force) continue;

            _propsPanel.Children.Add(EditorTheme.SectionHeader(GroupLabel(g.Key)));
            foreach (var attr in rows)
            {
                if (attr.Name == "margin") { _propsPanel.Children.Add(EditorTheme.FormRow(AttrLabel("margin"), BuildMarginEditor(n), 96, "margin")); continue; }
                if (attr.Name == "halign") { _propsPanel.Children.Add(EditorTheme.FormRow(AttrLabel("align"), BuildAlignGrid(n), 96, "halign / valign")); continue; }
                if (attr.Name == "valign") continue;
                _propsPanel.Children.Add(EditorTheme.FormRow(AttrLabel(attr.Name), BuildEditor(n, attr), 96, attr.Name));
            }
        }

        // 游戏里出现过、但不在上表里的属性照样可编辑
        var extra = n.Attributes.Where(kv => !known.Contains(kv.Key)).ToList();
        _propsPanel.Children.Add(EditorTheme.SectionHeader(Loc.F("Layout.RawAttrs", extra.Count), MakeAddAttrButton(n)));
        foreach (var kv in extra)
        {
            var tb = new TextBox { Text = kv.Value, FontSize = 12, MinHeight = 26 };
            string key = kv.Key;
            tb.LostFocus += (_, _) => SetAttr(n, key, tb.Text);
            tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SetAttr(n, key, tb.Text); e.Handled = true; } };
            var del = EditorTheme.GhostButton("×", (_, _) => { SetAttr(n, key, null); RenderProps(); });
            del.Padding = new Thickness(6, 0, 6, 0);
            del.MinHeight = 24;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(del, 1);
            g.Children.Add(tb);
            g.Children.Add(del);
            _propsPanel.Children.Add(EditorTheme.FormRow(kv.Key, g, 96));
        }
    }

    private static bool IsCoreGroup(string tag, string group) => (tag, group) switch
    {
        ("Image" or "Button" or "GroupBox" or "Group" or "ProgressBar" or "Slider" or "PlaceHolder", LayoutSchema.GroupImage) => true,
        ("Label" or "Button" or "EditBox" or "TextBox", LayoutSchema.GroupText) => true,
        ("HGroup" or "VGroup", LayoutSchema.GroupGroup) => true,
        ("Repeater" or "PlaceHolder" or "SlideList", LayoutSchema.GroupRepeater) => true,
        ("TmpWindow" or "Form", LayoutSchema.GroupWindow) => true,
        ("Animation", LayoutSchema.GroupAnimation) => true,
        ("ScrollViewer", LayoutSchema.GroupScroll) => true,
        _ => false
    };

    private Button MakeAddAttrButton(LayoutNode n)
    {
        var b = EditorTheme.ToolButton("＋ " + Loc.T("Layout.AddAttr"), async (_, _) =>
        {
            var owner = Window.GetWindow(this) ?? _window;
            using var dlg = new SingleInputDialog(owner)
            {
                Title = Loc.T("Layout.AddAttr"),
                Description = Loc.T("Layout.AddAttrPrompt"),
                DefaultValue = ""
            };
            var key = await dlg.ShowAsync();
            if (string.IsNullOrWhiteSpace(key)) return;
            key = key.Trim();
            if (!n.Has(key)) SetAttr(n, key, LayoutSchema.Get(n.Tag).Find(key)?.Default ?? "0");
            RenderProps();
        });
        b.Margin = new Thickness(0);
        b.MinHeight = 22;
        b.Padding = new Thickness(7, 1, 7, 1);
        return b;
    }

    private UIElement BuildInfoCard(LayoutNode n)
    {
        var sp = new StackPanel();
        var head = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold };
        head.Inlines.Add(new System.Windows.Documents.Run(TagName(n.Tag)) { Foreground = EditorTheme.Accent });
        head.Inlines.Add(new System.Windows.Documents.Run($"  <{n.Tag}>") { Foreground = EditorTheme.TextDim, FontWeight = FontWeights.Normal });
        sp.Children.Add(head);

        var parent = n.Parent;
        int depth = 0;
        for (var p = n.Parent; p != null && !ReferenceEquals(p, _doc.Root); p = p.Parent) depth++;
        var kidCount = LayoutDocument.RealKids(n).Count;
        string kidSuffix = n.Items is { Count: > 0 } items ? Loc.F("Layout.Info.Cells", items.Count)
                         : n.Expanded is { Count: > 0 } ex ? Loc.F("Layout.Info.Template", ex.Count) : "";
        var meta = EditorTheme.Muted(Loc.F("Layout.Info.Meta", depth,
            parent != null && !ReferenceEquals(parent, _doc.Root) ? NodeLabel(parent) : "—", kidCount, kidSuffix));
        meta.Margin = new Thickness(0, 4, 0, 0);
        meta.TextWrapping = TextWrapping.Wrap;
        sp.Children.Add(meta);

        if (LayoutDocument.ImageRefOf(n) is { } ir)
        {
            var rec = LayoutImageProvider.Resolve(ir.Ref);
            var line = EditorTheme.Muted(rec is { Ready: true }
                ? Loc.F("Layout.Info.ImageOk", ir.Ref, rec.W, rec.H, rec.Atlas != null ? LayoutImageProvider.AtlasDisplayName(rec.Atlas) : Loc.T("Layout.Info.LooseFile"))
                : Loc.F("Layout.Info.ImageMissing", ir.Ref));
            line.Foreground = rec is { Ready: true } ? EditorTheme.TextMuted : EditorTheme.Danger;
            line.TextWrapping = TextWrapping.Wrap;
            line.Margin = new Thickness(0, 2, 0, 0);
            sp.Children.Add(line);
        }

        var geo = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        for (int i = 0; i < 4; i++) geo.ColumnDefinitions.Add(new ColumnDefinition());
        UIElement Cell(string label, out TextBlock value)
        {
            var st = new StackPanel();
            st.Children.Add(new TextBlock { Text = label, Foreground = EditorTheme.TextDim, FontSize = 10 });
            value = new TextBlock { Text = "–", Foreground = EditorTheme.Text, FontSize = 13, FontWeight = FontWeights.SemiBold };
            st.Children.Add(value);
            return st;
        }
        geo.Children.Add(Cell("X", out _geoX));
        geo.Children.Add(Cell("Y", out _geoY));
        geo.Children.Add(Cell(Loc.T("Common.Width"), out _geoW));
        geo.Children.Add(Cell(Loc.T("Common.Height"), out _geoH));
        int idx = 0;
        foreach (var child in geo.Children.Cast<UIElement>().ToList()) Grid.SetColumn(child, idx++);
        sp.Children.Add(geo);

        var card = EditorTheme.Card(sp);
        card.Margin = new Thickness(8, 8, 8, 0);
        UpdateGeoCard();
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

    /// <summary>按属性类型生成编辑器。</summary>
    private UIElement BuildEditor(LayoutNode n, LayoutAttrDef attr)
    {
        string key = attr.Name;
        string? cur = n.Get(key);

        switch (attr.Kind)
        {
            case LayoutAttrKind.Enum:
                return MakeOptionCombo(new[] { "" }.Concat(attr.Options ?? Array.Empty<string>()).ToArray(), cur, v => SetAttr(n, key, v));

            case LayoutAttrKind.Bool:
                return MakeOptionCombo(new[] { "", "true", "false" }, cur != null && LayoutDocument.IsFalseValue(cur) ? "false" : cur != null ? "true" : "", v => SetAttr(n, key, v));

            case LayoutAttrKind.Color:
                return BuildColorEditor(n, key);

            case LayoutAttrKind.Image:
            case LayoutAttrKind.AnimRes:
                return BuildImageEditor(n, key, attr.Kind == LayoutAttrKind.AnimRes);

            case LayoutAttrKind.Font:
            {
                var combo = new ComboBox { IsEditable = true, FontSize = 12, MinHeight = 26 };
                foreach (var f in LayoutFontProvider.FontNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) combo.Items.Add(f);
                combo.Text = cur ?? "";
                combo.LostFocus += (_, _) => SetAttr(n, key, combo.Text);
                combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string s) SetAttr(n, key, s); };
                var meta = LayoutFontProvider.GetFontMeta(cur);
                var hint = EditorTheme.Muted(string.IsNullOrEmpty(cur) ? "" : meta.IsBitmap ? Loc.T("Layout.Font.Bitmap") : Loc.F("Layout.Font.Meta", meta.Size, meta.Outline));
                hint.FontSize = 11;
                hint.Margin = new Thickness(2, 2, 0, 0);
                var sp = new StackPanel();
                sp.Children.Add(combo);
                if (hint.Text.Length > 0) sp.Children.Add(hint);
                return sp;
            }

            case LayoutAttrKind.StringKey:
            {
                var sp = new StackPanel();
                var tb = new TextBox { Text = cur ?? "", FontSize = 12, MinHeight = 26 };
                tb.LostFocus += (_, _) => { SetAttr(n, key, tb.Text); RenderProps(); };
                tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SetAttr(n, key, tb.Text); RenderProps(); e.Handled = true; } };
                sp.Children.Add(tb);
                if (!string.IsNullOrEmpty(cur))
                {
                    bool found = StringTable(_lang).TryGetValue(cur, out var localized);
                    var preview = EditorTheme.Muted(found ? "“" + localized + "”" : Loc.T("Layout.Text.KeyMissing"));
                    preview.FontSize = 11;
                    preview.TextWrapping = TextWrapping.Wrap;
                    preview.Margin = new Thickness(2, 2, 0, 0);
                    if (!found) preview.Foreground = EditorTheme.Warning;
                    sp.Children.Add(preview);
                }
                return sp;
            }

            case LayoutAttrKind.TemplateId:
            {
                var combo = new ComboBox { IsEditable = true, FontSize = 12, MinHeight = 26 };
                foreach (var t in _doc.Templates.Keys.OrderBy(x => x)) combo.Items.Add(t);
                combo.Text = cur ?? "";
                combo.LostFocus += (_, _) => SetAttr(n, key, combo.Text);
                combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string s) SetAttr(n, key, s); };
                return combo;
            }

            case LayoutAttrKind.Int:
            case LayoutAttrKind.Number:
            {
                var tb = new TextBox { Text = cur ?? "", FontSize = 12, MinHeight = 26 };
                tb.LostFocus += (_, _) => SetAttr(n, key, tb.Text.Trim());
                tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SetAttr(n, key, tb.Text.Trim()); e.Handled = true; } };
                tb.PreviewMouseWheel += (_, e) =>
                {
                    if (!tb.IsKeyboardFocused) return;
                    if (!double.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) v = 0;
                    double step = attr.Kind == LayoutAttrKind.Int ? 1 : 0.1;
                    v += e.Delta > 0 ? step : -step;
                    tb.Text = v.ToString(attr.Kind == LayoutAttrKind.Int ? "0" : "0.##", CultureInfo.InvariantCulture);
                    SetAttr(n, key, tb.Text);
                    e.Handled = true;
                };
                return tb;
            }

            default:
            {
                var tb = new TextBox { Text = cur ?? "", FontSize = 12, MinHeight = 26 };
                tb.LostFocus += (_, _) => SetAttr(n, key, tb.Text);
                tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SetAttr(n, key, tb.Text); e.Handled = true; } };
                return tb;
            }
        }
    }

    private UIElement BuildColorEditor(LayoutNode n, string key)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var initial = ParseColor(n.Get(key));
        var swatch = new Border
        {
            Width = 22, Height = 22,
            Margin = new Thickness(0, 0, 6, 0),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(initial is { } c0 ? ToWpfColor(c0) : Colors.Transparent),
            BorderBrush = EditorTheme.BorderStrong,
            BorderThickness = new Thickness(1),
            ToolTip = Loc.T("Layout.Color.Hint")
        };
        var tb = new TextBox { Text = n.Get(key) ?? "", FontSize = 12, MinHeight = 26 };
        void Commit()
        {
            SetAttr(n, key, tb.Text.Trim());
            var parsed = ParseColor(tb.Text);
            swatch.Background = new SolidColorBrush(parsed is { } c1 ? ToWpfColor(c1) : Colors.Transparent);
        }
        tb.LostFocus += (_, _) => Commit();
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        Grid.SetColumn(tb, 1);
        grid.Children.Add(swatch);
        grid.Children.Add(tb);
        return grid;
    }

    /// <summary>图片属性：文本框 + 缩略图 + "从图库选"按钮（切到图库页，点一张就填回来）。</summary>
    private UIElement BuildImageEditor(LayoutNode n, string key, bool anim)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        string? cur = n.Get(key);
        var thumbBorder = new Border
        {
            Width = 26, Height = 26, Margin = new Thickness(0, 0, 6, 0),
            Background = EditorTheme.Bg0, BorderBrush = EditorTheme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3)
        };
        if (!string.IsNullOrEmpty(cur))
        {
            var rec = LayoutImageProvider.Resolve(cur);
            if (rec is { Ready: true, Atlas: { } atlas })
            {
                var defs = LayoutImageProvider.AtlasImages(atlas);
                var def = defs.FirstOrDefault(d => d.X == rec.X && d.Y == rec.Y && d.Width == rec.W && d.Height == rec.H);
                if (def != null)
                    thumbBorder.Child = new Image { Source = HdAtlasImageLoader.Load(atlas, def.Name), Stretch = Stretch.Uniform, Margin = new Thickness(2) };
            }
            else if (rec == null)
            {
                thumbBorder.BorderBrush = EditorTheme.Danger;
                thumbBorder.ToolTip = Loc.F("Layout.Info.ImageMissing", cur);
            }
        }
        grid.Children.Add(thumbBorder);

        var tb = new TextBox { Text = cur ?? "", FontSize = 12, MinHeight = 26 };
        tb.LostFocus += (_, _) => { SetAttr(n, key, tb.Text.Trim()); RenderProps(); };
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) { SetAttr(n, key, tb.Text.Trim()); RenderProps(); e.Handled = true; } };
        Grid.SetColumn(tb, 1);
        grid.Children.Add(tb);

        var pick = EditorTheme.ToolButton("…", (_, _) =>
        {
            _assignTarget = (n, key);
            _spAtlasFilter = anim ? null : _spAtlasFilter;
            _spSearch.Text = anim ? "anim_" : "";
            ShowSpritesTab();
        });
        pick.Margin = new Thickness(6, 0, 0, 0);
        pick.Padding = new Thickness(7, 1, 7, 1);
        pick.MinHeight = 26;
        pick.ToolTip = Loc.T("Layout.Sprites.PickHint");
        Grid.SetColumn(pick, 2);
        grid.Children.Add(pick);
        return grid;
    }

    private UIElement BuildMarginEditor(LayoutNode n)
    {
        var m = n.Margins();
        var grid = new Grid();
        for (int i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        void AddCell(int col, string tag, double value)
        {
            var sp = new StackPanel { Margin = new Thickness(col == 0 ? 0 : 3, 0, 0, 0) };
            var tb = new TextBox { Text = value.ToString("0.##", CultureInfo.InvariantCulture), FontSize = 11, MinHeight = 24, Padding = new Thickness(4, 2, 4, 2) };
            void Commit()
            {
                var mm = n.Margins();
                double v = double.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
                switch (tag) { case "L": mm.L = v; break; case "T": mm.T = v; break; case "R": mm.R = v; break; case "B": mm.B = v; break; }
                SetAttr(n, "margin", mm.ToString());
            }
            tb.LostFocus += (_, _) => Commit();
            tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
            sp.Children.Add(tb);
            sp.Children.Add(new TextBlock { Text = tag, FontSize = 9, Foreground = EditorTheme.TextDim, HorizontalAlignment = HorizontalAlignment.Center });
            Grid.SetColumn(sp, col);
            grid.Children.Add(sp);
        }
        AddCell(0, "L", m.L);
        AddCell(1, "T", m.T);
        AddCell(2, "R", m.R);
        AddCell(3, "B", m.B);
        return grid;
    }

    /// <summary>九宫格对齐选择器（中间一格 = 居中；左上 = 游戏默认，不写属性）。</summary>
    private UIElement BuildAlignGrid(LayoutNode n)
    {
        var grid = new Grid { Width = 96, HorizontalAlignment = HorizontalAlignment.Left };
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });

        int ha = LayoutEngine.HAlignOf(n.Get("halign"));
        int va = LayoutEngine.VAlignOf(n.Get("valign"));
        string[] glyphs = { "◤", "▲", "◥", "◀", "●", "▶", "◣", "▼", "◢" };

        for (int i = 0; i < 9; i++)
        {
            int r = i / 3, c = i % 3;
            bool on = c == ha && r == va;
            var b = new Button
            {
                Content = glyphs[i],
                FontSize = 10,
                Padding = new Thickness(0),
                Margin = new Thickness(1),
                MinHeight = 0,
                Background = on ? EditorTheme.Selection : EditorTheme.Bg3,
                BorderBrush = on ? EditorTheme.Accent : EditorTheme.Border,
                Foreground = on ? Brushes.White : EditorTheme.TextMuted,
                ToolTip = $"{LayoutSchema.AlignHOptions[c]} / {LayoutSchema.AlignVOptions[r]}"
            };
            b.Click += (_, _) =>
            {
                PushUndo();
                SetAttr(n, "halign", c == 0 ? null : LayoutSchema.AlignHOptions[c], recordUndo: false);
                SetAttr(n, "valign", r == 0 ? null : LayoutSchema.AlignVOptions[r], recordUndo: false);
                RenderProps();
            };
            Grid.SetRow(b, r);
            Grid.SetColumn(b, c);
            grid.Children.Add(b);
        }
        return grid;
    }

    private static ComboBox MakeOptionCombo(string[] items, string? current, Action<string> changed)
    {
        var combo = new ComboBox { FontSize = 12, MinHeight = 26 };
        foreach (var i in items) combo.Items.Add(OptLabel(i));
        int idx = Array.IndexOf(items, current ?? "");
        if (idx < 0 && current != null) idx = Array.FindIndex(items, i => string.Equals(i, current, StringComparison.OrdinalIgnoreCase));
        combo.SelectedIndex = idx >= 0 ? idx : 0;
        combo.SelectionChanged += (_, _) =>
        {
            int si = combo.SelectedIndex;
            if (si >= 0 && si < items.Length) changed(items[si]);
        };
        return combo;
    }

    private static Color ToWpfColor(SKColor c) => Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue);

    // ============================================================= 精灵图库 =============================================================
    private UIElement BuildSpritesPane()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var tools = new Grid { Margin = new Thickness(8, 8, 8, 4) };
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var search = EditorTheme.SearchBox(Loc.T("Layout.Sprites.Search"), out _spSearch);
        _spSearch.TextChanged += (_, _) =>
        {
            _spSearchDebounce?.Stop();
            _spSearchDebounce = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, (_, _) => { _spSearchDebounce?.Stop(); _spShown = 120; RefreshSprites(); }, Dispatcher.CurrentDispatcher);
            _spSearchDebounce.Start();
        };
        tools.Children.Add(search);
        _spCount = EditorTheme.Muted("");
        _spCount.VerticalAlignment = VerticalAlignment.Center;
        _spCount.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_spCount, 1);
        tools.Children.Add(_spCount);
        root.Children.Add(tools);

        _spAssignHint = new TextBlock { FontSize = 11, Foreground = EditorTheme.Warning, Margin = new Thickness(10, 0, 10, 4), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        Grid.SetRow(_spAssignHint, 1);
        root.Children.Add(_spAssignHint);

        _spAtlasPanel = new WrapPanel { Margin = new Thickness(6, 0, 6, 4) };
        Grid.SetRow(_spAtlasPanel, 2);
        root.Children.Add(_spAtlasPanel);

        _spGrid = new WrapPanel { Margin = new Thickness(6) };
        var gridScroll = new ScrollViewer { Content = _spGrid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(gridScroll, 3);
        root.Children.Add(gridScroll);

        var bottom = new StackPanel();
        _spMore = EditorTheme.ToolButton(Loc.T("Layout.Sprites.More"), (_, _) => { _spShown += 120; RefreshSprites(); });
        _spMore.Margin = new Thickness(8, 2, 8, 4);
        _spMore.Visibility = Visibility.Collapsed;
        bottom.Children.Add(_spMore);
        _spHits = new StackPanel { Margin = new Thickness(8, 0, 8, 8) };
        bottom.Children.Add(_spHits);
        Grid.SetRow(bottom, 4);
        root.Children.Add(bottom);

        return root;
    }

    private void RefreshSprites()
    {
        if (_spAtlasPanel == null) return;
        _spAtlasPanel.Children.Clear();

        if (_assignTarget is { } at)
        {
            _spAssignHint.Text = Loc.F("Layout.Sprites.Assigning", NodeLabel(at.Node), at.Key);
            _spAssignHint.Visibility = Visibility.Visible;
        }
        else _spAssignHint.Visibility = Visibility.Collapsed;

        var all = LayoutImageProvider.AtlasNames;
        var query = (_spSearch.Text ?? "").Trim().ToLowerInvariant();
        var names = new List<(string Name, string Atlas)>();
        var perAtlas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var atlas in all)
        {
            var defs = LayoutImageProvider.AtlasImages(atlas);
            perAtlas[atlas] = defs.Count;
            if (_spAtlasFilter != null && !string.Equals(atlas, _spAtlasFilter, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var def in defs)
            {
                if (query.Length > 0 && !def.Name.ToLowerInvariant().Contains(query) && !LayoutImageProvider.AtlasDisplayName(atlas).ToLowerInvariant().Contains(query)) continue;
                names.Add((def.Name, atlas));
            }
        }

        _spAtlasPanel.Children.Add(EditorTheme.Chip(Loc.F("Layout.Sprites.All", names.Count), _spAtlasFilter == null, (_, _) => { _spAtlasFilter = null; _spShown = 120; RefreshSprites(); }));
        foreach (var atlas in all)
        {
            string a = atlas;
            bool sel = string.Equals(atlas, _spAtlasFilter, StringComparison.OrdinalIgnoreCase);
            // 搜索时只列有命中的图集，避免几十个空芯片
            if (query.Length > 0 && !sel && !names.Any(x => string.Equals(x.Atlas, a, StringComparison.OrdinalIgnoreCase))) continue;
            _spAtlasPanel.Children.Add(EditorTheme.Chip($"{LayoutImageProvider.AtlasDisplayName(atlas)} ({perAtlas[atlas]})", sel, (_, _) =>
            {
                _spAtlasFilter = string.Equals(_spAtlasFilter, a, StringComparison.OrdinalIgnoreCase) ? null : a;
                _spShown = 120;
                RefreshSprites();
            }));
        }

        _spCount.Text = Loc.F("Layout.Sprites.Count", names.Count);

        _spGrid.Children.Clear();
        var usageByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in UsageMap()) usageByName[u.Name] = (usageByName.TryGetValue(u.Name, out var c) ? c : 0) + 1;

        foreach (var (name, atlas) in names.Take(_spShown))
        {
            bool isSel = string.Equals(name, _spSelectedName, StringComparison.OrdinalIgnoreCase);
            var cell = new Border
            {
                Width = 82,
                Margin = new Thickness(3),
                Padding = new Thickness(4),
                Cursor = Cursors.Hand,
                CornerRadius = new CornerRadius(4),
                Background = isSel ? EditorTheme.Selection : EditorTheme.Bg2,
                BorderBrush = isSel ? EditorTheme.Accent : EditorTheme.Border,
                BorderThickness = new Thickness(1),
                ToolTip = $"{name}\n{LayoutImageProvider.AtlasDisplayName(atlas)}"
            };
            var sp = new StackPanel();
            sp.Children.Add(new Image { Width = 64, Height = 64, Stretch = Stretch.Uniform, Source = HdAtlasImageLoader.Load(atlas, name) });
            var cap = new Grid { Margin = new Thickness(0, 3, 0, 0) };
            cap.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cap.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cap.Children.Add(new TextBlock { Text = name, Foreground = EditorTheme.TextMuted, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis });
            if (usageByName.TryGetValue(name, out var refCount) && refCount > 0)
            {
                var badge = EditorTheme.Badge(refCount.ToString(), EditorTheme.Selection);
                badge.ToolTip = Loc.F("Layout.Sprites.Refs", refCount);
                badge.Margin = new Thickness(3, 0, 0, 0);
                Grid.SetColumn(badge, 1);
                cap.Children.Add(badge);
            }
            sp.Children.Add(cap);
            cell.Child = sp;
            string nm = name;
            cell.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2 && _selected is { IsVirtual: false } target && LayoutSchema.Get(target.Tag).ImageKeys.FirstOrDefault() is { } primaryKey)
                {
                    SetAttr(target, primaryKey, nm);
                    RenderProps();
                    return;
                }
                SelectSprite(nm);
            };
            _spGrid.Children.Add(cell);
        }

        _spMore.Visibility = names.Count > _spShown ? Visibility.Visible : Visibility.Collapsed;
        if (_spSelectedName != null) RenderSpriteHits(_spSelectedName);
    }

    private void SelectSprite(string name)
    {
        // 选图模式：填回属性并回到属性页
        if (_assignTarget is { } at)
        {
            _assignTarget = null;
            SetAttr(at.Node, at.Key, name);
            ShowPropsTab();
            RenderProps();
            SetStatus(Loc.F("Layout.Sprites.Assigned", name, NodeLabel(at.Node), at.Key));
            return;
        }

        _spSelectedName = name;
        RefreshSprites();

        var hits = UsageMap().Where(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (hits.Count > 0)
        {
            var first = hits[0];
            if (!ReferenceEquals(first.Form, _form)) { _form = first.Form; _doc.BuildExpansion(first.Form); RefreshFormList(); }
            SelectNode(first.Node);
            CenterOnNode(first.Node);
            SetStatus(Loc.F("Layout.Sprites.UsedBy", name, hits.Count, first.FormId, NodeLabel(first.Node), first.Key));
        }
        else
        {
            SetStatus(Loc.F("Layout.Sprites.NotUsed", name));
        }
    }

    private void RenderSpriteHits(string name)
    {
        _spHits.Children.Clear();
        var hits = UsageMap().Where(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();

        var header = new Grid { Margin = new Thickness(0, 4, 0, 2) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock
        {
            Text = hits.Count > 0 ? Loc.F("Layout.Sprites.HitsTitle", name, hits.Count) : Loc.F("Layout.Sprites.NotUsed", name),
            Foreground = EditorTheme.Accent,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        header.Children.Add(title);
        var closeBtn = EditorTheme.GhostButton("×", (_, _) => { _spSelectedName = null; _spHits.Children.Clear(); RefreshSprites(); });
        closeBtn.Padding = new Thickness(6, 0, 6, 0);
        closeBtn.MinHeight = 20;
        closeBtn.ToolTip = Loc.T("Common.Close");
        Grid.SetColumn(closeBtn, 1);
        header.Children.Add(closeBtn);
        _spHits.Children.Add(header);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        int shown = 0;
        foreach (var h in hits)
        {
            string label = $"{h.FormId} · {NodeLabel(h.Node)} · {h.Key}";
            if (!seen.Add(label)) continue;
            if (shown++ >= 60) break;

            var item = new TextBlock
            {
                Text = label,
                Foreground = EditorTheme.TextMuted,
                FontSize = 11,
                Padding = new Thickness(4, 2, 4, 2),
                Cursor = Cursors.Hand,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var node = h.Node;
            var form = h.Form;
            item.MouseEnter += (_, _) => item.Foreground = EditorTheme.Text;
            item.MouseLeave += (_, _) => item.Foreground = EditorTheme.TextMuted;
            item.MouseLeftButtonDown += (_, _) =>
            {
                if (!ReferenceEquals(form, _form)) { _form = form; _doc.BuildExpansion(form); RefreshFormList(); }
                SelectNode(node);
                CenterOnNode(node);
            };
            _spHits.Children.Add(item);
        }
    }

    private void InvalidateUsage() => _usageMap = null;

    /// <summary>全文档的"图片名 → 引用位置"表（图库徽章 / 反查用），改图片属性后失效重建。</summary>
    private List<(string FormId, LayoutNode Form, LayoutNode Node, string Key, string Name)> UsageMap()
    {
        if (_usageMap != null) return _usageMap;

        var map = new List<(string, LayoutNode, LayoutNode, string, string)>();
        foreach (var form in _doc.Forms)
        {
            string fid = form.Get("id") ?? "";
            foreach (var n in LayoutDocument.Walk(form))
            {
                foreach (var kv in n.Attributes)
                {
                    if (!ImageKeys.Contains(kv.Key)) continue;
                    var key = kv.Value.Replace('\\', '/').Split('/')[^1];
                    if (key.Length == 0) continue;
                    map.Add((fid, form, n, kv.Key, key));
                }
            }
        }

        _usageMap = map;
        return map;
    }
}
