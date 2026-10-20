--[[============================================================================
  ai/lib/terrain.lua —— 地形 / 归属 / 省区
  ----------------------------------------------------------------------------
  地形只有"第一层地形类型"是可读的（map.GetTerrain），改地形用 SetTerrain。
  归属值 = 军团列表索引（255 = 无归属）；省区值约定上是"省会格的索引"。

  注意：征服地图的归属数据只覆盖裁剪区，区外写入会失败（引擎会自动打印原因）。
  本模块对区域写入做了保护：整片失败时提前中止，避免刷屏几千行错误。

  常用：
      local ai = require('ai')
      ai.terrain.set{ col=10, row=20, type=2 }
      ai.terrain.region{ col1=0, row1=0, col2=40, row2=30, type=2, dry_run=true }
      ai.terrain.belong_region{ col1=0, row1=0, col2=40, row2=30, value=3 }
      ai.terrain.coast()
      ai.terrain.stats{ step=2 }
============================================================================]]

local core = require('ai.lib.core')

local M = {}

local MAX_FAIL_LOG = 100   -- 区域写入失败超过这个数就中止，避免刷屏

-- ============================================================ 单格

function M.set(o)
    o = core.params(o)
    local col = core.to_int(core.need(o, 'col'), nil)
    local row = core.to_int(core.need(o, 'row'), nil)
    local t = core.to_int(core.need(o, 'type'), 0)

    local r = map.SetTerrain(col, row, t)
    print(string.format('[地形] (%d,%d) = %d → %s', col, row, t, tostring(r.Message)))
    return r
end

function M.change(o)
    o = core.params(o)
    local col = core.to_int(core.need(o, 'col'), nil)
    local row = core.to_int(core.need(o, 'row'), nil)
    local delta = core.to_int(core.need(o, 'delta'), 0)

    local r = map.ChangeTerrainType(col, row, delta)
    print(string.format('[地形类型切换] (%d,%d) delta=%d → %s', col, row, delta, tostring(r.Message)))
    return r
end

function M.decoration(o)
    o = core.params(o)
    local col = core.to_int(core.need(o, 'col'), nil)
    local row = core.to_int(core.need(o, 'row'), nil)
    local delta = core.to_int(core.need(o, 'delta'), 0)

    local r = map.ChangeDecoration(col, row, delta)
    print(string.format('[地形变体切换] (%d,%d) delta=%d → %s', col, row, delta, tostring(r.Message)))
    return r
end

function M.river(o)
    o = core.params(o)
    local col = core.to_int(core.need(o, 'col'), nil)
    local row = core.to_int(core.need(o, 'row'), nil)
    local value = core.to_int(core.need(o, 'value'), 0)

    local r = map.SetRiver(col, row, value)
    print(string.format('[河流] (%d,%d) = %d → %s', col, row, value, tostring(r.Message)))
    return r
end

function M.copy(o)
    o = core.params(o)
    local col = core.to_int(core.need(o, 'col'), nil)
    local row = core.to_int(core.need(o, 'row'), nil)

    local r = map.CopyTerrain(col, row)
    print(string.format('[复制地形] (%d,%d) → %s', col, row, tostring(r.Message)))
    return r
end

function M.paste(o)
    o = core.params(o)
    local col = core.to_int(core.need(o, 'col'), nil)
    local row = core.to_int(core.need(o, 'row'), nil)

    local r = map.PasteTerrain(col, row)
    print(string.format('[粘贴地形] (%d,%d) → %s', col, row, tostring(r.Message)))
    return r
end

-- ============================================================ 区域

