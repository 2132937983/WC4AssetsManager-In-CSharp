-- 落日行动：补齐归属 → 画省区 → 重建起始单位
local W, H = map.Width, map.Height

local function cell(lon, lat)
    local c = math.floor((lon - 122) / 28 * (W - 1) + 0.5)
    local r = math.floor((50 - lat) / 26 * (H - 1) - (c % 2 == 1 and 0.5 or 0) + 0.5)
    return c, r
end

-- 1) 补齐极少数无归属陆地
local fixed = 0
for r = 0, H - 1 do
    for c = 0, W - 1 do
        if map.GetTerrain(c, r) ~= 1 and map.GetBelong(c, r) == 255 then
            for rad = 1, 20 do
                local done = false
                for dr = -rad, rad do
                    for dc = -rad, rad do
                        local nc, nr = c + dc, r + dr
                        if nc >= 0 and nc < W and nr >= 0 and nr < H
                            and map.GetTerrain(nc, nr) ~= 1 and map.GetBelong(nc, nr) ~= 255 then
                            map.SetBelong(c, r, map.GetBelong(nc, nr))
                            fixed = fixed + 1
                            done = true
                            break
                        end
                    end
                    if done then break end
                end
                if done then break end
            end
        end
    end
end

-- 2) 清空省区
for r = 0, H - 1 do
    for c = 0, W - 1 do
        map.SetProvince(c, r, 65535)
    end
end

