--[[============================================================================
  ai/lib/building.lua —— 建筑查询与编辑
  ----------------------------------------------------------------------------
  对应 AI 工具里的 get_building_list / get_object / set_object_field /
  set_object_field_batch，但更细：可按区域、类型、归属、有无名称筛选，
  支持 dry_run 先看命中范围再动手。

  常用：
      local ai = require('ai')
      ai.building.list{ col1=0, row1=0, col2=60, row2=40, type=11 }
      ai.building.set{ col=10, row=12, field='FactoryLevel', value=3 }
      ai.building.bulk{ field='FactoryLevel', value=3, belong=2, dry_run=true }
      ai.building.add{ col=10, row=12, type=11, name='北京' }

  字段名用 ai.building.fields() 查（大小写不敏感）。改数据一律走 map.*，
  给快照字段赋值不会有任何效果。
============================================================================]]

local core = require('ai.lib.core')

local M = {}

local FIELDS = nil

local function fields()
    if FIELDS == nil then
        FIELDS = core.list_of(map.GetBuildingFields())
    end
    return FIELDS
end

local function belong_text(v)
    if v == nil or v == 255 then return '无归属' end
    return tostring(v)
end

local function name_text(b)
    if b.Name ~= nil and b.Name ~= '' then return b.Name end
    if b.HasName then return '#' .. tostring(b.NameId) end
    return '-'
end

