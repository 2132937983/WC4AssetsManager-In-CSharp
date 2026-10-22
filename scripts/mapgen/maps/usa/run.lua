--[[================================================================================
  建图工具 build-map.lua —— 按"地理数据"批量生成某地区的地图
  --------------------------------------------------------------------------------
  一、它做什么（只做这四件事，都是幂等可重复跑的）
    1. 把「真实经纬度」换算成格子坐标（投影/标定全部在这一步，可先干跑核对）
    2. 在城市格上落建筑（等级 1~5 → 建筑类型 11~15，并写入城市名）
    3. 给城市格写归属值（按 [owners] 把势力名解析成军团索引，归属值语义就是军团索引）
    4. 后处理：海岸线 / 绿化 / 建筑连线（开关在 [post]）

  二、怎么用
    1. 改下面那个 SPEC 常量（本文件里搜 `local SPEC`），指向你的地区数据文件
    2. 控制台 F3：  lua scripts/mapgen/core/build-map.lua
    3. 第一次务必让 [map] 里 dry_run = true —— 只打印报表、不动地图
    4. 照报表里的坐标在背景图上核对（或直接看渲染结果），改数据文件再跑
    5. 核对满意后把 dry_run 改成 false，再跑一次真正落数据（Ctrl+Z 可整体撤销）

  三、数据文件格式（纯文本 .txt，不是 Lua；# 之后是注释，字段用空格或 TAB 分隔）
    [map]            name=地区名 / dry_run= / overwrite= / clear_cities= / belong_radius= / print_limit= ...
    [projection]     mode = window | ref | manual      （三选一，见下方说明）
    [owners]         势力名 = 国家ID                    （国家ID 查 stringtable 的 country_<id>）
    [cities]         名称 纬度 经度 势力 等级(1~5)
    [ref]            格子列 格子行 纬度 经度           （仅 mode=ref 时需要，≥3 个点）
    [post]           create_coast= / process_ocean_layer2= / connect_buildings= / greening=  （可选）
    [terrain]        海陆地形：enabled= / mode=mask|from_cities / mask_file= / land_type= / sea_type= …
                     type_mask_file = 可选的地形类型掩码（0-9 / A-V 对应地形ID；陆地上覆盖 land_type）
    [belong]         归属掩码：enabled= / mode=mask / mask_file= / map_file= / land_only=
                     （掩码与字符映射由 `geo rasterize --owners` 生成；先铺国家领土，城市格随后覆盖）

    投影三种模式：
      window —— 最省事。给"背景图整幅覆盖的经纬度窗口"，按地图实际宽高线性换算。
                适合等距圆柱（矩形）投影的背景图，也是示例地区文件默认用的模式。
      ref    —— 最准。给 3 个以上"能在地图上认出来的地标"，脚本做最小二乘仿射拟合，
                能自动吸收缩放、斜切、旋转。有背景图时优先用这个。
      manual —— 给 origin_lat / origin_lon / origin_col / origin_row / scale_lon / scale_lat。

      ★ 六边形几何：本引擎"奇数列下移半格"（与 Camera.ScreenToHex 一致），
        脚本已按同一公式做修正，你只给经纬度即可，不用自己处理错位。

    [terrain] 海陆地形（默认关闭；开了才会动地形，仍受 [map] 的 dry_run 约束）：
      enabled = true 打开。两种模式：
        mode = from_cities —— 省事：按城市分布自动长陆地，每级城市一个半径
                              （radius_1..radius_5 可调），保证所有城市都落在陆地上
        mode = mask        —— 可控：手绘 ASCII 掩码覆盖整张地图，mask_file 指向掩码文件，
                              smooth = 1 做一次多数表决去掉孤立点
      掩码字符：X = 陆地　. = 海（其它字符也算海，空格＝海）　- = 保留原样不动　# 或 ; 开头 = 注释
      掩码按"归一化位置"铺满整张地图，采样行号带奇数列半格修正 → 和城市落点是同一套坐标，不会错位
      land_type = 陆地写什么地形值（默认 0）　sea_type = 海，必须是 1（引擎判海洋就是 Tile1==1）

      执行顺序：先铺海陆 → 再做城市校验（"落在海里"的告警这时才有意义）→ 落建筑与归属 → 后处理。
      干跑时也会拿计划地形做校验，所以**不写地形就能先看出哪些城市会掉进海里**。

  四、AI 建图建议流程
    0. **不要逐格 SetTerrain 画大陆**（模型没有几何先验，必然半海半陆）。
       先用 `geo rasterize` 从 GeoJSON 生成海陆/归属掩码，再用本脚本落地：
         geo rasterize --geojson <地区>.geojson --out <地区>.land.txt --width W --height H --smooth 1
         geo rasterize --geojson <国家边界>.geojson --owners --out <地区>.owner.txt --width W --height H
       详细步骤见记忆 map-generation-workflow（Resource/Memory/）。
    1. 先看地图现状：map.Width x map.Height 打印在报表头部；地形必须是海=1、陆=其它
    2. 定投影：有背景图 → 用 ref 标定（挑 3~4 个辨识度高的地标，报错残差 >1.5 格就重挑）；
       没有背景图 → 用 window 给一个大致经纬度窗口
    3. 填数据：城市名用中文/英文都行，脚本查不到会**自动写进 stringtable**（无需手工加）
    4. 干跑核对：看每个城市的 (col,row) 是否落在预期位置、有没有"落在海里/越界/撞格"
    5. 落数据：dry_run=false → 自动落建筑 + 归属 → 最后按需开 [post] 的海岸线/连线
    6. 势力没有军团时（该国家ID 不在军团列表里）→ 报表会单列出来，先建军团再重跑

  五、它不做的事（这些交给对应模式/命令，别指望本工具）
    · 省区（Province）：Lua 只能读不能写 → 用省区编辑模式 / province 系列命令
    · 地形细节：本工具只铺"陆 / 海"两种底（外加可选的绿化、海岸线、海洋第二层）。
      山、河、森林、地形变体这些精细地形还是走地形编辑模式（识别地形 / 画笔 / 变体随机）
    · 单位/将领/陷阱：走单位部署模式或 army 系列命令
    · 保存文件：脚本改完记得在编辑器里保存地图（.btl）
================================================================================]]