--- 把一块矩形区域的地形统一改成 type。
--- 参数：col1/row1/col2/row2、type、dry_run（只统计不改）、force（连"已经是该地形"的也重写）
function M.region(o)
    o = core.params(o)
    local rg = core.region(o)
    if rg == nil then
        error('[参数缺失] 需要 col1/row1/col2/row2（矩形区域）', 2)
    end

    local t = core.to_int(core.need(o, 'type'), 0)
    local dry = core.switch(core.opt(o, 'dry_run', false), false)
    local force = core.switch(core.opt(o, 'force', false), false)

    local cells, targets = 0, 0
    for row = rg.r1, rg.r2 do
        for col = rg.c1, rg.c2 do
            if core.valid(col, row) then
                cells = cells + 1
                if force or map.GetTerrain(col, row) ~= t then
                    targets = targets + 1
                end
            end
        end
    end

    core.head(string.format('地形区域 %s → %d', core.region_text(rg), t))
    print(string.format('区域内有效格 %d，需要改 %d 格', cells, targets))
    if dry then
        print('dry_run 模式：没有修改任何数据')
        return targets
    end

    local st = core.stat('地形区域修改')
    local fail_streak = 0
    for row = rg.r1, rg.r2 do
        for col = rg.c1, rg.c2 do
            if core.valid(col, row) and (force or map.GetTerrain(col, row) ~= t) then
                local ok = core.count(st, map.SetTerrain(col, row, t),
                    string.format('(%d,%d)', col, row))
                fail_streak = ok and 0 or (fail_streak + 1)
                if fail_streak >= MAX_FAIL_LOG then
                    core.warn(string.format('连续失败 %d 格，提前中止（可能是征服地图裁剪区外）', fail_streak))
                    return core.finish(st)
                end
            end
        end
    end

    return core.finish(st)
end

--- 设置单格归属
function M.belong(o)
    o = core.params(o)
    local col = core.to_int(core.need(o, 'col'), nil)
    local row = core.to_int(core.need(o, 'row'), nil)
    local value = core.to_int(core.need(o, 'value'), 255)

    local r = map.SetBelong(col, row, value)
    print(string.format('[归属] (%d,%d) = %d → %s', col, row, value, tostring(r.Message)))
    return r
end

--- 把一块矩形区域的归属统一改成 value（值 = 军团列表索引；255 = 清空归属）
function M.belong_region(o)
    o = core.params(o)
    local rg = core.region(o)
    if rg == nil then
        error('[参数缺失] 需要 col1/row1/col2/row2（矩形区域）', 2)
    end

    local value = core.to_int(core.need(o, 'value'), 255)
    local dry = core.switch(core.opt(o, 'dry_run', false), false)

    local cells, targets = 0, 0
    for row = rg.r1, rg.r2 do
        for col = rg.c1, rg.c2 do
            if core.valid(col, row) then
                cells = cells + 1
                if map.GetBelong(col, row) ~= value then targets = targets + 1 end
            end
        end
    end

    core.head(string.format('归属区域 %s → %d', core.region_text(rg), value))
    print(string.format('区域内有效格 %d，需要改 %d 格', cells, targets))

    if dry then
        print('dry_run 模式：没有修改任何数据')
        return targets
    end
    if targets == 0 then
        print('区域归属已经是目标值，无需修改')
        return 0
    end

    -- 先探一格：整片不可写（没有归属数据 / 裁剪区外）时立刻收手
    local probe = map.SetBelong(rg.c1, rg.r1, value)
    if not probe.Success then
        core.warn('区域起点写入失败，已中止：' .. tostring(probe.Message))
        return probe
    end

    local st = core.stat('归属区域修改')
    core.count(st, probe, string.format('(%d,%d)', rg.c1, rg.r1))
    local fail_streak = 0
    for row = rg.r1, rg.r2 do
        for col = rg.c1, rg.c2 do
            if core.valid(col, row) and (col ~= rg.c1 or row ~= rg.r1)
               and map.GetBelong(col, row) ~= value then
                local ok = core.count(st, map.SetBelong(col, row, value),
                    string.format('(%d,%d)', col, row))
                fail_streak = ok and 0 or (fail_streak + 1)
                if fail_streak >= MAX_FAIL_LOG then
                    core.warn(string.format('连续失败 %d 格，提前中止（这些格子不在归属数据范围内）', fail_streak))
                    return core.finish(st)
                end
            end
        end
    end

    return core.finish(st)
end

function M.province(o)
    o = core.params(o)
    local col = core.to_int(core.need(o, 'col'), nil)
    local row = core.to_int(core.need(o, 'row'), nil)
    local value = core.to_int(core.need(o, 'value'), 65535)

    local r = map.SetProvince(col, row, value)
    print(string.format('[省区] (%d,%d) = %d → %s', col, row, value, tostring(r.Message)))
    return r
