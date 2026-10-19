using System.Globalization;
using System.Text;
using WC4MapEditor.Core.Analyzers;

namespace WC4MapEditor.GUI.Views;

/// <summary>
/// 数据检查场景的十六进制视图。
/// <para>
/// 只排版可见的几十行（文件再大也一样快）；字节按所属数据块着色，多记录数据块的奇数记录加一层底纹，
/// 一眼能看出记录边界；单击选中字节，方向键 / PageUp / PageDown / Home / End 移动，滚轮滚动。
/// 行格式与 <see cref="BtlInspectorHex.FormatLine"/> 相同（命令行转储也用它）。
/// 滚动由外部滚动条驱动：读写 <see cref="TopLine"/>，监听 <see cref="ViewChanged"/>。
/// </para>
/// </summary>
internal sealed class DataCheckHexView : FrameworkElement
{
    private const int BytesPerLine = BtlInspectorHex.BytesPerLine;

    /// <summary>字节区起始列（地址 8 位 + 2 个空格）。</summary>
    private const int HexColumn = 10;

    /// <summary>ASCII 区起始列（字节区 16×3 + 中间多出的 1 个空格 + " |"）。</summary>
    private const int AsciiColumn = HexColumn + BytesPerLine * 3 + 1 + 2;

    private const double TextSize = 13;
    private const double SidePadding = 10;

    private readonly Typeface _typeface = new(EditorTheme.MonoFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private readonly Func<BtlBlock, Brush> _blockBrush;
    private readonly Brush _fieldBrush;
    private readonly double _charWidth;
    private readonly double _lineHeight;

    private BtlInspector? _inspector;
    private long _topLine;
    private long _selectionStart = -1;
    private int _selectionLength;
    private long _highlightStart = -1;
    private int _highlightLength;

    public DataCheckHexView(Func<BtlBlock, Brush> blockBrush)
    {
        _blockBrush = blockBrush;
        // 选中字节所在字段：选中色的半透明版（比主题的 AccentSoft 醒目，又不会盖过选中字节本身）
        _fieldBrush = new SolidColorBrush(EditorTheme.ColorOf(EditorTheme.Selection)) { Opacity = 0.5 };
        _fieldBrush.Freeze();
        var probe = Format("0", 1.0);
        _charWidth = probe.WidthIncludingTrailingWhitespace;
        _lineHeight = Math.Ceiling(probe.Height) + 3;

        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.IBeam;
        ClipToBounds = true;
        MinWidth = SidePadding * 2 + (AsciiColumn + BytesPerLine + 1) * _charWidth;
    }

    /// <summary>选中字节变化（参数 = 选中起点；没有选中为 -1）。</summary>
    public event Action<long>? SelectionChanged;

    /// <summary>滚动位置或可见行数变化（外部滚动条据此同步）。</summary>
    public event Action? ViewChanged;

    public long LineCount => _inspector == null ? 0 : (_inspector.Data.LongLength + BytesPerLine - 1) / BytesPerLine;

    public int VisibleLines => Math.Max(1, (int)((ActualHeight - HeaderHeight) / _lineHeight));

    public long MaxTopLine => Math.Max(0, LineCount - VisibleLines);

    public long SelectedOffset => _selectionStart;

    public long TopLine
    {
        get => _topLine;
        set
        {
            long clamped = Math.Clamp(value, 0, MaxTopLine);
            if (clamped == _topLine) return;
            _topLine = clamped;
            InvalidateVisual();
            ViewChanged?.Invoke();
        }
    }

    private double HeaderHeight => _lineHeight + 4;

    public void SetInspector(BtlInspector? inspector)
    {
        _inspector = inspector;
        _topLine = 0;
        _selectionStart = -1;
        _selectionLength = 0;
        _highlightStart = -1;
        _highlightLength = 0;
        InvalidateVisual();
        ViewChanged?.Invoke();
        SelectionChanged?.Invoke(-1);
    }

    /// <summary>选中一段字节；<paramref name="center"/> 为 true 时把它滚到视图上部三分之一处（跳转 / 查找），否则只做最小滚动。</summary>
    public void Select(long offset, int length, bool center)
    {
        if (_inspector == null || _inspector.Data.LongLength == 0) return;

        _selectionStart = Math.Clamp(offset, 0, _inspector.Data.LongLength - 1);
        _selectionLength = Math.Max(1, length);
        BringIntoView(_selectionStart / BytesPerLine, center);
        InvalidateVisual();
        SelectionChanged?.Invoke(_selectionStart);
    }

    /// <summary>浅色标出一段范围（选中字节所在的字段）；长度为 0 表示不标。</summary>
    public void SetHighlight(long start, int length)
    {
        _highlightStart = start;
        _highlightLength = length;
        InvalidateVisual();
    }

    private void BringIntoView(long line, bool center)
    {
        int visible = VisibleLines;
        if (center)
        {
            if (line < _topLine || line >= _topLine + visible) TopLine = line - visible / 3;
        }
        else if (line < _topLine)
        {
            TopLine = line;
        }
        else if (line >= _topLine + visible)
        {
            TopLine = line - visible + 1;
        }
    }

    // ============================================================= 绘制 =============================================================

    private FormattedText Format(string text, double pixelsPerDip, Brush? brush = null)
        => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, TextSize, brush ?? EditorTheme.TextMuted, pixelsPerDip);

