# map-data-conventions

> 保存时间: 2026-09-21（人工整理，非工具写入）

> 摘要: 坐标与线性索引、六边形"奇数列下移半格"、地形判海、省区值约定（省会索引）、归属=军团索引、建筑类型与海陆界、单位 v1/v3 分派、建筑名自动进 stringtable。

---

## 坐标

- `col` / `row` 从 0 起；线性索引 `index = row * MapWidth + col`。
- 建筑 `Building.Coordinate` 是线性索引：`col = Coordinate % MapWidth`、`row = Coordinate / MapWidth`。
- 查询建筑前若坐标索引可能过期，先 `mapData.RebuildBuildingCoordIndex()`（批量改完再 `InvalidateBuildingCoordIndex()`）。

## 六边形：奇数列下移半格

统一按这个错位算位置与邻居：

```
y = row * hexSpacingY + ((col % 2) == 1 ? hexSpacingY / 2 : 0)
```

**算邻居不要直接用 3×3 网格**，会错一格；参考 `BackgroundProvinceGenerator` / `ProvinceModifier` 里的邻居收集。

## 地形与"是不是海"

- **`terrain.TileType1 == 1` 就是海洋**（`OCEAN_TILE_TYPE = 1`）；`0` 是空地（不绘制陆地层）；其余才是陆地地形。
- 这个判据多处复用（`IsSeaTerrain`）：海洋**不参与**省区填充、不种陷阱等。
- 第二层是 `TileType2`（处理见 `TerrainModifier`），别与第一层混用。

## 省区（Province）

- `ProvinceValue`：**`0xFFFF`(65535) = 无省区**。
- 约定：**省区值 == 省会格的索引**（生成器会刻意保留省会原值）。
- 海洋不属于任何省区。
- **Lua 能写（2026-09-21 起）**：`map.SetProvince(col,row,value)` 可逐格写（0-65535，65535=无）。但它只适合**修边补漏** —— 铺整块省区仍要用省区编辑模式或区域填充，否则会破坏"省区值 == 省会索引"的约定。

## 归属（Belong）

- `Belong` 存的是**军团在 `mapData.Legions` 里的索引（0 ~ N-1）**，**不是国家 ID**。命令/脚本里填的归属值都按这个语义（`ResolveLegionByBelong`）。
- **`0xFF`(255) = 无归属**（判空用 `!= 0xFF`）。
- 军团自己有 `CountryId`；国家名显示走 `CountrySettings.json` → stringtable `country_{id}` → 兜底"国家{id}"（`CountryNameProvider`）。

## 建筑

- `BuildingType`：**> 30 视为海上建筑，< 30 视为陆上建筑**（`BTLRuleChecker` 按这个界纠正放错位置的建筑）。
- 可设首都的**等级城**类型范围是 **15–19**；低于 15 的普通城市不能当首都。
- **建筑名查不到会自动写进 stringtable 并保存**（`BuildingNameResolver.Resolve`）→ 新增建筑可以直接给中文名，不必先准备名称表。

## 单位（v1 / v3 两套）

- 判版本：`mapData.Header.BtlVersion >= 3` → 走 **v3**（`ArmyV3Modifier` + `FindArmyV3Index`），否则走 **v1**（`ArmyModifier`）。
- 命令与脚本都要按版本分派（参考 `MapScriptApi.RunArmyOp`、`CliCommandHost` 的 army 系列）。
- 按归属批量删单位是**精确匹配单位自身的军团归属**：`-1` 之类并不等于"全部"。

## 改完数据之后

- 触发重绘 + 通知数据已改（`NotifyDataModified` / `DataModified` / `InvalidateVisual`），否则界面与查询结果会滞后；
- 动了建筑（增删/移动）记得重建坐标索引。
