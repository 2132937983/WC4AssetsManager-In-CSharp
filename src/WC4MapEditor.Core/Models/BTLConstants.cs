namespace WC4MapEditor.Core.Models;

// 其余 BTL* 枚举（建筑类型 / 地形类型 / 军衔…）从未被引用，已删除；只保留六角方向枚举（HexCoord.GetNeighbor 用）。
public enum BTLDirection { North = 0, Northeast = 1, East = 2, Southeast = 3, South = 4, Southwest = 5, West = 6, Northwest = 7 }
