# mapgen 目录

```text
scripts/mapgen/
  core/                        # 通用建图引擎，不绑定具体地图
    build-map.lua
    make-provinces.lua

  maps/                        # 每张地图的专用工具
    europe/
      run.lua
    east_asia/
      run.lua
    north_america/
      run.lua
      provinces.lua
      city_provinces.lua
    china_taiwan/
      run.lua
    qingdao/
      run.lua
    shanghai/
      run.lua
      apply_zh_names.py
    usa/
      run.lua
    sunset/
      run.lua
      scenario.lua
      finalize.lua

  regions/                     # 所有地图共用的 spec / 掩码 / 城市数据
    europe_ww2.txt
    east_asia_1941.txt
    north_america.txt
    china_taiwan.txt
    qingdao.txt
    shanghai.txt
    usa.txt
    operation_sunset.txt
    ...
```

## 使用方式

欧洲：

```text
run_lua_file scripts/mapgen/maps/europe/run.lua
```

东亚：

```text
run_lua_file scripts/mapgen/maps/east_asia/run.lua
```

北美洲：

```text
run_lua_file scripts/mapgen/maps/north_america/run.lua
```

北美洲省区：

```text
run_lua_file scripts/mapgen/maps/north_america/provinces.lua
run_lua_file scripts/mapgen/maps/north_america/city_provinces.lua
```

上海中文城市名：

```bash
python scripts/mapgen/maps/shanghai/apply_zh_names.py
```

落日行动：

```text
run_lua_file scripts/mapgen/maps/sunset/run.lua
run_lua_file scripts/mapgen/maps/sunset/scenario.lua
run_lua_file scripts/mapgen/maps/sunset/finalize.lua
```

## 约定

- `core/` 只放不绑定地区的通用逻辑；
- `maps/<地区>/` 只放该地图的 run / 后处理脚本；
- `regions/` 只放 spec、海陆掩码、归属掩码、地形掩码、城市数据；
- 地图专用数据不要放回 `core/`；
- `last-report.txt` 是 build-map 自动生成的报告，不是工具。
