--[[================================================================================
  取图工具 capture-map.lua —— 把地图（整幅或局部）渲染成 PNG，交给 AI"看"
  --------------------------------------------------------------------------------
  为什么要有它：
    AI 只能读文字，判断"地形对不对、势力分得对不对、城市摆得像不像"必须看图。
    这个脚本是取图那一环：调用引擎的截图接口（map.CaptureScreenshot）落一张 PNG，
    并把"图像 ↔ 格子"的换算关系打印出来 —— 模型拿到图后才知道图上某处是第几格。

  用法（控制台 F3）：
      lua scripts/ai/capture-map.lua

  改下面的参数来看不同东西：
      MODE = 'full'    整幅地图（看全局：大陆轮廓、势力分布）
      MODE = 'region'  局部放大（看细节：某片城市群、某段海岸线）
                       —— 区域越小，格子在图上越大，模型越看得清
      MODE = 'scene'   整个编辑器窗口（界面截图：工具栏/面板/视口/状态栏）
                       —— 不看地图数据、要查"界面上是什么样"时用它

  输出：PNG 路径 + 像素尺寸 + 覆盖的格子范围 + 每格像素数（这些都会打在控制台，
        也会作为脚本返回值里的消息，AI 工具能直接读到）。

  注意：本脚本**不改任何数据**，只出图；就算跑错也不会弄坏地图。
================================================================================]]

-- ========== 参数 ==========
local MODE       = 'full'          -- 'full' = 整幅；'region' = 局部；'scene' = 整个编辑器窗口（界面）
local OUT        = ''              -- 输出路径（空 = 自动放到 exe 目录的 screenshots/ 下）
local COL, ROW   = 120, 78         -- 局部模式的中心格
local CELLS_W    = 48              -- 局部模式的宽（格）
local CELLS_H    = 36              -- 局部模式的高（格）
local MAX_PIXELS = 1400            -- 长边像素上限：越小越省视觉 token，越大越清晰
local LABELS     = true            -- 画坐标标注（强烈建议开：模型靠它定位）
local GRID       = false           -- 画六角网格线

-- ========== 执行 ==========
print('╔════════════════════════════════════════════════════════════╗')
print('║  取图工具：把地图渲染成 PNG 供 AI 查看                     ║')
print('╚════════════════════════════════════════════════════════════╝')

local result
if MODE == 'scene' then
    print('模式：整个编辑器窗口（界面截图，含工具栏/面板/视口/状态栏）')
    -- 第 9 个参数 true = 场景模式（区域参数会被忽略；窗口最小化时会失败）
    result = map.CaptureScreenshot(OUT, -1, -1, 0, 0, MAX_PIXELS, LABELS, GRID, true)
elseif MODE == 'region' then
    print(string.format('模式：局部放大　中心 (%d,%d)　大小 %d×%d 格', COL, ROW, CELLS_W, CELLS_H))
    result = map.CaptureScreenshot(OUT, COL, ROW, CELLS_W, CELLS_H, MAX_PIXELS, LABELS, GRID)
else
    print('模式：整幅地图')
    result = map.CaptureScreenshot(OUT, -1, -1, 0, 0, MAX_PIXELS, LABELS, GRID)
end

if not result or not result.Success then
    print('✗ 截图失败：' .. ((result and result.Message) or '接口不可用（可能是没有 GUI 场景）'))
    return
end

print('')
print(result.Message)
print('')
print('给 AI 的提示：')
print('  · 图上的文字标注就是格子坐标（列,行），可直接对应用户/工具里说的坐标')
print('  · 若需要看更细的地方，把本脚本 MODE 改成 region 并调小 CELLS_W/CELLS_H 再跑一次')
print('  · 这张图只反映渲染结果（地形/海岸/城市/势力着色），不代表数据本身是否合法')
print('★ 完成。')
