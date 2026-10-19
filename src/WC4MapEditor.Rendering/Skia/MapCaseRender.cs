using System.Diagnostics;
using SkiaSharp;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Skia;

/// <summary>
/// 方案（AI 方针）层，方案编辑模式下显示：
/// <list type="number">
/// <item>"走向地块"类方案的目标标记：同一地块上的多个方案合并为一个标记，旁边标出这些方案的方针编号；
/// 其他类型的目标值不是地块，不画标记。</item>
/// <item>右上角的方案面板：按触发回合列出全部方案（含方针编号为 0 的方案），超出可视高度时截断并注明剩余数量。</item>
/// </list>
/// 文字含中文，全部用支持中文的字体（默认字体会把中文画成方框）；排序用复用的缓冲，不做逐帧 LINQ。
/// </summary>
public class MapCaseRender : IDisposable
{
    private const double BASE_HEX_SIZE = 20.0;
    private const float MARKER_SIZE_RATIO = 0.5f;
    private const float MIN_LABEL_MARKER_SIZE = 8f;
    private const float PANEL_WIDTH = 320f;
    private const float PANEL_MARGIN = 10f;
    private const float PANEL_TOP = 50f;
    /// <summary>面板底部留给场景底部工具栏的高度（工具栏盖在画布上）。</summary>
    private const float PANEL_BOTTOM_RESERVE = 70f;
    private const float PANEL_PADDING = 8f;
    private const float TITLE_HEIGHT = 20f;
    private const float LINE_HEIGHT = 17f;
    private const float COLUMN_GAP = 8f;
    private const string ELLIPSIS = "…";

    private static readonly SKColor TARGET_MARKER_COLOR = new(0, 255, 128, 160);
    private static readonly SKColor TARGET_MARKER_BORDER = new(0, 200, 100, 220);

    // 常驻复用：字体与画笔不随帧变化。字体按"微软雅黑 → 系统里能画中文的字体"选取。
    private static readonly SKTypeface CjkTypeface = ResolveCjkTypeface();

    private readonly Camera _camera;
    private readonly MapData _mapData;
    private bool _disposed;
    private bool _showMapCases = true;

    /// <summary>排序缓冲：键 = 排序值 &lt;&lt; 32 | 方案下标，按需扩容、逐帧复用。</summary>
    private long[] _sortKeys = Array.Empty<long>();
    private readonly List<int> _groupPolicies = new();

    private readonly SKPaint _targetFillPaint;
    private readonly SKPaint _targetBorderPaint;
    private readonly SKPaint _crossPaint;
    private readonly SKPath _crossPath = new();
    private readonly SKFont _labelFont;
    private readonly SKPaint _labelPaint;
    private readonly SKPaint _shadowPaint;
    private readonly SKPaint _panelBgPaint;
    private readonly SKPaint _panelBorderPaint;
    private readonly SKFont _panelTitleFont;
    private readonly SKPaint _panelTitlePaint;
    private readonly SKFont _rowFont;
    private readonly SKPaint _policyPaint;
    private readonly SKPaint _detailPaint;
    private readonly SKPaint _mutedPaint;

    public bool ShowMapCases
    {
        get => _showMapCases;
        set => _showMapCases = value;
    }

    public MapCaseRender(Camera camera, MapData mapData)
    {
        _camera = camera;
        _mapData = mapData;

        _targetFillPaint = new SKPaint { IsAntialias = true, Color = TARGET_MARKER_COLOR };
        _targetBorderPaint = new SKPaint { IsAntialias = true, Color = TARGET_MARKER_BORDER, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        _crossPaint = new SKPaint { IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        _labelFont = new SKFont(CjkTypeface, 12);
        _labelPaint = new SKPaint { IsAntialias = true, Color = new SKColor(120, 255, 170) };
        _shadowPaint = new SKPaint { IsAntialias = true, Color = SKColors.Black };

        _panelBgPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 160) };
        _panelBorderPaint = new SKPaint { IsAntialias = true, Color = new SKColor(255, 165, 0, 180), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        _panelTitleFont = new SKFont(CjkTypeface, 13);
        _panelTitlePaint = new SKPaint { IsAntialias = true, Color = SKColors.Orange };
        _rowFont = new SKFont(CjkTypeface, 11);
        _policyPaint = new SKPaint { IsAntialias = true, Color = SKColors.Orange };
        _detailPaint = new SKPaint { IsAntialias = true, Color = new SKColor(235, 235, 235) };
        _mutedPaint = new SKPaint { IsAntialias = true, Color = new SKColor(170, 170, 170) };
    }

    public void Render(SKCanvas canvas)
    {
        if (canvas == null || _mapData?.Cases == null) return;
        if (!_showMapCases) return;

        try
        {
            var cases = _mapData.Cases;
            EnsureSortCapacity(cases.Count);
            DrawTargetMarkers(canvas, cases);
            DrawCasePanel(canvas, cases);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MapCaseRender] 绘制方案失败: {ex.Message}");
        }
    }

