--[[============================================================================
  dump-legions.lua —— 读取【当前地图】的军团列表（索引 ↔ 国家ID ↔ 国家名）
  ----------------------------------------------------------------------------
  这个脚本里**没有任何写死的势力名单、也没有内置国家表**：
    · 国家名   → 全部从 stringtable 现读（自动挑 country_ 条目最多的那一份）
    · 探测范围 → stringtable 里出现过的国家ID（自动取 min..max），不写死数字
    · 输出路径 → 相对路径，按顺序试，不写死某台机器的绝对路径
  换一张地图、换一份 stringtable、加删军团，直接再跑一次就行，不用改脚本。

  怎么"读"军团（当前 exe 没有 GetLegionCountryIdAt，所以拿只写接口当探针）：
    1) map.SetLegionActionId(cid, cid)
       内部走 FindLegionIndex(cid) —— 按 CountryId 查。查不到就返回失败且**什么都不改**，
       所以可以安全回答「国家ID=cid 的军团存在吗」。存在时它把 ActionId 设成 cid，
       而军团本来就要求 ActionId = 国家ID，所以这一步是幂等的、无副作用。
    2) map.SetLegionCountryIdAt(i, cid)
       唯一性校验是「除自己以外别人不能占用 cid」，所以**只有 i 真的持有 cid 时才成功**
       （成功后写回同值 = 没改）⇒ 可以安全定位「cid 在第几号索引」。

  如果哪天 exe 编进了 map.GetLegionCountryIdAt，脚本会自动改走直读，更快。

  用法：控制台 F3 里执行   lua Scripts/dump-legions.lua
        结果同时打到控制台，并尽量写一份 legion-map.txt
============================================================================]]

-- ================== 配置：只有候选路径，不含任何势力/国家名单 ==================

-- stringtable 候选路径（相对程序目录 + 上级目录，按顺序试探，取 country_ 条目最多的那份）
local INI_CANDIDATES = {
    [[Resource\WC4DATA\assets\stringtable_tw.ini]],
    [[Resource\WC4DATA\assets\stringtable.ini]],
    [[Resource/WC4DATA/assets/stringtable_tw.ini]],
    [[Resource/WC4DATA/assets/stringtable.ini]],
    [[../Resource/WC4DATA/assets/stringtable_tw.ini]],
    [[../Resource/WC4DATA/assets/stringtable.ini]],
    [[assets/stringtable_tw.ini]],
}

-- legion-map.txt 写到哪（按顺序试，第一个能写的就用）
local OUT_CANDIDATES = {
    [[Scripts/legion-map.txt]],
    [[scripts/legion-map.txt]],
    [[legion-map.txt]],
}

-- 探测上限 = stringtable 里最大的国家ID + 这个余量
-- （留余量是为了兜住"个别军团的 CountryId 不在 stringtable 里"的情况）
local ID_MARGIN = 16

-- =============================== 工具 ===============================

local function trim(s) return (string.gsub(s, '^%s*(.-)%s*$', '%1')) end

local function readFile(path)
    if not io then return nil end
    local f = io.open(path, 'r')
    if not f then return nil end
    local ok, data = pcall(function() return f:read('*a') end)
    f:close()
    if not ok then return nil end
    return data
end

