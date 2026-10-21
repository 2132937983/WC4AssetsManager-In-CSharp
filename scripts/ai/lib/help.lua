--[[============================================================================
  ai/lib/help.lua —— 工具箱速查表（纯输出，不改数据）
  ----------------------------------------------------------------------------
  AI 不确定怎么调时，先跑 scripts/ai/help.lua，或直接 ai.help()。
============================================================================]]

local M = {}

local TEXT = [[
================= WC4 地图编辑 Lua 工具箱 =================
用法（AI 工具 run_lua 里粘一段即可）：

    local ai = require('ai')
    ai.inspect.audit()                       -- 第一步：先体检

也可以直接跑现成脚本：
    run_lua_file  scripts/ai/audit.lua        -- 全景体检
    run_lua_file  scripts/ai/help.lua        -- 就是本页

前提：已经在渲染场景里打开了地图（开始界面/No map 时 Lua 通道不可用）。

----------------------------- 通用约定 -----------------------------
· 参数一律用表：ai.army.set{ col=10, row=20, field='Level', value=5 }
· 区域参数：col1,row1,col2,row2（矩形，两个角可反着写）或 col,row（单格）
· 批量修改建议先加 dry_run=true，看命中范围和样例，再正式跑
· op 默认 'set'；op='add' 表示在现有值上加减（可以是负数）
· 字段名大小写不敏感；不确定就调对应模块的 fields()
· 一次 run_lua = 一个撤销单元：Ctrl+Z 一次性退回整次脚本
· 单位/建筑的快照对象是只读的：改数据必须走 ai.* 或 map.*
· 逐格遍历用 ai.each_cell{ fn = function(col,row) ... end }（引擎没有 map.ForEachCell）
  可选 col1/row1/col2/row2 限定区域、step 采样步长；fn 返回 false 提前结束
· 单位的归属 = 它所在格子的归属（belong 就是军团列表索引）
· v3 专有字段写进 v1 地图会失败；ai.army.bulk 会自动跳过并计数

----------------------------- 巡检 -----------------------------
ai.inspect.summary()                  地图尺寸/建筑/单位/军团/记录条数
ai.inspect.audit()                    上面全部 + 分布 + 异常 + 抽样
ai.inspect.buildings{ limit=30 }      建筑类型分布 + 有名称城市清单
ai.inspect.armies{ limit=20 }         单位按兵种/等级/归属分布
ai.inspect.legions()                  军团表（索引=归属值）
ai.inspect.belongs{ step=4 }          全图归属分布
ai.inspect.terrain{ step=4 }          地形类型分布
ai.inspect.records()                  七种记录条数
ai.inspect.warnings()                 无归属/归属越界/国家ID重复
ai.shot()                             整幅截图（mode='region' 局部 / 'scene' 界面）

----------------------------- 建筑 -----------------------------
ai.building.fields()                  可写字段
ai.building.get{ col=0, row=0 }       单格详情
ai.building.list{ type=11, belong=2, has_name=true, limit=50 }
ai.building.add{ col=10, row=12, type=11, name='北京' }
ai.building.remove{ col=10, row=12 }
ai.building.set{ col=10, row=12, field='FactoryLevel', value=3 }
ai.building.set{ col=10, row=12, field='Name', value='柏林' }   -- 字符串走名称表
ai.building.bulk{ field='FactoryLevel', value=3, col1=0,row1=0,col2=60,row2=40 }
ai.building.bulk{ field='KeyPoint', value=1, type=11, dry_run=true }
ai.building.move{ from_col=1, from_row=2, to_col=3, to_row=4 }
ai.building.remove_probability{ probability=30, keep_named=true }

----------------------------- 单位 -----------------------------
ai.army.fields()
ai.army.get{ col=0, row=0 }
ai.army.list{ belong=2, level_max=3, unit_type=0, limit=50 }
ai.army.add{ col=40, row=30, unit_type=0, level=3, belong=2,
             fields={ Organization=100, General=5 } }
ai.army.remove{ col=40, row=30 }
ai.army.set{ col=40, row=30, field='Experience', value=50 }
ai.army.bulk{ field='Level', value=1, op='add', belong=2, level_max=4 }
ai.army.bulk{ field='HpLevel', value=2, dry_run=true }     -- v3 专有
ai.army.move{ from_col=10, from_row=20, to_col=11, to_row=20 }
ai.army.plan{ belong=2, plan=1 }            按归属统一方案（0-4）
ai.army.cycle_plan{ col=10, row=20 }
ai.army.remove_belong{ belong=2 }
ai.army.clear{}
ai.army.erase{ col=50, row=50, radius=5 }
ai.army.generate{ belong=2, probability=50 }
ai.army.generals{ belong=2, count=3 }
ai.army.strength{ belong=-1, level=-1 }      -1 = 全部/自动
ai.army.randomize{ belong=-1 }

----------------------------- 军团 -----------------------------
ai.legion.fields()
ai.legion.list() / ai.legion.get{ index=0 }
ai.legion.set{ index=0, field='InitialEconomy', value=2000 }
ai.legion.set{ index=0, field='CountryId', value=8 }   -- 走唯一性校验
ai.legion.bulk{ field='InitialTech', value=3, index_from=0, index_to=5 }
ai.legion.color{ index=0, r=255, g=0, b=0 }
ai.legion.player{ index=0 }                 index=-1 = 全部 AI
ai.legion.camp{ index=0, camp=1 }
ai.legion.defeat{ index=0, value=1 }
ai.legion.economy{ value=100 } / .industry / .tech
ai.legion.add{} / .remove{ index=3 } / .clear{}
ai.legion.move{ from=3, to=0, renumber_action_ids=true }
ai.legion.default_colors() / .colors_from_settings()
ai.legion.randomize_levels() / .rebuild()
ai.legion.find_by_country{ country_id=8 }

----------------------------- 地形 / 归属 / 省区 -----------------------------
ai.terrain.set{ col=10, row=20, type=2 }
ai.terrain.change{ col=10, row=20, delta=1 }
ai.terrain.decoration{ col=10, row=20, delta=1 }
ai.terrain.river{ col=10, row=20, value=15 }
ai.terrain.region{ col1=0,row1=0,col2=40,row2=30, type=2, dry_run=true }
ai.terrain.belong{ col=10, row=20, value=3 }
ai.terrain.belong_region{ col1=0,row1=0,col2=40,row2=30, value=3 }
ai.terrain.province{ col=10, row=20, value=12 }
ai.terrain.fill{ col=50, row=50, type=0 }        洪水填充
ai.terrain.copy{ col=1, row=1 } / .paste{ col=2, row=1 }
ai.terrain.coast() / .ocean_second_layer() / .connect_buildings() / .export_hd()
ai.terrain.greening{ probability=60 }
ai.terrain.randomize_flat{ probability=50 } / .randomize_variant{ probability=50 }
ai.terrain.stats{ step=2 }

----------------------------- 记录（事件/方针/天气/战略建设/空袭/支援/援军） -----------------------------
ai.record.kinds() / ai.record.counts()
ai.record.list{ kind='event', limit=20 }
ai.record.fields{ kind='event' }
ai.record.get{ kind='event', index=0 }
ai.record.set{ kind='event', index=0, field='TriggerRound', value=3 }
ai.record.bulk{ kind='weather', field='Duration', value=1, op='add',
                filter_field='WeatherType', filter_value=2, dry_run=true }
ai.record.add{ kind='event' } / .add{ kind='airraid', col=40, row=30 }
ai.record.remove{ kind='event', index=3 } / .clear{ kind='weather' }

----------------------------- 建图 / 几何校验 -----------------------------
先读记忆 map-generation-workflow（不要用 SetTerrain 逐格画大陆）：
  geo rasterize --geojson <GeoJSON> --out <mask.txt> --width 200 --height 180 \
                [--bbox minLon,minLat,maxLon,maxLat] [--smooth 1] [--min-island 4]
  geo rasterize ... --owners        （额外输出归属掩码与 <out>.map 字符映射）
然后 build-map.lua 读 [terrain] mode=mask / [belong] mode=mask 落地。

ai.mapgen.validate()                    当前地图：陆地比例/陆块/海岸线/城市落海/归属
ai.mapgen.validate{ expected_min=0.40, expected_max=0.55 }
ai.mapgen.validate_mask{ path='scripts/mapgen/regions/europe.land.txt' }

----------------------------- 不在 Lua 里的能力 -----------------------------
· 陷阱（trap）：只有 CLI 命令（trap list/place/remove/generate…）
· 切换场景 / 打开地图 / 新建地图：用 open_scene、open_map、new_campaign 等工具
· 资源(AM)/配置(CM)/stringtable、文件读写、记忆：用对应 AI 工具
· 退出前记得让用户确认：脚本改数据不可逐条撤销，只有整批 Ctrl+Z
==========================================================]]

function M.print()
    print(TEXT)
end

return M
