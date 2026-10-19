using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;

namespace WC4MapEditor.GUI.Views.EditScenes;

/// <summary>批量修改的结果：字段名、改动的记录数、因类型不符没改成的记录数。</summary>
public sealed record BatchResult(string Field, int Changed, int Failed);

/// <summary>
/// 批量工具（Ctrl+B）：对当前列表或选中的记录，按字段做查找替换，或统一赋值 / 加减乘除。
/// 字段从记录类型反射得到（整数、小数、布尔、字符串、整数 / 小数列表）；Id 不参与批量修改。
/// </summary>
public static class BatchEditDialog
{
    public static BatchResult? Show<T>(Window? owner, IReadOnlyList<T> selected, IReadOnlyList<T> visible) where T : class
    {
        var fields = BatchField.For(typeof(T));
        if (fields.Count == 0) return null;

        BatchResult? result = null;
        var window = EditorDialogs.CreateWindow(owner, Loc.T("JsonEditor.Batch.Title"), 520);
        var root = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };

        // 页签：查找替换 / 赋值运算
        bool replaceMode = true;
        var replaceTab = EditorTheme.TabButton(Loc.T("JsonEditor.Batch.ReplaceTab"), true);
        var arithTab = EditorTheme.TabButton(Loc.T("JsonEditor.Batch.ArithTab"), false);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        tabs.Children.Add(replaceTab);
        tabs.Children.Add(arithTab);
        root.Children.Add(tabs);

        var fieldCombo = EditorTheme.ComboBox();
        foreach (var f in fields) fieldCombo.Items.Add(f.Name);
        fieldCombo.SelectedIndex = 0;
        root.Children.Add(EditorTheme.FormRow(Loc.T("JsonEditor.Batch.Field"), fieldCombo, 120));

        var findBox = new TextBox();
        var replaceBox = new TextBox();
        var exact = new CheckBox { Content = Loc.T("JsonEditor.Batch.Exact"), IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        var replacePanel = new StackPanel();
        replacePanel.Children.Add(EditorTheme.FormRow(Loc.T("JsonEditor.Batch.Find"), findBox, 120));
        replacePanel.Children.Add(EditorTheme.FormRow(Loc.T("JsonEditor.Batch.ReplaceWith"), replaceBox, 120));
        replacePanel.Children.Add(EditorTheme.FormRow("", exact, 120));
        root.Children.Add(replacePanel);

        var opCombo = EditorTheme.ComboBox();
        foreach (var key in new[] { "Assign", "Add", "Subtract", "Multiply", "Divide" })
            opCombo.Items.Add(Loc.T("JsonEditor.Batch.Op." + key));
        opCombo.SelectedIndex = 0;
        var valueBox = new TextBox();
        var arithPanel = new StackPanel { Visibility = Visibility.Collapsed };
        arithPanel.Children.Add(EditorTheme.FormRow(Loc.T("JsonEditor.Batch.Operation"), opCombo, 120));
        arithPanel.Children.Add(EditorTheme.FormRow(Loc.T("JsonEditor.Batch.Value"), valueBox, 120));
        root.Children.Add(arithPanel);

        void SetMode(bool replace)
        {
            replaceMode = replace;
            EditorTheme.SetTabActive(replaceTab, replace);
            EditorTheme.SetTabActive(arithTab, !replace);
            replacePanel.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
            arithPanel.Visibility = replace ? Visibility.Collapsed : Visibility.Visible;
        }
        replaceTab.Click += (_, _) => SetMode(true);
        arithTab.Click += (_, _) => SetMode(false);

        var allRadio = new RadioButton { Content = Loc.F("JsonEditor.Batch.TargetVisible", visible.Count), IsChecked = true, GroupName = "batchTarget", Margin = new Thickness(0, 0, 16, 0) };
        var selRadio = new RadioButton { Content = Loc.F("JsonEditor.Batch.TargetSelected", selected.Count), GroupName = "batchTarget", IsEnabled = selected.Count > 0 };
        var targets = new StackPanel { Orientation = Orientation.Horizontal };
        targets.Children.Add(allRadio);
        targets.Children.Add(selRadio);
        root.Children.Add(EditorTheme.FormRow(Loc.T("JsonEditor.Batch.Target"), targets, 120));

        var error = new TextBlock { Foreground = EditorTheme.Danger, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        root.Children.Add(error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var apply = EditorTheme.PrimaryButton(Loc.T("Common.Apply"));
        apply.MinWidth = 88;
        apply.IsDefault = true;
        var cancel = EditorTheme.Button(Loc.T("Common.Cancel"), (_, _) => window.Close());
        cancel.MinWidth = 88;
        cancel.IsCancel = true;
        cancel.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(apply);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        apply.Click += (_, _) =>
        {
            var field = fields[fieldCombo.SelectedIndex];
            var items = selRadio.IsChecked == true ? selected : visible;
            int changed = 0, failed = 0;
            if (replaceMode)
            {
                string find = findBox.Text;
                if (find.Length == 0 && exact.IsChecked != true)
                {
                    error.Text = Loc.T("JsonEditor.Batch.FindEmpty");
                    return;
                }
                foreach (var item in items)
                {
                    string current = field.Format(item);
                    string next = exact.IsChecked == true ? (current == find ? replaceBox.Text : current) : current.Replace(find, replaceBox.Text, StringComparison.Ordinal);
                    if (next == current) continue;
                    if (field.TrySet(item, next)) changed++;
                    else failed++;
                }
            }
            else
            {
                var op = (BatchOp)opCombo.SelectedIndex;
                double operand = 0;
                if (op != BatchOp.Assign && !double.TryParse(valueBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out operand))
                {
                    error.Text = Loc.T("JsonEditor.Batch.NotNumber");
                    return;
                }
                if (op == BatchOp.Divide && operand == 0)
                {
                    error.Text = Loc.T("JsonEditor.Batch.DivideByZero");
                    return;
                }
                foreach (var item in items)
                {
                    switch (field.Apply(item, op, operand, valueBox.Text))
                    {
                        case true: changed++; break;
                        case null: failed++; break;
                    }
                }
            }
            if (changed == 0)
            {
                error.Text = failed > 0 ? Loc.F("JsonEditor.Batch.AllFailed", failed) : Loc.T("JsonEditor.Batch.NoChange");
                return;
            }
            result = new BatchResult(field.Name, changed, failed);
            window.Close();
        };

        window.Content = root;
        window.ShowDialog();
        return result;
    }
}

internal enum BatchOp
{
    Assign,
    Add,
    Subtract,
    Multiply,
    Divide
}

/// <summary>可批量修改的一个属性：负责与文本互转和加减乘除。</summary>
internal sealed class BatchField
{
    private enum Kind { Int, Double, Bool, String, IntList, DoubleList }

    private readonly PropertyInfo _property;
    private readonly Kind _kind;

    private BatchField(PropertyInfo property, Kind kind)
    {
        _property = property;
        _kind = kind;
        Name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
    }

    public string Name { get; }

    public static IReadOnlyList<BatchField> For(Type type)
    {
        var result = new List<BatchField>();
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || !p.CanWrite || p.Name == "Id" || p.GetIndexParameters().Length > 0) continue;
            Kind? kind = p.PropertyType switch
            {
                var t when t == typeof(int) => Kind.Int,
                var t when t == typeof(double) => Kind.Double,
                var t when t == typeof(bool) => Kind.Bool,
                var t when t == typeof(string) => Kind.String,
                var t when t == typeof(List<int>) => Kind.IntList,
                var t when t == typeof(List<double>) => Kind.DoubleList,
                _ => null
            };
            if (kind != null) result.Add(new BatchField(p, kind.Value));
        }
        return result;
    }

