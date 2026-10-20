--[[============================================================================
  ai/lib/record.lua —— 记录列表编辑（事件 / 方针 / 天气 / 战略建设 / 空袭 / 空中支援 / 援军）
  ----------------------------------------------------------------------------
  七种数据形状一致，这里用 kind 字符串统一入口：
      event        事件
      case         AI 方针
      weather      天气
      strategic    战略建设
      airraid      空袭（挂在格子上，需要 col/row）
      airsupport   空中支援
      reinforcement 援军（挂在格子上，需要 col/row）

  常用：
      local ai = require('ai')
      ai.record.counts()
      ai.record.list{ kind='event', limit=20 }
      ai.record.fields{ kind='airraid' }
      ai.record.set{ kind='event', index=0, field='TriggerRound', value=3 }
      ai.record.bulk{ kind='weather', field='Duration', value=2, dry_run=true }
      ai.record.add{ kind='airraid', col=40, row=30 }

  字段名先 ai.record.fields{ kind=... } 查；字段值只能写数字（文字标签是只读的）。
  军团类字段（TriggerLegion / AffectedLegion / LegionId / OwnerLegion …）
  存的是军团的 ActionId，不是归属值。
============================================================================]]

local core = require('ai.lib.core')

local M = {}

local KINDS = {
    event = {
        label = '事件',
        needs_cell = false,
        count = function() return map.EventCount end,
        get = function(i) return map.GetEvent(i) end,
        fields = function() return map.GetEventFields() end,
        set = function(i, f, v) return map.SetEventField(i, f, v) end,
        add = function(i, r) return map.AddEvent() end,
        remove = function(i) return map.RemoveEvent(i) end,
        clear = function() return map.RemoveAllEvents() end,
    },
    case = {
        label = 'AI方针',
        needs_cell = false,
        count = function() return map.CaseCount end,
        get = function(i) return map.GetCase(i) end,
        fields = function() return map.GetCaseFields() end,
        set = function(i, f, v) return map.SetCaseField(i, f, v) end,
        add = function(i, r) return map.AddCase() end,
        remove = function(i) return map.RemoveCase(i) end,
        clear = function() return map.RemoveAllCases() end,
    },
    weather = {
        label = '天气',
        needs_cell = false,
        count = function() return map.WeatherCount end,
        get = function(i) return map.GetWeather(i) end,
        fields = function() return map.GetWeatherFields() end,
        set = function(i, f, v) return map.SetWeatherField(i, f, v) end,
        add = function(i, r) return map.AddWeather() end,
        remove = function(i) return map.RemoveWeather(i) end,
        clear = function() return map.RemoveAllWeathers() end,
    },
    strategic = {
        label = '战略建设',
        needs_cell = false,
        count = function() return map.StrategicCount end,
        get = function(i) return map.GetStrategic(i) end,
        fields = function() return map.GetStrategicFields() end,
        set = function(i, f, v) return map.SetStrategicField(i, f, v) end,
        add = function(i, r) return map.AddStrategic() end,
        remove = function(i) return map.RemoveStrategic(i) end,
        clear = function() return map.RemoveAllStrategics() end,
    },
    airraid = {
        label = '空袭',
        needs_cell = true,
        count = function() return map.AirRaidCount end,
        get = function(i) return map.GetAirRaid(i) end,
        fields = function() return map.GetAirRaidFields() end,
        set = function(i, f, v) return map.SetAirRaidField(i, f, v) end,
        add = function(c, r) return map.AddAirRaid(c, r) end,
        remove = function(i) return map.RemoveAirRaid(i) end,
        clear = function() return map.RemoveAllAirRaids() end,
    },
    airsupport = {
        label = '空中支援',
        needs_cell = false,
        count = function() return map.AirSupportCount end,
        get = function(i) return map.GetAirSupport(i) end,
        fields = function() return map.GetAirSupportFields() end,
        set = function(i, f, v) return map.SetAirSupportField(i, f, v) end,
        add = function(i, r) return map.AddAirSupport() end,
        remove = function(i) return map.RemoveAirSupport(i) end,
        clear = function() return map.RemoveAllAirSupports() end,
    },
    reinforcement = {
        label = '援军',
        needs_cell = true,
        count = function() return map.ReinforcementCount end,
        get = function(i) return map.GetReinforcement(i) end,
        fields = function() return map.GetReinforcementFields() end,
        set = function(i, f, v) return map.SetReinforcementField(i, f, v) end,
        add = function(c, r) return map.AddReinforcement(c, r) end,
        remove = function(i) return map.RemoveReinforcement(i) end,
        clear = function() return map.RemoveAllReinforcements() end,
    },
}

