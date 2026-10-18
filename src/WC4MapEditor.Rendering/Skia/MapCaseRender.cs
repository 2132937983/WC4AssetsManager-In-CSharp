using System.Diagnostics;
using SkiaSharp;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Rendering.Skia;

public class MapCaseRender : IDisposable
{
    private const double ICON_SIZE_RATIO = 0.5;
    private const double BASE_HEX_SIZE = 20.0;
    private static readonly double HEX_VERTICAL_SPACING = BASE_HEX_SIZE * Math.Sqrt(3);

    private static readonly SKColor CASE_MARKER_COLOR = new(255, 165, 0, 180);
    private static readonly SKColor CASE_MARKER_BORDER = new(255, 140, 0, 220);
    private static readonly SKColor TARGET_MARKER_COLOR = new(0, 255, 128, 160);
    private static readonly SKColor TARGET_MARKER_BORDER = new(0, 200, 100, 200);

    private readonly Camera _camera;
    private readonly MapData _mapData;
    private bool _disposed;
    private bool _showMapCases = true;

    private readonly List<(SKPoint Position, float Size, MapCase Case)> _casePositions = new();
    private readonly List<(SKPoint Position, float Size)> _targetPositions = new();
    private readonly List<(SKPoint Position, string Text, float Size)> _policyTexts = new();
    private readonly List<(SKPoint Position, string Text, float Size)> _roundTexts = new();
    private readonly List<(SKPoint Position, string Text, float Size)> _eventTexts = new();

    private readonly SKPaint _caseFillPaint;
    private readonly SKPaint _caseBorderPaint;
    private readonly SKPaint _targetFillPaint;
    private readonly SKPaint _targetBorderPaint;
    private readonly SKFont _policyFont;
    private readonly SKPaint _policyPaint;
    private readonly SKPaint _policyShadowPaint;
    private readonly SKFont _roundFont;
    private readonly SKPaint _roundPaint;
    private readonly SKPaint _roundShadowPaint;
    private readonly SKFont _eventFont;
    private readonly SKPaint _eventPaint;
    private readonly SKPaint _eventShadowPaint;

    // 「方案列表」面板与目标标记十字的常量画笔 / 字体 / 路径。原先每帧重新创建
    // （面板标题还每帧 SKTypeface.FromFamilyName 查字体），这些设置不随帧变化，常驻复用即可。
    private static readonly SKTypeface PanelTitleTypeface = SKTypeface.FromFamilyName("Microsoft YaHei") ?? SKTypeface.Default;
    private readonly SKPaint _panelBgPaint;
    private readonly SKPaint _panelBorderPaint;
    private readonly SKFont _panelTitleFont;
    private readonly SKPaint _panelTitlePaint;
    private readonly SKPaint _crossPaint;
    private readonly SKPath _crossPath = new();

    public bool ShowMapCases
    {
        get => _showMapCases;
        set => _showMapCases = value;
    }

    public MapCaseRender(Camera camera, MapData mapData)
    {
        _camera = camera;
        _mapData = mapData;

        _caseFillPaint = new SKPaint { IsAntialias = true, Color = CASE_MARKER_COLOR };
        _caseBorderPaint = new SKPaint { IsAntialias = true, Color = CASE_MARKER_BORDER, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        _targetFillPaint = new SKPaint { IsAntialias = true, Color = TARGET_MARKER_COLOR };
        _targetBorderPaint = new SKPaint { IsAntialias = true, Color = TARGET_MARKER_BORDER, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };

        _policyFont = new SKFont(SKTypeface.Default, 10);
        _policyPaint = new SKPaint { IsAntialias = true, Color = SKColors.Orange };
        _policyShadowPaint = new SKPaint { IsAntialias = true, Color = SKColors.Black };

        _roundFont = new SKFont(SKTypeface.Default, 10);
        _roundPaint = new SKPaint { IsAntialias = true, Color = SKColors.Yellow };
        _roundShadowPaint = new SKPaint { IsAntialias = true, Color = SKColors.Black };

        _eventFont = new SKFont(SKTypeface.Default, 9);
        _eventPaint = new SKPaint { IsAntialias = true, Color = SKColors.Cyan };
        _eventShadowPaint = new SKPaint { IsAntialias = true, Color = SKColors.Black };

        _panelBgPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 160) };
        _panelBorderPaint = new SKPaint { IsAntialias = true, Color = new SKColor(255, 165, 0, 180), Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        _panelTitleFont = new SKFont(PanelTitleTypeface, 13);
        _panelTitlePaint = new SKPaint { IsAntialias = true, Color = SKColors.Orange };
        _crossPaint = new SKPaint { IsAntialias = true, Color = SKColors.White, StrokeWidth = 2 };
    }