    public string Format(object item)
    {
        var value = _property.GetValue(item);
        return value switch
        {
            null => "",
            int i => i.ToString(CultureInfo.InvariantCulture),
            double d => d.ToString(CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            List<int> li => ListText.Format(li),
            List<double> ld => ListText.Format(ld),
            _ => value.ToString() ?? ""
        };
    }

    /// <summary>把文本按字段类型写回；类型不符返回 false。</summary>
    public bool TrySet(object item, string text)
    {
        text = text.Trim();
        object? value;
        switch (_kind)
        {
            case Kind.Int:
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return false;
                value = i;
                break;
            case Kind.Double:
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || !double.IsFinite(d)) return false;
                value = d;
                break;
            case Kind.Bool:
                if (text is "1" or "true" or "True" or "yes") value = true;
                else if (text is "0" or "false" or "False" or "no") value = false;
                else return false;
                break;
            case Kind.IntList:
                if (!ListText.TryParseInts(text, out var li, out _)) return false;
                value = li;
                break;
            case Kind.DoubleList:
                if (!ListText.TryParseDoubles(text, out var ld, out _)) return false;
                value = ld;
                break;
            default:
                value = text;
                break;
        }
        _property.SetValue(item, value);
        return true;
    }

    /// <summary>赋值 / 运算：true = 改了，false = 结果相同，null = 这个字段不支持该运算或数值不合法。</summary>
    public bool? Apply(object item, BatchOp op, double operand, string rawValue)
    {
        string before = Format(item);
        if (op == BatchOp.Assign)
        {
            if (!TrySet(item, rawValue)) return null;
            return Format(item) != before;
        }
        switch (_kind)
        {
            case Kind.Int:
                _property.SetValue(item, RoundInt(Calc((int)_property.GetValue(item)!, op, operand)));
                break;
            case Kind.Double:
                _property.SetValue(item, Calc((double)_property.GetValue(item)!, op, operand));
                break;
            case Kind.IntList:
                _property.SetValue(item, ((List<int>?)_property.GetValue(item) ?? new List<int>()).Select(v => RoundInt(Calc(v, op, operand))).ToList());
                break;
            case Kind.DoubleList:
                _property.SetValue(item, ((List<double>?)_property.GetValue(item) ?? new List<double>()).Select(v => Calc(v, op, operand)).ToList());
                break;
            default:
                return null;
        }
        return Format(item) != before;
    }

    private static double Calc(double value, BatchOp op, double operand) => op switch
    {
        BatchOp.Add => value + operand,
        BatchOp.Subtract => value - operand,
        BatchOp.Multiply => value * operand,
        BatchOp.Divide => value / operand,
        _ => operand
    };

    /// <summary>四舍五入（.5 远离零），超出 int 范围时截到边界。</summary>
    private static int RoundInt(double value) => (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);
}