-- 3) 生成省区：五级城 + 部分四级城作为省会
local TARGET = 80
local lv5, lv4 = {}, {}
map.ForEachBuilding(function(b)
    if b.BuildingType >= 14 then
        local o = map.GetBelong(b.Col, b.Row)
        if o >= 0 and o < map.LegionCount and map.GetTerrain(b.Col, b.Row) ~= 1 then
            local item = { c = b.Col, r = b.Row, o = o, idx = b.Row * W + b.Col }
            if b.BuildingType >= 15 then lv5[#lv5 + 1] = item else lv4[#lv4 + 1] = item end
        end
    end
end)

local capitals, hasOwner = {}, {}
for _, x in ipairs(lv5) do
    capitals[#capitals + 1] = x
    hasOwner[x.o] = true
end
local need = TARGET - #capitals
if need > 0 and #lv4 > 0 then
    local step = math.max(1, math.floor(#lv4 / need))
    for i = 1, #lv4, step do
        if #capitals >= TARGET then break end
        capitals[#capitals + 1] = lv4[i]
        hasOwner[lv4[i].o] = true
    end
end
for _, x in ipairs(lv4) do
    if not hasOwner[x.o] then
        capitals[#capitals + 1] = x
        hasOwner[x.o] = true
    end
end

local byOwner = {}
for _, cap in ipairs(capitals) do
    byOwner[cap.o] = byOwner[cap.o] or {}
    table.insert(byOwner[cap.o], cap)
end

local function neighbors(c, r)
    local list = { { c - 1, r }, { c + 1, r }, { c, r - 1 }, { c, r + 1 } }
    if c % 2 == 1 then
        list[#list + 1] = { c - 1, r + 1 }
        list[#list + 1] = { c + 1, r + 1 }
    else
        list[#list + 1] = { c - 1, r - 1 }
        list[#list + 1] = { c + 1, r - 1 }
    end
    return list
end

local assigned, assignedCount = {}, 0
for o, list in pairs(byOwner) do
    local q, head = {}, 1
    for _, cap in ipairs(list) do
        if not assigned[cap.idx] then
            assigned[cap.idx] = true
            assignedCount = assignedCount + 1
            map.SetProvince(cap.c, cap.r, cap.idx)
        end
        q[#q + 1] = { c = cap.c, r = cap.r, idx = cap.idx }
    end
    while head <= #q do
        local p = q[head]
        head = head + 1
        for _, n in ipairs(neighbors(p.c, p.r)) do
            local nc, nr = n[1], n[2]
            if nc >= 0 and nc < W and nr >= 0 and nr < H
                and map.GetTerrain(nc, nr) ~= 1 and map.GetBelong(nc, nr) == o then
                local k = nr * W + nc
                if not assigned[k] then
                    assigned[k] = true
                    assignedCount = assignedCount + 1
                    map.SetProvince(nc, nr, p.idx)
                    q[#q + 1] = { c = nc, r = nr, idx = p.idx }
                end
            end
        end
    end
end

-- 兜底分配剩余陆地
for r = 0, H - 1 do
    for c = 0, W - 1 do
        local k = r * W + c
        if map.GetTerrain(c, r) ~= 1 and not assigned[k] then
            local o = map.GetBelong(c, r)
            local best, bestD = nil, 1e18
            for _, cap in ipairs(byOwner[o] or {}) do
                local d = (cap.c - c) ^ 2 + (cap.r - r) ^ 2
                if d < bestD then bestD, best = d, cap end
            end
            if best then
                assigned[k] = true
                assignedCount = assignedCount + 1
                map.SetProvince(c, r, best.idx)
            end
        end
    end
end

-- 4) 重建起始单位
local old = {}
map.ForEachArmy(function(a) old[#old + 1] = { a.Col, a.Row } end)
for _, p in ipairs(old) do map.RemoveArmy(p[1], p[2]) end

local function free(nc, nr)
    return nc >= 0 and nc < W and nr >= 0 and nr < H
        and map.GetTerrain(nc, nr) ~= 1
        and map.GetBuilding(nc, nr) == nil
        and map.GetArmy(nc, nr) == nil
end

local function army(lon, lat, legion, unitType, level, org)
    local c, r = cell(lon, lat)
    local nc, nr
    if free(c, r) then nc, nr = c, r
    else
        for rad = 1, 12 do
            local hit = false
            for dr = -rad, rad do
                for dc = -rad, rad do
                    local tc, tr = c + dc, r + dr
                    if free(tc, tr) then nc, nr, hit = tc, tr, true break end
                end
                if hit then break end
            end
            if hit then break end
        end
    end
    if not nc then return false end

    local res = map.AddArmy(nc, nr)
    if not res or not res.Success then return false end
    map.SetArmyLegion(nc, nr, legion)
    map.SetArmyField(nc, nr, 'UnitType', unitType)
    map.SetArmyField(nc, nr, 'Level', level)
    map.SetArmyField(nc, nr, 'Organization', org)
    map.SetArmyField(nc, nr, 'Experience', 60)
    map.SetArmyField(nc, nr, 'Morale', 80)
    return true
end

local UNIT = {
    -- 美军：冲绳 / 九州登陆场 / 硫磺岛
    { 127.70, 26.30, 1, 1, 4, 95 },
    { 130.55, 31.35, 1, 1, 4, 95 },
    { 141.32, 24.78, 1, 0, 3, 85 },
    -- 日本：本土与朝鲜防线
    { 139.69, 35.68, 0, 0, 4, 100 },
    { 135.50, 34.69, 0, 0, 3, 90 },
    { 132.46, 34.39, 0, 0, 3, 90 },
    { 130.40, 33.59, 0, 0, 3, 90 },
    { 141.35, 43.06, 0, 0, 3, 85 },
    { 126.98, 37.57, 0, 0, 3, 85 },
    { 125.75, 39.03, 0, 0, 3, 85 },
    { 129.08, 35.18, 0, 0, 3, 85 },
    -- 苏联：满洲 / 滨海 / 朝鲜北部
    { 131.89, 43.12, 2, 1, 4, 100 },
    { 135.07, 48.48, 2, 0, 3, 90 },
    { 126.53, 45.80, 2, 0, 3, 90 },
    { 125.32, 43.82, 2, 0, 3, 85 },
    { 127.44, 39.15, 2, 0, 3, 85 },
    -- 中国：华北 / 满洲南部
    { 123.43, 41.80, 3, 0, 3, 90 },
    { 124.38, 40.12, 3, 0, 3, 85 },
    { 126.53, 45.80, 3, 0, 3, 85 },
    { 126.55, 43.85, 3, 0, 3, 80 },
    { 129.51, 42.91, 3, 0, 3, 80 },
    -- 英军：奄美
    { 129.55, 28.30, 4, 0, 3, 80 },
}
local placed = 0
for _, u in ipairs(UNIT) do
    if army(u[1], u[2], u[3], u[4], u[5], u[6]) then placed = placed + 1 end
end

local counts = {}
for r = 0, H - 1 do
    for c = 0, W - 1 do
        local v = map.GetProvince(c, r)
        if v ~= 65535 then counts[v] = true end
    end
end
local prov = 0
for _ in pairs(counts) do prov = prov + 1 end

print(string.format('归属补齐=%d 省区=%d 省区格=%d 单位=%d/%d',
    fixed, prov, assignedCount, placed, #UNIT))
