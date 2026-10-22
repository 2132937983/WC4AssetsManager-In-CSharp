-- 北美洲：按真实州/省边界写 ProvinceValue
-- 省区值 = 该州/省会格子的线性索引；所有格子（含城市格）都写同一值
local W, H = map.Width, map.Height

local function open_first(rel)
    for _, p in ipairs({ rel, '../' .. rel, '../../' .. rel, '../../../' .. rel, '../../../../' .. rel }) do
        local f = io.open(p, 'r')
        if f then return f end
    end
    return nil
end

local f = assert(open_first('scripts/mapgen/regions/north_america.capitals.txt'), '找不到 capitals.txt')
local capitals = {}
for line in f:lines() do
    local pid, c, r, name = line:match('^(%d+)%s+(%d+)%s+(%d+)%s*(.*)$')
    if pid then
        pid, c, r = tonumber(pid), tonumber(c), tonumber(r)
        capitals[pid] = { c = c, r = r, idx = r * W + c, name = name }
    end
end
f:close()

f = assert(open_first('scripts/mapgen/regions/north_america.provinces.txt'), '找不到 provinces.txt')
local rows = {}
for line in f:lines() do
    if line ~= '' and line:sub(1, 1) ~= '#' then rows[#rows + 1] = line end
end
f:close()

local set, missing = 0, 0
for r = 0, H - 1 do
    local nums = {}
    for token in (rows[r + 1] or ''):gmatch('%d+') do nums[#nums + 1] = tonumber(token) end
    for c = 0, W - 1 do
        local pid = nums[c + 1] or 0
        local cap = capitals[pid]
        if cap then
            map.SetProvince(c, r, cap.idx)
            -- 真实州界和国界在栅格化后可能差 1 格：以州/省会归属为准，
            -- 保证一个省区不会跨美国/加拿大/墨西哥。
            local capOwner = map.GetBelong(cap.c, cap.r)
            if capOwner ~= 255 and map.GetBelong(c, r) ~= capOwner then
                map.SetBelong(c, r, capOwner)
            end
            set = set + 1
        else
            map.SetProvince(c, r, 65535)
            if pid ~= 0 then missing = missing + 1 end
        end
    end
end

print(string.format('省区写入=%d 缺失=%d 省会=%d', set, missing, (function()
    local n = 0
    for _ in pairs(capitals) do n = n + 1 end
    return n
end)()))
