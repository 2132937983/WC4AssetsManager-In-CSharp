-- 保留真实州/省边界，同时让每个城市成为独立省区：
-- 州/省边界是外层约束；州内每个城市各自作为省会，在州内做多源 BFS。
-- 结果：州界不被打断，城市又都有自己的 ProvinceValue。
local W, H = map.Width, map.Height

local function open_first(rel)
    for _, p in ipairs({ rel, '../' .. rel, '../../' .. rel, '../../../' .. rel, '../../../../' .. rel }) do
        local f = io.open(p, 'r')
        if f then return f end
    end
    return nil
end

-- 州/省 ID 网格
local stateGrid = {}
local f = assert(open_first('scripts/mapgen/regions/north_america.provinces.txt'), '找不到 provinces.txt')
for line in f:lines() do
    if line ~= '' and line:sub(1, 1) ~= '#' then
        local nums = {}
        for token in line:gmatch('%d+') do nums[#nums + 1] = tonumber(token) end
        stateGrid[#stateGrid + 1] = nums
    end
end
f:close()

-- 没有城市的州，用原来的州省会格兜底
local fallbackCap = {}
f = open_first('scripts/mapgen/regions/north_america.capitals.txt')
if f then
    for line in f:lines() do
        local pid, c, r = line:match('^(%d+)%s+(%d+)%s+(%d+)')
        if pid then
            pid, c, r = tonumber(pid), tonumber(c), tonumber(r)
            fallbackCap[pid] = { c = c, r = r, idx = r * W + c, name = '' }
        end
    end
    f:close()
end

-- 清空省区
for r = 0, H - 1 do
    for c = 0, W - 1 do
        map.SetProvince(c, r, 65535)
    end
end

-- 城市按州分组
local stateCities = {}
map.ForEachBuilding(function(b)
    if b.BuildingType >= 11 and b.BuildingType <= 15 then
        local o = map.GetBelong(b.Col, b.Row)
        if o >= 0 and o < map.LegionCount and map.GetTerrain(b.Col, b.Row) ~= 1 then
            local sid = (stateGrid[b.Row + 1] or {})[b.Col + 1] or 0
            if sid > 0 then
                stateCities[sid] = stateCities[sid] or {}
                stateCities[sid][#stateCities[sid] + 1] = {
                    c = b.Col, r = b.Row, o = o, idx = b.Row * W + b.Col, name = b.Name or '',
                }
            end
        end
    end
end)

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

local assigned, cells = {}, 0
local cityCapitals, stateCount, fallbackStates = 0, 0, 0

for sid, cities in pairs(stateCities) do
    stateCount = stateCount + 1
    local capitals = cities
    cityCapitals = cityCapitals + #cities

    local q, head = {}, 1
    for _, cap in ipairs(capitals) do
        if not assigned[cap.idx] then
            assigned[cap.idx] = true
            cells = cells + 1
            map.SetProvince(cap.c, cap.r, cap.idx)
        end
        q[#q + 1] = cap
    end

    while head <= #q do
        local p = q[head]
        head = head + 1
        for _, n in ipairs(neighbors(p.c, p.r)) do
            local nc, nr = n[1], n[2]
            if nc >= 0 and nc < W and nr >= 0 and nr < H
                and map.GetTerrain(nc, nr) ~= 1
                and (stateGrid[nr + 1] or {})[nc + 1] == sid then
                local k = nr * W + nc
                if not assigned[k] then
                    assigned[k] = true
                    cells = cells + 1
                    map.SetProvince(nc, nr, p.idx)
                    q[#q + 1] = { c = nc, r = nr, idx = p.idx }
                end
            end
        end
    end
end

-- 没有城市的州：保留一个省区，用原州省会格
for sid, cap in pairs(fallbackCap) do
    if not stateCities[sid] then
        if map.GetTerrain(cap.c, cap.r) == 1 then
            -- 原省会落在海里时，找同州陆地
            for r = 0, H - 1 do
                local found = false
                for c = 0, W - 1 do
                    if (stateGrid[r + 1] or {})[c + 1] == sid and map.GetTerrain(c, r) ~= 1 then
                        cap.c, cap.r, cap.idx = c, r, r * W + c
                        found = true
                        break
                    end
                end
                if found then break end
            end
        end
        if map.GetTerrain(cap.c, cap.r) ~= 1 then
            fallbackStates = fallbackStates + 1
            stateCount = stateCount + 1
            local q, head = { cap }, 1
            assigned[cap.idx] = true
            cells = cells + 1
            map.SetProvince(cap.c, cap.r, cap.idx)
            while head <= #q do
                local p = q[head]
                head = head + 1
                for _, n in ipairs(neighbors(p.c, p.r)) do
                    local nc, nr = n[1], n[2]
                    if nc >= 0 and nc < W and nr >= 0 and nr < H
                        and map.GetTerrain(nc, nr) ~= 1
                        and (stateGrid[nr + 1] or {})[nc + 1] == sid then
                        local k = nr * W + nc
                        if not assigned[k] then
                            assigned[k] = true
                            cells = cells + 1
                            map.SetProvince(nc, nr, p.idx)
                            q[#q + 1] = { c = nc, r = nr, idx = p.idx }
                        end
                    end
                end
            end
        end
    end
end

-- 州界栅格化后的零散海岸格：先由相邻同势力省区补齐，再按最近同势力省区兜底
for _ = 1, 4 do
    local changed = false
    for r = 0, H - 1 do
        for c = 0, W - 1 do
            if map.GetTerrain(c, r) ~= 1 and map.GetProvince(c, r) == 65535 then
                local o = map.GetBelong(c, r)
                for _, n in ipairs(neighbors(c, r)) do
                    local nc, nr = n[1], n[2]
                    if nc >= 0 and nc < W and nr >= 0 and nr < H
                        and map.GetBelong(nc, nr) == o then
                        local v = map.GetProvince(nc, nr)
                        if v ~= 65535 then
                            map.SetProvince(c, r, v)
                            cells = cells + 1
                            changed = true
                            break
                        end
                    end
                end
            end
        end
    end
    if not changed then break end
end

for r = 0, H - 1 do
    for c = 0, W - 1 do
        if map.GetTerrain(c, r) ~= 1 and map.GetProvince(c, r) == 65535 then
            local o = map.GetBelong(c, r)
            local best, bestD = nil, 1e18
            for rr = 0, H - 1 do
                for cc = 0, W - 1 do
                    local v = map.GetProvince(cc, rr)
                    if v ~= 65535 and map.GetBelong(cc, rr) == o then
                        local d = (cc - c) ^ 2 + (rr - r) ^ 2
                        if d < bestD then bestD, best = d, v end
                    end
                end
            end
            if best then
                map.SetProvince(c, r, best)
                cells = cells + 1
            end
        end
    end
end

print(string.format('州省=%d 城市省会=%d 无城市州=%d 省区格=%d',
    stateCount, cityCapitals, fallbackStates, cells))
