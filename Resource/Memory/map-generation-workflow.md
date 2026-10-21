# map-generation-workflow

> 保存时间: 2026-10-21（人工写入）
> 摘要: 生成/重画大范围地图（欧洲、亚洲、某国家）的标准流程：先用 geo rasterize 从 GeoJSON 产出海陆/归属掩码，再用 build-map.lua 落地，最后用 ai.mapgen 校验。禁止用 SetTerrain 逐格画大陆。

---

## 0. 先记住三条硬规则

1. **不要用 `SetTerrain` 逐格“画”大陆。** 模型没有全图的几何先验，逐格生成的结果一定是“一半海一半陆”或一团团城市斑块，而且自己看不出问题。
2. **几何来自数据，不来自模型。** 模型只负责：选地区、给 bbox、给宽高、选风格、填城市/势力；海岸线由 GeoJSON 栅格化得到。
3. **先干跑、再校验、最后落地。** 任何写地图的步骤先用 `dry_run=true` / `validate_mask`，数字过了再写。

---

## 1. 准备地理数据

用 Natural Earth（公有领域）的 1:110m 数据，放到 `Resource/Geo/`：

- 陆地：`ne_110m_land.geojson`
- 国家边界：`ne_110m_admin_0_countries.geojson`

下载地址（GitHub，raw）：

```
https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_110m_land.geojson
https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_110m_admin_0_countries.geojson
```

任意来源的 GeoJSON 都可以，只要能解析 `Polygon` / `MultiPolygon` / `FeatureCollection`。

---

## 2. 生成海陆掩码

```
geo rasterize --geojson Resource/Geo/ne_110m_land.geojson ^
              --out scripts/mapgen/regions/europe.land.txt ^
              --width 200 --height 180 ^
              --bbox -25,34,45,72 --smooth 1 --min-island 4
```

参数：

| 参数 | 说明 |
|---|---|
| `--geojson` | 输入 GeoJSON |
| `--out` | 输出 ASCII 掩码（`X`=陆地，`.`=海） |
| `--width/--height` | 地图尺寸（格），必须与地图一致 |
| `--bbox` | 经纬度窗口 `minLon,minLat,maxLon,maxLat`；省略则用数据自身范围 |
| `--smooth` | 3×3 多数表决次数，1 次可去掉锯齿与孤立点 |
| `--min-island` | 小于这个格数的陆块删除（4~8 比较自然） |

输出会打印：陆地格数/比例、陆块数量、最大陆块占比、长宽比提示。**陆地比例是第一个要看的数字**（欧洲 bbox `-25,34,45,72` 实测约 47%，11 个陆块，最大占 91%）。

掩码格式与 `scripts/mapgen/build-map.lua` 的 `[terrain] mode=mask` 完全兼容，投影也一致（含“奇数列下移半格”），不会与城市坐标错位。

---

## 3. 生成归属掩码（可选，但推荐）

```
geo rasterize --geojson Resource/Geo/ne_110m_admin_0_countries.geojson ^
              --owners ^
              --out scripts/mapgen/regions/europe.owner.txt ^
              --width 200 --height 180 --bbox -25,34,45,72
```

会额外生成 `europe.owner.txt.map`：

```
A = 8
B = 9
...
```

左边是掩码字符，右边是**国家 ID**（取自 GeoJSON 的 `id` / `ISO_A2` / `name`，可用 `--property` 指定）。  
若属性不是数字，用 `--id-property` 指定含国家 ID 的字段。

> 归属掩码的字符对不上军团时，`build-map` 会把这些格计入“跳过”，并在报表里列出缺失的国家 ID。

---

## 4. 改 build-map 的地区数据文件

以 `scripts/mapgen/regions/europe.txt` 为例：

