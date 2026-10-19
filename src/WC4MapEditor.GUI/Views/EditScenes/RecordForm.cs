using System.Globalization;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.GUI.Views.EditScenes;

/// <summary>逗号列表字段的格式化与解析（JSON 里的 int / double 数组）。</summary>
public static class ListText
{
    private static readonly char[] Separators = { ',', '，', ';', '；', ' ', '\t', '\r', '\n' };

    public static string Format(IEnumerable<int>? values) => values == null ? "" : string.Join(", ", values);

    public static string Format(IEnumerable<double>? values)
        => values == null ? "" : string.Join(", ", values.Select(v => v.ToString(CultureInfo.InvariantCulture)));

    /// <summary>解析整数列表；有无法解析的片段时返回 false，<paramref name="invalid"/> 列出这些片段。</summary>
    public static bool TryParseInts(string? text, out List<int> values, out string invalid)
    {
        values = new List<int>();
        var bad = new List<string>();
        foreach (var token in (text ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) values.Add(v);
            else bad.Add(token);
        }
        invalid = string.Join(" ", bad);
        return bad.Count == 0;
    }

    /// <summary>解析小数列表；规则同 <see cref="TryParseInts"/>。</summary>
    public static bool TryParseDoubles(string? text, out List<double> values, out string invalid)
    {
        values = new List<double>();
        var bad = new List<string>();
        foreach (var token in (text ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v)) values.Add(v);
            else bad.Add(token);
        }
        invalid = string.Join(" ", bad);
        return bad.Count == 0;
    }
}

/// <summary>输入框的错误标记：危险色边框 + 说明提示，清除时恢复原来的提示。</summary>
public static class FieldMarks
{
    private static readonly DependencyProperty OriginalToolTipProperty =
        DependencyProperty.RegisterAttached("OriginalToolTip", typeof(object), typeof(FieldMarks));

    private static readonly DependencyProperty ErrorProperty =
        DependencyProperty.RegisterAttached("Error", typeof(string), typeof(FieldMarks));

    public static string? GetError(Control control) => (string?)control.GetValue(ErrorProperty);

    public static void SetError(Control control, string? message)
    {
        bool marked = GetError(control) != null;
        if (message == null)
        {
            if (!marked) return;
            control.ToolTip = control.GetValue(OriginalToolTipProperty);
            control.ClearValue(Control.BorderBrushProperty);
            if (control is NumericUpDown) control.ClearValue(Control.BorderThicknessProperty);
            control.ClearValue(ErrorProperty);
            return;
        }
        if (!marked) control.SetValue(OriginalToolTipProperty, control.ToolTip);
        control.SetValue(ErrorProperty, message);
        control.BorderBrush = EditorTheme.Danger;
        if (control is NumericUpDown) control.BorderThickness = new Thickness(1);
        control.ToolTip = message;
    }
}

/// <summary>
/// 记录表单：按字段声明“怎么读、怎么写”，只把用户真正改动的字段写回记录。
/// <para>
/// 旧场景任何一个输入框变化都会把整张表单写回记录，会悄悄改动没碰过的字段（被控件范围截断的值、null 变空串等），
/// 还会在键入 Id 的每一个中间值时改写按 Id 索引的文本。这里每个字段独立写回；
/// Id 这类主键字段在失去焦点 / 回车时才提交，并由调用方校验（例如 Id 不能重复）。
/// </para>
/// </summary>
public sealed class RecordForm<T> where T : class
{
    private readonly List<(string Label, Action<T?> Load, Control? Input)> _fields = new();
    private readonly double _labelWidth;
    private ScrollViewer? _view;
    private TextBox? _preview;
    private Func<T, object?>? _previewSelector;
    private bool _loading;

    public RecordForm(double labelWidth = 200)
    {
        _labelWidth = labelWidth;
        Panel = new StackPanel();
    }

    /// <summary>字段容器（嵌进别的表单时直接用它）。</summary>
    public StackPanel Panel { get; }

    /// <summary>带滚动条的表单视图（放进场景布局用；第一次取用时才把 <see cref="Panel"/> 放进去）。</summary>
    public ScrollViewer View => _view ??= new ScrollViewer
    {
        Content = Panel,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };

    /// <summary>当前显示的记录。</summary>
    public T? Item { get; private set; }

