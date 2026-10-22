# map-generation-workflow

> 保存时间: 2026-10-21 21:52:09

---

# map-generation-workflow

> 摘要: 本项目生成欧洲/东亚/局部地区战役地图的标准能力。核心是 spec 驱动 + 真实地理数据 + 掩码栅格化 + build-map 落地 + 校验。禁止用 SetTerrain 逐格画大陆。

---

## 0. 硬规则

1. **几何来自数据，不来自模型。** 海岸线、国家边界、城市坐标必须来自 GeoJSON / GeoNames / spec，不能让 AI 凭记忆画。
2. **不要用 `map.SetTerrain` 逐格画大陆。** 结果一定是半海半陆或城市斑块，而且难以自查。
3. **先 dry_run，再 validate，最后落地。** 写地图前必须看报表；落图后必须跑 `ai.mapgen.validate`。
4. **不要新建 btl 才能改地形。** 用 MCP `apply_map_spec` 或 `run_lua_file` 在当前场景重跑；用 `save <路径>` 保存。
5. **不要写死绝对路径。** spec / lua 里用 `scripts/mapgen/regions/...` 相对路径；`build-map.lua` 会自动向上查找仓库根。
6. **台湾相关项目内命名统一为“中国台湾”。** 文件名如 `china_taiwan.txt`、`maps/china_taiwan.btl`；原始 GeoNames/Natural Earth 数据除外。

---

## 1. 已有可复用资产

地理数据：

- `Resource/Geo/ne_10m_land.geojson`
- `Resource/Geo/ne_10m_admin_0_countries.geojson`
- `Resource/Geo/ne_50m_admin_0_countries.geojson`
- `Resource/Geo/ne_10m_populated_places.geojson`
- `Resource/Geo/east_asia_countries.geojson`
- `Resource/Geo/europe_ww2_countries.geojson`
- `Resource/Geo/sunset_countries.geojson`
- `Resource/Geo/TW/TW.txt`、`Resource/Geo/TW.zip`：GeoNames 台湾原始数据，可选；当前 `china_taiwan.txt` 已含 650 城，不依赖它也能重建。

建图脚本与数据：

- `scripts/mapgen/core/build-map.lua`：核心建图器，支持 `type_mask_file`、`snap_sea_radius`、`clear_cities`、`belong`、`post`。
- `scripts/mapgen/maps/europe/run.lua`：欧洲二战 spec 运行脚本。
- `scripts/mapgen/maps/east_asia/run.lua`：东亚 spec 运行脚本。
 - scripts/mapgen/maps/qingdao/run.lua、_sunset_run.lua：局部/场景示例。
 - scripts/mapgen/maps/qingdao/run.lua、_sunset_run.lua：局部/场景示例。
- `scripts/mapgen/core/make-provinces.lua`：按省会多源 BFS 生成省区。
- `scripts/mapgen/maps/sunset/finalize.lua`：落日行动补齐归属/省区/单位示例。
- `scripts/mapgen/regions/*.txt`：spec + 海陆掩码 + 归属掩码 + 地形类型掩码。这些必须提交，另一台电脑靠它们复现。

已有成品图：

- `maps/ww2_europe_1941.btl`：260×180，22 势力，654 城。
- `maps/east_asia_1941.btl`：208×144，12 势力，922 城。
- `maps/china_taiwan.btl`：150×210，1 势力，650 城。
- `maps/operation_sunset.btl`：200×180，5 势力，231 城，23 单位，80 省区。
- `maps/qingdao.btl`：160×170，1 势力，15 城。

---

## 2. 标准生成流程

### 2.1 欧洲 260×180 / 22 势力

```text
new_campaign width=260 height=180 legions=22
```

设置真实 WC4 国家 ID：

```lua
local targets = {1,2,3,5,7,11,12,13,16,17,18,19,20,21,22,23,24,25,26,27,28,57}
for i = 0, #targets - 1 do map.SetLegionCountryIdAt(i, 1000 + i) end
for i = 0, #targets - 1 do map.SetLegionCountryIdAt(i, targets[i + 1]) end
map.ApplyLegionColorsFromSettings()
map.SetLegionField(2, 'IsPlayerControlled', 1)
```

落地：

```text
run_lua_file scripts/mapgen/maps/europe/run.lua
```

校验：

```lua
local ai = require('ai')
local r = ai.mapgen.validate{ expected_min = 0.40, expected_max = 0.55, min_largest = 0.80 }
print('validate=' .. tostring(r.ok))
```

保存：

```text
save maps/ww2_europe_1941.btl
```

预期：654 城、0 落海、归属 100%。

### 2.2 东亚 208×144 / 12 势力

```text
new_campaign width=208 height=144 legions=12
```

国家 ID：

