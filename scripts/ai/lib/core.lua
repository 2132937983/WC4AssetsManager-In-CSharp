--[[============================================================================
  ai/lib/core.lua —— AI 地图编辑工具箱：公共部件
  ----------------------------------------------------------------------------
  只放"所有模块都要用"的小工具：参数校验、区域过滤、字段名归一化、结果统计、
  打印格式。不含任何地图业务逻辑。

  一般不需要直接 require 它：require('ai') 已经把各模块挂好。
  自己写扩展模块时才用得到：
      local core = require('ai.lib.core')
============================================================================]]

local M = {}

M.VERSION = '1.0'

-- ============================================================ 输出

function M.p(...)
    print(...)
end

function M.head(title)
    print('')
    print('== ' .. tostring(title) .. ' ==')
end

function M.warn(msg)
    print('[警告] ' .. tostring(msg))
end

-- ============================================================ 数字 / 文本

--- 数字转文本：整数不带小数点，小数保留 3 位（避免 1.0000000001 之类）
function M.num(v)
    if v == nil then return 'nil' end
    if type(v) ~= 'number' then return tostring(v) end
    if v == math.floor(v) then return string.format('%d', v) end
    return string.format('%.3f', v)
end

--- 强制转数字（字符串数字也接受），失败直接报错
function M.as_number(v, name)
    if type(v) == 'number' then return v end
    if type(v) == 'string' then
        local n = tonumber(v)
        if n ~= nil then return n end
    end
    error('[参数类型] ' .. tostring(name or 'value') .. ' 需为数字，收到：' .. tostring(v), 3)
end

function M.to_int(v, default)
    if v == nil then return default end
    if type(v) == 'number' then
        if v >= 0 then return math.floor(v + 0.5) end
        return math.ceil(v - 0.5)
    end
    if type(v) == 'string' then
        local n = tonumber(v)
        if n ~= nil then return M.to_int(n, default) end
    end
    return default
end

--- 布尔参数：接受 true/false 或 'true'/'1'/'yes' 之类写法
function M.switch(v, default)
    if v == nil then return default end
    if type(v) == 'boolean' then return v end
    local s = string.lower(tostring(v))
    if s == 'true' or s == '1' or s == 'yes' or s == 'y' then return true end
    if s == 'false' or s == '0' or s == 'no' or s == 'n' then return false end
    return default
end

-- ============================================================ 参数

function M.opt(o, name, default)
    if o == nil then return default end
    local v = o[name]
    if v == nil then return default end
    return v
end

function M.need(o, name)
    local v = M.opt(o, name, nil)
    if v == nil then
        error('[参数缺失] 必须提供 ' .. tostring(name), 2)
    end
    return v
end

--- 取参数表格并做"必须是 table"的校验（防止写成 ai.army.set('10,20')）
function M.params(o)
    if o == nil then return {} end
    if type(o) ~= 'table' then
        error('[参数类型] 需要一个参数表，例如 { col = 10, row = 20 }', 2)
    end
    return o
end

-- ============================================================ 区域

--- 解析区域参数，返回 { c1, r1, c2, r2 } 或 nil：
---   { col1=, row1=, col2=, row2= }  矩形（两个角可反着写）
---   { col=, row= }                  单格
function M.region(o)
    local c1 = M.opt(o, 'col1', nil)
    local r1 = M.opt(o, 'row1', nil)
    local c2 = M.opt(o, 'col2', nil)
    local r2 = M.opt(o, 'row2', nil)

    if c1 ~= nil and r1 ~= nil and c2 ~= nil and r2 ~= nil then
        c1, r1, c2, r2 = M.to_int(c1, 0), M.to_int(r1, 0), M.to_int(c2, 0), M.to_int(r2, 0)
        if c1 > c2 then c1, c2 = c2, c1 end
        if r1 > r2 then r1, r2 = r2, r1 end
        return { c1 = c1, r1 = r1, c2 = c2, r2 = r2 }
    end

    local col = M.opt(o, 'col', nil)
    local row = M.opt(o, 'row', nil)
    if col ~= nil and row ~= nil then
        col, row = M.to_int(col, 0), M.to_int(row, 0)
        return { c1 = col, r1 = row, c2 = col, r2 = row }
    end

    return nil
end

function M.in_region(rg, col, row)
    if rg == nil then return true end
    return col >= rg.c1 and col <= rg.c2 and row >= rg.r1 and row <= rg.r2
end

function M.region_text(rg)
    if rg == nil then return '全图' end
    if rg.c1 == rg.c2 and rg.r1 == rg.r2 then
        return string.format('(%d,%d)', rg.c1, rg.r1)
    end
    return string.format('(%d,%d)-(%d,%d)', rg.c1, rg.r1, rg.c2, rg.r2)
end

function M.valid(col, row)
    if col == nil or row == nil then return false end
    return col >= 0 and row >= 0 and col < map.Width and row < map.Height
end

-- ============================================================ 字段名