    private static int HexColumnOf(int index) => HexColumn + index * 3 + (index >= 8 ? 1 : 0);

    private double X(double column) => SidePadding + column * _charWidth;

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _topLine = Math.Clamp(_topLine, 0, MaxTopLine);
        InvalidateVisual();
        ViewChanged?.Invoke();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(EditorTheme.Bg0, null, new Rect(RenderSize));
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        DrawColumnHeader(dc, pixelsPerDip);

        var inspector = _inspector;
        if (inspector == null) return;

        byte[] data = inspector.Data;
        var cursor = new SegmentCursor(inspector.NonEmptySegments, _topLine * BytesPerLine);
        int visible = VisibleLines + 1;   // 多画一行，底部半行也有内容

        for (int r = 0; r < visible; r++)
        {
            long lineStart = (_topLine + r) * BytesPerLine;
            if (lineStart >= data.LongLength) break;

            int count = (int)Math.Min(BytesPerLine, data.LongLength - lineStart);
            double y = HeaderHeight + r * _lineHeight;
            var text = Format(BtlInspectorHex.FormatLine(data, lineStart), pixelsPerDip);
            text.SetForegroundBrush(EditorTheme.TextDim, 0, 8);

            for (int i = 0; i < count; i++)
            {
                long offset = lineStart + i;
                var segment = cursor.At(offset);
                if (segment != null && IsOddRecord(segment, offset))
                {
                    DrawByteBackground(dc, i, y, EditorTheme.Bg2, extend: true);
                }

                var brush = segment == null ? EditorTheme.TextDim : _blockBrush(segment.Block);
                text.SetForegroundBrush(brush, HexColumnOf(i), 2);
                text.SetForegroundBrush(brush, AsciiColumn + i, 1);
            }

            DrawRange(dc, lineStart, count, y, _highlightStart, _highlightLength, _fieldBrush);
            DrawRange(dc, lineStart, count, y, _selectionStart, _selectionLength, EditorTheme.Selection);
            dc.DrawText(text, new Point(SidePadding, y + 1));
        }
    }

    private void DrawColumnHeader(DrawingContext dc, double pixelsPerDip)
    {
        dc.DrawRectangle(EditorTheme.Bg2, null, new Rect(0, 0, ActualWidth, HeaderHeight - 2));
        var columns = new StringBuilder();
        for (int i = 0; i < BytesPerLine; i++)
        {
            if (i == 8) columns.Append(' ');
            columns.Append(i.ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
        }

        double y = 2;
        dc.DrawText(Format(Loc.T("DataCheck.Hex.ColumnOffset"), pixelsPerDip, EditorTheme.TextDim), new Point(X(0), y));
        dc.DrawText(Format(columns.ToString(), pixelsPerDip, EditorTheme.TextDim), new Point(X(HexColumn), y));
        dc.DrawText(Format("ASCII", pixelsPerDip, EditorTheme.TextDim), new Point(X(AsciiColumn), y));
    }

    private static bool IsOddRecord(BtlSegment segment, long offset)
        => segment.Count > 1 && segment.RecordSize > 1 && (offset - segment.Start) / segment.RecordSize % 2 == 1;

    /// <summary>在字节区和 ASCII 区给第 <paramref name="index"/> 个字节画底色；<paramref name="extend"/> 时连同其后的空格一起画（相邻字节连成带）。</summary>
    private void DrawByteBackground(DrawingContext dc, int index, double y, Brush brush, bool extend)
    {
        double hexStart = HexColumnOf(index) - (extend ? 0.5 : 0.15);
        double hexWidth = extend ? 3 : 2.3;
        dc.DrawRectangle(brush, null, new Rect(X(hexStart), y, hexWidth * _charWidth, _lineHeight));
        dc.DrawRectangle(brush, null, new Rect(X(AsciiColumn + index), y, _charWidth, _lineHeight));
    }

    private void DrawRange(DrawingContext dc, long lineStart, int count, double y, long start, int length, Brush brush)
    {
        if (start < 0 || length <= 0) return;
        long from = Math.Max(start, lineStart);
        long to = Math.Min(start + length, lineStart + count);
        for (long offset = from; offset < to; offset++)
            DrawByteBackground(dc, (int)(offset - lineStart), y, brush, extend: false);
    }

    /// <summary>按偏移递增查询所属数据块（数据块按起点排序，只前进不回退）。</summary>
    private struct SegmentCursor
    {
        private readonly IReadOnlyList<BtlSegment> _segments;
        private int _index;

        public SegmentCursor(IReadOnlyList<BtlSegment> segments, long firstOffset)
        {
            _segments = segments;
            int lo = 0, hi = segments.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >>> 1;
                if (segments[mid].End <= firstOffset) lo = mid + 1;
                else hi = mid;
            }
            _index = lo;
        }

        public BtlSegment? At(long offset)
        {
            while (_index < _segments.Count && offset >= _segments[_index].End) _index++;
            return _index < _segments.Count && offset >= _segments[_index].Start ? _segments[_index] : null;
        }
    }

    // ============================================================= 输入 =============================================================

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (HitTestByte(e.GetPosition(this)) is { } offset) Select(offset, 1, center: false);
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        TopLine -= Math.Sign(e.Delta) * 3;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var inspector = _inspector;
        if (inspector == null || inspector.Data.LongLength == 0) return;

        long length = inspector.Data.LongLength;
        long current = Math.Max(0, _selectionStart);
        long page = (long)VisibleLines * BytesPerLine;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        long? target = e.Key switch
        {
            Key.Left => current - 1,
            Key.Right => current + 1,
            Key.Up => current - BytesPerLine,
            Key.Down => current + BytesPerLine,
            Key.PageUp => current - page,
            Key.PageDown => current + page,
            Key.Home => ctrl ? 0 : current - current % BytesPerLine,
            Key.End => ctrl ? length - 1 : current - current % BytesPerLine + BytesPerLine - 1,
            _ => null
        };
        if (target is not { } t) return;

        Select(Math.Clamp(t, 0, length - 1), 1, center: false);
        e.Handled = true;
    }

    /// <summary>鼠标位置 → 字节偏移（点在字节区或 ASCII 区才算；落在文件之外返回 null）。</summary>
    private long? HitTestByte(Point p)
    {
        var inspector = _inspector;
        if (inspector == null || p.Y < HeaderHeight) return null;

        long line = _topLine + (long)((p.Y - HeaderHeight) / _lineHeight);
        double column = (p.X - SidePadding) / _charWidth;
        int index;
        if (column >= HexColumn - 0.5 && column < AsciiColumn - 2)
        {
            double rel = column - HexColumn + 0.5;
            if (rel >= 8 * 3) rel -= 1;   // 第 8 个字节前多一个空格
            index = (int)(rel / 3);
        }
        else if (column >= AsciiColumn && column < AsciiColumn + BytesPerLine)
        {
            index = (int)(column - AsciiColumn);
        }
        else
        {
            return null;
        }

        long offset = line * BytesPerLine + Math.Clamp(index, 0, BytesPerLine - 1);
        return offset < inspector.Data.LongLength ? offset : null;
    }
}
