# WC4MapEditor 文档索引

## 使用类（写给用编辑器的人）

| 文档 | 内容 | 什么时候看 |
|---|---|---|
| [CLI 指令手册](cli-commands.md) | 控制台命令的完整参考：命令树、参数/选项、`--out` 输出重定向、归属值语义 | 想知道"有没有某个命令""参数怎么填" |
| [.zme 指令脚本](zme-scripts.md) | 把一串命令存成脚本批量跑：文件格式、运行方式、执行语义、模板、程序化生成 | 要做固定流程的批量操作 |
| [Lua 脚本接口](lua-scripting.md) | 内置 Lua（MoonSharp）的 `map` 接口完整参考、对象字段、坑、示例 | 要读数据再决定改什么（循环/判断/统计） |

三者的关系：

```
控制台手打一条命令   ──┐
                       ├─ 同一套命令解析器（CliCommandHost）
.zme 脚本（一行一条）──┘
                        └─ 需要逻辑时 → Lua 脚本（也可以在里面发命令）
```

## 开发类（写给改编辑器的人）

| 文档 | 内容 |
|---|---|
| [decoupling-plan.md](decoupling-plan.md) | 模块解耦方案 |
| [memory-optimization-plan.md](memory-optimization-plan.md) | 内存优化方案 |

## 给新接手的人的三句话

1. **命令的能力来源只有一处**：`WC4MapEditor.Core/Commands/CliCommandHost.cs`。加命令就在那里加，然后同步更新 `cli-commands.md`。
2. **Lua 的能力来源只有一处**：`WC4MapEditor.Scripting/IMapScriptApi.cs`。加接口的三步走见 `lua-scripting.md` 第九节。
3. **文档与源码的一致性**：`cli-commands.md` 第五节的命令表是按源码提取的，改了命令请一并改文档（否则下次照着文档写会失败）。