```ini
[map]
name = 欧洲
dry_run = true          # 先 true
overwrite = false

[projection]
mode = window           # 有底图/精确标定时可改 ref
lon_west = -25
lon_east = 45
lat_north = 72
lat_south = 34

[terrain]
enabled = true
mode = mask
mask_file = scripts/mapgen/regions/europe.land.txt
land_type = 0           # 陆地地形值
sea_type = 1            # 海必须是 1
smooth = 1

[belong]
enabled = true
mode = mask
mask_file = scripts/mapgen/regions/europe.owner.txt
map_file = scripts/mapgen/regions/europe.owner.txt.map
land_only = true

[post]
create_coast = true
connect_buildings = true
```

顺序语义：**先铺海陆 → 再铺归属 → 最后落城市**（城市自己的势力会覆盖归属掩码，城市格不会落海）。

---

## 5. 执行

```
lua scripts/mapgen/build-map.lua
```

第一次一定 `dry_run = true`，看报表：

- 城市坐标是否落在预期位置（`ref` 模式看残差是否 ≤1.5 格）；
- 有多少城市「落在海里」「越界」「重复格」；
- 归属掩码预计写多少格、跳过多少格；
- 缺失的国家 ID（先把军团建好再跑）。

确认后把 `dry_run` 改成 `false` 再跑一次；整次执行可 `Ctrl+Z` 一次撤销。

---

## 6. 校验几何

在 `run_lua` 里：

```lua
local ai = require('ai')
ai.mapgen.validate{ expected_min = 0.40, expected_max = 0.55 }
```

或在落地前只检查掩码：

```lua
ai.mapgen.validate_mask{ path = 'scripts/mapgen/regions/europe.land.txt',
                         expected_min = 0.40, expected_max = 0.55 }
```

指标与含义：

| 指标 | 含义 | 常见阈值 |
|---|---|---|
| `land_ratio` | 陆地占比 | 按 bbox 定，欧洲 `-25,34,45,72` 约 0.47 |
| `largest_ratio` | 最大陆块占陆地比例 | ≥ 0.80（太小 = 碎成群岛） |
| `coastline` | 海岸线格数 | 与陆地比例相称即可 |
| `uniform_row_ratio` / `uniform_col_ratio` | 整行/整列完全同色的比例（按行列硬切最可靠的信号） | ≤ 0.35（"一半海一半陆"会接近 1.0） |
| `max_land_run` / `max_sea_run` | 最长连续陆地/海洋（仅参考） | — |
| `buildings_in_sea` | 落海建筑数 | 必须 0 |
| `empty_legions` | 没有领土的军团索引 | 按设计检查 |

不达标时按顺序修：

1. **比例不对** → 调 `--bbox`（欧洲常见错误是把北非/中东算进来，或纬度范围太窄）。
2. **太碎** → `--min-island` 调大；`--smooth` 加一次。
3. **直线伪影** → 检查是否用了手绘/二值图掩码；`--smooth 1` 或重新栅格化。
4. **城市落海** → 城市经纬度或投影窗口错；先用 `dry_run` 看坐标，必要时改 `ref` 模式。
5. **空势力** → 归属掩码里该国被邻国覆盖，或国家 ID 与军团对不上。

---

## 7. 不要做的事

- 不要用 `ai.each_cell` + `map.SetTerrain` 手写大陆轮廓；
- 不要在 `from_cities` 模式下把半径调到几十格“糊出大陆”（会变成均匀大圆斑）；
- 不要跳过 `dry_run` 直接写数据；
- 不要把 `sea_type` 写成 1 以外的值（引擎判海洋看 `Tile1 == 1`）；
- 不要忘了保存地图（`.btl`），脚本不会自动存盘。

---

## 8. 之后仍然手工的部分

- 山脉、河流、森林、地形变体：地形编辑模式 / `terrain` 命令；
- 省区（Province）：省区编辑模式（Lua 只能读、不能写）；
- 单位、将领、陷阱、增援：对应编辑模式或命令；
- 城市等级/名称微调：`ai.building.set` / `ai.building.bulk`。

---

## 9. 一句话流程

```
GeoJSON → geo rasterize（land / owners）→ build-map（dry_run）→ 看报表
        → dry_run=false 落地 → ai.mapgen.validate → 修 bbox/掩码 → 保存
```