    /// <summary>某个字段的输入已经写回记录：(记录, 字段名, 是否需要立即记一步历史)。</summary>
    public event Action<T, string, bool>? FieldChanged;

    /// <summary>某个字段的输入被拒绝：(字段名, 原因)。</summary>
    public event Action<string, string>? FieldRejected;

    /// <summary>当前有错误输入（没有写回记录）的字段名。</summary>
    public IReadOnlyList<string> InvalidFields
        => _fields.Where(f => f.Input != null && FieldMarks.GetError(f.Input) != null).Select(f => f.Label).ToList();

    // ============================================================= 布局 =============================================================

    public void Section(string title) => Panel.Children.Add(EditorTheme.SectionHeader(title));

    public void Row(string label, UIElement content, string? tooltip = null)
        => Panel.Children.Add(EditorTheme.FormRow(label, content, _labelWidth, tooltip));

    public void Add(UIElement element) => Panel.Children.Add(element);

    /// <summary>登记一个随记录刷新的自定义部件（只读信息、图片预览等）。</summary>
    public void OnLoad(Action<T?> load) => _fields.Add(("", load, null));

    /// <summary>JSON 预览（只读）。<paramref name="selector"/> 可以换成要预览的对象（默认是记录本身）。</summary>
    public void JsonPreview(Func<T, object?>? selector = null, double height = 200)
    {
        Section(Loc.T("Common.JsonPreview"));
        _preview = EditorTheme.MonoTextBox(height);
        _preview.Margin = new Thickness(10, 8, 10, 12);
        _previewSelector = selector;
        Panel.Children.Add(_preview);
    }

    // ============================================================= 字段 =============================================================

    /// <summary>整数字段：输入即写回（连续输入由历史合并）。范围默认不限制，避免截断游戏数据里的特殊值。</summary>
    public NumericUpDown Int(string label, Func<T, int> get, Action<T, int> set, string? tooltip = null, Func<NumericUpDown, UIElement>? decorate = null)
    {
        var box = BindInt(label, get, set);
        Row(label, decorate?.Invoke(box) ?? box, tooltip);
        return box;
    }

    /// <summary>同一行的两个整数（数值 / 上限 这类成对字段）；<paramref name="extra"/> 在同一行右侧再加一个部件（星级之类）。</summary>
    public (NumericUpDown First, NumericUpDown Second) IntPair(string label, Func<T, int> get, Action<T, int> set, string secondLabel, Func<T, int> getSecond, Action<T, int> setSecond,
        Func<NumericUpDown, NumericUpDown, UIElement>? extra = null)
    {
        var first = BindInt(label, get, set);
        var second = BindInt(secondLabel, getSecond, setSecond);
        first.Width = 100;
        second.Width = 100;
        second.ToolTip = secondLabel;
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(first);
        panel.Children.Add(EditorTheme.Text_("  /  ", 13, EditorTheme.TextMuted));
        panel.Children.Add(second);
        if (extra != null) panel.Children.Add(extra(first, second));
        Row(label, panel);
        return (first, second);
    }

    private NumericUpDown BindInt(string label, Func<T, int> get, Action<T, int> set)
    {
        var box = NewNumber(1);
        box.ValueChanged += (_, v) =>
        {
            if (_loading || Item == null) return;
            int value = ToInt(v);
            if (get(Item) == value) return;
            set(Item, value);
            Raise(label, false);
        };
        Register(label, item => box.Value = item == null ? 0 : get(item), box);
        return box;
    }

    /// <summary>
    /// 主键 / 关联键字段：失去焦点、回车或点微调按钮时才提交。
    /// <paramref name="apply"/> 返回 null 表示已接受，否则返回拒绝原因（输入框恢复原值并标红）。
    /// </summary>
    public NumericUpDown KeyInt(string label, Func<T, int> get, Func<T, int, string?> apply, string? tooltip = null, Func<NumericUpDown, UIElement>? decorate = null)
    {
        var box = NewNumber(1);
        void Commit()
        {
            if (_loading || Item == null) return;
            int value = ToInt(box.Value);
            int old = get(Item);
            if (value == old)
            {
                FieldMarks.SetError(box, null);
                return;
            }
            var error = apply(Item, value);
            if (error != null)
            {
                _loading = true;
                box.Value = old;
                _loading = false;
                FieldMarks.SetError(box, error);
                FieldRejected?.Invoke(label, error);
                return;
            }
            FieldMarks.SetError(box, null);
            Raise(label, true);
        }
        box.ValueChanged += (_, _) => { if (!box.IsKeyboardFocusWithin) Commit(); };
        CommitOnLeave(box, Commit);
        Register(label, item => box.Value = item == null ? 0 : get(item), box);
        Row(label, decorate?.Invoke(box) ?? box, tooltip ?? Loc.T("JsonEditor.Form.KeyFieldTip"));
        return box;
    }

