--[[============================================================================
  ai/lib/army.lua —— 单位（部队）查询与编辑
  ----------------------------------------------------------------------------
  单位的"归属"就是它所在格子的归属值；接口内部自动区分 v1 / v3 两种记录格式，
  v3 专有字段写到 v1 地图上会失败（本模块在批量里会直接跳过这些格子并计数）。

  常用：
      local ai = require('ai')
      ai.army.list{ belong=2, level_max=3 }
      ai.army.add{ col=40, row=30, unit_type=0, level=3, belong=2 }
      ai.army.set{ col=40, row=30, field='Experience', value=50 }
      ai.army.bulk{ field='Level', value=1, op='add', belong=2, dry_run=true }
      ai.army.plan{ belong=2, plan=1 }        -- 归属 2 的部队方案统一设为 1
============================================================================]]

local core = require('ai.lib.core')

local M = {}

local FIELDS = nil

--- v3 专有字段：v1 地图上没有这些字段，写入会失败
local V3_ONLY = {
    HpLevel = true,
    TransportShip = true,
    Hatred = true,
    AttackTarget = true,
    DeathDialogue = true,
    LevelMarkDisplay = true,
    AntiDefenseMode = true,
}

local function fields()
    if FIELDS == nil then
        FIELDS = core.list_of(map.GetArmyFields())
    end
    return FIELDS
end

local function belong_text(v)
    if v == nil or v == 255 then return '无归属' end
    return tostring(v)
end

local function format_line(a)
    return string.format('(%d,%d) 类型=%d %s 等级=%d 归属=%s 方案=%d 编制=%d 将领=%d 经验=%d 士气=%d%s',
        a.Col, a.Row, a.UnitType, a.UnitTypeName, a.Level, belong_text(a.Belong),
        a.Plan, a.Organization, a.General, a.Experience, a.Morale,
        a.Version == 3 and ' [v3]' or '')
end

local function matches(a, rg, want_belong, want_type, level_min, level_max)
    if not core.in_region(rg, a.Col, a.Row) then return false end
    if want_belong ~= nil and a.Belong ~= want_belong then return false end
    if want_type ~= nil and a.UnitType ~= want_type then return false end
    if level_min ~= nil and a.Level < level_min then return false end
    if level_max ~= nil and a.Level > level_max then return false end
    return true
end

-- ============================================================ 查询

function M.fields()
    core.dump_fields('单位可写字段（v3 专有字段已标注）', fields())
    print('v3 专有：HpLevel, TransportShip, Hatred, AttackTarget, DeathDialogue, LevelMarkDisplay, AntiDefenseMode')
    return fields()
end

function M.get(o)
    o = core.params(o)
    local col = core.to_int(core.need(o, 'col'), nil)
    local row = core.to_int(core.need(o, 'row'), nil)

    local a = map.GetArmy(col, row)
    if a == nil then
        return core.fail(string.format('(%d,%d) 没有单位', col, row))
    end

    core.head(string.format('单位 (%d,%d)  v%d', col, row, a.Version))
    print(format_line(a))
    print(string.format('  朝向=%d(%s) 机动=%d 建造回合=%d 血量=%d/%d 加成=%d',
        a.Direction, a.DirectionName, a.Mobility, a.BuiltRound,
        a.CurrentHealth, a.MaxHealth, a.HealthBonus))
    print(string.format('  政策=%d(%s) 军衔=%d 关键点=%d(%s) 可攻击=%d',
        a.Policy, a.PolicyName, a.Rank, a.KeyPoint, a.KeyPointName, a.CanAttack))
    if a.Version == 3 then
        print(string.format('  v3：血量等级=%d 运输船=%d 仇恨=%d 攻击目标=%d 阵亡对话=%d 等级标记=%d 反防=%d',
            a.HpLevel, a.TransportShip, a.Hatred, a.AttackTarget,
            a.DeathDialogue, a.LevelMarkDisplay, a.AntiDefenseMode))
    end
    return a
end

