using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SkiaSharp.Views.WPF;
using WC4MapEditor.Core.Parsers;

namespace WC4MapEditor.Views.Dialogs;

public sealed class ObjectPropertiesDialog : IDisposable
{
    private readonly Window _owner;
    private readonly TacticalMapObject _target;
    private readonly int _srcMaxX;
    private readonly int _srcMaxY;
    private Border? _overlay;
    private TaskCompletionSource<bool>? _tcs;
    private bool _isClosed;

    // 输入控件
    private TextBox? _nameBox;
    private TextBox? _xBox;
    private TextBox? _yBox;
    private TextBox? _wBox;
    private TextBox? _hBox;
    private TextBox? _refXBox;
    private TextBox? _refYBox;
    private TextBlock? _errorBlock;

    public ObjectPropertiesDialog(Window owner, TacticalMapObject target, int srcWidth = int.MaxValue, int srcHeight = int.MaxValue)
    {
        _owner = owner;
        _target = target;
        _srcMaxX = Math.Max(1, srcWidth - 1);
        _srcMaxY = Math.Max(1, srcHeight - 1);
    }

    public Task<bool> ShowAsync()
    {
        _tcs = new TaskCompletionSource<bool>();
        _isClosed = false;

        var content = _owner.Content as FrameworkElement;
        Panel rootPanel;
        if (content is Panel panel)
            rootPanel = panel;
        else if (content?.Parent is Panel parentPanel)
            rootPanel = parentPanel;
        else
            throw new InvalidOperationException("Cannot find a suitable root panel for the dialog overlay.");

        _overlay = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)),
            Child = CreateDialogPanel(),
            Focusable = true
        };
        _overlay.KeyDown += Overlay_KeyDown;
        Panel.SetZIndex(_overlay, 9999);
        rootPanel.Children.Add(_overlay);

        _nameBox?.Focus();
        _nameBox?.SelectAll();

        return _tcs.Task;
    }

    private UIElement CreateDialogPanel()
    {
        var dialogPanel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(26),
            Width = 520,
            Effect = new DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 6,
                Opacity = 0.55,
                Color = Colors.Black
            },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var root = new StackPanel();

        // 标题
        root.Children.Add(new TextBlock
        {
            Text = "编辑对象属性",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 4)
        });

        // 对象名称（只读辅助说明）
        root.Children.Add(new TextBlock
        {
            Text = $"对象矩形在源图: ({_target.X}, {_target.Y}) + {_target.Width}x{_target.Height}  ref=({_target.RefX}, {_target.RefY})",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 170)),
            Margin = new Thickness(0, 0, 0, 16),
            TextWrapping = TextWrapping.Wrap
        });

        // ---------- 网格布局 2列 ----------
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        int row = 0;

        // ---- 名称 ----
        grid.RowDefinitions[row].Height = GridLength.Auto;
        grid.Children.Add(MakeLabel("名称", 0, row, 4));
        _nameBox = MakeTextBox(_target.Name, hasLabel: false);
        Grid.SetColumn(_nameBox, 0); Grid.SetRow(_nameBox, row);
        Grid.SetColumnSpan(_nameBox, 4);
        _nameBox.Margin = new Thickness(0, 4, 0, 14);
        grid.Children.Add(_nameBox);
        row++;

        // ---- 坐标 X / Y ----
        grid.Children.Add(MakeLabel("X:", 0, row));
        _xBox = MakeTextBox(_target.X.ToString());
        Grid.SetColumn(_xBox, 1); Grid.SetRow(_xBox, row);
        grid.Children.Add(_xBox);

        grid.Children.Add(MakeLabel("Y:", 2, row));
        _yBox = MakeTextBox(_target.Y.ToString());
        Grid.SetColumn(_yBox, 3); Grid.SetRow(_yBox, row);
        grid.Children.Add(_yBox);
        row++;

        // ---- 尺寸 W / H ----
        grid.Children.Add(MakeLabel("宽度:", 0, row));
        _wBox = MakeTextBox(_target.Width.ToString());
        Grid.SetColumn(_wBox, 1); Grid.SetRow(_wBox, row);
        grid.Children.Add(_wBox);

        grid.Children.Add(MakeLabel("高度:", 2, row));
        _hBox = MakeTextBox(_target.Height.ToString());
        Grid.SetColumn(_hBox, 3); Grid.SetRow(_hBox, row);
        grid.Children.Add(_hBox);
        row++;

        // ---- 参考点 Rx / Ry ----
        grid.Children.Add(MakeLabel("Ref X:", 0, row));
        _refXBox = MakeTextBox(_target.RefX.ToString());
        Grid.SetColumn(_refXBox, 1); Grid.SetRow(_refXBox, row);
        grid.Children.Add(_refXBox);

        grid.Children.Add(MakeLabel("Ref Y:", 2, row));
        _refYBox = MakeTextBox(_target.RefY.ToString());
        Grid.SetColumn(_refYBox, 3); Grid.SetRow(_refYBox, row);
        grid.Children.Add(_refYBox);
        row++;

        root.Children.Add(grid);

        // 快捷说明
        root.Children.Add(new TextBlock
        {
            Text = "提示: 整数坐标。参考点 (Ref X/Y) 是图像的「对齐锚点」，通常设为 (0,0) 或 (宽/2, 高) 等。",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 165)),
            Margin = new Thickness(0, 12, 0, 6),
            TextWrapping = TextWrapping.Wrap
        });

        // 错误提示
        _errorBlock = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 120, 120)),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(_errorBlock);

        // 底部按钮
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0)
        };

        var resetBtn = new Button
        {
            Content = "重置",
            FontSize = 12,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(90, 80, 120)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        resetBtn.Click += (s, e) => ResetToOriginal();
        buttons.Children.Add(resetBtn);

        var cancelBtn = new Button
        {
            Content = "取消",
            FontSize = 12,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(85, 85, 90)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(18, 6, 18, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        cancelBtn.Click += (s, e) => Cancel();
        buttons.Children.Add(cancelBtn);

        var okBtn = new Button
        {
            Content = "确定",
            FontSize = 12,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(0, 122, 204)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(24, 6, 24, 6),
            Cursor = Cursors.Hand
        };
        okBtn.Click += (s, e) => Confirm();
        buttons.Children.Add(okBtn);

        root.Children.Add(buttons);
        dialogPanel.Child = root;
        return dialogPanel;
    }

    private static TextBlock MakeLabel(string text, int col, int row, int colSpan = 1)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 230)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 6, 8, 0)
        };
        Grid.SetColumn(tb, col);
        Grid.SetRow(tb, row);
        if (colSpan > 1) Grid.SetColumnSpan(tb, colSpan);
        return tb;
    }

    private TextBox MakeTextBox(string value, bool hasLabel = true)
    {
        var tb = new TextBox
        {
            Text = value,
            FontSize = 13,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(28, 28, 32)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 95)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            CaretBrush = Brushes.White,
            Margin = hasLabel ? new Thickness(0, 4, 10, 0) : new Thickness(0)
        };
        tb.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter) { e.Handled = true; Confirm(); }
            else if (e.Key == Key.Escape) { e.Handled = true; Cancel(); }
        };
        return tb;
    }

    private void ResetToOriginal()
    {
        if (_nameBox != null) _nameBox.Text = _target.Name;
        if (_xBox != null) _xBox.Text = _target.X.ToString();
        if (_yBox != null) _yBox.Text = _target.Y.ToString();
        if (_wBox != null) _wBox.Text = _target.Width.ToString();
        if (_hBox != null) _hBox.Text = _target.Height.ToString();
        if (_refXBox != null) _refXBox.Text = _target.RefX.ToString();
        if (_refYBox != null) _refYBox.Text = _target.RefY.ToString();
        if (_errorBlock != null) { _errorBlock.Text = ""; _errorBlock.Visibility = Visibility.Collapsed; }
    }

    private void Overlay_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Cancel();
        }
    }

    private void SetError(string msg)
    {
        if (_errorBlock == null) return;
        _errorBlock.Text = "⚠ " + msg;
        _errorBlock.Visibility = string.IsNullOrEmpty(msg) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Confirm()
    {
        SetError("");
        if (string.IsNullOrWhiteSpace(_nameBox?.Text))
        {
            SetError("名称不能为空");
            return;
        }
        string name = _nameBox!.Text.Trim();

        static bool ReadInt(TextBox? box, string fieldName, int min, int max, out int val, out string? err)
        {
            val = 0; err = null;
            if (box == null) { err = $"{fieldName}控件丢失"; return false; }
            if (!int.TryParse(box.Text.Trim(), out val))
            {
                err = $"{fieldName}: 必须是整数 (收到 '{box.Text}')";
                return false;
            }
            if (val < min) { err = $"{fieldName}: 不能小于 {min} (收到 {val})"; return false; }
            if (val > max) { err = $"{fieldName}: 不能大于 {max} (收到 {val})"; return false; }
            return true;
        }

        if (!ReadInt(_xBox, "X", 0, _srcMaxX, out int x, out var e1)) { SetError(e1!); return; }
        if (!ReadInt(_yBox, "Y", 0, _srcMaxY, out int y, out var e2)) { SetError(e2!); return; }
        if (!ReadInt(_wBox, "宽度", 1, _srcMaxX + 1 - x, out int w, out var e3)) { SetError(e3!); return; }
        if (!ReadInt(_hBox, "高度", 1, _srcMaxY + 1 - y, out int h, out var e4)) { SetError(e4!); return; }

        if (!ReadInt(_refXBox, "Ref X", 0, w, out int rx, out var e5)) { SetError(e5!); return; }
        if (!ReadInt(_refYBox, "Ref Y", 0, h, out int ry, out var e6)) { SetError(e6!); return; }

        // 应用到目标对象
        _target.Name = name;
        _target.X = x;
        _target.Y = y;
        _target.Width = w;
        _target.Height = h;
        _target.RefX = rx;
        _target.RefY = ry;

        CloseDialog(true);
    }

    private void Cancel() => CloseDialog(false);

    private void CloseDialog(bool accepted)
    {
        if (_isClosed) return;
        _isClosed = true;

        var content = _owner.Content as FrameworkElement;
        if (content is Panel rootPanel && _overlay != null)
            rootPanel.Children.Remove(_overlay);

        RestoreOwnerFocus();
        _owner.Dispatcher.BeginInvoke(RestoreOwnerFocus, System.Windows.Threading.DispatcherPriority.Input);
        _tcs?.SetResult(accepted);
    }

    private void RestoreOwnerFocus()
    {
        var skElement = FindVisualChild<SKElement>(_owner);
        if (skElement != null) Keyboard.Focus(skElement);
        else _owner.Focus();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) return result;
            var d = FindVisualChild<T>(child);
            if (d != null) return d;
        }
        return null;
    }

    public void Dispose()
    {
        if (!_isClosed) CloseDialog(false);
    }
}