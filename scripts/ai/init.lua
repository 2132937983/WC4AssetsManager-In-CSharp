--[[============================================================================
  scripts/ai/init.lua —— AI 地图编辑工具箱聚合入口
  ----------------------------------------------------------------------------
  用法：
      local ai = require('ai')
      ai.inspect.audit()

  模块：
      ai.core      参数/区域/字段/统计等公共小工具（一般不用直接调）
      ai.inspect   巡检：概况、分布、异常
      ai.building  建筑
      ai.army      单位
      ai.legion    军团（索引 = 归属值）
      ai.terrain   地形 / 归属 / 省区
      ai.record    事件 / 方针 / 天气 / 战略建设 / 空袭 / 空中支援 / 援军
      ai.help()    打印速查表

  设计原则：
    · 所有写操作都转发到 map.*（引擎侧再走 Modifier 校验），不直接改快照；
    · 批量操作统一支持 dry_run，先看命中再动手；
    · 成功/失败逐条计数，失败只报首个原因，避免刷屏；
    · 一次执行 = 一个撤销单元，Ctrl+Z 可整批回退。
============================================================================]]

local ai = {}

ai.VERSION = '1.0'

ai.core = require('ai.lib.core')
ai.inspect = require('ai.lib.inspect')
ai.building = require('ai.lib.building')
ai.army = require('ai.lib.army')
ai.legion = require('ai.lib.legion')
ai.terrain = require('ai.lib.terrain')
ai.record = require('ai.lib.record')
ai.mapgen = require('ai.lib.mapgen')

-- 逐格遍历（引擎没有 map.ForEachCell）
ai.each_cell = ai.core.each_cell

ai.help = require('ai.lib.help').print

--- 地图截图（AI 视觉）：mode='full'|'region'|'scene'
---   ai.shot()                                          整幅
---   ai.shot{ mode='region', col=120, row=78, w=40, h=30 }
---   ai.shot{ mode='scene' }                            整个编辑器窗口
function ai.shot(o)
    o = ai.core.params(o)
    local core = ai.core
    local mode = string.lower(tostring(core.opt(o, 'mode', 'full')))
    local path = core.opt(o, 'path', '')
    local max_pixels = core.to_int(core.opt(o, 'max_pixels', 1400), 1400)
    local labels = core.switch(core.opt(o, 'show_labels', true), true)

    local r
    if mode == 'scene' then
        r = map.CaptureScreenshot(path, -1, -1, 0, 0, max_pixels, labels, false, true)
    elseif mode == 'region' then
        r = map.CaptureScreenshot(path,
            core.to_int(core.need(o, 'col'), 0), core.to_int(core.need(o, 'row'), 0),
            core.to_int(core.opt(o, 'w', 40), 40), core.to_int(core.opt(o, 'h', 30), 30),
            max_pixels, labels, false)
    else
        r = map.CaptureScreenshot(path, -1, -1, 0, 0, max_pixels, labels, false)
    end

    print('[截图] ' .. tostring(r.Message))
    return r
end

return ai
