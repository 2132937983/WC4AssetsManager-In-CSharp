--[[============================================================================
  ai/lib/inspect.lua —— 地图体检 / 概览
  ----------------------------------------------------------------------------
  对应 AI 工具里的 get_map_info / get_building_list / get_legion_list /
  get_army_list，但一次能给出分布、异常和抽样，省掉来回几十次查询。

  建议 AI 接手一张图时的第一步：
      local ai = require('ai')
      ai.inspect.audit()

  单项：
      ai.inspect.summary()
      ai.inspect.buildings{ limit=30 }
      ai.inspect.armies{ limit=20 }
      ai.inspect.legions()
      ai.inspect.belongs{ step=4 }
      ai.inspect.terrain{ step=4 }
      ai.inspect.records()
      ai.inspect.warnings()
============================================================================]]

local core = require('ai.lib.core')

local M = {}

-- ============================================================ 概况

function M.summary(o)
    local w, h = map.Width, map.Height
    local ver = core.army_version()

    core.head('地图概况')
    print(string.format('尺寸：%d x %d（%d 格）', w, h, w * h))
    print(string.format('单位格式：%s', ver == 3 and 'v3' or (ver == 1 and 'v1' or '未知（地图上没有单位）')))
    print(string.format('建筑 %d 个，单位 %d 个，军团 %d 个',
        map.BuildingCount, map.ArmyCount, map.LegionCount))
    print(string.format('记录：事件 %d，方针 %d，天气 %d，战略建设 %d，空袭 %d，空中支援 %d，援军 %d',
        map.EventCount, map.CaseCount, map.WeatherCount, map.StrategicCount,
        map.AirRaidCount, map.AirSupportCount, map.ReinforcementCount))
    return map.BuildingCount + map.ArmyCount + map.LegionCount
end

-- ============================================================ 建筑

function M.buildings(o)
    o = core.params(o)
    local limit = core.to_int(core.opt(o, 'limit', 30), 30)

    local by_type = {}
    local named, no_belong, total = 0, 0, 0

    map.ForEachBuilding(function(b)
        total = total + 1
        local key = b.BuildingType .. ' ' .. b.BuildingTypeName
        by_type[key] = (by_type[key] or 0) + 1
        if b.HasName then named = named + 1 end
        if b.Belong == 255 then no_belong = no_belong + 1 end
    end)

    core.head('建筑分布')
    print(string.format('共 %d 个：有名称 %d，无归属 %d', total, named, no_belong))
    for _, key in ipairs(core.top(by_type, 15)) do
        print(string.format('  %-18s %5d 个（%.1f%%）', key, by_type[key], core.pct(by_type[key], total)))
    end

    local shown = 0
    print('  有名称的建筑（前 ' .. limit .. ' 个）：')
    map.ForEachBuilding(function(b)
        if b.HasName and shown < limit then
            shown = shown + 1
            print(string.format('    (%d,%d) %s 归属=%s', b.Col, b.Row, b.Name,
                b.Belong == 255 and '无' or tostring(b.Belong)))
        end
    end)

    return by_type
end

-- ============================================================ 单位

function M.armies(o)
    o = core.params(o)
    local limit = core.to_int(core.opt(o, 'limit', 20), 20)

    local by_type, by_level, by_belong = {}, {}, {}
    local total, no_belong = 0, 0
    local v1, v3 = 0, 0

    map.ForEachArmy(function(a)
        total = total + 1
        by_type[a.UnitTypeName] = (by_type[a.UnitTypeName] or 0) + 1
        by_level[a.Level] = (by_level[a.Level] or 0) + 1
        by_belong[a.Belong] = (by_belong[a.Belong] or 0) + 1
        if a.Belong == 255 then no_belong = no_belong + 1 end
        if a.Version == 3 then v3 = v3 + 1 else v1 = v1 + 1 end
    end)

    core.head('单位分布')
    print(string.format('共 %d 个（v1 %d，v3 %d）：无归属 %d', total, v1, v3, no_belong))

    if total == 0 then return by_type end

    print('  按兵种：')
    for _, key in ipairs(core.top(by_type, 10)) do
        print(string.format('    %-10s %5d 个', key, by_type[key]))
    end

    print('  按等级：')
    for _, lv in ipairs(core.keys_sorted(by_level)) do
        print(string.format('    %-3s 级 %5d 个', tostring(lv), by_level[lv]))
    end

    print('  按归属（前 ' .. limit .. '）：')
    for _, key in ipairs(core.top(by_belong, limit)) do
        print(string.format('    归属 %-4s %5d 个', key == 255 and '无' or tostring(key), by_belong[key]))
    end

    return by_type
end