    // ================================================================== 目标标记

    private void DrawTargetMarkers(SKCanvas canvas, IList<MapCase> cases)
    {
        int width = _mapData.MapWidth;
        int tileCount = width * _mapData.MapHeight;
        if (width <= 0) return;

        int n = 0;
        for (int i = 0; i < cases.Count; i++)
        {
            var c = cases[i];
            if (!c.TargetsTile || c.TargetTile < 0 || c.TargetTile >= tileCount) continue;
            _sortKeys[n++] = ((long)c.TargetTile << 32) | (uint)i;
        }
        if (n == 0) return;
        Array.Sort(_sortKeys, 0, n);

        float markerSize = (float)(BASE_HEX_SIZE * Math.Sqrt(3) * _camera.ZoomLevel * MARKER_SIZE_RATIO);
        var visible = CalculateVisibleBounds();

        int start = 0;
        while (start < n)
        {
            int tile = (int)(_sortKeys[start] >> 32);
            int end = start + 1;
            while (end < n && (int)(_sortKeys[end] >> 32) == tile) end++;

            var (sx, sy) = _camera.HexToScreen(tile % width, tile / width);
            var center = new SKPoint((float)sx, (float)sy);
            if (visible.Contains(center.X, center.Y))
            {
                DrawMarker(canvas, center, markerSize);
                if (markerSize >= MIN_LABEL_MARKER_SIZE)
                    DrawMarkerLabel(canvas, cases, start, end, center, markerSize);
            }
            start = end;
        }
    }

    private void DrawMarker(SKCanvas canvas, SKPoint center, float size)
    {
        float half = size / 2;
        var rect = new SKRect(center.X - half, center.Y - half, center.X + half, center.Y + half);
        canvas.DrawOval(rect, _targetFillPaint);
        canvas.DrawOval(rect, _targetBorderPaint);

        float cross = half * 0.5f;
        _crossPath.Reset();
        _crossPath.MoveTo(center.X - cross, center.Y);
        _crossPath.LineTo(center.X + cross, center.Y);
        _crossPath.MoveTo(center.X, center.Y - cross);
        _crossPath.LineTo(center.X, center.Y + cross);
        canvas.DrawPath(_crossPath, _crossPaint);
    }

    /// <summary>标记上方写"方针 1,2,19"：同一地块所有方案的方针编号（去重、升序）。</summary>
    private void DrawMarkerLabel(SKCanvas canvas, IList<MapCase> cases, int start, int end, SKPoint center, float markerSize)
    {
        _groupPolicies.Clear();
        for (int k = start; k < end; k++)
        {
            int policy = cases[(int)(_sortKeys[k] & 0xFFFFFFFF)].PolicyNumber;
            if (!_groupPolicies.Contains(policy)) _groupPolicies.Add(policy);
        }
        _groupPolicies.Sort();

        string text = Loc.F("Render.MapCase.TargetLabel", string.Join(",", _groupPolicies));
        float y = center.Y - markerSize / 2 - 4;
        canvas.DrawText(text, center.X + 1, y + 1, SKTextAlign.Center, _labelFont, _shadowPaint);
        canvas.DrawText(text, center.X, y, SKTextAlign.Center, _labelFont, _labelPaint);
    }

    // ================================================================== 方案面板