--- 列出单位。
--- 参数：col1/row1/col2/row2 或 col/row、belong、unit_type、
---       level_min/level_max、limit（默认 100 行）
function M.list(o)
    o = core.params(o)
    local rg = core.region(o)
    local want_belong = core.to_int(core.opt(o, 'belong', nil), nil)
    local want_type = core.to_int(core.opt(o, 'unit_type', nil), nil)
    local level_min = core.to_int(core.opt(o, 'level_min', nil), nil)
    local level_max = core.to_int(core.opt(o, 'level_max', nil), nil)
    local limit = core.to_int(core.opt(o, 'limit', 100), 100)

    core.head(string.format('单位列表（区域 %s，归属 %s，兵种 %s）',
        core.region_text(rg),
        want_belong ~= nil and tostring(want_belong) or '全部',
        want_type ~= nil and tostring(want_type) or '全部'))

    local total, shown = 0, 0
    local rows = {}

    map.ForEachArmy(function(a)
        if not matches(a, rg, want_belong, want_type, level_min, level_max) then return end
        total = total + 1
        rows[#rows + 1] = a
        if shown < limit then
            shown = shown + 1
            print(format_line(a))
        end
    end)

    print(string.format('-- 命中 %d 个单位，显示 %d 个%s', total, shown,
        total > shown and '（用 limit 调大或加筛选条件）' or ''))
    return rows
end

-- ============================================================ 增删改

--- 放置单位。
--- 参数：col, row（必需）、unit_type、level、belong、plan、
---       fields = { Level=5, Organization=100, ... }（其它字段一次给全）
function M.add(o)
    o = core.params(o)
    local col = core.to_int(core.need(o, 'col'), nil)
    local row = core.to_int(core.need(o, 'row'), nil)
    local want_belong = core.to_int(core.opt(o, 'belong', nil), nil)

    if map.GetArmy(col, row) ~= nil then
        return core.fail(string.format('(%d,%d) 已经有单位；要替换请先 ai.army.remove', col, row))
    end

    local r = map.AddArmy(col, row)
    if not r.Success then
        print(string.format('[新增单位] (%d,%d) 失败：%s', col, row, tostring(r.Message)))
        return r
    end

    local function set_field(name, value)
        if value == nil then return end
        local rr = map.SetArmyField(col, row, name, core.as_number(value, name))
        if not rr.Success then
            print(string.format('  [警告] %s=%s 未写入：%s', name, tostring(value), tostring(rr.Message)))
        end
    end

    set_field('UnitType', core.opt(o, 'unit_type', nil))
    set_field('Level', core.opt(o, 'level', nil))

    local extra = core.opt(o, 'fields', nil)
    if type(extra) == 'table' then
        for k, v in pairs(extra) do
            set_field(k, v)
        end
    end

    if core.opt(o, 'plan', nil) ~= nil then
        map.SetArmyPlan(col, row, core.to_int(o.plan, 0))
    end

    if want_belong ~= nil then
        local rb = map.SetArmyLegion(col, row, want_belong)
        if not rb.Success then
            print('  [警告] 归属未写入：' .. tostring(rb.Message))
        end
    end

    local placed = map.GetArmy(col, row)
    if placed ~= nil then
        print('[新增单位] ' .. format_line(placed))
    else
        print(string.format('[新增单位] (%d,%d) 已放置', col, row))
    end
    return r
end

function M.remove(o)
    o = core.params(o)
    local col = core.to_int(core.need(o, 'col'), nil)
    local row = core.to_int(core.need(o, 'row'), nil)

    local r = map.RemoveArmy(col, row)
    print(string.format('[删除单位] (%d,%d) → %s', col, row, tostring(r.Message)))
    return r
end

function M.set(o)
    o = core.params(o)
    local col = core.to_int(core.need(o, 'col'), nil)
    local row = core.to_int(core.need(o, 'row'), nil)
    local field = core.canon(fields(), core.need(o, 'field'))
    local value = core.need(o, 'value')

    if field == nil then
        error('[字段名] 未知单位字段 ' .. tostring(o['field']) ..
              '；可用：' .. table.concat(fields(), ', '), 2)
    end

    local r = map.SetArmyField(col, row, field, core.as_number(value, 'value'))
    print(string.format('[单位] (%d,%d) %s=%s → %s', col, row, field, core.num(value), tostring(r.Message)))
    return r
end

--- 批量改单位字段。
--- 参数：field, value, op='set'|'add'
---       col1/row1/col2/row2（区域）、belong、unit_type、level_min/level_max
---       dry_run=true 只看命中、limit 预览行数（默认 10）
function M.bulk(o)
    o = core.params(o)
    local field = core.canon(fields(), core.need(o, 'field'))
    local value = core.as_number(core.need(o, 'value'), 'value')
    local op = string.lower(tostring(core.opt(o, 'op', 'set')))
    local rg = core.region(o)
    local want_belong = core.to_int(core.opt(o, 'belong', nil), nil)
    local want_type = core.to_int(core.opt(o, 'unit_type', nil), nil)
    local level_min = core.to_int(core.opt(o, 'level_min', nil), nil)
    local level_max = core.to_int(core.opt(o, 'level_max', nil), nil)
    local dry = core.switch(core.opt(o, 'dry_run', false), false)
    local limit = core.to_int(core.opt(o, 'limit', 10), 10)

    if field == nil then
        error('[字段名] 未知单位字段 ' .. tostring(o['field']) ..
              '；可用：' .. table.concat(fields(), ', '), 2)
    end
    if op ~= 'set' and op ~= 'add' then
        error("[参数] op 只能是 'set' 或 'add'，收到：" .. tostring(op), 2)
    end

    local targets = {}
    local v1_skipped = 0
    map.ForEachArmy(function(a)
        if not matches(a, rg, want_belong, want_type, level_min, level_max) then return end
        if V3_ONLY[field] and a.Version < 3 then
            v1_skipped = v1_skipped + 1
            return
        end
        targets[#targets + 1] = a
    end)

    core.head(string.format('单位批量 %s %s=%s（区域 %s，归属 %s，兵种 %s）',
        op, field, core.num(value), core.region_text(rg),
        want_belong ~= nil and tostring(want_belong) or '全部',
        want_type ~= nil and tostring(want_type) or '全部'))
    print(string.format('命中 %d 个单位%s', #targets,
        v1_skipped > 0 and string.format('（另有 %d 个 v1 单位不支持该字段，已跳过）', v1_skipped) or ''))

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

    local st = core.stat('单位批量修改 ' .. field)
    for _, a in ipairs(targets) do
        local v = value
        if op == 'add' then
            local cur = core.read(a, field)
            if type(cur) ~= 'number' then
                core.skip(st)
                v = nil
            else
                v = cur + value
            end
        end

        if v ~= nil then
            core.count(st, map.SetArmyField(a.Col, a.Row, field, v),
                string.format('(%d,%d)', a.Col, a.Row))
        end
    end

    return core.finish(st)
end

--- 把单位移到另一格（逐字段复制后删除源单位）。
--- 目标格已有单位会被替换。
function M.move(o)
    o = core.params(o)
    local fc = core.to_int(core.need(o, 'from_col'), nil)
    local fr = core.to_int(core.need(o, 'from_row'), nil)
    local tc = core.to_int(core.need(o, 'to_col'), nil)
    local tr = core.to_int(core.need(o, 'to_row'), nil)

    local src = map.GetArmy(fc, fr)
    if src == nil then
        return core.fail(string.format('(%d,%d) 没有单位', fc, fr))
    end
    if fc == tc and fr == tr then
        return core.ok('源格与目标格相同，未做任何修改')
    end

    if map.GetArmy(tc, tr) ~= nil then
        map.RemoveArmy(tc, tr)
    end

    local r = map.AddArmy(tc, tr)
    if not r.Success then
        print(string.format('[移动单位] 目标格 (%d,%d) 放置失败：%s', tc, tr, tostring(r.Message)))
        return r
    end

    local failed = 0
    for _, f in ipairs(fields()) do
        if not (V3_ONLY[f] and src.Version < 3) then
            local v = core.read(src, f)
            if type(v) == 'number' then
                local rr = map.SetArmyField(tc, tr, f, v)
                if not rr.Success then failed = failed + 1 end
            end
        end
    end

    if src.Belong ~= 255 then
        map.SetArmyLegion(tc, tr, src.Belong)
    end

    map.RemoveArmy(fc, fr)
    print(string.format('[移动单位] (%d,%d) → (%d,%d)，复制字段失败 %d 个',
        fc, fr, tc, tr, failed))
    return core.ok('已移动单位', 1)
end

-- ============================================================ 批量工具

function M.plan(o)
    o = core.params(o)
    local belong = core.to_int(core.need(o, 'belong'), nil)
    local plan = core.to_int(core.need(o, 'plan'), 0)

    local r = map.BatchModifyArmyPlanByLegion(belong, plan)
    print(string.format('[单位方案] 归属=%d 方案=%d → %s', belong, plan, tostring(r.Message)))
    return r
end

function M.cycle_plan(o)
    o = core.params(o)
    local col = core.to_int(core.need(o, 'col'), nil)
    local row = core.to_int(core.need(o, 'row'), nil)
    local r = map.CycleArmyPlan(col, row)
    print(string.format('[切换方案] (%d,%d) → %s', col, row, tostring(r.Message)))
    return r
end

--- 删除某归属的全部单位（belong = -1 表示不合法，请用 clear 清空全部）
function M.remove_belong(o)
    o = core.params(o)
    local belong = core.to_int(core.need(o, 'belong'), nil)
    local before = map.ArmyCount
    local r = map.RemoveArmyByBelong(belong)
    print(string.format('[删除归属单位] 归属=%d，删除 %d 个（剩余 %d）',
        belong, before - map.ArmyCount, map.ArmyCount))
    return r
end

function M.clear(o)
    local before = map.ArmyCount
    local r = map.RemoveAllArmies()
    print(string.format('[清空单位] 删除 %d 个（剩余 %d）', before, map.ArmyCount))
    return r
end

function M.erase(o)
    o = core.params(o)
    local col = core.to_int(core.need(o, 'col'), nil)
    local row = core.to_int(core.need(o, 'row'), nil)
    local radius = core.to_int(core.need(o, 'radius'), 1)

    local before = map.ArmyCount
    local r = map.EraseArmyBrush(col, row, radius)
    print(string.format('[橡皮擦] 中心 (%d,%d) 半径 %d，删除 %d 个',
        col, row, radius, before - map.ArmyCount))
    return r
end

--- 按概率生成单位（belong = -1 表示所有归属）
function M.generate(o)
    o = core.params(o)
    local belong = core.to_int(core.need(o, 'belong'), -1)
    local prob = core.to_int(core.need(o, 'probability'), 50)

    local before = map.ArmyCount
    local r = map.GenerateArmiesByProbability(belong, prob)
    print(string.format('[生成单位] 归属=%d 概率=%d%%，新增 %d 个（共 %d）',
        belong, prob, map.ArmyCount - before, map.ArmyCount))
    return r
end

function M.generals(o)
    o = core.params(o)
    local belong = core.to_int(core.need(o, 'belong'), -1)
    local count = core.to_int(core.need(o, 'count'), 1)

    local r = map.AutoAssignGenerals(belong, count)
    print(string.format('[分配将领] 归属=%d 数量=%d → %s', belong, count, tostring(r.Message)))
    return r
end

function M.strength(o)
    o = core.params(o)
    local belong = core.to_int(core.opt(o, 'belong', -1), -1)
    local level = core.to_int(core.opt(o, 'level', -1), -1)

    local r = map.ModifyLegionStrength(belong, level)
    print(string.format('[修改军团强度] 归属=%d 等级=%d → %s', belong, level, tostring(r.Message)))
    return r
end

function M.randomize(o)
    o = core.params(o)
    local belong = core.to_int(core.opt(o, 'belong', -1), -1)

    local before = map.ArmyCount
    local r = map.RandomizeLegionArmies(belong)
    print(string.format('[随机化单位] 归属=%d，单位数 %d → %d', belong, before, map.ArmyCount))
    return r
end

return M