    public void Render(SKCanvas canvas)
    {
        if (canvas == null || _mapData == null) return;
        if (!_showMapCases) return;
        if (_mapData.Cases == null || _mapData.Cases.Count == 0) return;

        try
        {
            ClearBuffers();

            var visibleBounds = CalculateVisibleBounds();
            double hexHeight = BASE_HEX_SIZE * Math.Sqrt(3) * _camera.ZoomLevel;
            float iconSize = (float)(hexHeight * ICON_SIZE_RATIO);

            foreach (var mapCase in _mapData.Cases)
            {
                if (mapCase.PolicyNumber <= 0 && mapCase.EndEvent <= 0) continue;

                if (mapCase.TargetTile >= 0 && mapCase.TargetTile < _mapData.MapWidth * _mapData.MapHeight)
                {
                    var targetPos = CalculateScreenPosition(mapCase.TargetTile);
                    if (targetPos != null && IsInVisibleBounds(targetPos.Value, visibleBounds))
                    {
                        _targetPositions.Add((targetPos.Value, iconSize));
                    }
                }

                if (mapCase.PolicyNumber > 0)
                {
                    float policyFontSize = iconSize * 0.4f;
                    _policyTexts.Add((new SKPoint(0, 0), $"方针:{mapCase.PolicyNumber}", policyFontSize));
                }

                if (mapCase.TriggerRound > 0)
                {
                    float roundFontSize = iconSize * 0.35f;
                    _roundTexts.Add((new SKPoint(0, 0), $"R{mapCase.TriggerRound}", roundFontSize));
                }

                if (mapCase.EndEvent > 0)
                {
                    float eventFontSize = iconSize * 0.3f;
                    _eventTexts.Add((new SKPoint(0, 0), $"E{mapCase.EndEvent}", eventFontSize));
                }
            }

            DrawTargetMarkers(canvas);
            DrawCaseInfoPanel(canvas);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MapCaseRender] 绘制方案失败: {ex.Message}");
        }
    }

    private void DrawTargetMarkers(SKCanvas canvas)
    {
        if (_targetPositions.Count == 0) return;

        foreach (var target in _targetPositions)
        {
            float halfSize = target.Size / 2;
            var rect = new SKRect(target.Position.X - halfSize, target.Position.Y - halfSize,
                                  target.Position.X + halfSize, target.Position.Y + halfSize);
            canvas.DrawOval(rect, _targetFillPaint);
            canvas.DrawOval(rect, _targetBorderPaint);

            // 复用路径（Reset 后重填）与画笔，避免每个目标标记每帧分配 SKPath / SKPaint。
            float crossSize = halfSize * 0.5f;
            _crossPath.Reset();
            _crossPath.MoveTo(target.Position.X - crossSize, target.Position.Y);
            _crossPath.LineTo(target.Position.X + crossSize, target.Position.Y);
            _crossPath.MoveTo(target.Position.X, target.Position.Y - crossSize);
            _crossPath.LineTo(target.Position.X, target.Position.Y + crossSize);
            canvas.DrawPath(_crossPath, _crossPaint);
        }
    }

    private void DrawCaseInfoPanel(SKCanvas canvas)
    {
        var validCases = _mapData.Cases
            .Where(c => c.PolicyNumber > 0 || c.EndEvent > 0)
            .OrderBy(c => c.TriggerRound)
            .ToList();

        if (validCases.Count == 0) return;

        float viewportWidth = (float)_camera.ViewportWidth;
        float lineHeight = 18f;
        float panelPadding = 8f;
        float panelMargin = 10f;
        float panelWidth = 180f;
        float panelHeight = panelPadding * 2 + lineHeight + validCases.Count * (lineHeight + 2f);

        float panelX = viewportWidth - panelWidth - panelMargin;
        float panelY = panelMargin + 40f;

        var panelRect = new SKRect(panelX, panelY, panelX + panelWidth, panelY + panelHeight);
        canvas.DrawRoundRect(panelRect, 6, 6, _panelBgPaint);
        canvas.DrawRoundRect(panelRect, 6, 6, _panelBorderPaint);

        float titleY = panelY + panelPadding + lineHeight * 0.8f;
        canvas.DrawText("方案列表", panelX + panelPadding, titleY, SKTextAlign.Left, _panelTitleFont, _panelTitlePaint);

        float itemY = titleY + lineHeight * 0.4f;

        foreach (var mapCase in validCases)
        {
            itemY += lineHeight + 2f;

            float textX = panelX + panelPadding;

            if (mapCase.PolicyNumber > 0)
            {
                string policyText = $"方针:{mapCase.PolicyNumber}";
                canvas.DrawText(policyText, textX + 1, itemY + 1, SKTextAlign.Left, _policyFont, _policyShadowPaint);
                canvas.DrawText(policyText, textX, itemY, SKTextAlign.Left, _policyFont, _policyPaint);
                textX += 70f;
            }

            if (mapCase.TriggerRound > 0)
            {
                string roundText = $"R{mapCase.TriggerRound}";
                canvas.DrawText(roundText, textX + 1, itemY + 1, SKTextAlign.Left, _roundFont, _roundShadowPaint);
                canvas.DrawText(roundText, textX, itemY, SKTextAlign.Left, _roundFont, _roundPaint);
                textX += 40f;
            }

            if (mapCase.EndEvent > 0)
            {
                string eventText = $"E{mapCase.EndEvent}";
                canvas.DrawText(eventText, textX + 1, itemY + 1, SKTextAlign.Left, _eventFont, _eventShadowPaint);
                canvas.DrawText(eventText, textX, itemY, SKTextAlign.Left, _eventFont, _eventPaint);
            }
        }
    }

    private void ClearBuffers()
    {
        _casePositions.Clear();
        _targetPositions.Clear();
        _policyTexts.Clear();
        _roundTexts.Clear();
        _eventTexts.Clear();
    }

    private SKRect CalculateVisibleBounds()
    {
        float viewWidth = (float)_camera.ViewportWidth;
        float viewHeight = (float)_camera.ViewportHeight;
        float margin = 200f * (float)_camera.ZoomLevel;
        return new SKRect(-margin, -margin, viewWidth + margin, viewHeight + margin);
    }

    private static bool IsInVisibleBounds(SKPoint point, SKRect bounds)
    {
        return bounds.Contains(point.X, point.Y);
    }

    private SKPoint? CalculateScreenPosition(int coordinate)
    {
        if (_mapData == null || _mapData.MapWidth <= 0 || _mapData.MapHeight <= 0) return null;

        int row = coordinate / _mapData.MapWidth;
        int col = coordinate % _mapData.MapWidth;

        if (row < 0 || row >= _mapData.MapHeight || col < 0 || col >= _mapData.MapWidth) return null;

        var (screenX, screenY) = _camera.HexToScreen(col, row);
        return new SKPoint((float)screenX, (float)screenY);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _caseFillPaint.Dispose();
        _caseBorderPaint.Dispose();
        _targetFillPaint.Dispose();
        _targetBorderPaint.Dispose();
        _policyFont.Dispose();
        _policyPaint.Dispose();
        _policyShadowPaint.Dispose();
        _roundFont.Dispose();
        _roundPaint.Dispose();
        _roundShadowPaint.Dispose();
        _eventFont.Dispose();
        _eventPaint.Dispose();
        _eventShadowPaint.Dispose();
        _panelBgPaint.Dispose();
        _panelBorderPaint.Dispose();
        _panelTitleFont.Dispose();
        _panelTitlePaint.Dispose();
        _crossPaint.Dispose();
        _crossPath.Dispose();
    }
}