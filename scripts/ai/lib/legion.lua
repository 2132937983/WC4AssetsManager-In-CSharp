--[[============================================================================
  ai/lib/legion.lua —— 军团（势力）查询与编辑
  ----------------------------------------------------------------------------
  军团用【列表索引】定位，索引同时也是地图上的归属值（belong）。
  所以"把某个国家的地盘改成 3 号军团"= 把归属设成 3。

  两个特殊字段：
    CountryId / ActionId 是定位键，不能走通用 SetLegionField，
    本模块会自动改走带唯一性校验的 map.SetLegionCountryIdAt / SetLegionActionIdAt。

  常用：
      local ai = require('ai')
      ai.legion.list()
      ai.legion.set{ index=0, field='InitialEconomy', value=2000 }
      ai.legion.color{ index=0, r=255, g=0, b=0 }
      ai.legion.player{ index=0 }
      ai.legion.bulk{ field='InitialTech', value=3 }
      ai.legion.find_by_country{ country_id=8 }
============================================================================]]

local core = require('ai.lib.core')

local M = {}

local FIELDS = nil

local function fields()
    if FIELDS == nil then
        FIELDS = core.list_of(map.GetLegionFields())
    end
    return FIELDS
end

local function format_line(l)
    return string.format(
        '[%d] Action=%d 国家=%d 玩家=%s 阵营=%d 经济=%d 工业=%d 科技=%d 颜色=(%d,%d,%d) 战败=%d 血率=%s 税率=%s',
        l.Index, l.ActionId, l.CountryId, l.IsPlayer and '是' or '否', l.Camp,
        l.InitialEconomy, l.InitialIndustry, l.InitialTech,
        l.ColorR, l.ColorG, l.ColorB, l.DefeatCondition,
        core.num(l.CountryHpRate), core.num(l.CountryTaxRate))
end

-- ============================================================ 查询

function M.fields()
    core.dump_fields('军团可写字段', fields())
    print("另有 CountryId / ActionId：用 ai.legion.set{ index=0, field='CountryId', value=8 }（会走唯一性校验）")
    return fields()
end