end

--- 省区值约定是"省会格索引"，逐格硬填会破坏结构；这里只在明确知道目标值时使用
function M.province_region(o)
    o = core.params(o)
    local rg = core.region(o)
    if rg == nil then
        error('[参数缺失] 需要 col1/row1/col2/row2（矩形区域）', 2)
    end

    local value = core.to_int(core.need(o, 'value'), 65535)
    local dry = core.switch(core.opt(o, 'dry_run', false), false)

    local targets = 0
    for row = rg.r1, rg.r2 do
        for col = rg.c1, rg.c2 do
            if core.valid(col, row) and map.GetProvince(col, row) ~= value then
                targets = targets + 1
            end
        end
    end

    core.head(string.format('省区区域 %s → %d', core.region_text(rg), value))
    print(string.format('需要改 %d 格', targets))
    if dry then
        print('dry_run 模式：没有修改任何数据')
        return targets
    end

    local st = core.stat('省区区域修改')
    for row = rg.r1, rg.r2 do
        for col = rg.c1, rg.c2 do
            if core.valid(col, row) and map.GetProvince(col, row) ~= value then
                core.count(st, map.SetProvince(col, row, value), string.format('(%d,%d)', col, row))
            end
        end
    end

    return core.finish(st)
end

-- ============================================================ 全局工具

function M.fill(o)
    o = core.params(o)
    local col = core.to_int(core.need(o, 'col'), nil)
    local row = core.to_int(core.need(o, 'row'), nil)
    local t = core.to_int(core.need(o, 'type'), 0)

    local before = map.GetTerrain(col, row)
    local r = map.FloodFillTerrain(col, row, t)
    print(string.format('[洪水填充] 起点 (%d,%d) 地形 %d → %d：%s',
        col, row, before, t, tostring(r.Message)))
    return r
end

function M.coast(o)
    local r = map.CreateCoast()
    print('[海岸线] ' .. tostring(r.Message))
    return r
end

function M.ocean_second_layer(o)
    local r = map.ProcessOceanSecondLayer()
    print('[海洋第二层] ' .. tostring(r.Message))
    return r
end

function M.greening(o)
    o = core.params(o)
    local p = core.to_int(core.need(o, 'probability'), 50)
    local r = map.Greening(p)
    print(string.format('[绿化平地] 概率=%d%%：%s', p, tostring(r.Message)))
    return r
end

function M.randomize_flat(o)
    o = core.params(o)
    local p = core.to_int(core.need(o, 'probability'), 50)
    local r = map.RandomizeFlatTerrain(p)
    print(string.format('[随机平地变体] 概率=%d%%：%s', p, tostring(r.Message)))
    return r
end

function M.randomize_variant(o)
    o = core.params(o)
    local p = core.to_int(core.need(o, 'probability'), 50)
    local r = map.RandomizeVariant(p)
    print(string.format('[随机当前层变体] 概率=%d%%：%s', p, tostring(r.Message)))
    return r
end

function M.export_hd(o)
    local r = map.ExportHd()
    print('[导出HD] ' .. tostring(r.Message))
    return r
end

function M.connect_buildings(o)
    local r = map.ConnectBuildings()
    print(string.format('[连接建筑] 影响 %d 条：%s', r.AffectedCount, tostring(r.Message)))
    return r
end

-- ============================================================ 统计

--- 地形类型分布（step 默认 2，大图可调大）
function M.stats(o)
    o = core.params(o)
    local step = core.to_int(core.opt(o, 'step', 2), 2)
    if step < 1 then step = 1 end

    local counts = {}
    local sampled = 0
    for row = 0, map.Height - 1, step do
        for col = 0, map.Width - 1, step do
            local t = map.GetTerrain(col, row)
            counts[t] = (counts[t] or 0) + 1
            sampled = sampled + 1
        end
    end

    core.head(string.format('地形分布（每 %d 格采样 1 格，共 %d 个样本）', step, sampled))
    for _, t in ipairs(core.top(counts, 20)) do
        print(string.format('  地形 %-3s %6d 格（%.1f%%）', tostring(t), counts[t], core.pct(counts[t], sampled)))
    end
    return counts
end

return M
