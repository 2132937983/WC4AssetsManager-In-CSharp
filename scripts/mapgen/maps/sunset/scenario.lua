-- 落日行动：修正归属 + 加登陆场 + 放起始部队
local W, H = map.Width, map.Height
local path = 'scripts/mapgen/regions/sunset.owner.txt'
local f
for _, cand in ipairs({ path, '../' .. path, '../../' .. path, '../../../' .. path, '../../../../' .. path }) do
    f = io.open(cand, 'r')
    if f then path = cand break end
end
assert(f, '找不到 sunset.owner.txt')
local rows = {}
for line in f:lines() do
    if line ~= '' and line:sub(1, 1) ~= '#' then rows[#rows + 1] = line end
end
f:close()

local ownerIndex = { A = 0, B = 2, C = 3, U = 1, K = 4 }

local function cell(lon, lat)
    local c = math.floor((lon - 122) / 28 * (W - 1) + 0.5)
    local r = math.floor((50 - lat) / 26 * (H - 1) - (c % 2 == 1 and 0.5 or 0) + 0.5)
    return c, r
end

local function setArea(lon, lat, radius, idx)
    local cc, rr = cell(lon, lat)
    for r = rr - radius, rr + radius do
        for c = cc - radius, cc + radius do
            if c >= 0 and c < W and r >= 0 and r < H and map.GetTerrain(c, r) ~= 1 then
                if (c - cc) ^ 2 + (r - rr) ^ 2 <= radius * radius then
                    map.SetBelong(c, r, idx)
                    if rows[r + 1] then
                        local line = rows[r + 1]
                        rows[r + 1] = line:sub(1, c) .. (idx == 1 and 'U' or (idx == 4 and 'K' or 'A')) .. line:sub(c + 2)
                    end
                end
            end
        end
    end
end

-- 先按掩码铺归属
for r = 0, H - 1 do
    local line = rows[r + 1] or ''
    for c = 0, W - 1 do
        local ch = line:sub(c + 1, c + 1)
        local idx = ownerIndex[ch]
        if idx ~= nil then map.SetBelong(c, r, idx) else map.SetBelong(c, r, 255) end
    end
end

-- 美军：冲绳、硫磺岛、九州南部登陆场；英军：奄美
setArea(127.70, 26.30, 4, 1)
setArea(141.32, 24.78, 2, 1)
setArea(130.55, 31.35, 3, 1)
setArea(129.55, 28.30, 4, 4)

-- 起始部队
local function nearestLand(c, r, radius)
    local function free(nc, nr)
        return nc >= 0 and nc < W and nr >= 0 and nr < H
            and map.GetTerrain(nc, nr) ~= 1
            and map.GetBuilding(nc, nr) == nil
            and map.GetArmy(nc, nr) == nil
    end
    if free(c, r) then return c, r end
    for rad = 1, radius do
        for dr = -rad, rad do
            for dc = -rad, rad do
                local nc, nr = c + dc, r + dr
                if free(nc, nr) then return nc, nr end
            end
        end
    end
    return nil
end

local function army(c, r, legion, unitType, level, org)
    local nc, nr = nearestLand(c, r, 12)
    if not nc then return false end
    local res = map.AddArmy(nc, nr)
    if not res or not res.Success then return false end
    map.SetArmyLegion(nc, nr, legion)
    map.SetArmyField(nc, nr, 'UnitType', unitType)
    map.SetArmyField(nc, nr, 'Level', level)
    map.SetArmyField(nc, nr, 'Organization', org)
    map.SetArmyField(nc, nr, 'Experience', 60)
    return true
end

local units = {
    -- 美军
    { 127.70, 26.30, 1, 1, 4, 90 },
    { 130.55, 31.35, 1, 1, 4, 90 },
    { 141.32, 24.78, 1, 0, 3, 80 },
    -- 日本本土
    { 126.0, 99.0, 0, 0, 4, 100 },
    { 96.0, 105.0, 0, 0, 3, 90 },
    { 74.0, 107.0, 0, 0, 3, 90 },
    { 137.0, 47.0, 0, 0, 3, 80 },
    { 36.0, 86.0, 0, 0, 3, 80 },
    -- 苏联
    { 69.0, 46.0, 2, 0, 4, 100 },
    { 93.0, 10.0, 2, 0, 3, 90 },
    -- 中国
    { 10.0, 56.0, 3, 0, 3, 90 },
    { 33.0, 29.0, 3, 0, 3, 80 },
    -- 英军
    { 129.55, 28.30, 4, 0, 3, 80 },
}
for _, u in ipairs(units) do
    local c, r = cell(u[1], u[2])
    army(c, r, u[3], u[4], u[5], u[6])
end

local counts = {}
for r = 0, H - 1 do
    for c = 0, W - 1 do
        local b = map.GetBelong(c, r)
        if b ~= 255 then counts[b] = (counts[b] or 0) + 1 end
    end
end
for i = 0, map.LegionCount - 1 do
    print(string.format('legion=%d country=%d cells=%d', i, map.GetLegionCountryIdAt(i), counts[i] or 0))
end
print('armies=' .. map.ArmyCount .. ' buildings=' .. map.BuildingCount)
