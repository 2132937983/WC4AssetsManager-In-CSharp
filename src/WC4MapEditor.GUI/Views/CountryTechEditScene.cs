using System.IO;
using System.Diagnostics;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Parsers.Country;
using WC4MapEditor.GUI.Views.Dialogs;
using WC4MapEditor.GUI.Views.EditScenes;
using WC4MapEditor.Rendering.Imaging;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 国家科技树编辑场景（对应 Python 工具的 CountryTech 编辑器，CountryTechSettings.json）。
/// 左：按分类筛选的列表；中：科技树画布（拖动节点、右键连接 / 解绑 / 新增）；右：属性与 INI 文本。
/// 名称 / 介绍按 Type 写入 stringtable 的 countrytech_{Type} / countrytech_desc_{Type}。
/// </summary>
public class CountryTechEditScene : JsonEditorScene
{
    private const double MinScale = 0.35, MaxScale = 2.5, NodeRadius = 35;

    private readonly CountryTechSettingParser _parser = CountryTechSettingParser.Instance;
    private readonly GroupFilter _categories;
    private readonly TableEditor<CountryTechData> _editor;
    private readonly RecordForm<CountryTechData> _iniForm = new(120);
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent, ClipToBounds = true };
    private Button _propTabBtn = null!, _iniTabBtn = null!;
    private bool _showIniPanel;

    // ---- 科技图标图集（image_countrytech_hd，按 Type 索引）----
    private readonly Dictionary<int, ImageSource> _techIcons = new();

    private bool _showNeedGuides = true;
    private bool _autoUpdateLines = true;
    private double _scale = 1, _offsetX = 720, _offsetY = 90;
    private bool _panning, _draggingNode;
    private Point _panStart;
    private double _panStartOffX, _panStartOffY;
    private Point _dragStart;
    private CountryTechData? _dragNode;
    private int _ghostLevel, _ghostPosition;
    private int _saveWarnings;

    public CountryTechEditScene(MainWindow window) : base(window, "CountryTech", CountryTechSettingParser.Instance)
    {
        _categories = new GroupFilter(Loc.T("CountryTech.AllCategories"), c => Loc.F("CountryTech.CategoryN", c));
        _editor = Register(new TableEditor<CountryTechData>(this, _parser.Table, new TableEditorOptions<CountryTechData>
        {
            Describe = t => Loc.F("CountryTech.ListItem", t.Id, ListName(t), t.CategoryId, t.ResearchLv),
            Matches = (t, q) => t.Id.ToString().Contains(q)
                                || _parser.GetTechNameByType(t.Type).Contains(q, StringComparison.OrdinalIgnoreCase)
                                || t.Type.ToString().Contains(q)
                                || t.CategoryId.ToString().Contains(q),
            Filter = t => _categories.Matches(t.CategoryId),
            GetId = t => t.Id,
            SetId = (t, id) => t.Id = id,
            NextId = copy => _parser.GetNextIdInCategory(copy.CategoryId),
            Remove = _parser.Remove,
            DeleteNote = Loc.T("JsonEditor.CountryTech.DeleteNote"),
            SearchPlaceholder = Loc.T("CountryTech.SearchPlaceholder"),
            Header = CategoryHeader()
        }, 160));
        BuildForm(_editor.Form);
        BuildIniForm();
        LoadTechIcons();

        _categories.Changed += () =>
        {
            _editor.RefreshList();
            FitView();
        };
        _editor.CurrentChanged += current =>
        {
            if (_showIniPanel) _iniForm.Load(current);
        };
        _editor.List.SelectionChanged += (_, _) => RenderCanvas();
        _editor.FieldEdited += OnFieldEdited;
        _editor.RecordsChanged += () =>
        {
            RefreshCategories();
            RenderCanvas();
        };

        Content = BuildLayout();
        var firstCategory = _parser.Items.Select(t => t.CategoryId).DefaultIfEmpty(-1).Min();
        _categories.Refresh(_parser.Items.Select(t => t.CategoryId), firstCategory);
        _editor.SelectFirst();
        Loaded += (_, _) => Dispatcher.BeginInvoke(FitView, DispatcherPriority.Loaded);
    }

    private string TypeName(CountryTechData t) => _parser.GetTechNameByType(t.Type);

    private string ListName(CountryTechData t)
    {
        var name = TypeName(t);
        return string.IsNullOrEmpty(name) ? Loc.F("CountryTech.UnnamedTech", t.Id) : name;
    }

    private void RefreshCategories() => _categories.Refresh(_parser.Items.Select(t => t.CategoryId));

    /// <summary>列表上方：分类下拉框 + 新建 / 删除分类（科技组）按钮，下面一行是按 NeedId 重写本组连线。</summary>
    private StackPanel CategoryHeader()
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(_categories.Combo);
        var add = CategoryButton("＋", "CountryTech.Group.AddTip", OnAddCategory);
        Grid.SetColumn(add, 1);
        row.Children.Add(add);
        var remove = CategoryButton("－", "CountryTech.Group.DeleteTip", OnDeleteCategory);
        Grid.SetColumn(remove, 2);
        row.Children.Add(remove);

        var rewrite = EditorTheme.ToolButton(Loc.T("CountryTech.RewriteLines"), (_, _) => OnRewriteCategoryLines());
        rewrite.ToolTip = Loc.T("CountryTech.RewriteLines.Tip");
        rewrite.Focusable = false;
        rewrite.HorizontalAlignment = HorizontalAlignment.Left;
        rewrite.Margin = new Thickness(8, 6, 0, 0);

        var header = new StackPanel();
        header.Children.Add(row);
        header.Children.Add(rewrite);
        return header;
    }

    private static Button CategoryButton(string text, string tipKey, Action action)
    {
        var button = EditorTheme.ToolButton(text, (_, _) => action());
        button.ToolTip = Loc.T(tipKey);
        button.Focusable = false;
        button.Margin = new Thickness(0, 8, 8, 0);
        return button;
    }

    // ============================================================= 布局 =============================================================

    private Grid BuildLayout()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });

        // 顶栏：两个勾选项直接改字段再重绘
        var top = CreateTopBar(Loc.T("CountryTech.Title"),
            SceneButton(Loc.T("CountryTech.GenerateLines"), OnGenerateLines),
            SceneButton(Loc.T("CountryTech.FitView"), FitView),
            EditorTheme.CheckBox(Loc.T("CountryTech.ShowNeedGuides"), _showNeedGuides, v => { _showNeedGuides = v; RenderCanvas(); }),
            EditorTheme.CheckBox(Loc.T("CountryTech.AutoLines"), _autoUpdateLines, v => _autoUpdateLines = v));
        Grid.SetColumnSpan(top, 5);
        root.Children.Add(top);

        Grid.SetRow(_editor.ListPanel, 1);
        root.Children.Add(_editor.ListPanel);
        var leftSplitter = EditorTheme.VSplitter();
        Grid.SetRow(leftSplitter, 1);
        Grid.SetColumn(leftSplitter, 1);
        root.Children.Add(leftSplitter);

        // 中间：科技树画布
        _canvas.MouseLeftButtonDown += Canvas_MouseDown;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseLeftButtonUp += Canvas_MouseUp;
        _canvas.MouseRightButtonDown += Canvas_MouseRightButtonDown;
        _canvas.MouseDown += Canvas_MiddleDown;
        _canvas.MouseUp += Canvas_MiddleUp;
        _canvas.MouseWheel += Canvas_MouseWheel;
        _canvas.SizeChanged += (_, _) => RenderCanvas();
        var canvasBorder = new Border
        {
            Background = EditorTheme.Bg0,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 0, 1, 0),
            ClipToBounds = true,
            Child = _canvas
        };
        Grid.SetRow(canvasBorder, 1);
        Grid.SetColumn(canvasBorder, 2);
        root.Children.Add(canvasBorder);
        var canvasSplitter = EditorTheme.VSplitter();
        Grid.SetRow(canvasSplitter, 1);
        Grid.SetColumn(canvasSplitter, 3);
        root.Children.Add(canvasSplitter);

        // 右栏：页签条（基本属性 / INI 文本）+ 两个可切换的表单
        var rightGrid = new Grid();
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        _propTabBtn = EditorTheme.TabButton(Loc.T("CountryTech.Tab.Props"), true, (_, _) => ShowIniPanel(false));
        _iniTabBtn = EditorTheme.TabButton(Loc.T("CountryTech.Tab.Ini"), false, (_, _) => ShowIniPanel(true));
        tabs.Children.Add(_propTabBtn);
        tabs.Children.Add(_iniTabBtn);
        rightGrid.Children.Add(new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6, 4, 6, 4),
            Child = tabs
        });
        Grid.SetRow(_editor.Form.View, 1);
        Grid.SetRow(_iniForm.View, 1);
        _iniForm.View.Visibility = Visibility.Collapsed;
        rightGrid.Children.Add(_editor.Form.View);
        rightGrid.Children.Add(_iniForm.View);
        var right = EditorTheme.Panel();
        right.Child = rightGrid;
        Grid.SetRow(right, 1);
        Grid.SetColumn(right, 4);
        root.Children.Add(right);

        var status = CreateStatusBar();
        Grid.SetRow(status, 2);
        Grid.SetColumnSpan(status, 5);
        root.Children.Add(status);
        return root;
    }

    private static Button SceneButton(string text, Action action)
    {
        var b = EditorTheme.Button(text, (_, _) => action());
        b.Focusable = false;
        return b;
    }

    private void BuildForm(RecordForm<CountryTechData> f)
    {
        f.Section(Loc.T("Common.BasicInfo"));
        f.KeyInt(Loc.T("CountryTech.Field.Id"), t => t.Id, (t, id) => _parser.ChangeId(t, id) ? null : Loc.F("JsonEditor.Error.IdTaken", id));
        f.StringText(Loc.T("JsonEditor.CountryTech.NameIni"), TypeName, (t, v) => _parser.SetTechNameByType(t.Type, v));
        f.Int(Loc.T("CountryTech.Field.CategoryId"), t => t.CategoryId, (t, v) => t.CategoryId = v);
        f.Int(Loc.T("CountryTech.Field.Categorys"), t => t.Categorys, (t, v) => t.Categorys = v);
        f.Int(Loc.T("CountryTech.Field.Type"), t => t.Type, (t, v) => t.Type = v);
        f.Int(Loc.T("CountryTech.Field.ResearchLv"), t => t.ResearchLv, (t, v) => t.ResearchLv = v);
        f.Int(Loc.T("CountryTech.Field.Position"), t => t.Position, (t, v) => t.Position = v);

        f.Section(Loc.T("CountryTech.Section.Cost"));
        f.Int(Loc.T("CountryTech.Field.CostMoney"), t => t.CostMoney, (t, v) => t.CostMoney = v);
        f.Int(Loc.T("CountryTech.Field.CostGear"), t => t.CostGear, (t, v) => t.CostGear = v);
        f.Int(Loc.T("CountryTech.Field.CostAtomic"), t => t.CostAtomic, (t, v) => t.CostAtomic = v);
        f.Int(Loc.T("CountryTech.Field.CostMerit"), t => t.CostMerit, (t, v) => t.CostMerit = v);

        f.Section(Loc.T("CountryTech.Section.Other"));
        f.Int(Loc.T("CountryTech.Field.Chance"), t => t.Chance, (t, v) => t.Chance = v);
        f.Int(Loc.T("CountryTech.Field.Value"), t => t.Value, (t, v) => t.Value = v);

        f.Section(Loc.T("CountryTech.Section.Links"));
        f.IntList(Loc.T("CountryTech.Field.NeedId"), t => t.NeedId, (t, v) => t.NeedId = v);
        f.DoubleList(Loc.T("CountryTech.Field.Lines"), t => t.Lines, (t, v) => t.Lines = v);
        f.DoubleList(Loc.T("CountryTech.Field.Lines2"), t => t.Lines2, (t, v) => t.Lines2 = v);

        f.JsonPreview(height: 220);
    }

    /// <summary>INI 文本页签：按 Type 显示 / 修改 countrytech_{Type} 与 countrytech_desc_{Type}。</summary>
    private void BuildIniForm()
    {
        var f = _iniForm;
        f.Section(Loc.T("CountryTech.IniSection"));
        var typeText = EditorTheme.Muted("");
        typeText.Margin = new Thickness(10, 8, 10, 4);
        f.Add(typeText);
        f.OnLoad(t => typeText.Text = t == null ? "" : Loc.F("CountryTech.IniTypeLabel", t.Type));
        f.StringText(Loc.T("CountryTech.IniName"), TypeName, (t, v) => _parser.SetTechNameByType(t.Type, v));
        f.StringText(Loc.T("CountryTech.IniDesc"), t => _parser.GetTechDescByType(t.Type), (t, v) => _parser.SetTechDescByType(t.Type, v), multiline: true);
        f.FieldChanged += (t, label, _) =>
        {
            Session.CommitLater(Loc.F("JsonEditor.History.Edit", _editor.Tag(t), label));
            SetStatus(Loc.F("JsonEditor.Status.Edited", _editor.Tag(t), label));
            _editor.RefreshRows();
            RenderCanvas();
        };
    }

    private void ShowIniPanel(bool ini)
    {
        _showIniPanel = ini;
        _editor.Form.View.Visibility = ini ? Visibility.Collapsed : Visibility.Visible;
        _iniForm.View.Visibility = ini ? Visibility.Visible : Visibility.Collapsed;
        EditorTheme.SetTabActive(_propTabBtn, !ini);
        EditorTheme.SetTabActive(_iniTabBtn, ini);
        // 两个页签都能改名称：切换时按记录当前的值重新显示
        if (ini) _iniForm.Load(_editor.Current);
        else _editor.Form.Reload();
    }

    private void OnFieldEdited(CountryTechData tech, string label)
    {
        if (label == Loc.T("CountryTech.Field.Type")) _editor.Form.Reload();
        else if (label == Loc.T("CountryTech.Field.CategoryId")) RefreshCategories();
        if (label == Loc.T("JsonEditor.CountryTech.NameIni") || label == Loc.T("CountryTech.Field.Type")) _editor.RefreshRows();
        // 路径坐标必须成对：奇数个时马上提醒（保存也会被校验拦下）
        if (label == Loc.T("CountryTech.Field.Lines") && tech.Lines.Count % 2 != 0) SetStatus(Loc.F("CountryTech.OddCoordsStatus", "Lines", tech.Lines.Count));
        else if (label == Loc.T("CountryTech.Field.Lines2") && tech.Lines2.Count % 2 != 0) SetStatus(Loc.F("CountryTech.OddCoordsStatus", "Lines2", tech.Lines2.Count));
        RenderCanvas();
    }

    /// <summary>双击画布上的节点：切到“基本属性”页签并把光标放进第一个输入框，直接开始改属性。</summary>
    private void FocusProperties()
    {
        if (_showIniPanel) ShowIniPanel(false);
        _editor.Form.FocusFirst();
    }

    protected override IReadOnlyList<string> InvalidFields() => _editor.Form.InvalidFields;

    protected override void OnStateReplaced()
    {
        base.OnStateReplaced();
        if (_showIniPanel) _iniForm.Load(_editor.Current);
    }

    protected override void OnTextsReloaded()
    {
        base.OnTextsReloaded();
        if (_showIniPanel) _iniForm.Reload();
        RenderCanvas();
    }

    /// <summary>Ctrl+0 适配视图，PageUp / PageDown 切换分类（跳过“全部”）。</summary>
    protected override bool HandleSceneKey(Key key, ModifierKeys modifiers, bool textFocused)
    {
        if (textFocused) return false;
        if (modifiers == ModifierKeys.Control && key is Key.D0 or Key.NumPad0)
        {
            FitView();
            return true;
        }
        if (modifiers != ModifierKeys.None) return false;
        if (key == Key.PageUp) { _categories.Step(-1); return true; }
        if (key == Key.PageDown) { _categories.Step(1); return true; }
        return false;
    }

    // ============================================================= 操作 =============================================================

    /// <summary>当前操作的分类：选了分类就用它；选“全部”时用当前节点的分类（没有时用第一个分类）。</summary>
    private int CurrentCategoryId()
        => !_categories.IsAll ? _categories.Selected : _editor.Current?.CategoryId ?? _parser.Items.Select(t => t.CategoryId).DefaultIfEmpty(1).Min();

    /// <summary>工具栏“新增”：在当前分类第 1 级里第一个空位上建节点（位置 0、1、-1、2、-2…）。</summary>
    protected override void OnAddCommand()
    {
        int categoryId = CurrentCategoryId();
        var taken = _parser.Items.Where(t => t.CategoryId == categoryId && t.ResearchLv == 1).Select(t => t.Position).ToHashSet();
        int position = 0;
        for (int step = 1; taken.Contains(position); step++) position = step % 2 == 1 ? (step + 1) / 2 : -(step / 2);
        AddAt(1, position);
    }

    /// <summary>
    /// 在当前分类的指定网格位置新增节点：对话框里选前置、填 Type（同分类内唯一）；
    /// 有前置时按网格位置生成连线。名称按 Type 共享，新增节点不写文本。
    /// </summary>
    private void AddAt(int level, int position)
    {
        Session.Flush();
        int categoryId = CurrentCategoryId();
        var inCategory = _parser.Items.Where(t => t.CategoryId == categoryId).ToList();
        if (inCategory.Any(t => t.ResearchLv == level && t.Position == position))
        {
            EditorDialogs.Warning(DialogOwner, Loc.T("CountryTech.MoveConflictTitle"), Loc.T("CountryTech.CellOccupied"));
            return;
        }
        int suggestedType = 101;
        while (_parser.IsTypeTaken(categoryId, suggestedType)) suggestedType++;
        var selected = _editor.Current is { } current && current.CategoryId == categoryId ? current : null;
        var choice = TechNodeDialog.Show(DialogOwner, categoryId, level, position, inCategory, selected, suggestedType,
            type => _parser.IsTypeTaken(categoryId, type), _parser.GetTechNameByType);
        if (choice == null) return;

        var tech = _parser.CreateNode(categoryId, level, position, choice.Type, choice.Parent);
        _editor.Insert(tech, Loc.F("JsonEditor.History.Add", tech.Id));
        SetStatus(Loc.F("CountryTech.Added", tech.Id));
    }

    /// <summary>新建分类（科技组）：输入 CategoryId，建一个根节点（第 1 级、位置 0）。</summary>
    private async void OnAddCategory()
    {
        Session.Flush();
        using var dialog = new SingleInputDialog(AppWindow)
        {
            Title = Loc.T("CountryTech.Group.AddTitle"),
            Description = Loc.T("CountryTech.Group.AddPrompt"),
            DefaultValue = ""
        };
        var text = (await dialog.ShowAsync())?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        if (!int.TryParse(text, out int categoryId))
        {
            EditorDialogs.Error(DialogOwner, Loc.T("Common.Error"), Loc.T("CountryTech.Group.Invalid"));
            return;
        }
        if (_parser.HasCategory(categoryId))
        {
            EditorDialogs.Warning(DialogOwner, Loc.T("Common.Hint"), Loc.F("CountryTech.Group.Exists", categoryId));
            return;
        }
        var root = _parser.CreateNode(categoryId, 1, 0, 101, null);
        _editor.Insert(root, Loc.F("CountryTech.Group.AddHistory", categoryId));
        // 切到新分类：下拉框的程序性切换不触发列表刷新，这里手动刷新列表并适配视图
        _categories.Refresh(_parser.Items.Select(t => t.CategoryId), categoryId);
        _editor.Select(root);
        FitView();
        SetStatus(Loc.F("CountryTech.Group.Added", categoryId, root.Id));
    }

    /// <summary>删除当前分类的全部节点（其它分类对它们的 NeedId 引用一并去掉）。</summary>
    private void OnDeleteCategory()
    {
        if (_categories.IsAll)
        {
            EditorDialogs.Info(DialogOwner, Loc.T("Common.Hint"), Loc.T("CountryTech.Group.SelectFirst"));
            return;
        }
        int categoryId = _categories.Selected;
        int count = _parser.Items.Count(t => t.CategoryId == categoryId);
        if (!EditorDialogs.Confirm(DialogOwner, Loc.T("CountryTech.Group.Delete"), Loc.F("CountryTech.Group.DeleteConfirm", categoryId, count),
                Loc.T("Common.Delete"), danger: true))
            return;
        Session.Flush();
        _parser.RemoveCategory(categoryId);
        _editor.Refresh();
        Session.Commit(Loc.F("CountryTech.Group.DeleteHistory", categoryId));
        SetStatus(Loc.F("CountryTech.Group.Deleted", categoryId, count));
    }

    /// <summary>按 NeedId 重写当前分类所有节点的连线（会覆盖现有路径，先确认）。</summary>
    private void OnRewriteCategoryLines()
    {
        if (_categories.IsAll)
        {
            EditorDialogs.Info(DialogOwner, Loc.T("Common.Hint"), Loc.T("CountryTech.Group.SelectFirst"));
            return;
        }
        int categoryId = _categories.Selected;
        int candidates = _parser.Items.Count(t => t.CategoryId == categoryId && t.NeedId.Count > 0);
        if (candidates == 0)
        {
            EditorDialogs.Info(DialogOwner, Loc.T("Common.Hint"), Loc.T("CountryTech.RewriteNone"));
            return;
        }
        if (!EditorDialogs.Confirm(DialogOwner, Loc.T("CountryTech.RewriteLines"), Loc.F("CountryTech.RewriteConfirm", categoryId, candidates)))
            return;
        Session.Flush();
        var result = _parser.RewriteCategoryLines(categoryId);
        _editor.Form.Reload();
        RenderCanvas();
        Session.Commit(Loc.F("CountryTech.RewriteHistory", categoryId));
        var message = Loc.F("CountryTech.RewriteDone", result.Changed, result.Candidates);
        if (result.Skipped.Count > 0) message += Environment.NewLine + Loc.F("CountryTech.RewriteSkipped", string.Join(", ", result.Skipped));
        SetStatus(message.Replace(Environment.NewLine, " "));
        EditorDialogs.Info(DialogOwner, Loc.T("CountryTech.RewriteLines"), message);
    }

    private void OnGenerateLines()
    {
        var tech = _editor.Current;
        if (tech == null)
        {
            EditorDialogs.Info(DialogOwner, Loc.T("Common.Hint"), Loc.T("Common.SelectItemFirst"));
            return;
        }
        if (tech.NeedId.Count == 0)
        {
            EditorDialogs.Info(DialogOwner, Loc.T("Common.Hint"), Loc.T("CountryTech.NoNeedId"));
            return;
        }
        Session.Flush();
        int applied = 0;
        var first = _parser.GetById(tech.NeedId[0]);
        if (first != null)
        {
            tech.Lines = TechTreeGeometry.MakeOrthogonalPath(first, tech);
            applied++;
        }
        else
        {
            SetStatus(Loc.F("CountryTech.NeedIdNotFound", tech.NeedId[0]));
        }
        if (tech.NeedId.Count >= 2 && _parser.GetById(tech.NeedId[1]) is { } second)
        {
            tech.Lines2 = TechTreeGeometry.MakeOrthogonalPath(second, tech);
            applied++;
        }
        _editor.Form.Reload();
        RenderCanvas();
        Session.Commit(Loc.F("CountryTech.LinesGenerated", applied));
        SetStatus(Loc.F("CountryTech.LinesGenerated", applied));
    }

    protected override void OnValidateCommand()
    {
        var issues = _parser.Validate();
        ShowValidation(issues.Select(FormatIssue).ToList(), _parser.Items.Count);
    }

    private static string FormatIssue(TechIssue issue)
        => $"{Loc.T(issue.Severity == TechIssueSeverity.Error ? "CountryTech.Validate.Error" : "CountryTech.Validate.Warning")} {issue.Text}";

    /// <summary>有错误的科技树不能保存（游戏读表会出错）；警告不阻断，保存后在状态栏提示条数。</summary>
    protected override string? CheckBeforeSave()
    {
        var issues = _parser.Validate();
        _saveWarnings = issues.Count(i => i.Severity == TechIssueSeverity.Warning);
        var errors = issues.Where(i => i.Severity == TechIssueSeverity.Error).Select(FormatIssue).ToList();
        if (errors.Count == 0) return null;
        var shown = string.Join(Environment.NewLine, errors.Take(12));
        if (errors.Count > 12) shown += Environment.NewLine + Loc.F("Common.ValidationMore", errors.Count, 12);
        return Loc.F("CountryTech.SaveBlocked", errors.Count, shown);
    }

    protected override void OnSaved()
    {
        if (_saveWarnings > 0) SetStatus(Loc.F("CountryTech.SavedWithWarnings", string.Join(", ", Session.FileNames), _saveWarnings));
    }

    // ============================================================= 科技树画布 =============================================================

    private static (double wx, double wy) Grid2World(double level, double position) => TechTreeGeometry.GridToWorld(level, position);
    private Point W2S(double wx, double wy) => new(wx * _scale + _offsetX, wy * _scale + _offsetY);
    private (double wx, double wy) S2W(double x, double y) => ((x - _offsetX) / _scale, (y - _offsetY) / _scale);

    private (int level, int position) EventGrid(Point screen)
    {
        var (wx, wy) = S2W(screen.X, screen.Y);
        int level = Math.Max(1, (int)Math.Round(wy / TechTreeGeometry.NodeStepY) + 1);
        int position = (int)Math.Round(wx / TechTreeGeometry.NodeStepX);
        return (level, position);
    }

    private List<CountryTechData> VisibleNodes() => _parser.Items.Where(t => _categories.Matches(t.CategoryId)).ToList();

    private void FitView()
    {
        var nodes = VisibleNodes();
        int pmin = 0, pmax = 0, lmin = 1, lmax = 1;
        if (nodes.Count == 0) { pmax = 4; lmax = 4; }
        else
        {
            foreach (var n in nodes)
            {
                pmin = Math.Min(pmin, n.Position); pmax = Math.Max(pmax, n.Position);
                lmin = Math.Min(lmin, n.ResearchLv); lmax = Math.Max(lmax, n.ResearchLv);
            }
            pmin -= 1; pmax += 1; lmin = Math.Max(1, lmin - 1); lmax += 1;
        }
        var g1 = Grid2World(lmin, pmin);
        var g2 = Grid2World(lmax, pmax);
        double width = Math.Max(g2.wx - g1.wx, 1), height = Math.Max(g2.wy - g1.wy, 1);
        double cw = Math.Max(_canvas.ActualWidth, 400), ch = Math.Max(_canvas.ActualHeight, 300);
        _scale = Math.Max(MinScale, Math.Min(1.35, Math.Min((cw - 180) / (width + 160), (ch - 130) / (height + 160))));
        _offsetX = cw / 2 - (g1.wx + g2.wx) * _scale / 2;
        _offsetY = 55 - g1.wy * _scale;
        RenderCanvas();
    }

    private void AddText(double wx, double wy, string text, Brush fg, double size)
    {
        var p = W2S(wx, wy);
        var tb = new TextBlock { Text = text, Foreground = fg, FontSize = size };
        Canvas.SetLeft(tb, p.X);
        Canvas.SetTop(tb, p.Y);
        _canvas.Children.Add(tb);
    }

    private void AddScreenTextPair(double x, double y, string left, Brush leftFg, double leftSize, string right, Brush rightFg, double rightSize)
    {
        var tb = new TextBlock { TextWrapping = TextWrapping.NoWrap };
        tb.Inlines.Add(new Run { Text = left, Foreground = leftFg, FontSize = leftSize });
        tb.Inlines.Add(new Run { Text = right, Foreground = rightFg, FontSize = rightSize });
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(tb, x - tb.DesiredSize.Width / 2);
        Canvas.SetTop(tb, y);
        _canvas.Children.Add(tb);
    }

    private void RenderCanvas()
    {
        _canvas.Children.Clear();
        var nodes = VisibleNodes();
        var selected = new HashSet<CountryTechData>(_editor.SelectedItems, ReferenceEqualityComparer.Instance);

        int pmin = 0, pmax = 0, lmin = 1, lmax = 1;
        if (nodes.Count == 0) { pmax = 4; lmax = 4; }
        else
        {
            foreach (var n in nodes)
            {
                pmin = Math.Min(pmin, n.Position); pmax = Math.Max(pmax, n.Position);
                lmin = Math.Min(lmin, n.ResearchLv); lmax = Math.Max(lmax, n.ResearchLv);
            }
            pmin -= 1; pmax += 1; lmin = Math.Max(1, lmin - 1); lmax += 1;
        }

        // 网格坐标标签 L(等级) / P(位置) 是坐标记号，不做翻译
        for (int level = lmin; level <= lmax; level++)
        {
            var g = Grid2World(level, pmin - 0.45);
            AddText(g.wx, g.wy, "L" + level, EditorTheme.TextDim, 12);
        }
        for (int pos = pmin; pos <= pmax; pos++)
        {
            var g = Grid2World(lmin - 0.45, pos);
            AddText(g.wx, g.wy, "P" + pos, EditorTheme.TextDim, 12);
        }

        var occupied = nodes.Select(n => (n.ResearchLv, n.Position)).ToHashSet();
        for (int level = lmin; level <= lmax; level++)
        {
            for (int pos = pmin; pos <= pmax; pos++)
            {
                if (occupied.Contains((level, pos))) continue;
                var gp = Grid2World(level, pos);
                var p = W2S(gp.wx, gp.wy);
                var r = Math.Max(3, 5 * _scale);
                var dot = new Ellipse { Width = 2 * r, Height = 2 * r, Stroke = EditorTheme.BorderStrong };
                Canvas.SetLeft(dot, p.X - r);
                Canvas.SetTop(dot, p.Y - r);
                _canvas.Children.Add(dot);
            }
        }

        foreach (var node in nodes)
        {
            DrawStoredLine(node, node.Lines);
            DrawStoredLine(node, node.Lines2);
        }

        if (_showNeedGuides)
        {
            foreach (var node in nodes)
            {
                foreach (var pid in node.NeedId)
                {
                    var parent = _parser.GetById(pid);
                    if (parent == null || !_categories.Matches(parent.CategoryId)) continue;
                    var ag = Grid2World(node.ResearchLv, node.Position);
                    var bg = Grid2World(parent.ResearchLv, parent.Position);
                    _canvas.Children.Add(new Polyline
                    {
                        Points = new PointCollection { W2S(ag.wx, ag.wy), W2S(bg.wx, bg.wy) },
                        Stroke = EditorTheme.Warning,
                        StrokeThickness = Math.Max(1, 2 * _scale),
                        StrokeDashArray = new DoubleCollection { 5, 4 }
                    });
                }
            }
        }

        foreach (var node in nodes)
            DrawNode(node, selected.Contains(node));

        if (_draggingNode && _dragNode != null)
        {
            var gp = Grid2World(_ghostLevel, _ghostPosition);
            var p = W2S(gp.wx, gp.wy);
            var r = NodeRadius * _scale;
            var ghost = new Ellipse { Width = 2 * r, Height = 2 * r, Stroke = EditorTheme.Danger, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 4 } };
            Canvas.SetLeft(ghost, p.X - r);
            Canvas.SetTop(ghost, p.Y - r);
            _canvas.Children.Add(ghost);
        }
    }

    private void DrawStoredLine(CountryTechData node, List<double> path)
    {
        if (path.Count < 2 || path.Count % 2 != 0) return;
        var g = Grid2World(node.ResearchLv, node.Position);
        var prev = new Point(g.wx, g.wy);
        var pts = new PointCollection { W2S(prev.X, prev.Y) };
        for (int i = 0; i + 1 < path.Count; i += 2)
        {
            double px = g.wx + path[i] * TechTreeGeometry.LineScaleX;
            double py = g.wy + path[i + 1] * TechTreeGeometry.LineScaleY;
            if (prev.X != px && prev.Y != py) pts.Add(W2S(prev.X, py));
            pts.Add(W2S(px, py));
            prev = new Point(px, py);
        }
        _canvas.Children.Add(new Polyline
        {
            Points = pts,
            Stroke = EditorTheme.Info,
            StrokeThickness = Math.Max(1, 4 * _scale)
        });
    }

    // 用 AM 获取 image_countrytech_hd 图集，借 tac 解析器(TacticalMapEditor)解析 xml，
    // 按 Type 裁出每个科技的子图缓存，供 DrawNode 显示。
    private void LoadTechIcons()
    {
        try
        {
            var am = AssetManager.Default;
            if (!am.IsLoaded) am.ScanDefault();
            var pngEntry = am.Find("image/image_countrytech_hd.png");
            var xmlEntry = am.Find("image/image_countrytech_hd.xml");
            if (pngEntry == null || xmlEntry == null)
            {
                Debug.WriteLine("[CountryTech] 图集资源 image_countrytech_hd 未找到");
                return;
            }

            var editor = new TacticalMapEditor();
            if (!editor.LoadFromFiles(pngEntry.FullPath, xmlEntry.FullPath) || editor.ImageData == null)
            {
                Debug.WriteLine("[CountryTech] 图集解析失败");
                return;
            }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(editor.ImageData);
            bmp.EndInit();
            bmp.Freeze();

            foreach (var obj in editor.Objects)
            {
                int type = ParseTechTypeFromName(obj.Name);
                if (type < 0) continue;
                int x = Math.Max(0, obj.X);
                int y = Math.Max(0, obj.Y);
                int w = Math.Min(obj.Width, bmp.PixelWidth - x);
                int h = Math.Min(obj.Height, bmp.PixelHeight - y);
                if (w <= 0 || h <= 0) continue;
                var cropped = new CroppedBitmap(bmp, new Int32Rect(x, y, w, h));
                cropped.Freeze();
                _techIcons[type] = cropped;
            }
            Debug.WriteLine($"[CountryTech] 加载科技图标 {_techIcons.Count} 个");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CountryTech] 加载图集失败: {ex.Message}");
        }
    }

    private static int ParseTechTypeFromName(string name)
    {
        const string prefix = "countrytech_";
        int i = name.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return -1;
        string s = name[(i + prefix.Length)..];
        int dot = s.IndexOf('.');
        if (dot >= 0) s = s[..dot];
        return int.TryParse(s, out int t) ? t : -1;
    }

    private void DrawNode(CountryTechData node, bool selected)
    {
        var g = Grid2World(node.ResearchLv, node.Position);
        var center = W2S(g.wx, g.wy);
        var radius = NodeRadius * _scale;
        var name = TypeName(node);
        if (string.IsNullOrEmpty(name)) name = Loc.F("CountryTech.UnnamedType", node.Type);

        if (_techIcons.TryGetValue(node.Type, out var icon) && icon is CroppedBitmap cb)
        {
            double imgH = 2 * radius;
            double imgW = imgH * cb.Width / cb.Height;
            var img = new Image { Source = icon, Width = imgW, Height = imgH, Stretch = Stretch.Fill };
            Canvas.SetLeft(img, center.X - imgW / 2);
            Canvas.SetTop(img, center.Y - imgH / 2);
            _canvas.Children.Add(img);
            if (selected)
            {
                var sel = new Rectangle
                {
                    Width = imgW + 4,
                    Height = imgH + 4,
                    Stroke = EditorTheme.Accent,
                    StrokeThickness = Math.Max(1.5, 2.5 * _scale),
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(sel, center.X - sel.Width / 2);
                Canvas.SetTop(sel, center.Y - sel.Height / 2);
                _canvas.Children.Add(sel);
            }
            AddScreenTextPair(center.X, center.Y + imgH / 2 + 2, node.Id + "  ", EditorTheme.TextMuted, 12, name, EditorTheme.Text, 11);
            return;
        }

        var pts = new PointCollection();
        for (int i = 0; i < 6; i++)
        {
            double a = Math.PI / 180 * (60 * i - 30);
            pts.Add(new Point(center.X + radius * Math.Sin(a), center.Y - radius * Math.Cos(a)));
        }
        _canvas.Children.Add(new Polygon
        {
            Points = pts,
            Fill = selected ? EditorTheme.Selection : EditorTheme.Bg3,
            Stroke = selected ? EditorTheme.Accent : EditorTheme.BorderStrong,
            StrokeThickness = Math.Max(1, 2 * _scale)
        });
        AddScreenTextPair(center.X, center.Y + 14, node.Id + "  ", EditorTheme.TextMuted, 12, name, EditorTheme.Text, 11);
    }

    // 用屏幕距离命中：遍历当前分类节点，点击落在节点半径内即命中。
    // 不依赖 VisualTreeHelper.HitTest（对 Image 命中不可靠，选中态节点拖不动）。
    private CountryTechData? FindNodeAt(Point p)
    {
        CountryTechData? best = null;
        double bestDist = NodeRadius * _scale;
        foreach (var n in VisibleNodes())
        {
            var g = Grid2World(n.ResearchLv, n.Position);
            var s = W2S(g.wx, g.wy);
            double dx = p.X - s.X, dy = p.Y - s.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d < bestDist) { bestDist = d; best = n; }
        }
        return best;
    }

    // 右键空白处弹出菜单：在此新增节点；右键节点弹出菜单：连接至此节点（设置 NeedId）
    private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pt = e.GetPosition(_canvas);
        var hit = FindNodeAt(pt);
        var menu = new ContextMenu();
        // 右键菜单是独立弹层，拿不到场景树上的隐式样式，需把主题字典合并进来
        menu.Resources.MergedDictionaries.Add(EditorTheme.Dictionary);
        if (hit != null)
        {
            var name = TypeName(hit);
            if (string.IsNullOrEmpty(name)) name = Loc.F("CountryTech.UnnamedType", hit.Type);
            var item = new MenuItem { Header = Loc.F("CountryTech.Menu.ConnectHere", hit.Id, name) };
            item.Click += (_, _) => ConnectToNode(hit);
            menu.Items.Add(item);
            var unlink = new MenuItem { Header = Loc.T("CountryTech.Menu.Unlink") };
            unlink.Click += (_, _) => DisconnectNodes();
            menu.Items.Add(unlink);
        }
        else
        {
            var (level, position) = EventGrid(pt);
            var item = new MenuItem { Header = Loc.F("CountryTech.Menu.AddHere", level, position) };
            item.Click += (_, _) => AddAt(level, position);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    /// <summary>把所有选中子节点的 NeedId 设为 target.Id（父级节点为被连接者）。</summary>
    private void ConnectToNode(CountryTechData target)
    {
        var children = _editor.SelectedItems.Where(t => !ReferenceEquals(t, target)).ToList();
        if (children.Count == 0)
        {
            EditorDialogs.Info(DialogOwner, Loc.T("Common.Hint"), Loc.T("CountryTech.ConnectHint"));
            return;
        }
        Session.Flush();
        foreach (var child in children)
        {
            child.NeedId = new List<int> { target.Id };
            if (_autoUpdateLines) UpdateConnectedLines(child.Id);
        }
        AfterCanvasEdit(Loc.F("CountryTech.Connected", children.Count, target.Id));
    }

    /// <summary>清空所有选中节点的 NeedId（断开与父级的连接）。</summary>
    private void DisconnectNodes()
    {
        var nodes = _editor.SelectedItems;
        if (nodes.Count == 0)
        {
            EditorDialogs.Info(DialogOwner, Loc.T("Common.Hint"), Loc.T("CountryTech.UnlinkHint"));
            return;
        }
        Session.Flush();
        foreach (var n in nodes)
        {
            n.NeedId.Clear();
            n.Lines.Clear();
            n.Lines2.Clear();
        }
        AfterCanvasEdit(Loc.F("CountryTech.Unlinked", nodes.Count));
    }

    private void AfterCanvasEdit(string message)
    {
        _editor.RefreshRows();
        _editor.Form.Reload();
        RenderCanvas();
        Session.Commit(message);
        SetStatus(message);
    }

    // 中键：任何位置都是平移视图
    private void Canvas_MiddleDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        _panning = true;
        _panStart = e.GetPosition(_canvas);
        _panStartOffX = _offsetX;
        _panStartOffY = _offsetY;
        _canvas.CaptureMouse();
    }

    private void Canvas_MiddleUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || !_panning) return;
        _panning = false;
        _canvas.ReleaseMouseCapture();
    }

    // 左键：点节点=拖动，点空白=平移视图
    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pt = e.GetPosition(_canvas);
        var node = FindNodeAt(pt);
        if (node != null && e.ClickCount == 2)
        {
            FocusProperties();
            return;
        }
        if (node != null)
        {
            _dragNode = node;
            _draggingNode = false;
            _dragStart = pt;
        }
        else
        {
            _panning = true;
            _panStart = pt;
            _panStartOffX = _offsetX;
            _panStartOffY = _offsetY;
        }
        _canvas.CaptureMouse();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        var pt = e.GetPosition(_canvas);
        if (_panning)
        {
            _offsetX = _panStartOffX + (pt.X - _panStart.X);
            _offsetY = _panStartOffY + (pt.Y - _panStart.Y);
            RenderCanvas();
            return;
        }
        if (_dragNode == null) return;
        if (!_draggingNode && (Math.Abs(pt.X - _dragStart.X) > 4 || Math.Abs(pt.Y - _dragStart.Y) > 4))
            _draggingNode = true;
        if (_draggingNode)
        {
            (_ghostLevel, _ghostPosition) = EventGrid(pt);
            RenderCanvas();
        }
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _canvas.ReleaseMouseCapture();
        if (_panning)
        {
            _panning = false;
            return;
        }
        var node = _dragNode;
        if (node == null) return;
        bool dragged = _draggingNode;
        _dragNode = null;
        _draggingNode = false;

        if (dragged)
        {
            var (level, position) = EventGrid(e.GetPosition(_canvas));
            bool conflict = _parser.Items.Any(t => !ReferenceEquals(t, node) && t.CategoryId == node.CategoryId && t.ResearchLv == level && t.Position == position);
            if (conflict)
            {
                EditorDialogs.Warning(DialogOwner, Loc.T("CountryTech.MoveConflictTitle"), Loc.T("CountryTech.MoveConflict"));
            }
            else if (level != node.ResearchLv || position != node.Position)
            {
                Session.Flush();
                node.ResearchLv = level;
                node.Position = position;
                int changed = _autoUpdateLines ? UpdateConnectedLines(node.Id) : 0;
                _editor.Select(node);
                AfterCanvasEdit(Loc.F("CountryTech.NodeMoved", changed));
                return;
            }
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _editor.ToggleSelection(node);
        }
        else
        {
            _editor.Select(node);
        }
        RenderCanvas();
    }

    private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.12 : 0.892857;
        double newScale = Math.Max(MinScale, Math.Min(MaxScale, _scale * factor));
        if (Math.Abs(newScale - _scale) < 0.0001) return;
        var pt = e.GetPosition(_canvas);
        var (wx, wy) = S2W(pt.X, pt.Y);
        _scale = newScale;
        _offsetX = pt.X - wx * _scale;
        _offsetY = pt.Y - wy * _scale;
        RenderCanvas();
    }

    /// <summary>按 NeedId 重建被拖节点及其直接子节点的 Lines/Lines2（与 Python update_connected_lines 一致）。</summary>
    private int UpdateConnectedLines(int nodeId)
    {
        int changed = 0;
        foreach (var item in _parser.Items)
        {
            var needs = item.NeedId;
            if (needs.Count == 0 || needs.Count > 2) continue;
            if (!needs.Contains(nodeId) && item.Id != nodeId) continue;
            for (int i = 0; i < needs.Count; i++)
            {
                var parent = _parser.GetById(needs[i]);
                if (parent == null) continue;
                var np = TechTreeGeometry.MakeOrthogonalPath(parent, item);
                var cur = i == 0 ? item.Lines : item.Lines2;
                if (cur.SequenceEqual(np)) continue;
                if (i == 0) item.Lines = np;
                else item.Lines2 = np;
                changed++;
            }
            if (needs.Count < 2 && item.Lines2.Count > 0)
            {
                item.Lines2 = new List<double>();
                changed++;
            }
        }
        return changed;
    }
}
