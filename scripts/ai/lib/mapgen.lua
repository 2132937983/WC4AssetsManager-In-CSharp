--[[============================================================================
  ai/lib/mapgen.lua —— 地图生成后的几何校验
  ----------------------------------------------------------------------------
  解决的问题：LLM 逐格画地形一定会画出"一半海一半陆"或一团团城市斑块，
  而且它自己看不出问题。这里把"像不像一张地图"变成可测的数字：

    · 陆地比例（防止整片海/整片陆）
    · 陆块数量与最大陆块占比（防止碎成群岛）
    · 海岸线密度（太低=假，太高=噪）
    · 最长直线陆地/海洋（抓"整行切分"这类 AI 伪影）
    · 城市是否落在海里
    · 归属覆盖率与空势力

  用法：
      local ai = require('ai')
      ai.mapgen.validate()                       -- 检查当前地图
      ai.mapgen.validate{ expected_min=0.30, expected_max=0.45 }
      ai.mapgen.validate_mask{ path='scripts/mapgen/regions/europe.land.txt' }

  返回一张表：{ ok=bool, land_ratio=..., largest_ratio=..., ... }，
  不达标时会打印每条原因和阈值，方便直接调参数重跑。
============================================================================]]

local core = require('ai.lib.core')

local M = {}

-- ============================================================ 基础