local ORDER = { 'event', 'case', 'weather', 'strategic', 'airraid', 'airsupport', 'reinforcement' }

local function kind_of(name)
    local key = string.lower(tostring(name or ''))
    local k = KINDS[key]
    if k == nil then
        error('[参数] 未知记录种类 ' .. tostring(name) ..
              '；可用：' .. table.concat(ORDER, ', '), 3)
    end
    return k, key
end

local function format_record(index, rec)
    local parts = {}
    for _, key in ipairs(core.keys_sorted(rec)) do
        if key ~= 'Index' then
            local v = rec[key]
            if type(v) == 'string' then
                parts[#parts + 1] = key .. '=' .. v
            elseif type(v) == 'number' and v ~= 0 then
                parts[#parts + 1] = key .. '=' .. core.num(v)
            end
        end
    end
    return string.format('[%d] %s', index, table.concat(parts, ' '))
end

-- ============================================================ 查询

function M.kinds()
    core.head('记录种类')
    for _, key in ipairs(ORDER) do
        print(string.format('  %-14s %s（当前 %d 条）', key, KINDS[key].label, KINDS[key].count()))
    end
    return ORDER
end

function M.counts()
    core.head('记录条数')
    for _, key in ipairs(ORDER) do
        print(string.format('  %-14s %s：%d', key, KINDS[key].label, KINDS[key].count()))
    end
end

function M.fields(o)
    o = core.params(o)
    local k, key = kind_of(core.need(o, 'kind'))
    core.dump_fields(key .. '（' .. k.label .. '）可写字段', k.fields())
    return k.fields()
end

--- 列出记录。参数：kind（必需）、limit（默认 50，最多显示这么多条）
function M.list(o)
    o = core.params(o)
    local k, key = kind_of(core.need(o, 'kind'))
    local limit = core.to_int(core.opt(o, 'limit', 50), 50)
    local total = k.count()

    core.head(string.format('%s（%s）共 %d 条', k.label, key, total))
    for i = 0, total - 1 do
        if i < limit then
            local rec = k.get(i)
            if rec ~= nil then
                print(format_record(i, rec))
            end
        end
    end
    if total > limit then
        print(string.format('-- 另有 %d 条未显示（用 limit 调大）', total - limit))
    end
    return total
end

--- 看单条记录的全部字段（含 0 值字段）
function M.get(o)
    o = core.params(o)
    local k, key = kind_of(core.need(o, 'kind'))
    local index = core.to_int(core.need(o, 'index'), 0)

    local rec = k.get(index)
    if rec == nil then
        return core.fail(string.format('%s 索引 %d 不存在（共 %d 条）', key, index, k.count()))
    end

    core.head(string.format('%s [%d]', k.label, index))
    for _, field in ipairs(core.keys_sorted(rec)) do
        print(string.format('  %-24s %s', field, tostring(rec[field])))
    end
    return rec
end

-- ============================================================ 修改

function M.set(o)
    o = core.params(o)
    local k, key = kind_of(core.need(o, 'kind'))
    local index = core.to_int(core.need(o, 'index'), 0)
    local field = core.canon(k.fields(), core.need(o, 'field'))
    local value = core.as_number(core.need(o, 'value'), 'value')

    if field == nil then
        error('[字段名] 未知字段 ' .. tostring(o['field']) ..
              '；可用：' .. table.concat(core.list_of(k.fields()), ', '), 2)
    end

    local r = k.set(index, field, value)
    print(string.format('[%s] [%d] %s=%s → %s', key, index, field, core.num(value), tostring(r.Message)))
    return r
end

--- 批量改记录字段。
--- 参数：kind, field, value, op='set'|'add'
---       index_from / index_to（可选）
---       filter_field / filter_value（只改该字段等于此值的记录，可选）
---       dry_run=true、limit 预览条数
function M.bulk(o)
    o = core.params(o)
    local k, key = kind_of(core.need(o, 'kind'))
    local field = core.canon(k.fields(), core.need(o, 'field'))
    local value = core.as_number(core.need(o, 'value'), 'value')
    local op = string.lower(tostring(core.opt(o, 'op', 'set')))
    local from = core.to_int(core.opt(o, 'index_from', 0), 0)
    local to = core.to_int(core.opt(o, 'index_to', k.count() - 1), k.count() - 1)
    local filter_field = core.opt(o, 'filter_field', nil)
    local filter_value = core.opt(o, 'filter_value', nil)
    local dry = core.switch(core.opt(o, 'dry_run', false), false)
    local limit = core.to_int(core.opt(o, 'limit', 10), 10)

    if field == nil then
        error('[字段名] 未知字段 ' .. tostring(o['field']) ..
              '；可用：' .. table.concat(core.list_of(k.fields()), ', '), 2)
    end
    if op ~= 'set' and op ~= 'add' then
        error("[参数] op 只能是 'set' 或 'add'，收到：" .. tostring(op), 2)
    end
    if from < 0 then from = 0 end
    if to > k.count() - 1 then to = k.count() - 1 end

    local canon_filter = nil
    if filter_field ~= nil then
        canon_filter = core.canon(k.fields(), filter_field)
        if canon_filter == nil then
            error('[字段名] 未知过滤字段 ' .. tostring(filter_field), 2)
        end
    end

    local targets = {}
    for i = from, to do
        local rec = k.get(i)
        if rec ~= nil then
            local hit = true
            if canon_filter ~= nil then
                hit = (rec[canon_filter] == tonumber(filter_value))
            end
            if hit then targets[#targets + 1] = i end
        end
    end

    core.head(string.format('%s 批量 %s %s=%s（索引 %d~%d）', k.label, op, field, core.num(value), from, to))
    print(string.format('命中 %d 条', #targets))

    if dry then
        for n = 1, math.min(limit, #targets) do
            local rec = k.get(targets[n])
            if rec ~= nil then print('  ' .. format_record(targets[n], rec)) end
        end
        print('dry_run 模式：没有修改任何数据')
        return targets
    end

    local st = core.stat(k.label .. '批量修改 ' .. field)
    for _, i in ipairs(targets) do
        local v = value
        if op == 'add' then
            local rec = k.get(i)
            local cur = rec and tonumber(rec[field]) or nil
            if cur == nil then
                core.skip(st)
                v = nil
            else
                v = cur + value
            end
        end

        if v ~= nil then
            core.count(st, k.set(i, field, v), '[' .. i .. ']')
        end
    end

    return core.finish(st)
end

function M.add(o)
    o = core.params(o)
    local k, key = kind_of(core.need(o, 'kind'))

    local col, row = -1, -1
    if k.needs_cell then
        col = core.to_int(core.need(o, 'col'), -1)
        row = core.to_int(core.need(o, 'row'), -1)
        if col < 0 or row < 0 then
            error('[参数缺失] ' .. key .. ' 需要 col / row', 2)
        end
    end

    local before = k.count()
    local r = k.add(col, row)
    print(string.format('[新增%s] %d → %d 条：%s', k.label, before, k.count(), tostring(r.Message)))
    return r
end

function M.remove(o)
    o = core.params(o)
    local k, key = kind_of(core.need(o, 'kind'))
    local index = core.to_int(core.need(o, 'index'), 0)

    local r = k.remove(index)
    print(string.format('[删除%s] 索引 %d，剩余 %d 条：%s', k.label, index, k.count(), tostring(r.Message)))
    return r
end

function M.clear(o)
    o = core.params(o)
    local k, key = kind_of(core.need(o, 'kind'))

    local before = k.count()
    local r = k.clear()
    print(string.format('[清空%s] %d → %d 条：%s', k.label, before, k.count(), tostring(r.Message)))
    return r
end

return M