-- ========== 0. 只改这一行：指向你要建的地区数据文件 ==========
local SPEC = 'scripts/mapgen/regions/usa.txt'

-- ========== 1. 常量 ==========
local REPORT_CANDIDATES = {
    'scripts/mapgen/last-report.txt',
    'Scripts/mapgen/last-report.txt',
    'mapgen-report.txt',
}
local SEA_TERRAIN = 1          -- 海：TileType1 == 1（与 ProvinceModifier.IsSeaTerrain 一致）
local CITY_TYPE_BASE = 10      -- 等级 1~5 → 建筑类型 11~15

-- ========== 2. 基础设施 ==========

local has_io = (type(io) == 'table' and io.open ~= nil)

local function read_file(path)
    if not has_io then return nil end
    local f = io.open(path, 'r')
    if not f then return nil end
    local ok, data = pcall(function() return f:read('*a') end)
    f:close()
    if not ok then return nil end
    return data
end

--- 依次试候选路径读文件，返回 文本, 命中的路径
--- 候选路径会向上多找几级：编辑器可能从 bin/Debug/net10.0-windows 启动，
--- 而数据文件在仓库根的 scripts/mapgen/regions/。
local function expand_paths(p)
    local list = { p, 'publish/' .. p, '../' .. p }
    local up = ''
    for _ = 1, 6 do
        up = up .. '../'
        list[#list + 1] = up .. p
    end

    -- 兼容另一台电脑上的绝对路径：只取 basename，再从 regions/ 找
    local base = tostring(p):match('[^/\\]+$')
    if base and tostring(p):find('regions/', 1, true) then
        local rel = 'scripts/mapgen/regions/' .. base
        list[#list + 1] = rel
        up = ''
        for _ = 1, 6 do
            up = up .. '../'
            list[#list + 1] = up .. rel
        end
    end
    return list
end

local function read_first(paths)
    for _, p in ipairs(paths) do
        for _, cand in ipairs(expand_paths(p)) do
            local text = read_file(cand)
            if text then return text, cand end
        end
    end
    return nil, nil
end

local function write_first(paths, text)
    if not has_io then return nil end
    for _, p in ipairs(paths) do
        for _, cand in ipairs(expand_paths(p)) do
            local f = io.open(cand, 'w')
            if f then
                f:write(text)
                f:close()
                return cand
            end
        end
    end
    return nil
end

local function rnd(x) return math.floor(x + 0.5) end

--- 手写 trim：不用 '^%s*(.-)%s*$'。
--- MoonSharp 的 gsub 在 200 字符的掩码行上会抛 "pattern too complex"。
local function trim(s)
    local a, b = 1, #s
    while a <= b do
        local c = string.sub(s, a, a)
        if c ~= ' ' and c ~= '\t' and c ~= '\r' and c ~= '\n' then break end
        a = a + 1
    end
    while b >= a do
        local c = string.sub(s, b, b)
        if c ~= ' ' and c ~= '\t' and c ~= '\r' and c ~= '\n' then break end
        b = b - 1
    end
    return string.sub(s, a, b)
end

--- 按 \n 切行。
--- 不用 string.gmatch(text, '([^\n]*)\n')：MoonSharp 下模式匹配按字节看字符，
--- 中文「上」的码位是 U+4E0A，低字节正好是 0x0A，会被当成换行把一行拆开。
--- plain find 不受这个影响。
local function split_lines(text)
    local lines = {}
    local start = 1
    while true do
        local nl = string.find(text, '\n', start, true)
        if nl == nil then
            lines[#lines + 1] = string.sub(text, start)
            break
        end
        lines[#lines + 1] = string.sub(text, start, nl - 1)
        start = nl + 1
    end
    return lines
end

--- 手动按 ASCII 空格/TAB 切词。
--- 不用 string.gmatch(line, '%S+')：MoonSharp 按码位低字节判断空白，
--- 「希」（U+5E0C）的低字节是 0x0C（换页符），会被当成空白切掉，城市势力变成「腊」。
local function split_ws(line)
    local t, buf = {}, {}

    local function flush()
        if #buf > 0 then
            t[#t + 1] = table.concat(buf)
            buf = {}
        end
    end

    for i = 1, #line do
        local c = string.sub(line, i, i)
        if c == ' ' or c == '\t' or c == '\r' or c == '\n' then
            flush()
        else
            buf[#buf + 1] = c
        end
    end
    flush()
    return t
end

--- 按第一个 '=' 切键值（手动切，理由同上：中文键可能被 %s 误判）
local function split_kv(line)
    local eq = string.find(line, '=', 1, true)
    if eq == nil then return nil, nil end
    return trim(string.sub(line, 1, eq - 1)), trim(string.sub(line, eq + 1))
end
local function to_num(s) return tonumber(s) end

-- ========== 3. 解析数据文件 ==========

local function parse_spec(text)
    local spec = { map = {}, proj = {}, owners = {}, cities = {}, refs = {}, post = {}, terrain = {}, belong = {} }
    local section, lineNo = nil, 0

    for _, raw in ipairs(split_lines(text)) do
        lineNo = lineNo + 1
        local line = string.gsub(raw, '#.*$', '')     -- 去注释
        line = trim(string.gsub(line, '\r', ''))
        if line ~= '' then
            local head = string.match(line, '^%[([%w_]+)%]$')
            if head then
                section = string.lower(head)
            elseif section == nil then
                return nil, string.format('第 %d 行出现在任何 [节] 之前：%s', lineNo, line)
            elseif section == 'owners' then
                local k, v = split_kv(line)
                local num = v and tonumber(v)
                if k and num then spec.owners[k] = num end
            elseif section == 'map' or section == 'proj' or section == 'post'
                or section == 'projection' or section == 'terrain' or section == 'belong' then
                local k, v = split_kv(line)
                if k then
                    k = string.lower(k)
                    local num = tonumber(v)
                    local target = (section == 'projection') and spec.proj or
                                    (section == 'map') and spec.map or
                                    (section == 'terrain') and spec.terrain or
                                    (section == 'belong') and spec.belong or spec.post
                    -- true/false 必须转成布尔值：留在字符串里的话 "false" 也是真值，
                    -- 开关（dry_run/overwrite/…）会静默失效
                    local low = string.lower(v)
                    if num ~= nil then target[k] = num
                    elseif low == 'true' then target[k] = true
                    elseif low == 'false' then target[k] = false
                    else target[k] = v end
                end
            elseif section == 'ref' then
                local f = split_ws(line)
                if #f >= 4 then
                    spec.refs[#spec.refs + 1] = {
                        col = to_num(f[1]), row = to_num(f[2]),
                        lat = to_num(f[3]), lon = to_num(f[4]),
                        note = f[5] or '', line = lineNo,
                    }
                end
            elseif section == 'cities' then
                local f = split_ws(line)
                if #f >= 4 then
                    spec.cities[#spec.cities + 1] = {
                        name = f[1], lat = to_num(f[2]), lon = to_num(f[3]),
                        owner = f[4], level = to_num(f[5] or '3') or 3, line = lineNo,
                    }
                end
            end
        end
    end

    return spec, nil
end

-- ========== 4. 投影 ==========

--- 参考点残差要用"未做奇偶修正"的行号：引擎里奇数列整体下移半格
local function unshift_row(col, row)
    return row + (((col % 2) == 1) and 0.5 or 0)
end

--- 建投影：返回 fn(lat, lon) -> colF, r0 ，以及描述文本与参考点残差
local function build_projection(spec, W, H)
    local proj = spec.proj or {}
    local mode = proj.mode or (#spec.refs >= 3 and 'ref' or 'window')

    if mode == 'ref' and #spec.refs >= 3 then
        -- 仿射拟合：col = a1*lon + b1*lat + c1 ；r0 = a2*lon + b2*lat + c2
        -- 先中心化（经纬度数值大，直接算正规方程条件数很差）
        local n = #spec.refs
        local mlon, mlat, mcol, mrow = 0, 0, 0, 0
        for _, p in ipairs(spec.refs) do
            mlon = mlon + p.lon; mlat = mlat + p.lat
            mcol = mcol + p.col; mrow = mrow + unshift_row(p.col, p.row)
        end
        mlon, mlat, mcol, mrow = mlon / n, mlat / n, mcol / n, mrow / n

        local sxx, sxy, syy = 0, 0, 0
        local sxu, syu, sxv, syv = 0, 0, 0, 0
        for _, p in ipairs(spec.refs) do
            local x = p.lon - mlon
            local y = p.lat - mlat
            local du = p.col - mcol
            local dv = unshift_row(p.col, p.row) - mrow
            sxx = sxx + x * x; sxy = sxy + x * y; syy = syy + y * y
            sxu = sxu + x * du; syu = syu + y * du
            sxv = sxv + x * dv; syv = syv + y * dv
        end

        -- 中心化之后常数项为 0，2×2 正规方程用 Cramer 法则直接解
        local det = sxx * syy - sxy * sxy
        if math.abs(det) < 1e-9 then
            return nil, '参考点退化（这些点近似共线或重合）：请挑位置分散的地标，或改用 window 模式'
        end

        local a1 = (sxu * syy - syu * sxy) / det
        local b1 = (syu * sxx - sxu * sxy) / det
        local a2 = (sxv * syy - syv * sxy) / det
        local b2 = (syv * sxx - sxv * sxy) / det
        local c1 = mcol - (a1 * mlon + b1 * mlat)
        local c2 = mrow - (a2 * mlon + b2 * mlat)

        local fn = function(lat, lon)
            return a1 * lon + b1 * lat + c1, a2 * lon + b2 * lat + c2
        end

        -- 参考点残差（格子数）：标定质量直接暴露给使用者
        local res = {}
        for _, p in ipairs(spec.refs) do
            local cf, rf = fn(p.lat, p.lon)
            local c = rnd(cf)
            local r = rnd(rf - (((c % 2) == 1) and 0.5 or 0))
            local d = math.sqrt((c - p.col) ^ 2 + (r - p.row) ^ 2)
            res[#res + 1] = { name = p.note, col = p.col, row = p.row, gc = c, gr = r, d = d }
        end

        return fn, string.format(
            'ref 仿射拟合（%d 点）：col=%+.4f·lon%+.4f·lat%+.2f　row=%+.4f·lon%+.4f·lat%+.2f',
            n, a1, b1, c1, a2, b2, c2), res
    end

    if mode == 'ref' then
        return nil, string.format('mode=ref 需要至少 3 个 [ref] 标定点（当前 %d 个）', #spec.refs)
    end

    if mode == 'manual' then
        local oLat = proj.origin_lat; local oLon = proj.origin_lon
        local oCol = proj.origin_col; local oRow = proj.origin_row
        local sx = proj.scale_lon; local sy = proj.scale_lat
        if oLat == nil or oLon == nil or oCol == nil or oRow == nil or sx == nil or sy == nil then
            return nil, 'mode=manual 需要 origin_lat/origin_lon/origin_col/origin_row/scale_lon/scale_lat'
        end
        local fn = function(lat, lon)
            return oCol + (lon - oLon) * sx, oRow + (oLat - lat) * sy
        end
        return fn, 'manual 线性换算', {}
    end

    -- window：背景图整幅覆盖的经纬度窗口（默认）
    local lonW = proj.lon_west; local lonE = proj.lon_east
    local latN = proj.lat_north; local latS = proj.lat_south
    if lonW == nil or lonE == nil or latN == nil or latS == nil then
        return nil, 'mode=window 需要 lon_west/lon_east/lat_north/lat_south'
    end
    if lonE == lonW or latN == latS then
        return nil, 'mode=window 的经纬度窗口退化（东西或南北跨度为 0）'
    end

    local fn = function(lat, lon)
        local cf = (lon - lonW) / (lonE - lonW) * (W - 1)
        local rf = (latN - lat) / (latN - latS) * (H - 1)
        return cf, rf
    end
    return fn, string.format('window 线性换算（经 %g~%g，纬 %g~%g）', lonW, lonE, latS, latN), {}
end

--- 经纬度 → 格子（含"奇数列下移半格"修正，与引擎一致）
local function to_hex(fn, lat, lon)
    local cf, rf = fn(lat, lon)
    local col = rnd(cf)
    local row = rnd(rf - (((col % 2) == 1) and 0.5 or 0))
    return col, row, cf, rf
end

-- ========== 5. 军团索引（归属值） ==========

--- 返回 countryId -> 军团索引 的映射（只解析用到的那些国家ID）
local function resolve_legion_indices(countryIds)
    local n = map.LegionCount
    local result, missing = {}, {}

    -- 优先直读（新 exe 带 GetLegionCountryIdAt）
    local direct = false
    do
        local ok, f = pcall(function() return map.GetLegionCountryIdAt end)
        direct = (ok and f ~= nil)
    end

    if direct then
        for i = 0, n - 1 do
            local cid = map.GetLegionCountryIdAt(i)
            if cid and cid >= 0 then result[cid] = result[cid] or i end
        end
    else
        -- 老 exe：用只写接口当安全探针（失败即无副作用，成功即写回同值）
        for _, cid in ipairs(countryIds) do
            local ok, r = pcall(function() return map.SetLegionActionId(cid, cid) end)
            if ok and r and r.Success then
                for i = 0, n - 1 do
                    local ok2, r2 = pcall(function() return map.SetLegionCountryIdAt(i, cid) end)
                    if ok2 and r2 and r2.Success then result[cid] = i; break end
                end
            end
        end
    end

    for _, cid in ipairs(countryIds) do
        if result[cid] == nil then missing[#missing + 1] = cid end
    end
    return result, missing, direct
end

-- ========== 6. 六边形邻居（与渲染层奇数列下移的规则一致） ==========

local function neighbors(col, row, W, H)
    local list = {}
    local function add(c, r)
        if c >= 0 and c < W and r >= 0 and r < H then list[#list + 1] = c .. ',' .. r end
    end
    add(col - 1, row); add(col + 1, row); add(col, row - 1); add(col, row + 1)
    if (col % 2) == 1 then add(col - 1, row + 1); add(col + 1, row + 1)
    else add(col - 1, row - 1); add(col + 1, row - 1) end
    return list
end

-- ========== 6.5 海陆地形 ==========
--
-- 掩码文件（纯文本画布，一个字符≈一格）：
--     X = 陆地      . = 海（其实"既不是陆也不是'不动'"的字符都算海，空格也算海）
--     - = 保留原样，不动这一格
--     # 或 ; 开头的行 = 注释
--   掩码覆盖**整张地图**，按归一化位置铺开；采样行号带"奇数列下移半格"修正，
--   与城市落点用的是同一套约定，所以地形和城市不会错位。

local function read_mask(path)
    local text, hit = read_first({ path, 'publish/' .. path, '../' .. path })
    if not text then return nil, nil end

    local rows, maxLen = {}, 0
    for _, raw in ipairs(split_lines(text)) do
        local line = string.gsub(raw, '\r', '')
        if not string.match(line, '^%s*[#;]') then
            rows[#rows + 1] = line
            if #line > maxLen then maxLen = #line end
        end
    end
    while #rows > 0 and trim(rows[#rows]) == '' do table.remove(rows) end
    if #rows == 0 then return nil, hit end
    return { rows = rows, w = math.max(1, maxLen), h = #rows }, hit
end

--- 读归属掩码的字符映射（geo rasterize --owners 生成的 <out>.map）：
---   每行 `A = 7`，A 是掩码里的字符，7 是国家ID。
local function read_owner_map(path)
    if path == nil or path == '' then return nil, nil end
    local text, hit = read_first({ path, 'publish/' .. path, '../' .. path })
    if not text then return nil, nil end

    local map, count = {}, 0
    for _, raw in ipairs(split_lines(text)) do
        local line = trim(string.gsub(string.gsub(raw, '\r', ''), '#.*$', ''))
        if line ~= '' then
            local ch, id = string.match(line, '^(%S)%s*=%s*(%-?%d+)$')
            if ch and id then
                map[ch] = tonumber(id)
                count = count + 1
            end
        end
    end

    return (count > 0) and map or nil, hit
end

--- 六边形 7 邻域多数表决：去掉孤立点，让海岸线不那么碎。
--- 不能再用 3×3：奇数列下移半格，左上/左下/右上/右下并不是六个邻居。
local function smooth_mask(mask, landChar)
    local w, h = mask.w, mask.h
    local function at(x, y)
        if x < 0 or y < 0 or x >= w or y >= h then return '' end
        return string.sub(mask.rows[y + 1] or '', x + 1, x + 1)
    end

    local out = {}
    for y = 0, h - 1 do
        local line = {}
        for x = 0, w - 1 do
            local cur = at(x, y)
            if cur == '-' then
                line[#line + 1] = '-'                       -- "不动"格不参与平滑
            else
                local cnt = 0
                local offsets = { {0,0}, {-1,0}, {1,0}, {0,-1}, {0,1} }
                if (x % 2) == 1 then
                    offsets[#offsets + 1] = { -1, 1 }
                    offsets[#offsets + 1] = { 1, 1 }
                else
                    offsets[#offsets + 1] = { -1, -1 }
                    offsets[#offsets + 1] = { 1, -1 }
                end
                for _, o in ipairs(offsets) do
                    if at(x + o[1], y + o[2]) == landChar then cnt = cnt + 1 end
                end
                line[#line + 1] = (cnt >= 4) and landChar or '.'
            end
        end
        out[#out + 1] = table.concat(line)
    end
    return { rows = out, w = w, h = h }
end

--- 地形类型掩码字符：0-9 → 0-9，A-V → 10-31，a-v → 10-31。
local function decode_terrain_char(ch)
    local b = string.byte(ch)
    if not b then return nil end
    if b >= 48 and b <= 57 then return b - 48 end       -- '0'-'9'
    if b >= 65 and b <= 90 then return b - 55 end       -- 'A'=10
    if b >= 97 and b <= 122 then return b - 87 end      -- 'a'=10
    return nil
end

--- 掩码 → 每格地形。返回 plan[row*W+col] = 地形类型
--- typeMask 可选：与海陆掩码同尺寸，陆地上的字符决定具体地形（山/森林/沙漠…）。
local function plan_from_mask(mask, W, H, t, typeMask)
    local landChar = tostring(t.land_char or 'X')
    local keepChar = tostring(t.keep_char or '-')
    local landType = tonumber(t.land_type) or 0
    local seaType = tonumber(t.sea_type) or SEA_TERRAIN

    local dW, dH = math.max(1, W - 1), math.max(1, H - 1)
    local plan, stats = {}, { land = 0, sea = 0, keep = 0 }
    local chars = {}
    local sameSize = (mask.w == W and mask.h == H)

    for col = 0, W - 1 do
        for row = 0, H - 1 do
            local mx, my
            if sameSize then
                -- 掩码与地图同尺寸：直接一一对应。
                -- 掩码本身已经按“奇数列下移半格”的十六进制几何生成，
                -- 这里再加一次偏移会让海岸线错位半格。
                mx, my = col + 1, row + 1
            else
                -- 归一化位置：与城市投影同源（col/(W-1)、未修正行号/(H-1)）
                local r0 = row + (((col % 2) == 1) and 0.5 or 0)
                mx = math.floor(col / dW * mask.w) + 1
                my = math.floor(r0 / dH * mask.h) + 1
                if mx > mask.w then mx = mask.w end
                if my > mask.h then my = mask.h end
            end

            local ch = string.sub(mask.rows[my] or '', mx, mx)
            chars[ch] = (chars[ch] or 0) + 1

            if ch == keepChar then
                stats.keep = stats.keep + 1
            elseif ch == landChar then
                local terrain = landType
                if typeMask then
                    local tch = string.sub(typeMask.rows[my] or '', mx, mx)
                    local tv = decode_terrain_char(tch)
                    -- 类型掩码里的海/空格/未知字符一律回落到 land_type
                    if tv ~= nil and tv ~= seaType then terrain = tv end
                end
                plan[row * W + col] = terrain
                stats.land = stats.land + 1
            else
                plan[row * W + col] = seaType
                stats.sea = stats.sea + 1
            end
        end
    end
    return plan, stats, chars
end

--- 按城市分布自动长陆地：每座城市按等级画一个半径内的圆（六边形距离）
local function plan_from_cities(spec, fn, W, H, t)
    local radii = {
        tonumber(t.radius_1) or 2, tonumber(t.radius_2) or 3, tonumber(t.radius_3) or 4,
        tonumber(t.radius_4) or 6, tonumber(t.radius_5) or 8,
    }
    local landType = tonumber(t.land_type) or 0
    local seaType = tonumber(t.sea_type) or SEA_TERRAIN

    local plan, stats = {}, { land = 0, sea = W * H, keep = 0 }
    for i = 0, W * H - 1 do plan[i] = seaType end

    local isLand = {}
    for _, c in ipairs(spec.cities) do
        local col, row = to_hex(fn, c.lat, c.lon)
        if col >= 0 and col < W and row >= 0 and row < H then
            local lvl = math.max(1, math.min(5, c.level or 3))
            local radius = radii[lvl]
            local seen, queue, head = { [col .. ',' .. row] = 0 }, { col .. ',' .. row }, 1
            while head <= #queue do
                local key = queue[head]; head = head + 1
                local depth = seen[key]
                if not isLand[key] then
                    isLand[key] = true
                    stats.land = stats.land + 1
                    stats.sea = stats.sea - 1
                end
                if depth < radius then
                    local cc, rr = string.match(key, '(%d+),(%d+)')
                    for _, nk in ipairs(neighbors(tonumber(cc), tonumber(rr), W, H)) do
                        if not seen[nk] then seen[nk] = depth + 1; queue[#queue + 1] = nk end
                    end
                end
            end
        end
    end

    for key in pairs(isLand) do
        local cc, rr = string.match(key, '(%d+),(%d+)')
        plan[tonumber(rr) * W + tonumber(cc)] = landType
    end
    return plan, stats
end

--- 落地形；返回 改动数, 本来就对, 失败数
local function paint_terrain(plan, W, H, say)
    local changed, same, failed, processed = 0, 0, 0, 0
    for row = 0, H - 1 do
        for col = 0, W - 1 do
            local target = plan[row * W + col]
            if target ~= nil then
                if map.GetTerrain(col, row) == target then
                    same = same + 1
                else
                    local r = map.SetTerrain(col, row, target)
                    if r and r.Success then changed = changed + 1 else failed = failed + 1 end
                end
            end
            processed = processed + 1
            if processed % 5000 == 0 then
                say(string.format('   …地形处理 %d / %d 格', processed, W * H))
            end
        end
    end
    return changed, same, failed
end

-- ========== 7. 主流程 ==========

local function main()
    local lines = {}
    local function say(s) print(s); lines[#lines + 1] = tostring(s) end

    say('╔════════════════════════════════════════════════════════════╗')
    say('║  建图工具 build-map.lua                                    ║')
    say('╚════════════════════════════════════════════════════════════╝')

    local specText, specPath = read_first({ SPEC, 'publish/' .. SPEC, '../' .. SPEC })
    if not specText then
        say('✗ 读不到数据文件：' .. SPEC)
        say('  ' .. (has_io and '当前目录下找不到该文件；确认路径是否相对 exe 所在目录。'
                          or '当前 Lua 沙箱没有 io 模块，无法读取数据文件。'))
        return
    end
    say(string.format('数据文件：%s', specPath))

    local spec, err = parse_spec(specText)
    if not spec then say('✗ 解析失败：' .. err); return end

    local W, H = map.Width, map.Height
    local m = spec.map or {}
    local dryRun = (m.dry_run ~= false)                 -- 默认干跑
    local overwrite = (m.overwrite == true)
    -- 在当前地图上重跑时，先清掉旧城市建筑再按新坐标重建，避免地形变化后重复/错位
    local clearCities = (m.clear_cities == true)
    local belongRadius = tonumber(m.belong_radius) or 0
    local printLimit = tonumber(m.print_limit) or 80
    local skipSea = (m.skip_sea ~= false)
    -- >0 时，落在海里的城市会在半径内找最近陆地格并吸附（掩码精度不够时很实用）
    local snapSea = tonumber(m.snap_sea_radius) or 0

    say(string.format('地图尺寸：%d × %d　势力：%d 个　城市：%d 座　ref 标定点：%d 个',
        W, H, (function() local c = 0; for _ in pairs(spec.owners) do c = c + 1 end; return c end)(),
        #spec.cities, #spec.refs))
    say(string.format('模式：%s　归属半径：%d　干跑：%s　覆盖已有建筑：%s',
        dryRun and '干跑（不写数据）' or '落数据', belongRadius,
        tostring(dryRun), tostring(overwrite)))
    say('')

    -- ---- 7.1 投影 ----
    local fn, modeText, residuals = build_projection(spec, W, H)
    if not fn then say('✗ 投影失败：' .. tostring(modeText)); return end
    say('投影：' .. modeText)

    if #residuals > 0 then
        say('参考点残差（格子数，>1.5 就该重挑标定点）：')
        local worst = 0
        for _, r in ipairs(residuals) do
            say(string.format('   %-20s 实际(%3d,%3d) → 拟合(%3d,%3d)　偏差 %.2f',
                r.name ~= '' and r.name or '(未命名)', r.col, r.row, r.gc, r.gr, r.d))
            if r.d > worst then worst = r.d end
        end
        if worst > 1.5 then say(string.format('   ⚠ 最大偏差 %.2f 格，建议换更准的地标或改用 window 模式', worst)) end
        say('')
    end

    -- ---- 7.2 海陆地形（先算出计划；干跑时也用它做"落在海里"的判定）----
    local planned, terrainStats, terrainDesc = nil, nil, nil
    do
        local t = spec.terrain or {}
        if t.enabled then
            local mode = tostring(t.mode or 'mask')
            if mode == 'mask' then
                if not t.mask_file then
                    say('⚠ [terrain] mode=mask 需要 mask_file 指向掩码文件')
                else
                    local mask, hit = read_mask(tostring(t.mask_file))
                    if not mask then
                        say('⚠ 读不到掩码文件：' .. tostring(t.mask_file))
                    else
                        local landChar = tostring(t.land_char or 'X')
                        local smooth = tonumber(t.smooth) or 1
                        if smooth ~= 0 then mask = smooth_mask(mask, landChar) end
                        say(string.format('海陆掩码：%s（%d × %d%s）', hit, mask.w, mask.h,
                            smooth ~= 0 and '，已多数表决平滑' or ''))

                        local typeMask = nil
                        if t.type_mask_file then
                            typeMask = read_mask(tostring(t.type_mask_file))
                            if typeMask then
                                say(string.format('地形类型掩码：%s（%d × %d）',
                                    tostring(t.type_mask_file), typeMask.w, typeMask.h))
                            else
                                say('⚠ 读不到地形类型掩码：' .. tostring(t.type_mask_file) .. '（回落到 land_type）')
                            end
                        end

                        local maskAspect = mask.w / math.max(1, mask.h)
                        local mapAspect = (W - 1) / math.max(1, H - 1)
                        if maskAspect > mapAspect * 1.25 or maskAspect < mapAspect * 0.8 then
                            say(string.format('   ⚠ 掩码长宽比 %.2f 与地图的 %.2f 差得多，地形会被拉伸：建议按地图比例重画',
                                maskAspect, mapAspect))
                        end

                        planned, terrainStats, _ = plan_from_mask(mask, W, H, t, typeMask)
                        terrainDesc = 'mask（' .. tostring(hit) .. '）'
                    end
                end
            elseif mode == 'from_cities' then
                planned, terrainStats = plan_from_cities(spec, fn, W, H, t)
                terrainDesc = 'from_cities（按城市分布自动长陆地）'
            else
                say('⚠ [terrain] mode 只支持 mask / from_cities，当前是：' .. mode)
            end
        end
    end

    if planned then
        local seaType = tonumber((spec.terrain or {}).sea_type) or SEA_TERRAIN
        say(string.format('海陆计划：%s　陆 %d 格　海 %d 格　不动 %d 格　%s',
            terrainDesc, terrainStats.land, terrainStats.sea, terrainStats.keep,
            dryRun and '（干跑：只用于下面的校验，不会写进地图）' or '（落数据时会写入地形）'))
        if seaType ~= SEA_TERRAIN then
            say(string.format('   ⚠ sea_type=%d 不是 1：引擎判海洋看的就是 Tile1==1，写别的值不会被当成海', seaType))
        end
        say('')
    end

    --- 校验统一走这里：有计划就用计划值（干跑时等价于"铺完地形后的样子"），否则读地图实际地形
    local function getTerrain(col, row)
        if planned then
            local v = planned[row * W + col]
            if v ~= nil then return v end
        end
        return map.GetTerrain(col, row)
    end

    -- ---- 7.3 逐城市换算 + 校验 ----
    local rows, seen, problems = {}, {}, {}
    for _, c in ipairs(spec.cities) do
        local col, row = to_hex(fn, c.lat, c.lon)
        local st, note = 'OK', ''
        local snapped = nil

        -- 沿海城市在粗掩码上常常差一格：在 snap_sea_radius 内找最近的、未被占用的陆地格
        if snapSea > 0 and col >= 0 and col < W and row >= 0 and row < H
            and getTerrain(col, row) == SEA_TERRAIN then
            for radius = 1, snapSea do
                local found = nil
                for dr = -radius, radius do
                    for dc = -radius, radius do
                        if math.max(math.abs(dc), math.abs(dr)) == radius then
                            local nc, nr = col + dc, row + dr
                            if nc >= 0 and nc < W and nr >= 0 and nr < H
                                and getTerrain(nc, nr) ~= SEA_TERRAIN
                                and not seen[nc .. ',' .. nr] then
                                found = { nc, nr }
                                break
                            end
                        end
                    end
                    if found then break end
                end
                if found then
                    snapped = { col, row }
                    col, row = found[1], found[2]
                    break
                end
            end
        end

        if snapped then
            note = string.format('吸附自 (%d,%d)', snapped[1], snapped[2])
        end

        if col < 0 or col >= W or row < 0 or row >= H then
            st, note = '越界', string.format('算出 (%d,%d) 超出地图', col, row)
        elseif seen[col .. ',' .. row] then
            -- 两个城市被吸附/投影到同一格：在附近找一格空闲陆地，别把第二座城丢掉
            local found = nil
            for radius = 1, 3 do
                for dr = -radius, radius do
                    for dc = -radius, radius do
                        if math.max(math.abs(dc), math.abs(dr)) == radius then
                            local nc, nr = col + dc, row + dr
                            if nc >= 0 and nc < W and nr >= 0 and nr < H
                                and getTerrain(nc, nr) ~= SEA_TERRAIN
                                and not seen[nc .. ',' .. nr] then
                                found = { nc, nr }
                                break
                            end
                        end
                    end
                    if found then break end
                end
                if found then break end
            end

            if found then
                note = string.format('避让重复格 (%d,%d) → (%d,%d)', col, row, found[1], found[2])
                col, row = found[1], found[2]
            else
                st, note = '重复格', '已被 ' .. seen[col .. ',' .. row] .. ' 占用'
            end
        else
            local terrain = getTerrain(col, row)
            if terrain == SEA_TERRAIN then
                st, note = '落在海里', string.format('地形=%d', terrain)
            else
                local exist = map.GetBuilding(col, row)
                -- clear_cities 模式下旧城市会被统一删除，所以这里不要把"已有建筑"当成跳过理由
                if exist and not overwrite and not clearCities then
                    st, note = '已有建筑', tostring(exist.BuildingTypeName or exist.BuildingType)
                end
            end
        end

        -- 这一格到底要不要落下去
        local wantIt = (st == 'OK')
            or (st == '已有建筑' and overwrite)
            or (st == '落在海里' and not skipSea)

        if wantIt then seen[col .. ',' .. row] = c.name end

        rows[#rows + 1] = { c = c, col = col, row = row, st = st, note = note, skip = not wantIt }
        if not wantIt then
            problems[#problems + 1] = string.format('%s(%d,%d)：%s', c.name, col, row, note)
        end
    end

    -- ---- 7.3 势力 → 军团索引 ----
    local countryIds, ownerOf = {}, {}

    -- [belong] mode=mask：先把归属掩码里的国家ID并进来，一起解析军团索引
    local belongSpec = spec.belong or {}
    local ownerMask, ownerMaskPath, ownerMap, ownerMapPath = nil, nil, nil, nil
    if belongSpec.enabled and tostring(belongSpec.mode or 'mask') == 'mask' then
        local maskPath = tostring(belongSpec.mask_file or '')
        if maskPath == '' then
            say('⚠ [belong] mode=mask 需要 mask_file')
        else
            ownerMask, ownerMaskPath = read_mask(maskPath)
            if not ownerMask then
                say('⚠ 读不到归属掩码：' .. maskPath)
            else
                local mapPath = tostring(belongSpec.map_file or (maskPath .. '.map'))
                ownerMap, ownerMapPath = read_owner_map(mapPath)
                if not ownerMap then
                    say('⚠ 读不到归属字符映射：' .. mapPath)
                else
                    local mapCount = 0
                    for _ in pairs(ownerMap) do mapCount = mapCount + 1 end
                    say(string.format('归属掩码：%s（%d×%d）　字符映射：%s（%d 条）',
                        ownerMaskPath or maskPath, ownerMask.w, ownerMask.h, ownerMapPath or mapPath, mapCount))
                    for _, cid in pairs(ownerMap) do
                        if not ownerOf[cid] then ownerOf[cid] = true; countryIds[#countryIds + 1] = cid end
                    end
                end
            end
        end
    end

    for _, r in ipairs(rows) do
        local cid = spec.owners[r.c.owner]
        r.cid = cid
        if cid then
            if not ownerOf[cid] then ownerOf[cid] = true; countryIds[#countryIds + 1] = cid end
        else
            r.st = '势力未定义'
            r.note = '[owners] 里没有 ' .. tostring(r.c.owner)
            problems[#problems + 1] = r.c.name .. '：势力 ' .. tostring(r.c.owner) .. ' 未在 [owners] 定义'
        end
    end

    local indexOf, missingCids, direct = resolve_legion_indices(countryIds)
    say(string.format('军团总数：%d　归属解析：%s', map.LegionCount, direct and '直读国家ID' or '只写探针（老 exe）'))
    if #missingCids > 0 then
        say(string.format('⚠ 有 %d 个国家ID 在地图军团里不存在（这些城市不会写归属，先建军团再跑）：', #missingCids))
        for _, cid in ipairs(missingCids) do
            local names = {}
            for _, r in ipairs(rows) do
                if r.cid == cid then names[#names + 1] = r.c.name end
            end
            say(string.format('   国家ID=%-4d → %s', cid, table.concat(names, '、')))
        end
    end
    say('')

    -- ---- 7.3b 归属掩码：先把国家领土铺开，城市格随后会覆盖 ----
    local belongApplied, belongFailed, belongSkipped = 0, 0, 0
    local function applyBelong(write)
        if not ownerMask or not ownerMap then return end

        local landOnly = (belongSpec.land_only ~= false)
        local dW, dH = math.max(1, W - 1), math.max(1, H - 1)

        for col = 0, W - 1 do
            for row = 0, H - 1 do
                local r0 = row + (((col % 2) == 1) and 0.5 or 0)
                local mx = math.floor(col / dW * ownerMask.w) + 1
                local my = math.floor(r0 / dH * ownerMask.h) + 1
                if mx > ownerMask.w then mx = ownerMask.w end
                if my > ownerMask.h then my = ownerMask.h end

                local ch = string.sub(ownerMask.rows[my] or '', mx, mx)
                local cid = ownerMap[ch]
                if cid ~= nil then
                    local idx = indexOf[cid]
                    if idx == nil then
                        belongSkipped = belongSkipped + 1
                    elseif landOnly and getTerrain(col, row) == SEA_TERRAIN then
                        belongSkipped = belongSkipped + 1
                    elseif write then
                        local res = map.SetBelong(col, row, idx)
                        if res and res.Success then belongApplied = belongApplied + 1
                        else belongFailed = belongFailed + 1 end
                    else
                        belongApplied = belongApplied + 1
                    end
                end
            end
        end

        say(string.format('%s：%d 格，失败 %d 格，跳过 %d 格%s',
            write and '归属掩码写入' or '归属掩码预演',
            belongApplied, belongFailed, belongSkipped,
            landOnly and '（跳过海面与未知军团）' or ''))
        say('')
    end

    -- ---- 7.4 报表 ----
    local report = {}
    report[#report + 1] = string.format('# 建图报表　%s　地图 %d×%d', tostring(m.name or specPath), W, H)
    report[#report + 1] = string.format('# 模式=%s　城市=%d　干跑=%s', modeText, #rows, tostring(dryRun))
    report[#report + 1] = '# 名称\t势力\t国家ID\t军团索引\t纬度\t经度\t列\t行\t状态'
    for _, r in ipairs(rows) do
        report[#report + 1] = string.format('%s\t%s\t%s\t%s\t%.4f\t%.4f\t%d\t%d\t%s%s',
            r.c.name, r.c.owner, tostring(r.cid or '-'), tostring(indexOf[r.cid] or '-'),
            r.c.lat, r.c.lon, r.col, r.row, r.st, r.note ~= '' and ('（' .. r.note .. '）') or '')
    end

    say(string.format('城市清单（前 %d 行，完整表见报表文件）：', math.min(printLimit, #rows)))
    say(string.format('  %-14s %-8s %-6s %-8s %s', '名称', '势力', '索引', '格子', '状态'))
    for i = 1, math.min(printLimit, #rows) do
        local r = rows[i]
        say(string.format('  %-14s %-8s %-6s (%3d,%3d) %s%s',
            r.c.name, r.c.owner, tostring(indexOf[r.cid] or '-'), r.col, r.row,
            r.st, r.note ~= '' and (' — ' .. r.note) or ''))
    end
    if #rows > printLimit then say(string.format('  … 其余 %d 行见报表文件', #rows - printLimit)) end
    say('')

    if #problems > 0 then
        say(string.format('⚠ 有 %d 处需要处理：', #problems))
        for i = 1, math.min(40, #problems) do say('   ' .. problems[i]) end
        if #problems > 40 then say(string.format('   … 其余 %d 条见报表文件', #problems - 40)) end
        say('')
    end

    -- ---- 7.5 写数据 ----
    local built, named, belonged, failed = 0, 0, 0, 0
    if dryRun then
        applyBelong(false)
        say('★ 干跑结束：没有改任何数据。确认坐标无误后，把数据文件 [map] 里的 dry_run 改成 false 再跑。')
    else
        -- ---- 先铺海陆地形，再铺归属，最后落城市（城市是落在已定型的陆地上）----
        local tBuild = os.clock()
        local tPhase = os.clock()
        if planned then
            say('开始写海陆地形…')
            local changed, same, failed = paint_terrain(planned, W, H, say)
            say(string.format('地形写入完成：改动 %d 格，原本就对 %d 格，失败 %d 格', changed, same, failed))
            say(string.format('阶段耗时（地形）：%.2fs', os.clock() - tPhase))
            say('')
        end

        tPhase = os.clock()
        applyBelong(true)
        say(string.format('阶段耗时（归属）：%.2fs', os.clock() - tPhase))

        if clearCities then
            local old = {}
            map.ForEachBuilding(function(b)
                if b.BuildingType >= 11 and b.BuildingType <= 15 then
                    old[#old + 1] = { b.Col, b.Row }
                end
            end)

            local removed = 0
            for _, p in ipairs(old) do
                local res = map.RemoveBuilding(p[1], p[2])
                if res and res.Success then removed = removed + 1 end
            end
            say(string.format('清除旧城市建筑：%d 座', removed))
            say('')
        end

        for _, r in ipairs(rows) do
            if not r.skip and r.st ~= '越界' and r.st ~= '重复格' and r.st ~= '势力未定义' then
                local level = math.max(1, math.min(5, r.c.level or 3))
                local res = map.AddBuilding(r.col, r.row, CITY_TYPE_BASE + level, r.c.name)
                if res and res.Success then
                    built = built + 1
                    local b = map.GetBuilding(r.col, r.row)
                    if b and b.HasName then named = named + 1
                    else problems[#problems + 1] = r.c.name .. '：建筑已建但名字没写进 stringtable' end
                else
                    failed = failed + 1
                    problems[#problems + 1] = string.format('%s(%d,%d)：落建筑失败 %s',
                        r.c.name, r.col, r.row, (res and res.Message) or '')
                end

                local idx = indexOf[r.cid]
                if idx then
                    local cells = { r.col .. ',' .. r.row }
                    if belongRadius > 0 then
                        local seenCell, queue, head = { [r.col .. ',' .. r.row] = 0 }, { r.col .. ',' .. r.row }, 1
                        while head <= #queue do
                            local curKey = queue[head]; head = head + 1
                            local depth = seenCell[curKey]
                            if depth < belongRadius then
                                local cc, rr = string.match(curKey, '(%d+),(%d+)')
                                for _, nk in ipairs(neighbors(tonumber(cc), tonumber(rr), W, H)) do
                                    if not seenCell[nk] then
                                        seenCell[nk] = depth + 1
                                        queue[#queue + 1] = nk
                                        cells[#cells + 1] = nk
                                    end
                                end
                            end
                        end
                    end

                    local okSet = 0
                    for _, key in ipairs(cells) do
                        local cc, rr = string.match(key, '(%d+),(%d+)')
                        cc, rr = tonumber(cc), tonumber(rr)
                        if not (skipSea and getTerrain(cc, rr) == SEA_TERRAIN) then
                            local res2 = map.SetBelong(cc, rr, idx)
                            if res2 and res2.Success then okSet = okSet + 1 end
                        end
                    end
                    if okSet > 0 then belonged = belonged + 1 end
                end
            end
        end

        -- ---- 7.6 后处理 ----
        local post = spec.post or {}
        if post.create_coast then
            local res = map.CreateCoast()
            say('后处理 海岸线：' .. ((res and res.Message) or '已执行'))
        end
        if post.process_ocean_layer2 then
            local res = map.ProcessOceanSecondLayer()
            say('后处理 海洋第二层：' .. ((res and res.Message) or '已执行'))
        end
        if post.connect_buildings then
            local res = map.ConnectBuildings()
            say('后处理 建筑连线：' .. ((res and res.Message) or '已执行'))
        end
        if post.greening then
            local prob = tonumber(post.greening)
            if prob and prob > 0 then
                local res = map.Greening(prob)
                say('后处理 绿化：' .. ((res and res.Message) or '已执行'))
            end
        end

        say('')
        say(string.format('落建筑 %d 座（其中带名字 %d 座），写归属 %d 座，失败 %d 座',
            built, named, belonged, failed))
        say(string.format('总耗时：%.2fs', os.clock() - tBuild))
        say('★ 完成。Ctrl+Z 可整体撤销；记得保存地图。')
    end

    -- ---- 7.7 报表落盘 ----
    local reportPath = write_first(REPORT_CANDIDATES, table.concat(report, '\r\n') .. '\r\n')
    say(reportPath and ('报表已写出：' .. reportPath) or '⚠ 报表写文件失败，上面的清单请直接复制。')
end

main()