-- ============================================================ 军团

function M.legions(o)
    local legion = require('ai.lib.legion')
    return legion.list(o)
end

-- ============================================================ 归属

--- 逐格统计归属分布（step 默认 2，大地图可调大）
function M.belongs(o)
    o = core.params(o)
    local step = core.to_int(core.opt(o, 'step', 2), 2)
    if step < 1 then step = 1 end

    local counts = {}
    local sampled = 0
    for row = 0, map.Height - 1, step do
        for col = 0, map.Width - 1, step do
            local v = map.GetBelong(col, row)
            counts[v] = (counts[v] or 0) + 1
            sampled = sampled + 1
        end
    end

    core.head(string.format('归属分布（每 %d 格采样 1 格，共 %d 个样本，军团 %d 个）',
        step, sampled, map.LegionCount))

    for _, v in ipairs(core.top(counts, 20)) do
        local name = (v == 255) and '无归属' or ('军团 ' .. tostring(v))
        if v ~= 255 and v >= map.LegionCount then
            name = name .. '（越界，军团没有这个索引）'
        end
        print(string.format('  %-28s %6d 格（%.1f%%）', name, counts[v], core.pct(counts[v], sampled)))
    end

    return counts
end

-- ============================================================ 地形 / 记录

function M.terrain(o)
    local terrain = require('ai.lib.terrain')
    return terrain.stats(o)
end

function M.records(o)
    local record = require('ai.lib.record')
    return record.counts(o)
end

-- ============================================================ 异常检查

function M.warnings(o)
    o = core.params(o)
    local sample_limit = core.to_int(core.opt(o, 'limit', 10), 10)

    local building_no_belong = 0
    local building_bad_belong = 0
    local army_no_belong = 0
    local army_bad_belong = 0
    local samples = {}

    map.ForEachBuilding(function(b)
        if b.Belong == 255 then
            building_no_belong = building_no_belong + 1
            if #samples < sample_limit then
                samples[#samples + 1] = string.format('建筑 (%d,%d) %s', b.Col, b.Row, b.BuildingTypeName)
            end
        elseif b.Belong >= map.LegionCount then
            building_bad_belong = building_bad_belong + 1
        end
    end)

    map.ForEachArmy(function(a)
        if a.Belong == 255 then
            army_no_belong = army_no_belong + 1
            if #samples < sample_limit then
                samples[#samples + 1] = string.format('单位 (%d,%d) %s', a.Col, a.Row, a.UnitTypeName)
            end
        elseif a.Belong >= map.LegionCount then
            army_bad_belong = army_bad_belong + 1
        end
    end)

    -- 军团：国家 ID 是否重复（BTL 校验会报这个）
    local seen, duplicates = {}, {}
    for i = 0, map.LegionCount - 1 do
        local cid = map.GetLegionCountryIdAt(i)
        if cid >= 0 then
            if seen[cid] ~= nil then
                duplicates[#duplicates + 1] = string.format('%d(索引 %d 与 %d)', cid, seen[cid], i)
            else
                seen[cid] = i
            end
        end
    end

    core.head('异常检查')
    print(string.format('建筑无归属：%d 个（归属值 255）', building_no_belong))
    print(string.format('建筑归属越界：%d 个（归属 >= 军团数 %d）', building_bad_belong, map.LegionCount))
    print(string.format('单位无归属：%d 个', army_no_belong))
    print(string.format('单位归属越界：%d 个', army_bad_belong))
    print(string.format('军团国家ID重复：%d 处%s', #duplicates,
        #duplicates > 0 and ('（' .. table.concat(duplicates, '，') .. '）') or ''))

    if #samples > 0 then
        print('抽样：')
        for _, s in ipairs(samples) do print('  ' .. s) end
    end

    local issues = building_bad_belong + army_bad_belong + #duplicates
    print(issues == 0 and '→ 没有发现结构性异常（无归属不等于错误，可能是海面/中立区）'
        or '→ 建议先修掉"归属越界/国家ID重复"，否则游戏侧可能读不出军团')
    return issues
end

-- ============================================================ 一键体检

function M.audit(o)
    o = core.params(o)
    M.summary(o)
    M.legions(o)
    M.buildings(o)
    M.armies(o)
    M.belongs{ step = core.opt(o, 'step', 4) }
    M.records(o)
    M.warnings(o)
    print('')
    print('体检完成。下一步常见操作：')
    print("  ai.building.list{ belong=2 }        -- 看某军团的城市")
    print("  ai.army.bulk{ field='Level', value=1, op='add', belong=2, dry_run=true }")
    print("  ai.legion.set{ index=0, field='InitialEconomy', value=2000 }")
    return true
end

return M