local out = {}
local function say(s) out[#out + 1] = s; print(s) end

-- =============================== 主流程 ===============================

say('╔══════════════════════════════════════════════════════════╗')
say('║  读取当前地图的军团列表（索引 ↔ 国家ID ↔ 国家名）        ║')
say('╚══════════════════════════════════════════════════════════╝')

local n = map.LegionCount
say(string.format('军团总数（map.LegionCount）：%d', n))
say('')

-- ---- 1. 读 stringtable：取 country_ 条目最多的那一份 ----
local id2name = {}
local bestPath, bestCount, maxId = nil, 0, -1

for _, path in ipairs(INI_CANDIDATES) do
    local text = readFile(path)
    if text then
        local t, cnt, mx = {}, 0, -1
        for id, nm in string.gmatch(text, 'country_(%d+)=([^\r\n]*)') do
            nm = trim(nm)
            local num = tonumber(id)
            if num and nm ~= '' then
                cnt = cnt + 1
                t[num] = nm
                if num > mx then mx = num end
            end
        end
        say(string.format('  试探 %s → %d 条 country_', path, cnt))
        if cnt > bestCount then
            bestCount, bestPath, maxId = cnt, path, mx
            id2name = t
        end
    end
end

if not bestPath then
    say('')
    say('⚠ 没找到 stringtable（国家名会显示不出来，但索引和数字仍然正确）')
    say('  要显示国家名，把 INI_CANDIDATES 里的路径改成实际的 stringtable 位置。')
else
    say(string.format('国家表：%s（%d 条，最大国家ID=%d）', bestPath, bestCount, maxId))
end
say('')

-- 探测上限：完全由 stringtable 推导
local maxProbe = (maxId >= 0) and (maxId + ID_MARGIN) or 255
say(string.format('探测范围：国家ID 0 ~ %d（= stringtable 最大ID + %d）', maxProbe, ID_MARGIN))

-- ---- 2. 有没有读接口？有就直读 ----
local hasRead = false
do
    local ok, f = pcall(function() return map.GetLegionCountryIdAt end)
    hasRead = (ok and f ~= nil)
end

local rows = {}   -- { index = , cid = , name = }

if hasRead then
    say('✓ 引擎带 GetLegionCountryIdAt，直接读取（不需要探针）')
    for i = 0, n - 1 do
        local cid = map.GetLegionCountryIdAt(i)
        if cid and cid >= 0 then
            rows[#rows + 1] = { index = i, cid = cid, name = id2name[cid] or ('国家ID ' .. cid) }
        end
    end
else
    say('ℹ 引擎没有 GetLegionCountryIdAt，改用「只写接口」做安全探针（不改坏数据）')
    say('')

    --- 该国家ID的军团存在吗？（不存在 ⇒ 失败且无副作用）
    local function exists(cid)
        local ok, r = pcall(function() return map.SetLegionActionId(cid, cid) end)
        return ok and r and r.Success
    end

    --- cid 在第几号索引？（只会在真的是它时成功；成功 = 写回同值）
    local function findIndex(cid)
        for i = 0, n - 1 do
            local ok, r = pcall(function() return map.SetLegionCountryIdAt(i, cid) end)
            if ok and r and r.Success then return i end
        end
        return -1
    end

    local probed, found = 0, 0
    for cid = 0, maxProbe do
        probed = probed + 1
        if exists(cid) then
            local idx = findIndex(cid)
            if idx >= 0 then
                found = found + 1
                rows[#rows + 1] = { index = idx, cid = cid, name = id2name[cid] or ('国家ID ' .. cid) }
            else
                say(string.format('  ⚠ 国家ID=%-4d 存在但定位不到索引', cid))
            end
        end
    end
    say(string.format('探测了 %d 个国家ID，命中 %d 个军团', probed, found))
end

table.sort(rows, function(a, b) return a.index < b.index end)

-- ---- 3. 输出 ----
say('')
say('══════════════ 军团列表（belong 要填的就是【索引】）══════════════')
local buf = {}
buf[#buf + 1] = '# 当前地图军团列表   军团总数=' .. n
buf[#buf + 1] = '# 索引  国家ID  国家名'
for _, r in ipairs(rows) do
    local line = string.format('%5d  %6d  %s', r.index, r.cid, r.name)
    buf[#buf + 1] = line
    say(line)
end
say('──────────────────────────────────────────────────────────')
say(string.format('共列出 %d 个军团', #rows))

if #rows < n then
    local miss = n - #rows
    say(string.format('注：另有 %d 个军团的国家ID 不在 0~%d 范围内，没列出来（把 ID_MARGIN 调大再跑）。',
        miss, maxProbe))
end

-- ---- 4. 存文件 ----
local saved = nil
for _, path in ipairs(OUT_CANDIDATES) do
    local f = io and io.open(path, 'w')
    if f then
        f:write(table.concat(buf, '\r\n'))
        f:write('\r\n')
        f:close()
        saved = path
        break
    end
end
say('')
say(saved and ('已写出：' .. saved) or '⚠ 写文件失败，请直接复制上面的表。')
say('★ 完成。')