function M.list(o)
    o = core.params(o)

    core.head('军团列表（索引 = 归属值）')
    print(string.format('共 %d 个军团', map.LegionCount))

    local rows = {}
    map.ForEachLegion(function(l)
        rows[#rows + 1] = l
        print(format_line(l))
    end)

    print('-- 平均等级（陆/海/空/防）用 ai.legion.get{ index=... } 查看单个军团')
    return rows
end

function M.get(o)
    o = core.params(o)
    local index = core.to_int(core.need(o, 'index'), nil)

    local l = map.GetLegionAt(index)
    if l == nil then
        return core.fail(string.format('军团索引 %d 不存在（共 %d 个）', index, map.LegionCount))
    end

    core.head('军团 ' .. index)
    print(format_line(l))
    print(string.format('  科技等级=%d AI行为=%d 平均陆/海/空/防=%s/%s/%s/%s',
        l.InitialTechLevel, l.CountryAiBehavior,
        core.num(l.AverageArmyLevel), core.num(l.AverageNavyLevel),
        core.num(l.AverageAirForceLevel), core.num(l.AverageDefenseLevel)))
    print(string.format('  核弹：原子弹=%d 氢弹=%d 三相弹=%d 反物质弹=%d',
        l.AtomicBombCount, l.HydrogenBombCount, l.NeutronBombCount, l.AntimatterBombCount))
    return l
end

--- 按国家 ID 找军团索引（找不到返回 -1）
function M.find_by_country(o)
    o = core.params(o)
    local cid = core.to_int(core.need(o, 'country_id'), nil)

    local found = -1
    for i = 0, map.LegionCount - 1 do
        if map.GetLegionCountryIdAt(i) == cid then
            found = i
            break
        end
    end

    print(string.format('[查军团] 国家ID=%d → 索引 %d%s', cid, found,
        found < 0 and '（没有军团使用该国家 ID）' or ''))
    return found
end

-- ============================================================ 修改

function M.set(o)
    o = core.params(o)
    local index = core.to_int(core.need(o, 'index'), nil)
    local raw = string.lower(tostring(core.need(o, 'field')))
    local value = core.as_number(core.need(o, 'value'), 'value')

    local r
    if raw == 'countryid' then
        r = map.SetLegionCountryIdAt(index, core.to_int(value, 0))
        print(string.format('[军团] [%d] CountryId=%s → %s', index, core.num(value), tostring(r.Message)))
        return r
    elseif raw == 'actionid' then
        r = map.SetLegionActionIdAt(index, core.to_int(value, 0))
        print(string.format('[军团] [%d] ActionId=%s → %s', index, core.num(value), tostring(r.Message)))
        return r
    end

    local field = core.canon(fields(), raw)
    if field == nil then
        error('[字段名] 未知军团字段 ' .. tostring(o['field']) ..
              '；可用：' .. table.concat(fields(), ', '), 2)
    end

    r = map.SetLegionField(index, field, value)
    print(string.format('[军团] [%d] %s=%s → %s', index, field, core.num(value), tostring(r.Message)))
    return r
end

--- 批量改所有/一段军团。
--- 参数：field, value, op='set'|'add'、index_from（默认 0）、index_to（默认最后一个）、
---       dry_run=true、limit 预览行数
function M.bulk(o)
    o = core.params(o)
    local raw = string.lower(tostring(core.need(o, 'field')))
    local value = core.as_number(core.need(o, 'value'), 'value')
    local op = string.lower(tostring(core.opt(o, 'op', 'set')))
    local from = core.to_int(core.opt(o, 'index_from', 0), 0)
    local to = core.to_int(core.opt(o, 'index_to', map.LegionCount - 1), map.LegionCount - 1)
    local dry = core.switch(core.opt(o, 'dry_run', false), false)
    local limit = core.to_int(core.opt(o, 'limit', 10), 10)

    if op ~= 'set' and op ~= 'add' then
        error("[参数] op 只能是 'set' 或 'add'，收到：" .. tostring(op), 2)
    end

    local is_key = (raw == 'countryid' or raw == 'actionid')
    local field = raw
    if not is_key then
        field = core.canon(fields(), raw)
        if field == nil then
            error('[字段名] 未知军团字段 ' .. tostring(o['field']) ..
                  '；可用：' .. table.concat(fields(), ', '), 2)
        end
    elseif op == 'add' then
        error('[参数] CountryId / ActionId 是定位键，不能用 op=add', 2)
    end

    if from < 0 then from = 0 end
    if to > map.LegionCount - 1 then to = map.LegionCount - 1 end

    core.head(string.format('军团批量 %s %s=%s（索引 %d~%d）', op, field, core.num(value), from, to))
    print(string.format('命中 %d 个军团', math.max(0, to - from + 1)))

    if dry then
        local shown = 0
        for i = from, to do
            local l = map.GetLegionAt(i)
            if l ~= nil and shown < limit then
                shown = shown + 1
                print('  ' .. format_line(l))
            end
        end
        print('dry_run 模式：没有修改任何数据')
        return nil
    end

    local st = core.stat('军团批量修改 ' .. field)
    for i = from, to do
        local l = map.GetLegionAt(i)
        if l ~= nil then
            local r
            if field == 'countryid' then
                r = map.SetLegionCountryIdAt(i, core.to_int(value, 0))
            elseif field == 'actionid' then
                r = map.SetLegionActionIdAt(i, core.to_int(value, 0))
            else
                local v = value
                if op == 'add' then
                    local cur = core.read(l, field)
                    if type(cur) ~= 'number' then
                        core.skip(st)
                        v = nil
                    else
                        v = cur + value
                    end
                end
                if v ~= nil then
                    r = map.SetLegionField(i, field, v)
                end
            end

            if r ~= nil then
                core.count(st, r, '[' .. i .. ']')
            end
        end
    end

    return core.finish(st)
end

function M.color(o)
    o = core.params(o)
    local index = core.to_int(core.need(o, 'index'), nil)
    local r = core.to_int(core.need(o, 'r'), 0)
    local g = core.to_int(core.need(o, 'g'), 0)
    local b = core.to_int(core.need(o, 'b'), 0)

    local res = map.SetLegionColor(index, r, g, b)
    print(string.format('[军团颜色] [%d] = (%d,%d,%d)：%s', index, r, g, b, tostring(res.Message)))
    return res
end

function M.country(o)
    o = core.params(o)
    local index = core.to_int(core.need(o, 'index'), nil)
    local cid = core.to_int(core.need(o, 'country_id'), 0)

    local r = map.SetLegionCountryIdAt(index, cid)
    print(string.format('[军团国家] [%d] CountryId=%d → %s', index, cid, tostring(r.Message)))
    return r
end

function M.action(o)
    o = core.params(o)
    local index = core.to_int(core.need(o, 'index'), nil)
    local aid = core.to_int(core.need(o, 'action_id'), 0)

    local r = map.SetLegionActionIdAt(index, aid)
    print(string.format('[军团Action] [%d] ActionId=%d → %s', index, aid, tostring(r.Message)))
    return r
end

--- 设置玩家军团（index < 0 表示没有玩家军团，全部 AI）
function M.player(o)
    o = core.params(o)
    local index = core.to_int(core.need(o, 'index'), -1)

    local r = map.SetPlayerLegionAt(index)
    print(string.format('[玩家军团] index=%d → %s', index, tostring(r.Message)))
    return r
end

function M.camp(o)
    o = core.params(o)
    local index = core.to_int(core.need(o, 'index'), nil)
    local camp = core.to_int(core.need(o, 'camp'), 0)

    local r = map.SetLegionCampAt(index, camp)
    print(string.format('[军团阵营] [%d] = %d → %s', index, camp, tostring(r.Message)))
    return r
end

function M.defeat(o)
    o = core.params(o)
    local index = core.to_int(core.need(o, 'index'), nil)
    local value = core.to_int(core.need(o, 'value'), 0)

    local r = map.SetLegionDefeatConditionAt(index, value)
    print(string.format('[战败条件] [%d] = %d → %s', index, value, tostring(r.Message)))
    return r
end

function M.add(o)
    local before = map.LegionCount
    local r = map.AddLegion()
    print(string.format('[新增军团] 总数 %d → %d：%s', before, map.LegionCount, tostring(r.Message)))
    return r
end

function M.remove(o)
    o = core.params(o)
    local index = core.to_int(core.need(o, 'index'), nil)

    local r = map.RemoveLegionAt(index)
    print(string.format('[删除军团] 索引 %d，剩余 %d 个（索引会重排）', index, map.LegionCount))
    return r
end

function M.clear(o)
    local n = map.LegionCount
    local st = core.stat('清空军团')
    for i = n - 1, 0, -1 do
        core.count(st, map.RemoveLegionAt(i), '[' .. i .. ']')
    end
    print(string.format('清空前 %d 个，清空后 %d 个', n, map.LegionCount))
    return core.finish(st)
end

function M.move(o)
    o = core.params(o)
    local from = core.to_int(core.need(o, 'from'), nil)
    local to = core.to_int(core.need(o, 'to'), nil)
    local renumber = core.switch(core.opt(o, 'renumber_action_ids', false), false)

    local r = map.MoveLegion(from, to, renumber)
    print(string.format('[移动军团] %d → %d（重排ActionId=%s）：%s',
        from, to, tostring(renumber), tostring(r.Message)))
    return r
end

-- ============================================================ 批量工具

function M.economy(o)
    o = core.params(o)
    local v = core.to_int(core.need(o, 'value'), 0)
    local r = map.SetAllLegionsEconomy(v)
    print(string.format('[全体经济] = %d：%s', v, tostring(r.Message)))
    return r
end

function M.industry(o)
    o = core.params(o)
    local v = core.to_int(core.need(o, 'value'), 0)
    local r = map.SetAllLegionsIndustry(v)
    print(string.format('[全体工业] = %d：%s', v, tostring(r.Message)))
    return r
end

function M.tech(o)
    o = core.params(o)
    local v = core.to_int(core.need(o, 'value'), 0)
    local r = map.SetAllLegionsTech(v)
    print(string.format('[全体科技] = %d：%s', v, tostring(r.Message)))
    return r
end

function M.default_colors(o)
    local r = map.ApplyDefaultLegionColors()
    print('[默认颜色] ' .. tostring(r.Message))
    return r
end

function M.colors_from_settings(o)
    local r = map.ApplyLegionColorsFromSettings()
    print('[配置颜色] ' .. tostring(r.Message))
    return r
end

function M.randomize_levels(o)
    local r = map.RandomizeLegionLevels()
    print('[随机等级/经济] ' .. tostring(r.Message))
    return r
end

function M.rebuild(o)
    local r = map.RebuildLegionActionBelong()
    print('[重建ActionId与归属] ' .. tostring(r.Message))
    return r
end

return M