```lua
local targets = {1,2,5,6,9,10,19,32,33,34,35,36}
for i = 0, #targets - 1 do map.SetLegionCountryIdAt(i, 1000 + i) end
for i = 0, #targets - 1 do map.SetLegionCountryIdAt(i, targets[i + 1]) end
map.ApplyLegionColorsFromSettings()
```

落地：

```text
run_lua_file scripts/mapgen/maps/east_asia/run.lua
```

补省区：

```text
province clear
run_lua_file scripts/mapgen/core/make-provinces.lua
```

校验：

```lua
local r = ai.mapgen.validate{ expected_min = 0.35, expected_max = 0.60, min_largest = 0.80 }
```

保存：

```text
save maps/east_asia_1941.btl
```

预期：922 城、0 落海、归属 100%。

### 2.3 中国台湾 150×210 / 1 势力

```text
new_campaign width=150 height=210 legions=1
```

```lua
map.SetLegionCountryIdAt(0, 9)
map.ApplyLegionColorsFromSettings()
map.SetLegionField(0, 'IsPlayerControlled', 1)
```

落地：

```text
 - scripts/mapgen/maps/qingdao/run.lua、_sunset_run.lua：局部/场景示例。
```

校验：

```lua
local r = ai.mapgen.validate{ expected_min = 0.25, expected_max = 0.40, min_largest = 0.90 }
```

保存：

```text
save maps/china_taiwan.btl
```

预期：650 城、0 落海、归属 100%。

---

## 3. 城市生成方法

欧洲/东亚用 Natural Earth `ne_10m_populated_places.geojson`：

1. 按 bbox 过滤 `LONGITUDE/LATITUDE`。
2. 名称优先取 `NAME_ZH` / `NAME_ZHT`，去掉空格。
3. 按 `ISO_A2` / `ADM0_A3` 映射到 spec 的势力名。
4. 按人口排序、去重、每势力配额，目标数量通常 600~950。
5. 城市等级阈值：
   - ≥200 万：5
   - ≥50 万：4
   - ≥15 万：3
   - ≥5 万：2
   - 其余：1
6. 沿海城市用 spec 的 `snap_sea_radius = 2~3` 吸附到最近陆地。

中国台湾历史数据来自 GeoNames `TW/TW.txt`，但当前 spec 已固化 650 城；复建不依赖原始文件。

上海/GeoNames 小地名补中文规则（重要）：GeoNames `CN.txt` 里很多村镇只有罗马化/Wade-Giles 名，没有中文 alternate，直接生成会出英文。规则：

1. 先选 GeoNames 记录，若该记录 alternate 有中文则直接用；
2. 若没有，用 `scripts/mapgen/regions/shanghai_zh_names.json` 映射转换；
3. 该映射来源：GeoNames 全量同反查中文 alternate + MyMemory 在线翻译 + OpenStreetMap 逆地理中文名，不用 Google；
4. 用 `scripts/mapgen/maps/shanghai/apply_zh_names.py` 应用到 spec；
5. 映射值不能含空格，否则 spec 解析会错；改完必须 `clear_cities = true` 重建。


城市写入后检查：

```lua
print('buildings=' .. map.BuildingCount)
```

`ai.mapgen.validate` 中 `buildings_in_sea` 必须为 0。

---

## 4. 地形生成方法

### 4.1 海陆与归属

```text
geo rasterize --geojson Resource/Geo/ne_10m_land.geojson --out scripts/mapgen/regions/xxx.land.txt --width W --height H --bbox=minLon,minLat,maxLon,maxLat --smooth 0 --min-island 1
```

归属：

```text
geo rasterize --geojson Resource/Geo/xxx_countries.geojson --owners --out scripts/mapgen/regions/xxx.owner.txt --width W --height H --bbox=... --smooth 0
```

`geo rasterize --owners` 会生成 `.map`，格式 `字符 = 国家ID`。

### 4.2 地形类型掩码

`build-map.lua` 支持：

```ini
[terrain]
enabled = true
mode = mask
mask_file = scripts/mapgen/regions/xxx.land.txt
type_mask_file = scripts/mapgen/regions/xxx.terrain.txt
smooth = 0
```

类型掩码字符：`0-9` 直接作为地形 ID，`A-V` 对应 10-31。陆地格按字符写地形；海/未知字符回落到 `land_type`。

常用地形 ID：

| ID | 地形 |
|---|---|
| 0 | 平地 |
| 1 | 海洋 |
| 2 | 沙漠 |
| 3/4/5 | 矮/中/高雪山 |
| 6/7/8 | 矮/中/高土山 |
| 9/10/11 | 矮/中/高绿山 |
| 12/13/14 | 矮/中/高沙山 |
| 15 | 仙人掌 |
| 16 | 阔叶林 |
| 20 | 针叶林 |
| 21 | 积雪针叶林 |
| 22 | 热带森林 |
| 26 | 农田 |
| 31 | 雪地 |

