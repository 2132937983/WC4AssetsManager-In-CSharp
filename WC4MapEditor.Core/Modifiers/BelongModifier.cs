using WC4MapEditor.Core.Brush;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

public sealed class BelongModifier : ModifierBase, IBrushTarget
{
    public override string Name => "belong";
    public override string DisplayName => "归属修改器";

    private byte _copiedCountryId;
    private int _selectedCountryId;
    private readonly BrushEngine _brushEngine = new();

    public int SelectedCountryId
    {
        get => _selectedCountryId;
        set => _selectedCountryId = value;
    }

    public override ModifierResult Apply(int col, int row, object? parameter = null)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        byte countryId = parameter switch
        {
            byte b => b,
            int id => (byte)Math.Clamp(id, 0, 255),
            _ => (byte)Math.Clamp(_selectedCountryId, 0, 255)
        };

        _mapData.SetBelongValue(col, row, countryId);
        MarkModified();
        return ModifierResult.Ok($"已设置归属 ({col}, {row}) = {countryId}");
    }

    public override ModifierResult Remove(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        if (_mapData == null) return ModifierResult.Fail("地图数据未初始化");

        _mapData.SetBelongValue(col, row, 0xFF);
        MarkModified();
        return ModifierResult.Ok($"已清除归属 ({col}, {row})");
    }

    public override bool CanApply(int col, int row) => IsValidCoord(col, row);
    public override bool CanRemove(int col, int row) => IsValidCoord(col, row);

    public override object? GetDataAt(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        return _mapData!.GetBelongValue(col, row);
    }

    public override bool SetDataAt(int col, int row, object data)
    {
        if (!IsValidCoord(col, row) || _mapData == null) return false;

        int belongValue = data switch
        {
            byte b => b,
            int i => Math.Clamp(i, 0, 255),
            _ => -1
        };

        if (belongValue < 0) return false;
        _mapData.SetBelongValue(col, row, belongValue);
        MarkModified();
        return true;
    }

    public ModifierResult CopyBelongValue(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        _copiedCountryId = (byte)_mapData!.GetBelongValue(col, row);
        return ModifierResult.Ok($"已复制归属值: {_copiedCountryId}");
    }

    public ModifierResult PasteBelongValue(int col, int row)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        _mapData!.SetBelongValue(col, row, _copiedCountryId);
        MarkModified();
        return ModifierResult.Ok($"已粘贴归属值: {_copiedCountryId}");
    }

    public int? GetBelongValue(int col, int row)
    {
        if (!IsValidCoord(col, row)) return null;
        return _mapData!.GetBelongValue(col, row);
    }

    public ModifierResult SetBelongByCountryId(int col, int row, int countryId)
    {
        if (!IsValidCoord(col, row)) return ModifierResult.Fail("坐标超出范围");
        _mapData!.SetBelongValue(col, row, countryId);
        MarkModified();
        return ModifierResult.Ok($"已设置归属 ({col}, {row}) = 国家ID {countryId}");
    }

    public int CleanOrphanBelongs()
    {
        if (_mapData == null) return 0;
        int count = 0;
        for (int i = 0; i < _mapData.MapWidth * _mapData.MapHeight; i++)
        {
            int belong = _mapData.GetBelongValueByIndex(i);
            if (belong != 0xFF && belong > 0)
            {
                bool hasBuilding = false;
                int col = i % _mapData.MapWidth;
                int row = i / _mapData.MapWidth;
                if (_mapData.FindBuildingIndex(col, row) >= 0) hasBuilding = true;
                if (_mapData.GetArmyAt(col, row) != null) hasBuilding = true;
                if (!hasBuilding)
                {
                    _mapData.SetBelongValueByIndex(i, 0xFF);
                    count++;
                }
            }
        }
        if (count > 0) MarkModified();
        return count;
    }

    public int RemoveBorderEntities()
    {
        if (_mapData == null) return 0;
        int count = 0;
        for (int row = 0; row < _mapData.MapHeight; row++)
        {
            for (int col = 0; col < _mapData.MapWidth; col++)
            {
                int belong = _mapData.GetBelongValue(col, row);
                if (belong == 0xFF || belong == 0) continue;
                bool isBorder = false;
                int[][] dirs = col % 2 == 0
                    ? new[] { new[] { -1, -1 }, new[] { 0, -1 }, new[] { 1, 0 }, new[] { 0, 1 }, new[] { -1, 1 }, new[] { -1, 0 } }
                    : new[] { new[] { 0, -1 }, new[] { 1, -1 }, new[] { 1, 0 }, new[] { 1, 1 }, new[] { 0, 1 }, new[] { -1, 0 } };
                foreach (var d in dirs)
                {
                    int nc = col + d[0], nr = row + d[1];
                    if (nc < 0 || nc >= _mapData.MapWidth || nr < 0 || nr >= _mapData.MapHeight) continue;
                    int nb = _mapData.GetBelongValue(nc, nr);
                    if (nb != belong) { isBorder = true; break; }
                }
                if (isBorder)
                {
                    if (_mapData.FindBuildingIndex(col, row) >= 0)
                    {
                        _mapData.Buildings.RemoveAt(_mapData.FindBuildingIndex(col, row));
                        count++;
                    }
                    if (_mapData.GetArmyAt(col, row) != null)
                    {
                        var army = _mapData.GetArmyAt(col, row)!.Value;
                        _mapData.Armies.Remove(army);
                        count++;
                    }
                }
            }
        }
        if (count > 0) MarkModified();
        return count;
    }

    public int RemoveBorderArmies()
    {
        if (_mapData == null) return 0;
        int count = 0;
        var toRemove = new List<Army>();
        for (int row = 0; row < _mapData.MapHeight; row++)
        {
            for (int col = 0; col < _mapData.MapWidth; col++)
            {
                int belong = _mapData.GetBelongValue(col, row);
                if (belong == 0xFF || belong == 0) continue;
                if (_mapData.GetArmyAt(col, row) == null) continue;
                bool isBorder = false;
                int[][] dirs = col % 2 == 0
                    ? new[] { new[] { -1, -1 }, new[] { 0, -1 }, new[] { 1, 0 }, new[] { 0, 1 }, new[] { -1, 1 }, new[] { -1, 0 } }
                    : new[] { new[] { 0, -1 }, new[] { 1, -1 }, new[] { 1, 0 }, new[] { 1, 1 }, new[] { 0, 1 }, new[] { -1, 0 } };
                foreach (var d in dirs)
                {
                    int nc = col + d[0], nr = row + d[1];
                    if (nc < 0 || nc >= _mapData.MapWidth || nr < 0 || nr >= _mapData.MapHeight) continue;
                    int nb = _mapData.GetBelongValue(nc, nr);
                    if (nb != belong) { isBorder = true; break; }
                }
                if (isBorder)
                {
                    toRemove.Add(_mapData.GetArmyAt(col, row)!.Value);
                    count++;
                }
            }
        }
        foreach (var army in toRemove)
            _mapData.Armies.Remove(army);
        if (count > 0) MarkModified();
        return count;
    }

    public int BatchSetBelongByProvince(int targetCountryId)
    {
        if (_mapData == null) return 0;
        int count = 0;
        for (int i = 0; i < _mapData.MapWidth * _mapData.MapHeight; i++)
        {
            int belong = _mapData.GetBelongValueByIndex(i);
            if (belong != 0xFF && belong > 0)
            {
                _mapData.SetBelongValueByIndex(i, targetCountryId);
                count++;
            }
        }
        if (count > 0) MarkModified();
        return count;
    }

    public int ClearAllBelongs()
    {
        if (_mapData == null) return 0;
        int count = 0;
        for (int i = 0; i < _mapData.MapWidth * _mapData.MapHeight; i++)
        {
            int belong = _mapData.GetBelongValueByIndex(i);
            if (belong != 0xFF)
            {
                _mapData.SetBelongValueByIndex(i, 0xFF);
                count++;
            }
        }
        if (count > 0) MarkModified();
        return count;
    }

    public int RandomizeBelongs(Random random)
    {
        if (_mapData == null) return 0;
        int count = 0;
        for (int i = 0; i < _mapData.MapWidth * _mapData.MapHeight; i++)
        {
            int belong = _mapData.GetBelongValueByIndex(i);
            if (belong != 0xFF && belong > 0)
            {
                _mapData.SetBelongValueByIndex(i, random.Next(1, 16));
                count++;
            }
        }
        if (count > 0) MarkModified();
        return count;
    }

    public int RandomizeBelongByTargetId(int targetBelongId, Random random)
    {
        if (_mapData == null) return 0;
        int count = 0;
        for (int i = 0; i < _mapData.MapWidth * _mapData.MapHeight; i++)
        {
            int belong = _mapData.GetBelongValueByIndex(i);
            if (belong == targetBelongId)
            {
                _mapData.SetBelongValueByIndex(i, random.Next(0, 256));
                count++;
            }
        }
        if (count > 0) MarkModified();
        return count;
    }

    public int ReplaceBelong(int oldBelongId, int newBelongId)
    {
        if (_mapData == null) return 0;
        int count = 0;
        for (int i = 0; i < _mapData.MapWidth * _mapData.MapHeight; i++)
        {
            int belong = _mapData.GetBelongValueByIndex(i);
            if (belong == oldBelongId)
            {
                _mapData.SetBelongValueByIndex(i, newBelongId);
                count++;
            }
        }
        if (count > 0) MarkModified();
        return count;
    }

    #region IBrushTarget Implementation

    public BrushEngine Brush => _brushEngine;

    bool IBrushTarget.BrushActive
    {
        get => _brushEngine.Active;
        set => _brushEngine.Active = value;
    }

    int IBrushTarget.BrushSize
    {
        get => _brushEngine.Radius;
        set => _brushEngine.Radius = value;
    }

    string IBrushTarget.BrushShape
    {
        get => _brushEngine.Shape;
        set => _brushEngine.Shape = value;
    }

    public void PaintWithBrush(int centerCol, int centerRow)
    {
        if (_mapData == null || !_brushEngine.Active) return;

        byte countryId = _copiedCountryId;
        var hexes = _brushEngine.GetHexesInBrush(centerCol, centerRow, _mapData.MapWidth, _mapData.MapHeight);
        foreach (var (c, r) in hexes)
            _mapData.SetBelongValue(c, r, countryId);
        MarkModified();
    }

    public void PaintWithBrushMasked(int centerCol, int centerRow, HashSet<int> maskTerrainIds, bool maskIncludeMode)
    {
        if (_mapData == null || !_brushEngine.Active) return;

        byte countryId = _copiedCountryId;
        var hexes = _brushEngine.GetHexesInBrush(centerCol, centerRow, _mapData.MapWidth, _mapData.MapHeight);
        foreach (var (c, r) in hexes)
        {
            byte terrainType = _mapData.GetTerrainRef(c, r).TileType1;
            bool isInMask = maskTerrainIds.Contains(terrainType);

            // maskIncludeMode=true: 只在选中地形上绘制
            // maskIncludeMode=false: 跳过选中地形
            if (maskIncludeMode && !isInMask) continue;
            if (!maskIncludeMode && isInMask) continue;

            _mapData.SetBelongValue(c, r, countryId);
        }
        MarkModified();
    }

    #endregion
}