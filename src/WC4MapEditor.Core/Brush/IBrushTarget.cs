namespace WC4MapEditor.Core.Brush;

public interface IBrushTarget
{
    bool BrushActive { get; set; }
    int BrushSize { get; set; }
    string BrushShape { get; set; }

    void PaintWithBrush(int centerCol, int centerRow);
}