    /// <summary>关联键文本字段（例如将领 EName）：失去焦点 / 回车时才提交，<paramref name="apply"/> 规则同 <see cref="KeyInt"/>。</summary>
    public TextBox KeyText(string label, Func<T, string?> get, Func<T, string, string?> apply, string? tooltip = null)
    {
        var tb = new TextBox();
        void Commit()
        {
            if (_loading || Item == null) return;
            string old = get(Item) ?? "";
            if (tb.Text == old)
            {
                FieldMarks.SetError(tb, null);
                return;
            }
            var error = apply(Item, tb.Text);
            if (error != null)
            {
                _loading = true;
                tb.Text = old;
                _loading = false;
                FieldMarks.SetError(tb, error);
                FieldRejected?.Invoke(label, error);
                return;
            }
            FieldMarks.SetError(tb, null);
            Raise(label, true);
        }
        CommitOnLeave(tb, Commit);
        Register(label, item => tb.Text = item == null ? "" : get(item) ?? "", tb);
        Row(label, tb, tooltip ?? Loc.T("JsonEditor.Form.KeyFieldTip"));
        return tb;
    }

    /// <summary>输入控件失去焦点或按回车时提交。</summary>
    private static void CommitOnLeave(Control input, Action commit)
    {
        input.IsKeyboardFocusWithinChanged += (_, e) => { if (!(bool)e.NewValue) commit(); };
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            commit();
            e.Handled = true;
        };
    }

    /// <summary>小数字段。</summary>
    public NumericUpDown Double(string label, Func<T, double> get, Action<T, double> set, double increment = 0.01, string? tooltip = null)
    {
        var box = NewNumber(increment);
        box.ValueChanged += (_, v) =>
        {
            if (_loading || Item == null) return;
            if (Math.Abs(get(Item) - v) < 1e-12) return;
            set(Item, v);
            Raise(label, false);
        };
        Register(label, item => box.Value = item == null ? 0 : get(item), box);
        Row(label, box, tooltip);
        return box;
    }

    /// <summary>JSON 字符串字段（单行）。读出为 null 时显示空串，不改就不会写回。</summary>
    public TextBox Text(string label, Func<T, string?> get, Action<T, string> set, string? tooltip = null)
    {
        var tb = new TextBox();
        tb.TextChanged += (_, _) =>
        {
            if (_loading || Item == null) return;
            if ((get(Item) ?? "") == tb.Text) return;
            set(Item, tb.Text);
            Raise(label, false);
        };
        Register(label, item => tb.Text = item == null ? "" : get(item) ?? "", tb);
        Row(label, tb, tooltip);
        return tb;
    }

    /// <summary>
    /// 字符串表文本。多行时游戏的字面量 \n 显示为换行，写入时由字符串表转回 \n，
    /// 回车不会把 INI 的一行拆成两行。
    /// </summary>
    public TextBox StringText(string label, Func<T, string> get, Action<T, string> set, bool multiline = false, string? tooltip = null)
    {
        var tb = new TextBox();
        if (multiline)
        {
            tb.TextWrapping = TextWrapping.Wrap;
            tb.AcceptsReturn = true;
            tb.VerticalContentAlignment = VerticalAlignment.Top;
            tb.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            tb.Height = 80;
        }
        string Display(T item) => multiline ? get(item).Replace("\\n", "\n") : get(item);
        tb.TextChanged += (_, _) =>
        {
            if (_loading || Item == null) return;
            if (Display(Item) == tb.Text.Replace("\r\n", "\n")) return;
            set(Item, tb.Text);
            Raise(label, false);
        };
        Register(label, item => tb.Text = item == null ? "" : Display(item), tb);
        Row(label, tb, tooltip);
        return tb;
    }

    /// <summary>整数列表字段：有无法解析的片段时标红并保留原值，不会悄悄丢掉输错的数字。</summary>
    public TextBox IntList(string label, Func<T, List<int>?> get, Action<T, List<int>> set, string? tooltip = null, Func<TextBox, UIElement>? decorate = null)
    {
        var tb = new TextBox();
        tb.TextChanged += (_, _) =>
        {
            if (_loading || Item == null) return;
            if (!ListText.TryParseInts(tb.Text, out var values, out var invalid))
            {
                FieldMarks.SetError(tb, Loc.F("JsonEditor.Form.InvalidNumbers", invalid));
                return;
            }
            FieldMarks.SetError(tb, null);
            if ((get(Item) ?? new List<int>()).SequenceEqual(values)) return;
            set(Item, values);
            Raise(label, false);
        };
        Register(label, item => tb.Text = item == null ? "" : ListText.Format(get(item)), tb);
        Row(label, decorate?.Invoke(tb) ?? tb, tooltip ?? Loc.T("JsonEditor.Form.ListTip"));
        return tb;
    }

    /// <summary>小数列表字段（规则同 <see cref="IntList"/>）。</summary>
    public TextBox DoubleList(string label, Func<T, List<double>?> get, Action<T, List<double>> set, string? tooltip = null)
    {
        var tb = new TextBox();
        tb.TextChanged += (_, _) =>
        {
            if (_loading || Item == null) return;
            if (!ListText.TryParseDoubles(tb.Text, out var values, out var invalid))
            {
                FieldMarks.SetError(tb, Loc.F("JsonEditor.Form.InvalidNumbers", invalid));
                return;
            }
            FieldMarks.SetError(tb, null);
            if ((get(Item) ?? new List<double>()).SequenceEqual(values)) return;
            set(Item, values);
            Raise(label, false);
        };
        Register(label, item => tb.Text = item == null ? "" : ListText.Format(get(item)), tb);
        Row(label, tb, tooltip ?? Loc.T("JsonEditor.Form.ListTip"));
        return tb;
    }

    /// <summary>布尔字段。</summary>
    public CheckBox Bool(string label, Func<T, bool> get, Action<T, bool> set, string? tooltip = null)
    {
        var cb = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
        void Changed()
        {
            if (_loading || Item == null) return;
            bool value = cb.IsChecked == true;
            if (get(Item) == value) return;
            set(Item, value);
            Raise(label, true);
        }
        cb.Checked += (_, _) => Changed();
        cb.Unchecked += (_, _) => Changed();
        Register(label, item => cb.IsChecked = item != null && get(item), cb);
        Row(label, cb, tooltip);
        return cb;
    }

    // ============================================================= 加载 =============================================================

    /// <summary>把键盘焦点放到第一个输入框（双击画布节点后进入属性编辑）。</summary>
    public void FocusFirst()
    {
        var input = _fields.Select(f => f.Input).FirstOrDefault(i => i != null);
        if (input != null && !input.Focus()) input.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    /// <summary>显示一条记录（null = 清空并禁用表单）。</summary>
    public void Load(T? item)
    {
        Item = item;
        _loading = true;
        try
        {
            foreach (var (_, load, input) in _fields)
            {
                load(item);
                if (input != null) FieldMarks.SetError(input, null);
            }
        }
        finally
        {
            _loading = false;
        }
        Panel.IsEnabled = item != null;
        UpdatePreview();
    }

    /// <summary>按记录当前的值重新显示（撤销、批量修改之后）。</summary>
    public void Reload() => Load(Item);

    /// <summary>刷新 JSON 预览。</summary>
    public void UpdatePreview()
    {
        if (_preview == null) return;
        if (Item == null)
        {
            _preview.Text = "";
            return;
        }
        try
        {
            _preview.Text = System.Text.Json.JsonSerializer.Serialize(_previewSelector?.Invoke(Item) ?? Item, GameJson.PreviewOptions);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
            _preview.Text = Loc.T("Common.SerializeFailed");
        }
    }

    // ============================================================= 内部 =============================================================

    private static NumericUpDown NewNumber(double increment)
        => new() { MinValue = int.MinValue, MaxValue = int.MaxValue, Increment = increment };

    private static int ToInt(double v) => (int)Math.Clamp(Math.Round(v, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);

    private void Register(string label, Action<T?> load, Control input) => _fields.Add((label, load, input));

    private void Raise(string label, bool immediate)
    {
        if (Item == null) return;
        UpdatePreview();
        FieldChanged?.Invoke(Item, label, immediate);
    }
}
