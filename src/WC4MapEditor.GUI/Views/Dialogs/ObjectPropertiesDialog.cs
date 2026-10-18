using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using SkiaSharp.Views.WPF;
using WC4MapEditor.Core.Parsers;
using WC4MapEditor.Rendering.Imaging;

namespace WC4MapEditor.GUI.Views.Dialogs;

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
            Background = CreateScrim(),
            Focusable = true
        };
        // 主窗口（渲染场景）本身不套编辑主题，对话框内的输入框/按钮要靠合并到覆盖层的字典拿到深色样式
        EditorTheme.Apply(_overlay);
        _overlay.Child = CreateDialogPanel();
        _overlay.KeyDown += Overlay_KeyDown;
        Panel.SetZIndex(_overlay, 9999);
        // Tab 在对话框内部循环切换焦点，避免焦点跑到覆盖层外面落到主界面的控件上
        KeyboardNavigation.SetTabNavigation(_overlay, KeyboardNavigationMode.Cycle);
        rootPanel.Children.Add(_overlay);

        _nameBox?.Focus();
        _nameBox?.SelectAll();

        return _tcs.Task;
    }

    private UIElement CreateDialogPanel()
    {
        var dialogPanel = new Border
        {
            Background = EditorTheme.Bg2,
            BorderBrush = EditorTheme.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(26),
            Width = 520,
            Effect = new DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 4,
                Opacity = 0.6,
                Color = EditorTheme.ColorOf(EditorTheme.Bg0)
            },
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var root = new StackPanel();

        // 标题
        var title = EditorTheme.Title(Loc.T("Dialog.Props.Title"));
        title.Margin = new Thickness(0, 0, 0, 4);
        root.Children.Add(title);

        // 对象在源图中的位置（只读辅助说明）
        var info = EditorTheme.Muted(Loc.F("Dialog.Props.SourceRect",
            _target.X, _target.Y, _target.Width, _target.Height, _target.RefX, _target.RefY));
        info.FontSize = 11;
        info.Margin = new Thickness(0, 0, 0, 16);
        info.TextWrapping = TextWrapping.Wrap;
        root.Children.Add(info);

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
        grid.Children.Add(MakeLabel(Loc.T("Common.Name"), 0, row, 4));
        _nameBox = MakeTextBox(_target.Name, hasLabel: false);
        Grid.SetColumn(_nameBox, 0); Grid.SetRow(_nameBox, row);
        Grid.SetColumnSpan(_nameBox, 4);
        _nameBox.Margin = new Thickness(0, 4, 0, 14);
        grid.Children.Add(_nameBox);
        row++;

        // ---- 坐标 X / Y ----（X / Y / Ref X / Ref Y 是技术标识，不翻译）
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
        grid.Children.Add(MakeLabel(Loc.T("Common.Width") + ":", 0, row));
        _wBox = MakeTextBox(_target.Width.ToString());
        Grid.SetColumn(_wBox, 1); Grid.SetRow(_wBox, row);
        grid.Children.Add(_wBox);

        grid.Children.Add(MakeLabel(Loc.T("Common.Height") + ":", 2, row));
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
        var hint = EditorTheme.Muted(Loc.T("Dialog.Props.Hint"));
        hint.FontSize = 11;
        hint.Margin = new Thickness(0, 12, 0, 6);
        hint.TextWrapping = TextWrapping.Wrap;
        root.Children.Add(hint);

        // 错误提示
        _errorBlock = new TextBlock
        {
            FontSize = 12,
            Foreground = EditorTheme.Danger,
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

        var resetBtn = EditorTheme.Button(Loc.T("Common.Reset"), (_, _) => ResetToOriginal());
        resetBtn.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(resetBtn);

        var cancelBtn = EditorTheme.Button(Loc.T("Common.Cancel"), (_, _) => Cancel());
        cancelBtn.MinWidth = 80;
        cancelBtn.Margin = new Thickness(0, 0, 8, 0);
        buttons.Children.Add(cancelBtn);

        var okBtn = EditorTheme.PrimaryButton(Loc.T("Common.Ok"), (_, _) => Confirm());
        okBtn.MinWidth = 88;
        buttons.Children.Add(okBtn);

        root.Children.Add(buttons);
        dialogPanel.Child = root;
        return dialogPanel;
    }

    /// <summary>覆盖层的半透明遮罩：由主题最深底色派生，不写死颜色。</summary>
    private static Brush CreateScrim()
    {
        var c = EditorTheme.ColorOf(EditorTheme.Bg0);
        return new SolidColorBrush(Color.FromArgb(150, c.R, c.G, c.B));
    }

    private static TextBlock MakeLabel(string text, int col, int row, int colSpan = 1)
    {
        var tb = EditorTheme.FieldLabel(text);
        tb.Margin = new Thickness(0, 6, 8, 0);
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
            SetError(Loc.T("Dialog.Props.NameRequired"));
            return;
        }
        string name = _nameBox!.Text.Trim();

        static bool ReadInt(TextBox? box, string fieldName, int min, int max, out int val, out string? err)
        {
            val = 0; err = null;
            if (box == null) { err = Loc.F("Dialog.Props.ControlMissing", fieldName); return false; }
            if (!int.TryParse(box.Text.Trim(), out val))
            {
                err = Loc.F("Dialog.Props.MustBeInteger", fieldName, box.Text);
                return false;
            }
            if (val < min) { err = Loc.F("Dialog.Props.TooSmall", fieldName, min, val); return false; }
            if (val > max) { err = Loc.F("Dialog.Props.TooLarge", fieldName, max, val); return false; }
            return true;
        }

        if (!ReadInt(_xBox, "X", 0, _srcMaxX, out int x, out var e1)) { SetError(e1!); return; }
        if (!ReadInt(_yBox, "Y", 0, _srcMaxY, out int y, out var e2)) { SetError(e2!); return; }
        if (!ReadInt(_wBox, Loc.T("Common.Width"), 1, _srcMaxX + 1 - x, out int w, out var e3)) { SetError(e3!); return; }
        if (!ReadInt(_hBox, Loc.T("Common.Height"), 1, _srcMaxY + 1 - y, out int h, out var e4)) { SetError(e4!); return; }

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