    private void DrawCasePanel(SKCanvas canvas, IList<MapCase> cases)
    {
        int count = cases.Count;
        float viewportWidth = (float)_camera.ViewportWidth;
        float viewportHeight = (float)_camera.ViewportHeight;

        // 行数上限：面板不伸到底部工具栏下面；截断时最后一行写"还有 N 个方案"
        float rowsSpace = viewportHeight - PANEL_TOP - PANEL_BOTTOM_RESERVE - PANEL_PADDING * 2 - TITLE_HEIGHT;
        int maxLines = Math.Max(1, (int)(rowsSpace / LINE_HEIGHT));
        int shown = count <= maxLines ? count : maxLines - 1;
        int lines = count == 0 ? 1 : (shown < count ? shown + 1 : shown);

        float panelX = viewportWidth - PANEL_WIDTH - PANEL_MARGIN;
        float panelY = PANEL_TOP;
        float panelHeight = PANEL_PADDING * 2 + TITLE_HEIGHT + lines * LINE_HEIGHT;
        var panelRect = new SKRect(panelX, panelY, panelX + PANEL_WIDTH, panelY + panelHeight);
        canvas.DrawRoundRect(panelRect, 6, 6, _panelBgPaint);
        canvas.DrawRoundRect(panelRect, 6, 6, _panelBorderPaint);

        float textX = panelX + PANEL_PADDING;
        float textRight = panelX + PANEL_WIDTH - PANEL_PADDING;
        float baseline = panelY + PANEL_PADDING + TITLE_HEIGHT * 0.75f;
        canvas.DrawText(Loc.F("Render.MapCase.PanelTitle", count), textX, baseline, SKTextAlign.Left, _panelTitleFont, _panelTitlePaint);
        baseline += TITLE_HEIGHT * 0.25f;

        if (count == 0)
        {
            baseline += LINE_HEIGHT;
            DrawShadowedText(canvas, Loc.T("Render.MapCase.Empty", "暂无方案（按 Q 打开方案列表新增）"), textX, baseline, _mutedPaint);
            return;
        }

        for (int i = 0; i < count; i++)
            _sortKeys[i] = ((long)cases[i].TriggerRound << 32) | (uint)i;
        Array.Sort(_sortKeys, 0, count);

        var caseTypes = ConfigManager.Instance.GetCaseEditConfig().CaseList;
        for (int row = 0; row < shown; row++)
        {
            baseline += LINE_HEIGHT;
            var c = cases[(int)(_sortKeys[row] & 0xFFFFFFFF)];

            string head = Loc.F("Render.MapCase.RowHead", c.PolicyNumber, c.TriggerRound);
            DrawShadowedText(canvas, head, textX, baseline, _policyPaint);

            float detailX = textX + _rowFont.MeasureText(head) + COLUMN_GAP;
            string detail = FitText(DescribeAction(c, caseTypes), textRight - detailX);
            DrawShadowedText(canvas, detail, detailX, baseline, _detailPaint);
        }

        if (shown < count)
        {
            baseline += LINE_HEIGHT;
            DrawShadowedText(canvas, Loc.F("Render.MapCase.More", count - shown), textX, baseline, _mutedPaint);
        }
    }

    /// <summary>方案动作：类型名（来自 setting.txt CaseList）+ 目标（地块写坐标，其他写数值）。</summary>
    private string DescribeAction(MapCase c, IReadOnlyList<ConfigKeyValue> caseTypes)
    {
        string type = ConfigKeyValue.NameOf(caseTypes, c.CaseType) ?? Loc.F("Render.MapCase.UnknownType", c.CaseType);
        int width = _mapData.MapWidth;
        if (c.TargetsTile && width > 0 && c.TargetTile >= 0 && c.TargetTile < width * _mapData.MapHeight)
            return Loc.F("Render.MapCase.ActionTile", type, c.TargetTile % width, c.TargetTile / width);
        return Loc.F("Render.MapCase.ActionValue", type, c.TargetTile);
    }

    /// <summary>超出宽度时从末尾截断并补"…"。</summary>
    private string FitText(string text, float maxWidth)
    {
        if (maxWidth <= 0) return "";
        if (_rowFont.MeasureText(text) <= maxWidth) return text;

        int length = text.Length;
        while (length > 0 && _rowFont.MeasureText(string.Concat(text.AsSpan(0, length), ELLIPSIS)) > maxWidth)
            length--;
        return length > 0 ? string.Concat(text.AsSpan(0, length), ELLIPSIS) : "";
    }

    private void DrawShadowedText(SKCanvas canvas, string text, float x, float y, SKPaint paint)
    {
        canvas.DrawText(text, x + 1, y + 1, SKTextAlign.Left, _rowFont, _shadowPaint);
        canvas.DrawText(text, x, y, SKTextAlign.Left, _rowFont, paint);
    }

    // ================================================================== 辅助

    private void EnsureSortCapacity(int count)
    {
        if (_sortKeys.Length < count)
            _sortKeys = new long[Math.Max(count, _sortKeys.Length * 2)];
    }

    private SKRect CalculateVisibleBounds()
    {
        float viewWidth = (float)_camera.ViewportWidth;
        float viewHeight = (float)_camera.ViewportHeight;
        float margin = 200f * (float)_camera.ZoomLevel;
        return new SKRect(-margin, -margin, viewWidth + margin, viewHeight + margin);
    }

    private static SKTypeface ResolveCjkTypeface()
    {
        var yahei = SKTypeface.FromFamilyName("Microsoft YaHei");
        if (yahei != null && yahei.FamilyName.Contains("YaHei", StringComparison.OrdinalIgnoreCase)) return yahei;
        return SKFontManager.Default.MatchCharacter('中') ?? yahei ?? SKTypeface.Default;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _targetFillPaint.Dispose();
        _targetBorderPaint.Dispose();
        _crossPaint.Dispose();
        _crossPath.Dispose();
        _labelFont.Dispose();
        _labelPaint.Dispose();
        _shadowPaint.Dispose();
        _panelBgPaint.Dispose();
        _panelBorderPaint.Dispose();
        _panelTitleFont.Dispose();
        _panelTitlePaint.Dispose();
        _rowFont.Dispose();
        _policyPaint.Dispose();
        _detailPaint.Dispose();
        _mutedPaint.Dispose();
    }
}