--- 把 MoonSharp 的 .NET 列表（IReadOnlyList<string>）转成真正的 Lua 数组
function M.list_of(fields)
    local out = {}
    if fields == nil then return out end
    for _, f in ipairs(fields) do
        out[#out + 1] = f
    end
    return out
end

--- 按名匹配字段（大小写不敏感），返回数据里的规范写法；找不到返回 nil
function M.canon(fields, name)
    if name == nil then return nil end
    local want = string.lower(tostring(name))
    for _, f in ipairs(M.list_of(fields)) do
        if string.lower(f) == want then return f end
    end
    return nil
end

function M.dump_fields(title, fields)
    M.head(title)
    print(table.concat(M.list_of(fields), ', '))
end

--- 读快照对象上的某个字段（只读；失败返回 nil）
function M.read(obj, field)
    if obj == nil or field == nil then return nil end
    local ok, v = pcall(function() return obj[field] end)
    if ok then return v end
    return nil
end

-- ============================================================ 结果统计

function M.stat(tag)
    return { tag = tag, ok = 0, fail = 0, skip = 0, first = nil }
end

function M.count(st, r, label)
    if r == nil then
        st.fail = st.fail + 1
        if st.first == nil then
            st.first = tostring(label or '操作') .. ' 返回空'
        end
        return false
    end

    if r.Success then
        st.ok = st.ok + 1
        return true
    end

    st.fail = st.fail + 1
    if st.first == nil then
        local msg = tostring(r.Message or '')
        st.first = (label and (tostring(label) .. ' → ') or '') .. msg
    end
    return false
end

function M.skip(st, n)
    st.skip = st.skip + (n or 1)
end

function M.finish(st)
    local line = string.format('%s：成功 %d，失败 %d', st.tag, st.ok, st.fail)
    if st.skip > 0 then
        line = line .. string.format('，跳过 %d', st.skip)
    end
    if st.first ~= nil then
        line = line .. '\n首个失败原因：' .. tostring(st.first)
    end
    print(line)
    return st
end

--- 构造一个和 ScriptOperationResult 形状一致的表，便于调用方统一判断
function M.fail(msg)
    print('[失败] ' .. tostring(msg))
    return { Success = false, Message = tostring(msg), AffectedCount = 0 }
end

function M.ok(msg, affected)
    return { Success = true, Message = tostring(msg or ''), AffectedCount = affected or 0 }
end

-- ============================================================ 统计辅助

function M.keys_sorted(t)
    local keys = {}
    for k in pairs(t) do keys[#keys + 1] = k end
    table.sort(keys, function(a, b) return tostring(a) < tostring(b) end)
    return keys
end

--- 按值从大到小取前 n 个键（值相同按名字排序）
function M.top(t, n)
    local keys = M.keys_sorted(t)
    table.sort(keys, function(a, b)
        local va, vb = t[a] or 0, t[b] or 0
        if va == vb then return tostring(a) < tostring(b) end
        return va > vb
    end)

    local out = {}
    for i = 1, math.min(n or #keys, #keys) do
        out[i] = keys[i]
    end
    return out
end

function M.pct(part, total)
    if total == nil or total <= 0 then return 0 end
    return part * 100.0 / total
end

--- 当前地图的单位格式版本：1 = v1，3 = v3，0 = 没有单位可判断
function M.army_version()
    local v = 0
    map.ForEachArmy(function(a)
        v = a.Version
        return false
    end)
    return v
end

-- ============================================================ 逐格遍历

--- 逐格遍历（引擎没有 map.ForEachCell，这是它的等价物）。
--- 参数：
---   fn = function(col, row) ... end   必需；返回 false 可提前结束
---   col1/row1/col2/row2（区域，可选；省略 = 全图）
---   step（步长，默认 1；大图做统计时可调大，2 表示隔一格采一个）
--- 返回实际访问的格子数。
---
--- 例：
---   ai.each_cell{ fn = function(col, row)
---       if map.GetTerrain(col, row) == 1 then map.SetTerrain(col, row, 2) end
---   end }
function M.each_cell(o)
    o = M.params(o)

    local fn = o.fn
    if type(fn) ~= 'function' then
        error('[参数缺失] each_cell 需要 fn = function(col, row) ... end', 2)
    end

    local rg = M.region(o)
    local c1, r1, c2, r2
    if rg ~= nil then
        c1, r1, c2, r2 = rg.c1, rg.r1, rg.c2, rg.r2
    else
        c1, r1, c2, r2 = 0, 0, map.Width - 1, map.Height - 1
    end

    local step = M.to_int(M.opt(o, 'step', 1), 1)
    if step < 1 then step = 1 end

    if c1 < 0 then c1 = 0 end
    if r1 < 0 then r1 = 0 end
    if c2 > map.Width - 1 then c2 = map.Width - 1 end
    if r2 > map.Height - 1 then r2 = map.Height - 1 end

    local visited = 0
    for row = r1, r2, step do
        for col = c1, c2, step do
            visited = visited + 1
            if fn(col, row) == false then
                return visited
            end
        end
    end
    return visited
end

return M
