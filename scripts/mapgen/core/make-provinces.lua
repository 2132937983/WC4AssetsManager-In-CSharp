-- 在当前地图上按城市生成省区：
-- 1) 选五级城 + 部分四级城作为省会（目标约 160 个）
-- 2) 每个势力内做多源 BFS，把陆地格分给最近的省会
-- 3) 省区不跨势力边界；省区值 = 省会格子的线性索引
-- 运行前建议先执行控制台命令 `province clear`

local W, H = map.Width, map.Height
local function key(c, r) return r * W + c end
local function inMap(c, r) return c >= 0 and c < W and r >= 0 and r < H end
local function isLand(c, r) return map.GetTerrain(c, r) ~= 1 end
local function ownerOf(c, r) return map.GetBelong(c, r) end

local lv5, lv4 = {}, {}
map.ForEachBuilding(function(b)
    if b.BuildingType >= 14 then
        local o = map.GetBelong(b.Col, b.Row)
        if o >= 0 and o < map.LegionCount and isLand(b.Col, b.Row) then
            local item = { c = b.Col, r = b.Row, o = o, idx = key(b.Col, b.Row) }
            if b.BuildingType >= 15 then lv5[#lv5 + 1] = item else lv4[#lv4 + 1] = item end
        end
    end
end)

local TARGET = 160
local capitals = {}
local hasOwner = {}
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

-- 每个势力至少有一个省会
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

local assigned = {}
local assignedCount = 0

local function neighbors(c, r)
    local list = {
        { c - 1, r }, { c + 1, r }, { c, r - 1 }, { c, r + 1 },
    }
    if c % 2 == 1 then
        list[#list + 1] = { c - 1, r + 1 }
        list[#list + 1] = { c + 1, r + 1 }
    else
        list[#list + 1] = { c - 1, r - 1 }
        list[#list + 1] = { c + 1, r - 1 }
    end
    return list
end

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
            if inMap(nc, nr) and isLand(nc, nr) and ownerOf(nc, nr) == o then
                local k = key(nc, nr)
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

-- 兜底：同势力但被海隔开的格子，按六边形近似距离找最近省会
local leftovers = 0
for r = 0, H - 1 do
    for c = 0, W - 1 do
        local k = key(c, r)
        if isLand(c, r) and not assigned[k] then
            local o = ownerOf(c, r)
            local best, bestD = nil, 1e18
            for _, cap in ipairs(byOwner[o] or {}) do
                local dx, dy = math.abs(cap.c - c), math.abs(cap.r - r)
                local d = dx * dx + dy * dy
                if d < bestD then bestD, best = d, cap end
            end
            if best then
                assigned[k] = true
                assignedCount = assignedCount + 1
                leftovers = leftovers + 1
                map.SetProvince(c, r, best.idx)
            end
        end
    end
end

print(string.format('省会=%d 势力=%d 已分省区格=%d 兜底=%d',
    #capitals, (function() local n = 0 for _ in pairs(byOwner) do n = n + 1 end return n end)(),
    assignedCount, leftovers))
