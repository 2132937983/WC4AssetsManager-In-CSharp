using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

public interface IModifier
{
    string Name { get; }
    string DisplayName { get; }

    void Initialize(MapData mapData);
    void Deinitialize();

    ModifierResult Apply(int col, int row, object? parameter = null);
    ModifierResult ApplyRange(IEnumerable<HexCoord> coords, object? parameter = null);
    ModifierResult Remove(int col, int row);
    ModifierResult RemoveRange(IEnumerable<HexCoord> coords);

    bool CanApply(int col, int row);
    bool CanRemove(int col, int row);

    object? GetDataAt(int col, int row);
    bool SetDataAt(int col, int row, object data);
}

public readonly struct ModifierResult
{
    public bool Success { get; }
    public string? Message { get; }
    public int AffectedCount { get; }

    public ModifierResult(bool success, string? message = null, int affectedCount = 0)
    {
        Success = success;
        Message = message;
        AffectedCount = affectedCount;
    }

    public static ModifierResult Ok(string? message = null, int count = 1) => new(true, message, count);
    public static ModifierResult Fail(string message) => new(false, message, 0);
}