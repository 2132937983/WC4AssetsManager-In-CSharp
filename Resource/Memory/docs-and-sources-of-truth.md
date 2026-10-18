# docs-and-sources-of-truth

> 保存时间: 2026-09-21（人工整理，非工具写入）

> 摘要: 仓库自带文档索引 + "能力来源只有一处"的铁律（加命令/加 Lua 接口该改哪、必须同步改文档）+ 记忆体系用法。答用户"有没有某个命令/接口"前先查这里。

---

## 1. 仓库自带文档（比记忆更权威，先看它们）

`docs/README.md` 是索引，其中三份是**使用类**，内容与源码同步维护：

| 文档 | 内容 | 什么时候看 |
|---|---|---|
| `docs/cli-commands.md` | 控制台命令完整参考：命令树、参数/选项、`--out` 输出重定向、**归属值语义** | 想知道"有没有某个命令""参数怎么填" |
| `docs/zme-scripts.md` | `.zme` 命令脚本：文件格式、运行方式、执行语义、模板、程序化生成 | 要把固定流程批量跑 |
| `docs/lua-scripting.md` | Lua `map` 接口完整参考、对象字段、坑、示例；**加接口的三步走** | 要读数据再决定改什么 |

开发类：`docs/decoupling-plan.md`（模块解耦方案）、`docs/memory-optimization-plan.md`（内存优化方案）。

## 2. 铁律：能力来源只有一处

1. **命令**的能力来源只有 `WC4MapEditor.Core/Commands/CliCommandHost.cs` —— 加命令在那儿加，**并同步更新 `docs/cli-commands.md`**（第五节命令表是按源码提取的，不同步的话下次照文档写会失败）；
2. **Lua** 的能力来源只有 `WC4MapEditor.Scripting/IMapScriptApi.cs` —— 加接口按 `docs/lua-scripting.md` 第九节三步走（实现 `MapScriptApi` + 暴露到 `LuaMapApi` + 更新文档）；
3. **AI 工具**的能力来源只有 `WC4MapEditor.AI/Tools/AiToolRegistry.cs`（工具清单 + `Execute` 分派）。工具描述就是给模型看的说明书，改行为要连描述一起改。

推论：
- 用户问"有没有 xxx 功能" → 先在这三处（+ `docs/`）搜，**不要凭记忆答"有/没有"**；
- 用户要一个新能力 → 先判断它属于哪一类（命令 / Lua / AI 工具 / 纯 GUI 操作），放对位置，再考虑要不要顺手在另两处也暴露。

## 3. 记忆体系（`Resource/Memory/`）

- 位置：`Resource/Memory/<短横线英文名>.md`（`AiToolRegistry.GetMemoryDir` 从 exe 目录向上找 `*.csproj` / `setting.txt` 定位项目根）；
- AI 侧工具：`list_memories`（列全部 + 摘要）、`read_memory`、`save_memory`（**同名覆盖，旧版自动备份为 `.bak`**）、`delete_memory`；
- 准则（写在系统提示里）：**新会话先 `list_memories` 回顾**；用户表达偏好、总结出可复用流程、发现项目关键约定时主动 `save_memory`；只记跨会话仍有效的知识，不记"当前正在编辑哪个文件"这种临时状态；
- 文件格式（`save_memory` 生成的样子，手工写也照这个来）：

```markdown
# 标题-英文短横线

> 保存时间: yyyy-MM-dd HH:mm:ss

---

正文（Markdown）
```

现有记忆一览（同类知识请**更新**而不是新建）：

| 文件 | 内容 |
|---|---|
| `basic-concepts.md` | 领域基础：地图类型/文件、场景与编辑模式、实体与值语义、BTL 段结构 |
| `project-architecture.md` | 分层与依赖方向、能力注册模式、单例/DI、命令分发、构建验证 |
| `map-data-conventions.md` | 坐标/六边形/判海/省区/归属/建筑/单位 v1v3 的具体约定 |
| `screenshot-and-vision.md` | 两种截图与前提、绿字序号怎么关、坐标换算、AI 视觉限制 |
| `building-attributes.md` | **建筑每个字段的含义/取值范围**（类型表、设施上限、ValidAirDefenseWeapon 等） |
| `army-attributes.md` | **单位每个字段的含义**（兵种/政策/士气/军衔等枚举值、v1-v3 差异、HealthBonus=100 基准） |
| `legion-attributes.md` | **军团每个字段的含义**（经济/工业/科技默认值、科技等级族 54 项对应装备、派生平均等级） |
| `editor-shortcuts-and-pitfalls.md` | 快捷键、"命令为何只能在地图场景用"、Lua 范围、AI 边界、改代码习惯 |

## 4. 排查问题的固定思路（本项目大量 bug 是"能力存在但没接上"）

按这条链子从下往上找，别一上来就怀疑算法：

```
Modifier/Service 里有没有实现？
   → 有没有注册进 EditModeManager（RegisterModifier）？
      → 命令有没有加进 CliCommandHost（或 CommandManager 本地表）？
         → 场景有没有绑键 / 注册回调（RegisterBinding、Set*Callback）？
            → 跨模块的话，桥接有没有注册（AiBridge.*、EditModeManager.Set*）？
```

把"哪一层没接上"先定位清楚，再动代码；改完记得同步文档与记忆。
