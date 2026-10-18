# 界面语言文件 / UI language files

每种语言一个 JSON 文件，文件名就是语言代码：`zh-CN.json`、`en-US.json`。
程序启动时按 `setting.txt` 的 `Language` 键加载；开始界面右侧的「文A」按钮可以切换并记住选择。

- 键名形如 `区域.条目`（`Common.Save`、`Layout.Title`）；`{0}` `{1}` 是运行时填入的参数，翻译时保留。
- 找不到的键会退回 `zh-CN.json`，再找不到就显示键名本身，不会报错。
- 允许 `//` 注释与末尾逗号；也可以写成嵌套对象，`{"Common": {"Save": "保存"}}` 与 `"Common.Save"` 等价。
- 新增一种语言：复制 `en-US.json` 为 `<code>.json`（如 `ja-JP.json`），改掉 `Language.Name`（菜单里显示的名字）和各条文本即可。

One JSON file per language, named by its language code. Loaded at startup according to the `Language`
key in `setting.txt`; the "文A" button on the start screen switches languages and remembers the choice.
Missing keys fall back to `zh-CN.json`, then to the key itself. To add a language, copy `en-US.json`
to `<code>.json`, change `Language.Name` and translate the values.