local function format_line(b)
    local extra = {}
    if b.FactoryLevel > 0 then extra[#extra + 1] = '工厂' .. b.FactoryLevel end
    if b.ResearchLevel > 0 then extra[#extra + 1] = '研究' .. b.ResearchLevel end
    if b.MedicalLevel > 0 then extra[#extra + 1] = '医疗' .. b.MedicalLevel end
    if b.AviationLevel > 0 then extra[#extra + 1] = '航空' .. b.AviationLevel end
    if b.MissileLevel > 0 then extra[#extra + 1] = '导弹' .. b.MissileLevel end
    if b.NuclearLevel > 0 then extra[#extra + 1] = '核' .. b.NuclearLevel end
    if b.KeyPoint ~= 0 then extra[#extra + 1] = '关键点' .. b.KeyPoint end

    return string.format('(%d,%d) 类型=%d %s 名称=%s 归属=%s%s',
        b.Col, b.Row, b.BuildingType, b.BuildingTypeName,
        name_text(b), belong_text(b.Belong),
        #extra > 0 and (' ' .. table.concat(extra, ' ')) or '')
end

-- ============================================================ 查询

function M.fields()
    core.dump_fields('建筑可写字段', fields())
    return fields()
end

function M.get(o)
    o = core.params(o)
    local col = core.to_int(core.need(o, 'col'), nil)
    local row = core.to_int(core.need(o, 'row'), nil)

    local b = map.GetBuilding(col, row)
    if b == nil then
        return core.fail(string.format('(%d,%d) 没有建筑', col, row))
    end

    core.head(string.format('建筑 (%d,%d)', col, row))
    print(format_line(b))
    print(string.format('  名称ID=%d 外观=%d 地标=%d 装饰=%d 解锁技能=%d 奖励=%d',
        b.NameId, b.Appearance, b.LandmarkBuilding, b.DecorativeBuilding, b.SkillUnlock, b.RewardCount))
    print(string.format('  仇恨值=%d 占领事件=%d 起火=%d 燃烧=%d 防空武器=%d 防空雷达=%d',
        b.HatredValue, b.OccupationEvent, b.FireIgnition, b.FireDuration, b.AirDefenseWeapon, b.AirDefenseRadar))
    print(string.format('  工厂=%d 研究=%d 医疗=%d 航空=%d 导弹=%d 核=%d',
        b.FactoryLevel, b.ResearchLevel, b.MedicalLevel, b.AviationLevel, b.MissileLevel, b.NuclearLevel))
    return b
end

--- 列出建筑。
--- 参数：col1/row1/col2/row2（区域）、col/row（单格）、type、belong、
---       has_name（true/false）、limit（默认 100 行）
function M.list(o)
    o = core.params(o)
    local rg = core.region(o)
    local want_type = core.to_int(core.opt(o, 'type', core.opt(o, 'building_type', nil)), nil)
    local want_belong = core.to_int(core.opt(o, 'belong', nil), nil)
    local want_named = core.switch(core.opt(o, 'has_name', nil), nil)
    local limit = core.to_int(core.opt(o, 'limit', 100), 100)

    local total, shown = 0, 0
    local rows = {}

    core.head(string.format('建筑列表（区域 %s，类型 %s，归属 %s）',
        core.region_text(rg),
        want_type ~= nil and tostring(want_type) or '全部',
        want_belong ~= nil and tostring(want_belong) or '全部'))

    map.ForEachBuilding(function(b)
        if not core.in_region(rg, b.Col, b.Row) then return end
        if want_type ~= nil and b.BuildingType ~= want_type then return end
        if want_belong ~= nil and b.Belong ~= want_belong then return end
        if want_named ~= nil and b.HasName ~= want_named then return end

        total = total + 1
        rows[#rows + 1] = b
        if shown < limit then
            shown = shown + 1
            print(format_line(b))
        end
    end)

    print(string.format('-- 命中 %d 个，显示 %d 个%s', total, shown,
        total > shown and '（用 limit 调大或加筛选条件）' or ''))
    return rows
end

-- ============================================================ 修改

function M.add(o)
    o = core.params(o)
    local col = core.to_int(core.need(o, 'col'), nil)
    local row = core.to_int(core.need(o, 'row'), nil)
    local btype = core.to_int(core.need(o, 'type'), nil)
    local name = core.opt(o, 'name', nil)

    local r
    if name == nil then
        r = map.AddBuilding(col, row, btype)
    else
        r = map.AddBuilding(col, row, btype, tostring(name))
    end

    print(string.format('[新增建筑] (%d,%d) 类型=%d → %s', col, row, btype, tostring(r.Message)))
    return r
end

function M.remove(o)
    o = core.params(o)
    local col = core.to_int(core.need(o, 'col'), nil)
    local row = core.to_int(core.need(o, 'row'), nil)

    local r = map.RemoveBuilding(col, row)
    print(string.format('[删除建筑] (%d,%d) → %s', col, row, tostring(r.Message)))
    return r
end

--- 改单个建筑的一个字段。
--- field='Name' 时可以传字符串（走 SetBuildingName，自带名称表解析）
function M.set(o)
    o = core.params(o)
    local col = core.to_int(core.need(o, 'col'), nil)
    local row = core.to_int(core.need(o, 'row'), nil)
    local field = core.canon(fields(), core.need(o, 'field'))
    local value = core.need(o, 'value')

    if field == nil then
        error('[字段名] 未知建筑字段 ' .. tostring(o['field']) ..
              '；可用：' .. table.concat(fields(), ', '), 2)
    end

    if field == 'Name' and type(value) ~= 'number' then
        local r = map.SetBuildingName(col, row, tostring(value))
        print(string.format('[建筑] (%d,%d) Name=%s → %s', col, row, tostring(value), tostring(r.Message)))
        return r
    end

    local r = map.SetBuildingField(col, row, field, core.as_number(value, 'value'))
    print(string.format('[建筑] (%d,%d) %s=%s → %s', col, row, field, core.num(value), tostring(r.Message)))
    return r
end

--- 批量改同一类建筑的同名个字段（最常用的"按区域/归属统一改"）。
--- 参数：
---   field, value, op='set'|'add'
---   col1,row1,col2,row2 区域（可选）
---   type / building_type 建筑类型（可选）
---   belong 归属（可选）
---   has_name=true|false 只处理有/无名称的建筑（可选）
---   dry_run=true 只看命中，不改数据（强烈建议先跑一次）
---   limit 预览行数（默认 10）
function M.bulk(o)
    o = core.params(o)
    local field = core.canon(fields(), core.need(o, 'field'))
    local value = core.need(o, 'value')
    local op = string.lower(tostring(core.opt(o, 'op', 'set')))
    local rg = core.region(o)
    local want_type = core.to_int(core.opt(o, 'type', core.opt(o, 'building_type', nil)), nil)
    local want_belong = core.to_int(core.opt(o, 'belong', nil), nil)
    local want_named = core.switch(core.opt(o, 'has_name', nil), nil)
    local dry = core.switch(core.opt(o, 'dry_run', false), false)
    local limit = core.to_int(core.opt(o, 'limit', 10), 10)

    if field == nil then
        error('[字段名] 未知建筑字段 ' .. tostring(o['field']) ..
              '；可用：' .. table.concat(fields(), ', '), 2)
    end
    if op ~= 'set' and op ~= 'add' then
        error("[参数] op 只能是 'set' 或 'add'，收到：" .. tostring(op), 2)
    end
    if field == 'Name' then
        if op == 'add' then
            error('[参数] 建筑的 Name 是名称 ID，不能用 op=add', 2)
        end
        if type(value) ~= 'number' and type(value) ~= 'string' then
            error('[参数] Name 只能是名称 ID（数字）或名称文字（字符串）', 2)
        end
    else
        value = core.as_number(value, 'value')
    end

    local targets = {}
    map.ForEachBuilding(function(b)
        if not core.in_region(rg, b.Col, b.Row) then return end
        if want_type ~= nil and b.BuildingType ~= want_type then return end
        if want_belong ~= nil and b.Belong ~= want_belong then return end
        if want_named ~= nil and b.HasName ~= want_named then return end
        targets[#targets + 1] = b
    end)

    core.head(string.format('建筑批量 %s %s=%s（区域 %s，类型 %s，归属 %s）',
        op, field, tostring(value), core.region_text(rg),
        want_type ~= nil and tostring(want_type) or '全部',
        want_belong ~= nil and tostring(want_belong) or '全部'))
    print(string.format('命中 %d 个建筑', #targets))

    if dry then
        for i = 1, math.min(limit, #targets) do
            print('  ' .. format_line(targets[i]))
        end
        if #targets > limit then
            print(string.format('  …（另有 %d 个未显示）', #targets - limit))
        end
        print('dry_run 模式：没有修改任何数据')
        return targets
    end

    local st = core.stat('建筑批量修改 ' .. field)
    for _, b in ipairs(targets) do
        local v = value
        if op == 'add' then
            local cur = core.read(b, field)
            if type(cur) ~= 'number' then
                core.skip(st)
                v = nil
            else
                v = cur + value
            end
        end

        if v ~= nil then
            local r
            if field == 'Name' then
                r = map.SetBuildingName(b.Col, b.Row, tostring(v))
            else
                r = map.SetBuildingField(b.Col, b.Row, field, v)
            end
            core.count(st, r, string.format('(%d,%d)', b.Col, b.Row))
        end
    end

    return core.finish(st)
end

function M.move(o)
    o = core.params(o)
    local fc = core.to_int(core.need(o, 'from_col'), nil)
    local fr = core.to_int(core.need(o, 'from_row'), nil)
    local tc = core.to_int(core.need(o, 'to_col'), nil)
    local tr = core.to_int(core.need(o, 'to_row'), nil)

    local r = map.MoveBuilding(fc, fr, tc, tr)
    print(string.format('[移动建筑] (%d,%d) → (%d,%d)：%s', fc, fr, tc, tr, tostring(r.Message)))
    return r
end

--- 按概率删除建筑（每个建筑独立掷骰，不是"总数抽 n%"）
function M.remove_probability(o)
    o = core.params(o)
    local prob = core.to_int(core.need(o, 'probability'), nil)
    local keep_named = core.switch(core.opt(o, 'keep_named', false), false)

    local before = map.BuildingCount
    local r = map.RemoveBuildingsByProbability(prob, keep_named)
    local after = map.BuildingCount
    print(string.format('[按概率删除建筑] 概率=%d%% 保留有名称=%s，删除 %d 个（%d → %d）',
        prob, tostring(keep_named), before - after, before, after))
    return r
end

return M