local function neighbors(c, r, W, H)
    local list = {}
    local function add(cc, rr)
        if cc >= 0 and cc < W and rr >= 0 and rr < H then
            -- land[] 是 1 基数组，这里必须 +1，否则连通分量会被拆成一条条
            list[#list + 1] = rr * W + cc + 1
        end
    end

    add(c - 1, r); add(c + 1, r); add(c, r - 1); add(c, r + 1)
    if (c % 2) == 1 then
        add(c - 1, r + 1); add(c + 1, r + 1)
    else
        add(c - 1, r - 1); add(c + 1, r - 1)
    end
    return list
end

--- 连通分量：返回 数量, 最大分量格数, 每格所属分量
local function components(land, W, H)
    local comp = {}
    local sizes = {}

    for start = 1, W * H do
        if land[start] and comp[start] == nil then
            local id = #sizes + 1
            local stack, head, size = { start }, 1, 0
            comp[start] = id

            while head <= #stack do
                local cur = stack[head]
                head = head + 1
                size = size + 1

                local c = (cur - 1) % W
                local r = math.floor((cur - 1) / W)
                for _, n in ipairs(neighbors(c, r, W, H)) do
                    if land[n] and comp[n] == nil then
                        comp[n] = id
                        stack[#stack + 1] = n
                    end
                end
            end

            sizes[#sizes + 1] = size
        end
    end

    local maxSize = 0
    for _, s in ipairs(sizes) do
        if s > maxSize then maxSize = s end
    end
    return #sizes, maxSize, comp
end

--- 统计一行/一列里最长的连续陆地/海洋长度
local function longest_runs(getter, W, H)
    local maxLand, maxSea = 0, 0

    for r = 0, H - 1 do
        local landRun, seaRun = 0, 0
        for c = 0, W - 1 do
            if getter(c, r) then
                landRun = landRun + 1
                seaRun = 0
                if landRun > maxLand then maxLand = landRun end
            else
                seaRun = seaRun + 1
                landRun = 0
                if seaRun > maxSea then maxSea = seaRun end
            end
        end
    end

    for c = 0, W - 1 do
        local landRun, seaRun = 0, 0
        for r = 0, H - 1 do
            if getter(c, r) then
                landRun = landRun + 1
                seaRun = 0
                if landRun > maxLand then maxLand = landRun end
            else
                seaRun = seaRun + 1
                landRun = 0
                if seaRun > maxSea then maxSea = seaRun end
            end
        end
    end

    return maxLand, maxSea
end

--- 统计"整行/整列完全同色"的行列数。
--- 这是"按行列硬切"最可靠的信号：真正的地理轮廓几乎不会整行整列同色，
--- 而模型常画出的"上半陆下半海"会让 100% 的行都同色。
local function uniform_lines(land, W, H)
    local uniformRows, uniformCols = 0, 0

    for r = 0, H - 1 do
        local base = land[r * W + 1]
        local same = true
        for c = 1, W - 1 do
            if land[r * W + c + 1] ~= base then
                same = false
                break
            end
        end
        if same then uniformRows = uniformRows + 1 end
    end

    for c = 0, W - 1 do
        local base = land[c + 1]
        local same = true
        for r = 1, H - 1 do
            if land[r * W + c + 1] ~= base then
                same = false
                break
            end
        end
        if same then uniformCols = uniformCols + 1 end
    end

    return uniformRows, uniformCols
end

local function pct(a, b)
    if b == nil or b <= 0 then return 0 end
    return a * 100.0 / b
end

local function verdict(ok, label)
    return string.format('  [%s] %s', ok and 'OK' or '不达标', label)
end

-- ============================================================ 校验：当前地图

function M.validate(o)
    o = core.params(o)
    local W, H = map.Width, map.Height
    local seaType = core.to_int(core.opt(o, 'sea_type', 1), 1)
    local expectedMin = tonumber(core.opt(o, 'expected_min', 0.02)) or 0.02
    local expectedMax = tonumber(core.opt(o, 'expected_max', 0.85)) or 0.85
    local minLargest = tonumber(core.opt(o, 'min_largest', 0.80)) or 0.80
    -- 整行/整列完全同色的比例：按行列硬切（"一半海一半陆"）最典型的伪影
    local maxUniform = tonumber(core.opt(o, 'max_uniform_ratio',
        core.opt(o, 'max_straight_ratio', 0.35))) or 0.35
    local requireCities = core.switch(core.opt(o, 'require_cities_land', true), true)
    local requireBelong = core.switch(core.opt(o, 'require_full_belong', false), false)
    local verbose = core.switch(core.opt(o, 'verbose', true), true)

    local land = {}
    local landCount, seaCount = 0, 0

    for row = 0, H - 1 do
        for col = 0, W - 1 do
            local t = map.GetTerrain(col, row)
            local idx = row * W + col + 1
            if t ~= seaType and t >= 0 then
                land[idx] = true
                landCount = landCount + 1
            else
                seaCount = seaCount + 1
            end
        end
    end

    local landRatio = (landCount + seaCount > 0) and (landCount / (landCount + seaCount)) or 0
    local compCount, largest, comp = components(land, W, H)
    local largestRatio = landCount > 0 and (largest / landCount) or 0

    -- 海岸线：陆地格，且至少一个邻居是海/越界
    local coast = 0
    for row = 0, H - 1 do
        for col = 0, W - 1 do
            local idx = row * W + col + 1
            if land[idx] then
                local isCoast = false
                local c = col - 1
                if c < 0 or not land[row * W + c + 1] then isCoast = true end
                c = col + 1
                if not isCoast and (c >= W or not land[row * W + c + 1]) then isCoast = true end
                if not isCoast and (row - 1 < 0 or not land[(row - 1) * W + col + 1]) then isCoast = true end
                if not isCoast and (row + 1 >= H or not land[(row + 1) * W + col + 1]) then isCoast = true end
                if isCoast then coast = coast + 1 end
            end
        end
    end

    local maxLandRun, maxSeaRun = longest_runs(function(c, r)
        return land[r * W + c + 1] == true
    end, W, H)

    local uniformRows, uniformCols = uniform_lines(land, W, H)
    local uniformRowRatio = H > 0 and (uniformRows / H) or 0
    local uniformColRatio = W > 0 and (uniformCols / W) or 0
    local uniformMax = math.max(uniformRowRatio, uniformColRatio)

    -- 建筑落海
    local buildingsInSea = 0
    local samples = {}
    map.ForEachBuilding(function(b)
        if map.GetTerrain(b.Col, b.Row) == seaType then
            buildingsInSea = buildingsInSea + 1
            if #samples < 5 then
                samples[#samples + 1] = string.format('(%d,%d) %s', b.Col, b.Row,
                    b.Name ~= '' and b.Name or b.BuildingTypeName)
            end
        end
    end)

    -- 归属覆盖 & 空势力
    local belongs, belongCount = {}, 0
    for row = 0, H - 1 do
        for col = 0, W - 1 do
            if land[row * W + col + 1] then
                local v = map.GetBelong(col, row)
                if v ~= 255 then
                    belongCount = belongCount + 1
                    belongs[v] = (belongs[v] or 0) + 1
                end
            end
        end
    end

    local emptyLegions = {}
    for i = 0, map.LegionCount - 1 do
        if (belongs[i] or 0) == 0 then emptyLegions[#emptyLegions + 1] = i end
    end

    local belongRatio = landCount > 0 and (belongCount / landCount) or 0

    -- ---- 判定 ----
    local failures = {}
    local function check(ok, message)
        if not ok then failures[#failures + 1] = message end
        return ok
    end

    local okRatio = check(landRatio >= expectedMin and landRatio <= expectedMax,
        string.format('陆地比例 %.1f%% 不在 %.0f%%~%.0f%% 之间', landRatio * 100, expectedMin * 100, expectedMax * 100))
    local okLargest = check(largestRatio >= minLargest,
        string.format('最大陆块只占陆地 %.1f%%（要求 ≥ %.0f%%，地图太碎）', largestRatio * 100, minLargest * 100))
    local okStraight = check(uniformMax <= maxUniform,
        string.format('整行/整列完全同色占 %.0f%%（阈值 %.0f%%），像按行列硬切',
            uniformMax * 100, maxUniform * 100))
    local okCities = check((not requireCities) or buildingsInSea == 0,
        string.format('%d 座建筑落在海里', buildingsInSea))
    local okBelong = check((not requireBelong) or belongRatio >= 0.95,
        string.format('归属只覆盖陆地 %.1f%%（要求 ≥ 95%%）', belongRatio * 100))

    if verbose then
        core.head('地图几何检查')
        print(string.format('尺寸：%d x %d', W, H))
        print(string.format('陆地：%d 格（%.1f%%），海洋 %d 格', landCount, landRatio * 100, seaCount))
        print(string.format('陆块：%d 个，最大 %d 格（占陆地 %.1f%%）', compCount, largest, largestRatio * 100))
        print(string.format('海岸线：%d 格（密度 %.1f%%）', coast, pct(coast, landCount)))
        print(string.format('最长连续：陆地 %d 格、海洋 %d 格（图宽 %d）', maxLandRun, maxSeaRun, W))
        print(string.format('整行同色：%d 行（%.0f%%）　整列同色：%d 列（%.0f%%）',
            uniformRows, uniformRowRatio * 100, uniformCols, uniformColRatio * 100))
        print(string.format('建筑落海：%d%s', buildingsInSea,
            #samples > 0 and ('（' .. table.concat(samples, '，') .. '）') or ''))
        print(string.format('归属覆盖：陆地 %.1f%%%s', belongRatio * 100,
            #emptyLegions > 0 and ('，空势力 ' .. #emptyLegions .. ' 个：' .. table.concat(emptyLegions, ', ')) or ''))
        print('')
        print(verdict(okRatio, '陆地比例'))
        print(verdict(okLargest, '最大陆块占比'))
        print(verdict(okStraight, '直线伪影'))
        print(verdict(okCities, '城市落海'))
        print(verdict(okBelong, '归属覆盖'))
        print(#failures == 0 and '→ 通过' or string.format('→ %d 项不达标，建议调整掩码/参数后重跑', #failures))
    end

    return {
        ok = #failures == 0,
        land_ratio = landRatio,
        land_cells = landCount,
        sea_cells = seaCount,
        components = compCount,
        largest_component = largest,
        largest_ratio = largestRatio,
        coastline = coast,
        max_land_run = maxLandRun,
        max_sea_run = maxSeaRun,
        uniform_row_ratio = uniformRowRatio,
        uniform_col_ratio = uniformColRatio,
        buildings_in_sea = buildingsInSea,
        belong_ratio = belongRatio,
        empty_legions = emptyLegions,
        failures = failures,
    }
end

-- ============================================================ 校验：掩码文件

local function read_mask_file(path)
    local candidates = { path, 'publish/' .. path, '../' .. path }
    local text = nil
    if io ~= nil then
        for _, p in ipairs(candidates) do
            local f = io.open(p, 'r')
            if f then
                text = f:read('*a')
                f:close()
                break
            end
        end
    end
    if text == nil then return nil, '读不到掩码文件：' .. tostring(path) end

    local rows, width = {}, 0
    for raw in string.gmatch(text .. '\n', '([^\n]*)\n') do
        local line = string.gsub(raw, '\r', '')
        if not string.match(line, '^%s*[#;]') and line ~= '' then
            rows[#rows + 1] = line
            if #line > width then width = #line end
        end
    end

    if #rows == 0 then return nil, '掩码里没有有效行' end
    return { rows = rows, w = math.max(1, width), h = #rows }
end

--- 在**不改地图**的前提下检查一份海陆掩码（geo rasterize 的产物）
function M.validate_mask(o)
    o = core.params(o)
    local path = core.need(o, 'path')
    local landChar = tostring(core.opt(o, 'land_char', 'X'))
    local expectedMin = tonumber(core.opt(o, 'expected_min', 0.02)) or 0.02
    local expectedMax = tonumber(core.opt(o, 'expected_max', 0.85)) or 0.85
    local minLargest = tonumber(core.opt(o, 'min_largest', 0.80)) or 0.80
    local maxUniform = tonumber(core.opt(o, 'max_uniform_ratio',
        core.opt(o, 'max_straight_ratio', 0.35))) or 0.35

    local mask, err = read_mask_file(tostring(path))
    if not mask then return core.fail(err) end

    local W, H = mask.w, mask.h
    local land = {}
    local landCount = 0

    for row = 0, H - 1 do
        local line = mask.rows[row + 1] or ''
        for col = 0, W - 1 do
            if string.sub(line, col + 1, col + 1) == landChar then
                land[row * W + col + 1] = true
                landCount = landCount + 1
            end
        end
    end

    local compCount, largest = components(land, W, H)
    local landRatio = landCount / (W * H)
    local largestRatio = landCount > 0 and (largest / landCount) or 0
    local maxLandRun, maxSeaRun = longest_runs(function(c, r)
        return land[r * W + c + 1] == true
    end, W, H)

    local failures = {}
    if landRatio < expectedMin or landRatio > expectedMax then
        failures[#failures + 1] = string.format('陆地比例 %.1f%% 不在 %.0f%%~%.0f%%',
            landRatio * 100, expectedMin * 100, expectedMax * 100)
    end
    if largestRatio < minLargest then
        failures[#failures + 1] = string.format('最大陆块只占 %.1f%%（要求 ≥ %.0f%%）', largestRatio * 100, minLargest * 100)
    end
    local uniformRows, uniformCols = uniform_lines(land, W, H)
    local uniformRowRatio = H > 0 and (uniformRows / H) or 0
    local uniformColRatio = W > 0 and (uniformCols / W) or 0
    local uniformMax = math.max(uniformRowRatio, uniformColRatio)
    if uniformMax > maxUniform then
        failures[#failures + 1] = string.format('整行/整列完全同色占 %.0f%%（阈值 %.0f%%）',
            uniformMax * 100, maxUniform * 100)
    end

    core.head('掩码检查：' .. tostring(path))
    print(string.format('尺寸：%d x %d　陆地 %d 格（%.1f%%）　海 %d 格',
        W, H, landCount, landRatio * 100, W * H - landCount))
    print(string.format('陆块：%d 个，最大 %d 格（占陆地 %.1f%%）', compCount, largest, largestRatio * 100))
    print(string.format('最长连续：陆地 %d 格、海洋 %d 格', maxLandRun, maxSeaRun))
    print(string.format('整行同色：%d 行（%.0f%%）　整列同色：%d 列（%.0f%%）',
        uniformRows, uniformRowRatio * 100, uniformCols, uniformColRatio * 100))
    for _, f in ipairs(failures) do print('  [不达标] ' .. f) end
    print(#failures == 0 and '→ 通过' or string.format('→ %d 项不达标', #failures))

    return {
        ok = #failures == 0,
        width = W, height = H,
        land_cells = landCount,
        land_ratio = landRatio,
        components = compCount,
        largest_component = largest,
        largest_ratio = largestRatio,
        max_land_run = maxLandRun,
        max_sea_run = maxSeaRun,
        uniform_row_ratio = uniformRowRatio,
        uniform_col_ratio = uniformColRatio,
        failures = failures,
    }
end

return M