山脉不要涂成一坨同款中山。按“山脊核心/中段/外缘”分大、中、小三级，并保留缺口：

- 核心：55%~70% 大山，其余中/小山；
- 中段：50%~70% 中山，其余小山；
- 外缘：25%~55% 小山，其余留给平地/森林。

已有案例：阿尔卑斯、喀尔巴阡、日本中央山脉、朝鲜山地、长白山、锡霍特山、中国台湾中央山脉/玉山/雪山。

城市周围 1 格强制平地，避免城市长在山上。

### 4.3 后处理

```ini
[post]
create_coast = true
process_ocean_layer2 = true
greening = 0
```

- `create_coast`：海岸线地形。
- `process_ocean_layer2`：海洋第二层。
- `greening`：随机把平地转森林，已有类型掩码时通常设 0。
- `smooth` 必须为 0：1 格宽海峡会被 3×3 多数表决填成陆地。

---

## 5. 省区、归属、单位

### 5.1 省区

```text
province clear
run_lua_file scripts/mapgen/core/make-provinces.lua
```

规则：

- 选五级城 + 部分四级城作为省会；
- 省区值 = 省会格子线性索引；
- 每个势力内多源 BFS，不跨势力；
- 兜底把剩余陆地分给最近同势力省会。

检查：

```lua
local W,H=map.Width,map.Height
local prov,cap,cells={},0,0
for r=0,H-1 do for c=0,W-1 do
  local v=map.GetProvince(c,r)
  if v~=65535 then cells=cells+1; prov[v]=true; if v==r*W+c then cap=cap+1 end end
end end
local n=0
for _ in pairs(prov) do n=n+1 end
print('provinces='..n..' cells='..cells..' capitals='..cap)
```

### 5.2 归属

用归属掩码落地后，必须没有无归属陆地：

```lua
local miss=0
for r=0,map.Height-1 do for c=0,map.Width-1 do
  if map.GetTerrain(c,r)~=1 and map.GetBelong(c,r)==255 then miss=miss+1 end
end end
print('missing_belong='..miss)
```

`missing_belong` 必须为 0。`map.GetBelong` 返回军团索引；每个势力格数总和应等于陆地格数。

### 5.3 单位

清旧单位：

```lua
local old={}
map.ForEachArmy(function(a) old[#old+1]={a.Col,a.Row} end)
for _,p in ipairs(old) do map.RemoveArmy(p[1],p[2]) end
```

放新单位：

```lua
map.AddArmy(c,r)
map.SetArmyLegion(c,r,legionIndex)
map.SetArmyField(c,r,'UnitType',unitType)
map.SetArmyField(c,r,'Level',level)
map.SetArmyField(c,r,'Organization',org)
map.SetArmyField(c,r,'Experience',60)
map.SetArmyField(c,r,'Morale',80)
```

落点必须避开建筑和已有单位，优先找城市附近空闲陆地。

---

## 6. MCP / 记忆能力

MCP 已具备记忆读写能力，不需要补：

- `list_memories`：列出 `Resource/Memory/*.md`。
- `read_memory`：读取指定记忆。
- `save_memory`：保存/覆盖记忆，同名会先备份。
- `delete_memory`：删除记忆。

本文件就是通过 `save_memory title=map-generation-workflow` 维护的。新会话开始应先 `list_memories`，再按需 `read_memory`。

MCP 地图能力要点：

- `apply_map_spec`：在当前场景重跑 spec，不新建 btl。
- `run_cli_command save <路径>`：直接保存，不弹对话框。
- `run_cli_command edit_mode <模式>` / `redraw`：模式切换与强制重绘，用于性能诊断。
- `run_lua` / `run_lua_file`：执行 Lua 与建图脚本。
- `capture_screenshot` / `ai.shot`：截图。

性能诊断日志：

```text
%TEMP%\wc4-render-profile.log
```

建筑/单位模式已去掉逐格归属旗层，避免高倍缩放卡顿。

---

## 7. 跨电脑复现

必须提交：

```text
scripts/mapgen/regions/
scripts/mapgen/*.lua
Resource/Geo/
maps/
.gitattributes
```

不要提交/不要依赖：

- 本机绝对路径；
- 临时 Python 生成脚本；
- `screenshots/`；
- `Logs/`。

另一台电脑只需：

1. 拉取仓库；
2. 启动编辑器，确认 MCP 地址 `http://127.0.0.1:4396/wc4-mcp`；
3. 用 `run_lua_file scripts/mapgen/maps/<地区>/run.lua` 或直接打开 `maps/*.btl`。

---

## 8. 一句话流程

```text
GeoJSON → geo rasterize（land/owners）→ 写 spec + cities + terrain mask
        → new_campaign + 设置国家ID → run_lua_file _xxx_run.lua
        → province clear + make-provinces → 放单位 → validate → save
```
