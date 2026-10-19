using System.IO;
using System.Text.Json;
using WC4MapEditor.AI.Providers;
using WC4MapEditor.Core.Assets;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Helpers;
using WC4MapEditor.Core.Parsers.Country;

namespace WC4MapEditor.AI.Tools;

/// <summary>
/// AI 工具集：定义（JSON Schema）+ 执行。
/// <para>
/// 工具即"用户操作的等效入口"：
/// run_cli_command ≈ 用户在控制台敲命令；run_lua / run_lua_file ≈ 拖入脚本；
/// open_scene ≈ 点导航按钮。细粒度地图操作由 run_lua 内的全量 Lua API 承担。
/// </para>
/// </summary>
public static class AiToolRegistry
{
    public static IReadOnlyList<AiToolSpec> BuildTools()
    {
        var list = new List<AiToolSpec>
        {
            new()
            {
                Name = "get_map_info",
                Description = "获取当前地图基本信息：尺寸、版本、建筑数、单位数、军团数等。任何操作前先调用它了解地图。",
                ParametersJson = """{"type":"object","properties":{},"required":[]}"""
            },
            new()
            {
                Name = "open_scene",
                Description = "切换到指定的编辑场景（等效用户点击导航栏按钮）。编辑器各场景专注不同数据：地图地形/建筑/单位军团归属/事件等。",
                ParametersJson = """
{"type":"object","properties":{"scene":{"type":"string","description":"场景名，用 list_scenes 查询"}},"required":["scene"]}
""",
            },
            new()
            {
                Name = "list_scenes",
                Description = "列出所有可切换的编辑场景名。",
                ParametersJson = """{"type":"object","properties":{},"required":[]}"""
            },
            new()
            {
                Name = "open_map",
                Description = "打开地图文件（.btl 战役/征服 或 .bin/.dat 世界地图）并切换到对应编辑场景。路径可为绝对路径、相对路径或 assets 内相对路径（如 stage/stage10101.btl）。文件名以 conquest 开头按征服格式解析，其他 .btl 按战役格式解析。",
                ParametersJson = """
{"type":"object","properties":{"path":{"type":"string","description":"地图文件路径，如 stage/stage10101.btl 或 D:/maps/mymap.btl"}},"required":["path"]}
""",
            },
            new()
            {
                Name = "new_campaign",
                Description = "创建新的战役文件（.btl）并切换到战役编辑场景。战役是单关场景，包含地形/建筑/军团/单位等完整数据。",
                ParametersJson = """
{"type":"object","properties":{"width":{"type":"integer","description":"地图宽度（格子数），1-500"},"height":{"type":"integer","description":"地图高度（格子数），1-500"},"legions":{"type":"integer","description":"军团数量，默认2","default":2}},"required":["width","height"]}
""",
            },
            new()
            {
                Name = "new_conquest",
                Description = "创建新的征服文件（.btl）并切换到征服编辑场景。征服是基于世界地图的多关战役，需要指定地图编号。",
                ParametersJson = """
{"type":"object","properties":{"width":{"type":"integer","description":"地图宽度（格子数），1-500"},"height":{"type":"integer","description":"地图高度（格子数），1-500"},"legions":{"type":"integer","description":"军团数量，默认2","default":2},"map_number":{"type":"integer","description":"地图编号(1-255)，默认1","default":1}},"required":["width","height"]}
""",
            },
            new()
            {
                Name = "new_map",
                Description = "创建新的世界地图文件（.bin）并切换到地图编辑场景。世界地图是战略层大地图，用于征服模式的基础。",
                ParametersJson = """
{"type":"object","properties":{"width":{"type":"integer","description":"地图宽度（格子数），10-500"},"height":{"type":"integer","description":"地图高度（格子数），10-500"}},"required":["width","height"]}
""",
            },
            new()
            {
                Name = "run_cli_command",
                Description = "执行一条编辑器控制台命令并返回输出。可用于查询与修改：help 查看命令列表；常用如 goto、goto_building、list_buildings、scale_map、resize_map、create_coast、bg_province、geo_* 系列等。一条命令一个字符串（含参数）。",
                ParametersJson = """
{"type":"object","properties":{"command":{"type":"string","description":"完整命令行，如 'goto_building 10 5' 或 'help'"}},"required":["command"]}
""",
            },
            new()
            {
                Name = "run_lua",
                Description = "执行一段 Lua 代码操作地图。全局对象 map 暴露完整编辑 API：地形(GetTerrain/SetTerrain/FloodFillTerrain)、归属(GetBelong/SetBelong)、建筑(AddBuilding/ForEachBuilding/RemoveBuilding...)、单位(AddArmy/SetArmyPlan/GenerateArmiesByProbability...)、军团(AddLegion/SetLegionColor/SetLegionCampAt/SetLegionDefeatConditionAt/SetAllLegionsEconomy...)、工具(ExportHd/ConnectBuildings...)。用 print() 输出结果。整次执行可一次撤销。适合批量/复杂操作。",
                ParametersJson = """
{"type":"object","properties":{"code":{"type":"string","description":"Lua 源码"}},"required":["code"]}
""",
            },
            new()
            {
                Name = "run_lua_file",
                Description = "执行编辑器 scripts 目录下的一个 .lua 脚本文件（等效用户拖入脚本）。先 list_scripts 看有哪些。",
                ParametersJson = """
{"type":"object","properties":{"path":{"type":"string","description":"脚本相对路径，如 scripts/lua-example.lua"}},"required":["path"]}
""",
            },
            new()
            {
                Name = "list_scripts",
                Description = "列出编辑器 scripts 目录下可执行的 .lua 脚本文件。",
                ParametersJson = """{"type":"object","properties":{},"required":[]}"""
            },
            // -------- AssetManager (AM)：资源/图集 --------
            new()
            {
                Name = "list_assets",
                Description = "[AM] 列出 assets 目录下的资源条目（相对路径 + 大小字节）。可按扩展名/顶层目录/名称关键字过滤。结果最多 200 条。",
                ParametersJson = """
{"type":"object","properties":{"extension":{"type":"string","description":"扩展名过滤，如 json/png/ini，不带点"},"directory":{"type":"string","description":"顶层目录过滤，如 json/texture/sound"},"nameContains":{"type":"string","description":"文件名关键字（不区分大小写）"}},"required":[]}
""",
            },
            new()
            {
                Name = "read_asset_text",
                Description = "[AM] 读取 assets 中某个文本资源的内容（json/ini/xml/lua/txt 等）。按相对路径定位，如 'json/ConquerEventSettings.json'。返回内容截断到 maxChars（默认 8000）。",
                ParametersJson = """
{"type":"object","properties":{"path":{"type":"string","description":"相对 assets 根的路径，如 json/CountrySettings.json"},"maxChars":{"type":"integer","description":"返回内容最大字符数，默认 8000"}},"required":["path"]}
""",
            },
            new()
            {
                Name = "get_asset_summary",
                Description = "[AM] 获取 assets 目录概览：根路径、总文件数、按目录/类别的计数分布。了解资源整体结构时调用。",
                ParametersJson = """{"type":"object","properties":{},"required":[]}"""
            },
            // -------- ConfigManager (CM)：setting.txt / stringtable / 配置表 --------
            new()
            {
                Name = "get_config_text",
                Description = "[CM] 读取 setting.txt 中的键值（界面文本/参数配置）。键不存在返回 defaultValue。",
                ParametersJson = """
{"type":"object","properties":{"key":{"type":"string","description":"setting.txt 中的键名"},"defaultValue":{"type":"string","description":"键缺失时的回退值，默认空"}},"required":["key"]}
""",
            },
            new()
            {
                Name = "get_stringtable_value",
                Description = "[CM] 读取 stringtable_tw.ini 中的键值（国家名/城市名/事件名等游戏文本）。键如 country_8、battle_cityname_001、conquest_event_5。",
                ParametersJson = """
{"type":"object","properties":{"key":{"type":"string","description":"stringtable 键名"}},"required":["key"]}
""",
            },
            new()
            {
                Name = "list_stringtable_keys",
                Description = "[CM] 列出 stringtable_tw.ini 中以指定前缀开头的键（如 country_、battle_cityname_、conquest_event_）。省略前缀则列出全部键（最多 500 个）。",
                ParametersJson = """
{"type":"object","properties":{"prefix":{"type":"string","description":"键前缀过滤，如 country_"}},"required":[]}
""",
            },
            new()
            {
                Name = "get_terrain_types",
                Description = "[CM] 获取地形类型表：地形ID → 名称。修改地形前用它了解可用类型。",
                ParametersJson = """{"type":"object","properties":{},"required":[]}"""
            },
            // -------- 查询工具：地图数据 --------
            new()
            {
                Name = "get_building_list",
                Description = "列出当前地图上的建筑：坐标、类型、城市名ID、工厂/研究所/医疗等等级。可按坐标范围过滤。",
                ParametersJson = """
{"type":"object","properties":{"maxCount":{"type":"integer","description":"最多返回条数，默认 100"}},"required":[]}
""",
            },
            new()
            {
                Name = "get_legion_list",
                Description = "列出当前地图上的军团：国家ID、阵营、是否玩家控制、初始经济/工业/科技、颜色等概要。",
                ParametersJson = """{"type":"object","properties":{},"required":[]}"""
            },
            new()
            {
                Name = "get_army_list",
                Description = "列出当前地图上的单位/部队：坐标、类型、等级、军团ID、经验等。可限制返回条数。",
                ParametersJson = """
{"type":"object","properties":{"maxCount":{"type":"integer","description":"最多返回条数，默认 100"}},"required":[]}
""",
            },
            new()
            {
                Name = "get_country_list",
                Description = "列出所有国家：ID、名称（取 stringtable）、是否玩家控制等。修改归属/军团前用它了解国家。",
                ParametersJson = """{"type":"object","properties":{},"required":[]}"""
            },
            new()
            {
                Name = "search_stringtable",
                Description = "[CM] 按关键字搜索 stringtable_tw.ini 的键值（值包含关键字即匹配，不区分大小写）。用于查找国家名/事件名/城市名对应的键。",
                ParametersJson = """
{"type":"object","properties":{"keyword":{"type":"string","description":"搜索关键字（值中包含此字符串即匹配）"},"maxCount":{"type":"integer","description":"最多返回条数，默认 50"}},"required":["keyword"]}
""",
            },
            // -------- 文件操作工具 --------
            new()
            {
                Name = "read_file",
                Description = "读取项目根目录下的文本文件内容。路径可为绝对路径或相对路径（相对于项目根目录，如 setting.txt、Resource/config.json）。自动尝试多种基目录解析。",
                ParametersJson = """
{"type":"object","properties":{"path":{"type":"string","description":"文件路径，如 setting.txt 或 Resource/xxx.json"},"maxChars":{"type":"integer","description":"最多返回字符数，默认 20000"}},"required":["path"]}
""",
            },
            new()
            {
                Name = "write_file",
                Description = "写入/修改项目根目录下的文本文件。路径同 read_file。覆盖前自动创建 .bak 备份。",
                ParametersJson = """
{"type":"object","properties":{"path":{"type":"string","description":"文件路径"},"content":{"type":"string","description":"要写入的完整内容"}},"required":["path","content"]}
""",
            },
            new()
            {
                Name = "list_dir",
                Description = "列出项目根目录下某目录的文件和子目录。路径同 read_file。省略 path 则列出项目根目录。",
                ParametersJson = """
{"type":"object","properties":{"path":{"type":"string","description":"目录路径，省略则列项目根目录"}},"required":[]}
""",
            },
            // -------- 记忆工具：Memory --------
            new()
            {
                Name = "save_memory",
                Description = "将重要信息保存为 .md 记忆文件，存入 Resource/Memory/ 目录。用于持久化跨会话的关键知识：用户偏好、常用操作流程、地图编辑经验、项目约定等。文件名自动加 .md 后缀，同名覆盖（先备份）。",
                ParametersJson = """
{"type":"object","properties":{"title":{"type":"string","description":"记忆标题/文件名（不含扩展名），如 'user-preferences' 或 'map-edit-workflow'，用短横线分隔单词"},"content":{"type":"string","description":"记忆内容（Markdown 格式）"}},"required":["title","content"]}
""",
            },
            new()
            {
                Name = "read_memory",
                Description = "读取一条 .md 记忆文件的内容。先用 list_memories 查看有哪些记忆。",
                ParametersJson = """
{"type":"object","properties":{"title":{"type":"string","description":"记忆标题/文件名（不含扩展名）"}},"required":["title"]}
""",
            },
            new()
            {
                Name = "list_memories",
                Description = "列出 Resource/Memory/ 目录下所有 .md 记忆文件：文件名 + 首行摘要 + 修改时间。新会话开始时调用此工具回顾已有记忆。",
                ParametersJson = """{"type":"object","properties":{},"required":[]}"""
            },
            new()
            {
                Name = "delete_memory",
                Description = "删除一条 .md 记忆文件。删除前确认内容已过时或不再需要。",
                ParametersJson = """
{"type":"object","properties":{"title":{"type":"string","description":"记忆标题/文件名（不含扩展名）"}},"required":["title"]}
""",
            },
            // -------- 地图对象属性读写（建筑 / 单位 / 军团）--------
            // 实现上转成控制台命令执行，复用 CLI 里那份字段白名单与校验，避免两套口径。
            new()
            {
                Name = "list_object_fields",
                Description = "列出某种地图对象上可改写的属性字段名。改属性前先调它，不要猜字段名。",
                ParametersJson = """
{"type":"object","properties":{"kind":{"type":"string","description":"building=建筑；army=单位；legion=军团"}},"required":["kind"]}
""",
            },
            new()
            {
                Name = "get_object",
                Description = "查看某个地图对象的全部属性（人可读清单）。建筑/单位按坐标定位；军团按列表索引定位（索引就是归属值）。"
                            + "要批量或按条件处理时，改用 run_lua 里的 map.ForEachArmy / ForEachBuilding / ForEachLegion。",
                ParametersJson = """
{"type":"object","properties":{"kind":{"type":"string","description":"building / army / legion"},"col":{"type":"integer","description":"列（building/army 用）"},"row":{"type":"integer","description":"行（building/army 用）"},"index":{"type":"integer","description":"军团列表索引（legion 用，等于归属值）"}},"required":["kind"]}
""",
            },
            new()
            {
                Name = "set_object_field",
                Description = "改某个地图对象的一个属性。字段名先用 list_object_fields 查。"
                            + "军团改 CountryId / ActionId 会自动走带唯一性校验的专用命令，直接填字段名即可。",
                ParametersJson = """
{"type":"object","properties":{"kind":{"type":"string","description":"building / army / legion"},"field":{"type":"string","description":"字段名，如 FactoryLevel / Level / InitialEconomy"},"value":{"type":"number","description":"数值（按字段宽度自动钳位）"},"col":{"type":"integer","description":"列（building/army 用）"},"row":{"type":"integer","description":"行（building/army 用）"},"index":{"type":"integer","description":"军团列表索引（legion 用）"}},"required":["kind","field","value"]}
""",
            },
            new()
            {
                Name = "set_object_field_batch",
                Description = "批量改同一类对象的同一个字段，可用区域/类型/归属/等级/索引区间过滤"
                            + "（例如：把某片矩形内所有城市升到 15 级、把归属 2 的单位等级加 1）。"
                            + "内部生成 Lua 执行，整批可一次 Ctrl+Z 撤销；不需要模型写代码。"
                            + "过滤器不足以表达条件（如需要看邻居、需要多字段联动）时才改用 run_lua。",
                ParametersJson = """
{"type":"object","properties":{"kind":{"type":"string","description":"building / army / legion"},"field":{"type":"string","description":"字段名（先用 list_object_fields 查）"},"value":{"type":"number","description":"目标值；op=add 时表示增量"},"op":{"type":"string","description":"set=直接赋值（默认）；add=在现有值上加 value"},"col1":{"type":"integer","description":"区域左上列（可选）"},"row1":{"type":"integer","description":"区域左上行（可选）"},"col2":{"type":"integer","description":"区域右下列（可选）"},"row2":{"type":"integer","description":"区域右下行（可选）"},"building_type":{"type":"integer","description":"只处理该类型的建筑（可选）"},"belong":{"type":"integer","description":"只处理归属值为它的建筑（可选）"},"has_name":{"type":"boolean","description":"true=只处理有名称的建筑；false=只处理无名称（可选）"},"legion":{"type":"integer","description":"只处理属于该军团的单位（可选）"},"level_min":{"type":"integer","description":"单位等级下限（可选）"},"level_max":{"type":"integer","description":"单位等级上限（可选）"},"index_from":{"type":"integer","description":"军团索引起点（可选，默认 0）"},"index_to":{"type":"integer","description":"军团索引终点（可选，默认最后一个）"}},"required":["kind","field","value"]}
""",
            },
            new()
            {
                Name = "capture_screenshot",
                Description = "把地图（整幅或局部）渲染成图片，随后你会直接看到这张图。"
                            + "要判断地形/海岸线/势力分布/城市摆放是否合理时用它，不要凭空猜。"
                            + "局部图更清晰：想看某片区域就传 mode=region 加 col/row 与 w/h。"
                            + "图上带坐标标注，可直接读出格子号。"
                            + "想在界面上排查问题（工具栏、编辑面板、视口、报错对话框）就用 mode=scene，"
                            + "它截的是整个编辑器窗口而不是地图数据。",
                ParametersJson = """
{"type":"object","properties":{"mode":{"type":"string","description":"full=整幅地图；region=以 col,row 为中心的局部放大图；scene=整个编辑器窗口界面（不含地图数据语义，界面类问题用它）"},"col":{"type":"integer","description":"局部模式的中心列"},"row":{"type":"integer","description":"局部模式的中心行"},"w":{"type":"integer","description":"局部模式的宽（格），如 40"},"h":{"type":"integer","description":"局部模式的高（格），如 30"},"max_pixels":{"type":"integer","description":"长边像素上限，默认 1200（越小越省 token）"},"show_labels":{"type":"boolean","description":"是否画坐标标注，默认 true"}},"required":["mode"]}
""",
            },
        };

        return list;
    }

    /// <summary>执行一个工具调用，返回给 AI 的结果文本。</summary>
    public static string Execute(string toolName, string argumentsJson)
    {
        var args = ParseArgs(argumentsJson);

        try
        {
            switch (toolName)
            {
                case "get_map_info": return ToolGetMapInfo();
                case "open_scene": return ToolOpenScene(args);
                case "list_scenes": return ToolListScenes();
                case "open_map": return ToolOpenMap(args);
                case "new_campaign": return ToolNewCampaign(args);
                case "new_conquest": return ToolNewConquest(args);
                case "new_map": return ToolNewMap(args);
                case "run_cli_command": return ToolRunCommand(args);
                case "run_lua": return ToolRunLua(args);
                case "run_lua_file": return ToolRunLuaFile(args);
                case "list_scripts": return ToolListScripts();
                // AM
                case "list_assets": return ToolListAssets(args);
                case "read_asset_text": return ToolReadAssetText(args);
                case "get_asset_summary": return ToolGetAssetSummary();
                // CM
                case "get_config_text": return ToolGetConfigText(args);
                case "get_stringtable_value": return ToolGetStringTableValue(args);
                case "list_stringtable_keys": return ToolListStringTableKeys(args);
                case "get_terrain_types": return ToolGetTerrainTypes();
                // 地图数据查询
                case "get_building_list": return ToolGetBuildingList(args);
                case "get_legion_list": return ToolGetLegionList();
                case "get_army_list": return ToolGetArmyList(args);
                case "get_country_list": return ToolGetCountryList();
                case "search_stringtable": return ToolSearchStringTable(args);
                // 文件操作
                case "read_file": return ToolReadFile(args);
                case "write_file": return ToolWriteFile(args);
                case "list_dir": return ToolListDir(args);
                // 地图对象属性
                case "list_object_fields": return ToolListObjectFields(args);
                case "get_object": return ToolGetObject(args);
                case "set_object_field": return ToolSetObjectField(args);
                case "set_object_field_batch": return ToolSetObjectFieldBatch(args);
                // 记忆
                case "save_memory": return ToolSaveMemory(args);
                case "read_memory": return ToolReadMemory(args);
                case "list_memories": return ToolListMemories();
                case "delete_memory": return ToolDeleteMemory(args);
                default: return $"未知工具: {toolName}";
            }
        }
        catch (Exception ex)
        {
            return $"工具执行异常: {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>
    /// 带"产物"的工具执行。
    /// <para>
    /// 绝大多数工具只回一段文本；截图类工具还会产出一张图片，图片路径通过返回的列表交出去，
    /// 由 AgentLoop 补一条带图消息 —— 这样模型才真的"看得见"，而不是只收到一个文件路径。
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> ExecuteWithArtifacts(string toolName, string argumentsJson, out string output)
    {
        if (toolName == "capture_screenshot")
        {
            var (text, images) = ToolCaptureScreenshot(ParseArgs(argumentsJson));
            output = text;
            return images;
        }

        output = Execute(toolName, argumentsJson);
        return Array.Empty<string>();
    }

    /// <summary>截图工具：借主程序的截图能力取图，把文件路径作为"产物"回传。</summary>
    private static (string Text, List<string> Images) ToolCaptureScreenshot(JsonElement args)
    {
        string mode = (GetString(args, "mode") ?? "").Trim();
        bool region = string.Equals(mode, "region", StringComparison.OrdinalIgnoreCase);
        bool scene = string.Equals(mode, "scene", StringComparison.OrdinalIgnoreCase);
        int col = GetInt(args, "col", -1);
        int row = GetInt(args, "row", -1);
        int w = GetInt(args, "w", 48);
        int h = GetInt(args, "h", 36);
        int maxPixels = GetInt(args, "max_pixels", 1200);
        bool labels = GetBool(args, "show_labels", true);

        if (region && (col < 0 || row < 0))
            return ("失败：mode=region 需要 col 与 row（区域中心格）。", new List<string>());

        var result = AiBridge.CaptureScreenshot(new WC4MapEditor.Core.Services.MapScreenshotRequest
        {
            CenterCol = region ? col : -1,
            CenterRow = region ? row : -1,
            CellsW = region ? Math.Max(4, w) : 0,
            CellsH = region ? Math.Max(4, h) : 0,
            MaxPixels = Math.Clamp(maxPixels, 256, 4096),
            ShowHexLabels = labels,
            MarkCenter = region,
            SceneMode = scene,          // scene：截整个编辑器窗口（界面），不是地图数据
        });

        if (!result.Ok)
            return ($"失败：{result.Message}", new List<string>());

        if (AiImage.FromFile(result.Path) == null)
            return ($"截图已生成但无法读取（文件可能不存在或过大）：{result.Path}", new List<string>());

        return ($"截图完成：{result.Path}（{result.PixelWidth}×{result.PixelHeight} 像素）。"
                + $"{result.Message}。图片会随下一条消息发给你，请直接看图分析。",
                new List<string> { result.Path });
    }

    private static bool GetBool(JsonElement args, string name, bool def = false)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var v)) return def;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => def,
        };
    }

    private static JsonElement ParseArgs(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return doc.RootElement.Clone();
        }
        catch
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
    }

    private static string GetString(JsonElement args, string name, string def = "")
        => args.ValueKind == JsonValueKind.Object
           && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? def : def;

    private static int GetInt(JsonElement args, string name, int def = 0)
        => args.ValueKind == JsonValueKind.Object
           && args.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : def;

    private static int GetIntWithDefault(JsonElement args, string name, int def)
        => GetInt(args, name, def);

    // ------------------------------------------------------------ 实现 ------------------------------------------------------------

    private static string ToolGetMapInfo()
    {
        var map = AiBridge.GetMapData();
        if (map == null) return "失败：尚未加载地图";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"地图尺寸: {map.MapWidth} x {map.MapHeight}");
        sb.AppendLine($"BTL版本: v{map.Header.BtlVersion}");
        sb.AppendLine($"建筑数: {map.Buildings.Count}");
        sb.AppendLine($"军团数: {map.Legions.Count}");
        int armyCount = map.Header.UsesV3Records ? map.ArmiesV3.Count : map.Armies.Count;
        sb.AppendLine($"单位数: {armyCount}");
        sb.AppendLine($"陷阱数: {map.Traps.Count}");
        return sb.ToString().TrimEnd();
    }

    private static string ToolOpenScene(JsonElement args)
    {
        string scene = GetString(args, "scene");
        if (string.IsNullOrWhiteSpace(scene)) return "失败：缺少 scene 参数";
        var known = AiBridge.ListScenes();
        if (known.Count > 0 && !known.Any(s => s.Equals(scene, StringComparison.OrdinalIgnoreCase)))
            return $"失败：未知场景 '{scene}'。可用场景: {string.Join(", ", known)}";
        bool ok = AiBridge.OpenScene(scene);
        return ok ? $"已切换到场景 {scene}" : $"失败：切换到 {scene} 未成功";
    }

    private static string ToolListScenes()
    {
        var scenes = AiBridge.ListScenes();
        if (scenes.Count == 0) return "（主程序未注册场景列表）";
        return "可用场景: " + string.Join(", ", scenes);
    }

    private static string ToolOpenMap(JsonElement args)
    {
        string path = GetString(args, "path");
        if (string.IsNullOrWhiteSpace(path)) return "失败：缺少 path 参数";
        var (ok, output) = AiBridge.RunCommand($"open {path}");
        return ok ? $"[OK] {output}" : $"[FAIL] {output}";
    }

    private static string ToolNewCampaign(JsonElement args)
    {
        int width = GetInt(args, "width");
        int height = GetInt(args, "height");
        int legions = GetIntWithDefault(args, "legions", 2);
        if (width < 1 || width > 500) return "失败：宽度须在 1-500 之间";
        if (height < 1 || height > 500) return "失败：高度须在 1-500 之间";
        if (legions < 1) return "失败：军团数量至少为 1";
        var (ok, msg) = AiBridge.CreateMap("campaign", width, height, legions, 0);
        return ok ? $"[OK] {msg}" : $"[FAIL] {msg}";
    }

    private static string ToolNewConquest(JsonElement args)
    {
        int width = GetInt(args, "width");
        int height = GetInt(args, "height");
        int legions = GetIntWithDefault(args, "legions", 2);
        int mapNumber = GetIntWithDefault(args, "map_number", 1);
        if (width < 1 || width > 500) return "失败：宽度须在 1-500 之间";
        if (height < 1 || height > 500) return "失败：高度须在 1-500 之间";
        if (legions < 1) return "失败：军团数量至少为 1";
        if (mapNumber < 1 || mapNumber > 255) return "失败：地图编号须在 1-255 之间";
        var (ok, msg) = AiBridge.CreateMap("conquest", width, height, legions, mapNumber);
        return ok ? $"[OK] {msg}" : $"[FAIL] {msg}";
    }

    private static string ToolNewMap(JsonElement args)
    {
        int width = GetInt(args, "width");
        int height = GetInt(args, "height");
        if (width < 10 || width > 500) return "失败：宽度须在 10-500 之间";
        if (height < 10 || height > 500) return "失败：高度须在 10-500 之间";
        var (ok, msg) = AiBridge.CreateMap("map", width, height, 0, 0);
        return ok ? $"[OK] {msg}" : $"[FAIL] {msg}";
    }

    private static string ToolRunCommand(JsonElement args)
    {
        string command = GetString(args, "command");
        if (string.IsNullOrWhiteSpace(command)) return "失败：缺少 command 参数";
        var (ok, output) = AiBridge.RunCommand(command);
        string status = ok ? "OK" : "FAIL";
        return $"[{status}] {output}";
    }

    private static string ToolRunLua(JsonElement args)
    {
        string code = GetString(args, "code");
        if (string.IsNullOrWhiteSpace(code)) return "失败：缺少 code 参数";
        var result = AiBridge.RunLua(code);
        if (result == null) return "失败：主程序未注册 Lua 执行器";
        return FormatScriptResult(result);
    }

    private static string ToolRunLuaFile(JsonElement args)
    {
        string path = GetString(args, "path");
        if (string.IsNullOrWhiteSpace(path)) return "失败：缺少 path 参数";

        path = path.Replace('\\', '/').TrimStart('/');
        string full;
        if (File.Exists(path)) full = System.IO.Path.GetFullPath(path);
        else
        {
            // 相对路径以编辑器 scripts 目录 / 程序目录为根
            string baseDir = AppContext.BaseDirectory;
            string byBase = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, path));
            string byScripts = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", path));
            if (File.Exists(byBase)) full = byBase;
            else if (File.Exists(byScripts)) full = byScripts;
            else return $"失败：找不到脚本 {path}";
        }

        string code = File.ReadAllText(full);
        var result = AiBridge.RunLua(code);
        if (result == null) return "失败：主程序未注册 Lua 执行器";
        return FormatScriptResult(result);
    }

    private static string ToolListScripts()
    {
        string baseDir = AppContext.BaseDirectory;
        string scriptsDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "..", "scripts"));
        if (!Directory.Exists(scriptsDir))
            scriptsDir = System.IO.Path.Combine(baseDir, "scripts");
        if (!Directory.Exists(scriptsDir)) return "（scripts 目录不存在）";

        var files = Directory.GetFiles(scriptsDir, "*.lua", SearchOption.AllDirectories)
            .Select(f => System.IO.Path.GetRelativePath(scriptsDir, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return files.Count == 0 ? "（scripts 目录没有 .lua 文件）"
            : "可用脚本:\n" + string.Join("\n", files);
    }

    // ============================================================= AM 工具（AssetManager） =============================================================

    private static string ToolListAssets(JsonElement args)
    {
        var am = AssetManager.Default;
        if (!am.IsLoaded) { try { am.ScanDefault(); } catch { } }
        if (!am.IsLoaded) return "失败：assets 目录未加载";

        string ext = GetString(args, "extension").TrimStart('.');
        string dir = GetString(args, "directory");
        string name = GetString(args, "nameContains");

        var entries = am.Query(
            extension: string.IsNullOrEmpty(ext) ? null : ext,
            topDirectory: string.IsNullOrEmpty(dir) ? null : dir,
            nameContains: string.IsNullOrEmpty(name) ? null : name);

        if (entries.Count == 0) return "（无匹配的资源条目）";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"共 {entries.Count} 条（显示前 200）:");
        foreach (var e in entries.Take(200))
            sb.AppendLine($"  {e.RelativePath}  ({e.Size} B)");
        if (entries.Count > 200) sb.AppendLine($"  …（共 {entries.Count} 条，截断）");
        return sb.ToString().TrimEnd();
    }

    private static string ToolReadAssetText(JsonElement args)
    {
        var am = AssetManager.Default;
        if (!am.IsLoaded) { try { am.ScanDefault(); } catch { } }
        if (!am.IsLoaded) return "失败：assets 目录未加载";

        string path = GetString(args, "path");
        if (string.IsNullOrWhiteSpace(path)) return "失败：缺少 path 参数";
        int maxChars = GetInt(args, "maxChars", 8000);

        var entry = am.Find(path);
        if (entry == null) return $"失败：找不到资源 {path}";

        try
        {
            string text = am.ReadText(entry);
            if (text.Length > maxChars) text = text[..maxChars] + $"\n…（共 {text.Length} 字符，截断到 {maxChars}）";
            return text;
        }
        catch (Exception ex) { return $"失败：读取 {path} 出错: {ex.Message}"; }
    }

    private static string ToolGetAssetSummary()
    {
        var am = AssetManager.Default;
        if (!am.IsLoaded) { try { am.ScanDefault(); } catch { } }
        if (!am.IsLoaded) return "失败：assets 目录未加载";
        return am.GetSummaryReport();
    }

    // ============================================================= CM 工具（ConfigManager） =============================================================

    private static string ToolGetConfigText(JsonElement args)
    {
        string key = GetString(args, "key");
        if (string.IsNullOrWhiteSpace(key)) return "失败：缺少 key 参数";
        string def = GetString(args, "defaultValue");
        try { return ConfigManager.Instance.GetText(key, def); }
        catch (Exception ex) { return $"失败: {ex.Message}"; }
    }

    private static string ToolGetStringTableValue(JsonElement args)
    {
        string key = GetString(args, "key");
        if (string.IsNullOrWhiteSpace(key)) return "失败：缺少 key 参数";
        try
        {
            var v = ConfigManager.Instance.GetStringTableValue(key, "");
            return string.IsNullOrEmpty(v) ? $"（键 {key} 不存在或值为空）" : v;
        }
        catch (Exception ex) { return $"失败: {ex.Message}"; }
    }

    private static string ToolListStringTableKeys(JsonElement args)
    {
        string prefix = GetString(args, "prefix");
        try
        {
            var data = ConfigManager.Instance.GetStringTableData();
            if (data.Count == 0) return "（stringtable 未加载）";
            var keys = string.IsNullOrEmpty(prefix)
                ? data.Keys.ToList()
                : data.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            if (keys.Count == 0) return $"（无以 {prefix} 开头的键）";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"共 {keys.Count} 个键（显示前 500）:");
            foreach (var k in keys.Take(500))
            {
                string v = data[k];
                sb.AppendLine($"  {k} = {(v.Length > 60 ? v[..60] + "…" : v)}");
            }
            if (keys.Count > 500) sb.AppendLine($"  …（共 {keys.Count} 个键，截断）");
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) { return $"失败: {ex.Message}"; }
    }

    private static string ToolGetTerrainTypes()
    {
        try
        {
            var types = ConfigManager.Instance.GetTerrainTypes();
            if (types.Count == 0) return "（地形类型表为空）";
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"共 {types.Count} 种地形:");
            foreach (var kv in types.OrderBy(kv => kv.Key)) sb.AppendLine($"  {kv.Key,3} → {kv.Value}");
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) { return $"失败: {ex.Message}"; }
    }

    // ============================================================= 地图数据查询工具 =============================================================

    private static string ToolGetBuildingList(JsonElement args)
    {
        var map = AiBridge.GetMapData();
        if (map == null) return "失败：尚未加载地图";
        int maxCount = GetInt(args, "maxCount", 100);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"共 {map.Buildings.Count} 个建筑（显示前 {maxCount}）:");
        int shown = 0;
        foreach (var b in map.Buildings)
        {
            if (shown >= maxCount) break;
            int col = b.Coordinate % map.MapWidth;
            int row = b.Coordinate / map.MapWidth;
            sb.AppendLine($"  [{shown}] 坐标({col},{row}) 类型={b.BuildingType} 城市名ID={b.Name} 工厂={b.FactoryLevel} 研究={b.ResearchLevel} 医疗={b.MedicalLevel} 航空={b.AviationLevel}");
            shown++;
        }
        if (map.Buildings.Count > maxCount) sb.AppendLine($"  …（共 {map.Buildings.Count} 个，截断）");
        return sb.ToString().TrimEnd();
    }

    private static string ToolGetLegionList()
    {
        var map = AiBridge.GetMapData();
        if (map == null) return "失败：尚未加载地图";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"共 {map.Legions.Count} 个军团:");
        for (int i = 0; i < map.Legions.Count; i++)
        {
            var l = map.Legions[i];
            sb.AppendLine($"  [{i}] 国家ID={l.CountryId} 阵营={l.Camp} 玩家={l.IsPlayer} 经济={l.InitialEconomy} 工业={l.InitialIndustry} 科技={l.InitialTech} 颜色=({l.ColorR},{l.ColorG},{l.ColorB})");
        }
        return sb.ToString().TrimEnd();
    }

    private static string ToolGetArmyList(JsonElement args)
    {
        var map = AiBridge.GetMapData();
        if (map == null) return "失败：尚未加载地图";
        int maxCount = GetInt(args, "maxCount", 100);

        int count = map.Header.UsesV3Records ? map.ArmiesV3.Count : map.Armies.Count;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"共 {count} 个单位（显示前 {maxCount}）:");
        int shown = 0;

        // 单位的归属就是所在格子的归属值（单位自带的 LegionId 只是编辑器运行期字段）
        void AppendArmy(int cell, int unitType, int level, int experience)
        {
            int col = cell % map.MapWidth;
            int row = cell / map.MapWidth;
            sb.AppendLine($"  [{shown}] 坐标({col},{row}) 类型={unitType} 等级={level} 归属={map.GetBelongValue(col, row)} 经验={experience}");
            shown++;
        }

        if (map.Header.UsesV3Records)
        {
            foreach (var a in map.ArmiesV3)
            {
                if (shown >= maxCount) break;
                AppendArmy(a.CoordinateIndex, a.UnitType, a.Level, a.Experience);
            }
        }
        else
        {
            foreach (var a in map.Armies)
            {
                if (shown >= maxCount) break;
                AppendArmy(a.CoordinateIndex, a.UnitType, a.Level, a.Experience);
            }
        }
        if (count > maxCount) sb.AppendLine($"  …（共 {count} 个，截断）");
        return sb.ToString().TrimEnd();
    }

    private static string ToolGetCountryList()
    {
        try
        {
            var csp = CountrySettingParser.Instance;
            var countries = csp.Countries;
            if (countries.Count == 0) return "（国家列表为空，可能未加载 CountrySettings.json）";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"共 {countries.Count} 个国家:");
            foreach (var c in countries)
            {
                string name = csp.GetCountryName(c.Id);
                sb.AppendLine($"  ID={c.Id} 名称={name}");
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) { return $"失败: {ex.Message}"; }
    }

    private static string ToolSearchStringTable(JsonElement args)
    {
        string keyword = GetString(args, "keyword");
        if (string.IsNullOrWhiteSpace(keyword)) return "失败：缺少 keyword 参数";
        int maxCount = GetInt(args, "maxCount", 50);

        try
        {
            var data = ConfigManager.Instance.GetStringTableData();
            if (data.Count == 0) return "（stringtable 未加载）";

            var matches = data
                .Where(kv => kv.Value.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Take(maxCount)
                .ToList();

            if (matches.Count == 0) return $"（无值包含 '{keyword}' 的条目）";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"共匹配 {matches.Count} 条（限制 {maxCount}）:");
            foreach (var kv in matches)
                sb.AppendLine($"  {kv.Key} = {kv.Value}");
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) { return $"失败: {ex.Message}"; }
    }

    // ============================================================= 文件操作工具 =============================================================

    /// <summary>解析项目文件路径：原路径 → 当前目录 → 程序目录逐级上溯。</summary>
    private static string? ResolveProjectFile(string rawPath)
    {
        if (File.Exists(rawPath)) return rawPath;
        if (Directory.Exists(rawPath)) return rawPath;

        string baseDir = AppContext.BaseDirectory;
        string[] candidates =
        [
            System.IO.Path.Combine(Directory.GetCurrentDirectory(), rawPath),
            System.IO.Path.Combine(baseDir, rawPath),
            System.IO.Path.Combine(baseDir, "..", "..", "..", rawPath),
            System.IO.Path.Combine(baseDir, "..", "..", "..", "..", rawPath),
        ];

        foreach (var p in candidates)
        {
            string full = System.IO.Path.GetFullPath(p);
            if (File.Exists(full) || Directory.Exists(full)) return full;
        }
        return null;
    }

    private static string ToolReadFile(JsonElement args)
    {
        string path = GetString(args, "path");
        if (string.IsNullOrWhiteSpace(path)) return "失败：缺少 path 参数";
        int maxChars = GetInt(args, "maxChars", 20000);

        string? resolved = ResolveProjectFile(path);
        if (resolved == null || !File.Exists(resolved))
            return $"失败：找不到文件 {path}（已尝试原路径/当前目录/程序目录上溯）";

        try
        {
            string text = File.ReadAllText(resolved);
            if (text.Length > maxChars)
                text = text[..maxChars] + $"\n…（共 {text.Length} 字符，截断到 {maxChars}）";
            return text;
        }
        catch (Exception ex) { return $"失败：读取 {path} 出错: {ex.Message}"; }
    }

    private static string ToolWriteFile(JsonElement args)
    {
        string path = GetString(args, "path");
        if (string.IsNullOrWhiteSpace(path)) return "失败：缺少 path 参数";
        string content = GetString(args, "content");

        string? resolved = ResolveProjectFile(path);
        if (resolved == null)
        {
            string baseDir = AppContext.BaseDirectory;
            resolved = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", path));
        }

        try
        {
            string? dir = System.IO.Path.GetDirectoryName(resolved);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(resolved))
            {
                string bak = resolved + ".bak";
                File.Copy(resolved, bak, overwrite: true);
            }

            File.WriteAllText(resolved, content);
            return $"[OK] 已写入 {resolved}（{content.Length} 字符）" +
                   (File.Exists(resolved + ".bak") ? $"\n备份: {resolved}.bak" : "");
        }
        catch (Exception ex) { return $"失败：写入 {path} 出错: {ex.Message}"; }
    }

    private static string ToolListDir(JsonElement args)
    {
        string path = GetString(args, "path");
        string? resolved = string.IsNullOrEmpty(path) ? ResolveProjectDir() : ResolveProjectFile(path);

        if (resolved == null || !Directory.Exists(resolved))
            return $"失败：找不到目录 {path}";

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"目录: {resolved}");

            var dirs = Directory.GetDirectories(resolved)
                .Select(d => System.IO.Path.GetFileName(d))
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var files = Directory.GetFiles(resolved)
                .Select(f => (name: System.IO.Path.GetFileName(f), size: new FileInfo(f).Length))
                .OrderBy(f => f.name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (dirs.Count > 0)
            {
                sb.AppendLine($"子目录 ({dirs.Count}):");
                foreach (var d in dirs.Take(100)) sb.AppendLine($"  [DIR] {d}");
                if (dirs.Count > 100) sb.AppendLine($"  …（共 {dirs.Count} 个，截断）");
            }
            if (files.Count > 0)
            {
                sb.AppendLine($"文件 ({files.Count}):");
                foreach (var f in files.Take(100)) sb.AppendLine($"  {f.name}  ({f.size} B)");
                if (files.Count > 100) sb.AppendLine($"  …（共 {files.Count} 个，截断）");
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) { return $"失败: {ex.Message}"; }
    }

    private static string? ResolveProjectDir()
    {
        string baseDir = AppContext.BaseDirectory;
        for (int i = 0; i <= 4; i++)
        {
            string dir = baseDir;
            for (int j = 0; j < i; j++) dir = System.IO.Path.Combine(dir, "..");
            dir = System.IO.Path.GetFullPath(dir);
            if (File.Exists(System.IO.Path.Combine(dir, "setting.txt")) ||
                Directory.GetFiles(dir, "*.csproj").Length > 0)
                return dir;
        }
        return Directory.GetCurrentDirectory();
    }

    private static string FormatScriptResult(WC4MapEditor.Scripting.ScriptResult r)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[{(r.Success ? "OK" : "FAIL")}] 影响条数: {r.AffectedCount}, 耗时: {r.Elapsed.TotalMilliseconds:F0}ms");
        if (!r.Success) sb.AppendLine($"错误: {r.Error}{(string.IsNullOrEmpty(r.ErrorLocation) ? "" : $" @ {r.ErrorLocation}")}");
        if (r.Output.Count > 0)
        {
            sb.AppendLine("输出:");
            foreach (var line in r.Output.Take(200)) sb.AppendLine($"  {line}");
            if (r.Output.Count > 200) sb.AppendLine($"  …（共 {r.Output.Count} 行，截断）");
        }
        return sb.ToString().TrimEnd();
    }

    // ============================================================= 地图对象属性（建筑 / 单位 / 军团） =============================================================

    /// <summary>
    /// 把一条控制台命令交给主程序执行并取回输出。
    /// <para>
    /// 对象属性读写刻意不在 AI 侧重写一遍：CLI 那边已经有完整的字段白名单、按类型钳位、
    /// 定位键校验（如国家 ID 唯一性）。转成命令执行，保证"AI 改的"和"手敲命令改的"完全同一套行为。
    /// </para>
    /// </summary>
    private static string RunConsoleCommand(string commandLine)
    {
        var runner = AiBridge.CommandRunner;
        if (runner == null) return "失败：命令执行通道未注册（当前场景不支持执行命令）";

        var (ok, output) = runner(commandLine);
        string text = (output ?? "").Trim();
        if (text.Length > 0) return text;

        return ok ? $"已执行：{commandLine}（该命令没有输出）" : $"执行失败：{commandLine}（没有更多信息）";
    }

    /// <summary>把 kind 归一化：unit 视为 army；返回 null 表示不支持的 kind。</summary>
    private static string? NormalizeObjectKind(string kind) => kind.Trim().ToLowerInvariant() switch
    {
        "building" => "building",
        "army" or "unit" => "army",
        "legion" => "legion",
        _ => null
    };

    private static string ToolListObjectFields(JsonElement args)
    {
        string? kind = NormalizeObjectKind(GetString(args, "kind") ?? "");
        if (kind == null) return "失败：kind 只能是 building / army / legion";

        return kind switch
        {
            "building" => RunConsoleCommand("building fields"),
            "army" => RunConsoleCommand("army fields"),
            _ => RunConsoleCommand("legion fields")
        };
    }

    private static string ToolGetObject(JsonElement args)
    {
        string? kind = NormalizeObjectKind(GetString(args, "kind") ?? "");
        if (kind == null) return "失败：kind 只能是 building / army / legion";

        int col = GetInt(args, "col", -1);
        int row = GetInt(args, "row", -1);
        int index = GetInt(args, "index", -1);

        if (kind == "legion")
        {
            if (index < 0) return "失败：看军团需要 index（军团列表索引，等于归属值）";
            return RunConsoleCommand($"legion show-at {index}");
        }

        if (col < 0 || row < 0) return $"失败：看{kind}需要 col 与 row";
        return kind == "building"
            ? RunConsoleCommand($"building show {col} {row}")
            : RunConsoleCommand($"army show {col} {row}");
    }

    private static string ToolSetObjectField(JsonElement args)
    {
        string? kind = NormalizeObjectKind(GetString(args, "kind") ?? "");
        if (kind == null) return "失败：kind 只能是 building / army / legion";

        string field = (GetString(args, "field") ?? "").Trim();
        if (field.Length == 0) return "失败：缺少 field（先用 list_object_fields 查可写字段名）";

        double value = GetDouble(args, "value", 0);
        string valueText = FormatNumber(value);

        int col = GetInt(args, "col", -1);
        int row = GetInt(args, "row", -1);
        int index = GetInt(args, "index", -1);

        if (kind == "legion")
        {
            if (index < 0) return "失败：改军团需要 index（军团列表索引，等于归属值）";

            // CountryId / ActionId 是军团定位键，通用字段白名单刻意不含它们：
            // 这里自动改走带唯一性校验的专用命令，免得模型踩"重复国家 ID"的坑。
            if (field.Equals("CountryId", StringComparison.OrdinalIgnoreCase))
                return RunConsoleCommand($"legion set-country-at {index} {valueText}");
            if (field.Equals("ActionId", StringComparison.OrdinalIgnoreCase))
                return RunConsoleCommand($"legion set-action-at {index} {valueText}");

            return RunConsoleCommand($"legion set-field {index} {field} {valueText}");
        }

        if (col < 0 || row < 0) return $"失败：改{kind}需要 col 与 row";
        return kind == "building"
            ? RunConsoleCommand($"building set-field {col} {row} {field} {valueText}")
            : RunConsoleCommand($"army set-field {col} {row} {field} {valueText}");
    }

    /// <summary>
    /// 数字转命令参数文本：整数不带小数点；小数保留最多 4 位（军团的血率/税率是浮点字段，
    /// 用整数格式会把 0.85 变成 1，那是静默改错数据）。
    /// </summary>
    private static string FormatNumber(double value)
        => Math.Abs(value - Math.Round(value)) < 1e-9
            ? ((long)Math.Round(value)).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    private static double GetDouble(JsonElement args, string name, double def = 0)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var v)) return def;
        if (v.ValueKind != JsonValueKind.Number) return def;
        return v.TryGetDouble(out double d) ? d : def;
    }

    /// <summary>
    /// 批量改同一类对象的同一个字段。
    /// <para>
    /// 实现方式是<b>生成一段 Lua 丢给已有的 Lua 通道执行</b>，而不是在 C# 里逐个调命令：
    /// ① 复用同一套对象 API 与字段白名单；② 天然获得"整批一个撤销单元"（Ctrl+Z 一次退回）；
    /// ③ 模型不用写代码，只填过滤条件。
    /// </para>
    /// </summary>
    private static string ToolSetObjectFieldBatch(JsonElement args)
    {
        string? kind = NormalizeObjectKind(GetString(args, "kind") ?? "");
        if (kind == null) return "失败：kind 只能是 building / army / legion";

        string field = (GetString(args, "field") ?? "").Trim();
        if (field.Length == 0) return "失败：缺少 field（先用 list_object_fields 查可写字段名）";
        if (!IsSimpleIdentifier(field)) return $"失败：字段名 '{field}' 不合法（只能是字母/数字/下划线）";

        string op = (GetString(args, "op") ?? "set").Trim().ToLowerInvariant();
        if (op is not ("set" or "add")) return "失败：op 只能是 set 或 add";

        bool isLegionKey = field.Equals("CountryId", StringComparison.OrdinalIgnoreCase)
                        || field.Equals("ActionId", StringComparison.OrdinalIgnoreCase);

        if (op == "add")
        {
            if (kind == "building" && field.Equals("Name", StringComparison.OrdinalIgnoreCase))
                return "失败：建筑的 Name 是名称ID（不是数值等级），只能用 op=set";
            if (kind == "legion" && isLegionKey)
                return $"失败：军团 {field} 是定位键，不能做加法，请用 op=set";
        }

        string condition = BuildBatchCondition(args, kind, field);
        string lua = kind switch
        {
            "building" => BuildBuildingBatchLua(field, FormatNumber(GetDouble(args, "value", 0)), op, condition),
            "army" => BuildArmyBatchLua(field, FormatNumber(GetDouble(args, "value", 0)), op, condition),
            _ => BuildLegionBatchLua(field, FormatNumber(GetDouble(args, "value", 0)), op, args)
        };

        var runner = AiBridge.LuaCodeRunner;
        if (runner == null) return "失败：Lua 通道未注册（当前场景没有地图，请先切到地图编辑场景）";

        var result = runner(lua);
        if (result == null) return "失败：Lua 未执行（当前场景没有地图数据）";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[{(result.Success ? "OK" : "FAIL")}] 批量修改 {kind}.{field}（op={op}，条件：{condition}）");
        if (!result.Success) sb.AppendLine($"错误: {result.Error}");
        foreach (var line in result.Output) sb.AppendLine(line);
        return sb.ToString().TrimEnd();
    }

    /// <summary>区域/属性过滤条件（转成 Lua 布尔表达式）；无条件返回 "true"。</summary>
    private static string BuildBatchCondition(JsonElement args, string kind, string field)
    {
        var parts = new List<string>();

        int col1 = GetInt(args, "col1", -1);
        int row1 = GetInt(args, "row1", -1);
        int col2 = GetInt(args, "col2", -1);
        int row2 = GetInt(args, "row2", -1);
        if (col1 >= 0 && row1 >= 0 && col2 >= 0 && row2 >= 0)
        {
            // 允许用户把两个角反着写
            int c1 = Math.Min(col1, col2), c2 = Math.Max(col1, col2);
            int r1 = Math.Min(row1, row2), r2 = Math.Max(row1, row2);
            string v = kind == "building" ? "b" : "a";
            parts.Add($"({v}.Col >= {c1} and {v}.Col <= {c2} and {v}.Row >= {r1} and {v}.Row <= {r2})");
        }

        if (kind == "building")
        {
            int buildingType = GetInt(args, "building_type", -1);
            if (buildingType >= 0) parts.Add($"b.BuildingType == {buildingType}");

            int belong = GetInt(args, "belong", -1);
            if (belong >= 0) parts.Add($"b.Belong == {belong}");

            if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("has_name", out var hn)
                && (hn.ValueKind == JsonValueKind.True || hn.ValueKind == JsonValueKind.False))
            {
                parts.Add(hn.ValueKind == JsonValueKind.True ? "b.HasName" : "not b.HasName");
            }
        }
        else if (kind == "army")
        {
            int legion = GetInt(args, "legion", -1);
            if (legion >= 0) parts.Add($"a.Belong == {legion}");

            if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("level_min", out var lmin) && lmin.ValueKind == JsonValueKind.Number)
                parts.Add($"a.Level >= {GetInt(args, "level_min", 0)}");
            if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("level_max", out var lmax) && lmax.ValueKind == JsonValueKind.Number)
                parts.Add($"a.Level <= {GetInt(args, "level_max", 0)}");

            // v3 专有字段在 v1 地图上写必失败 —— 直接把版本条件并进去，省得满屏失败计数
            if (IsV3OnlyArmyField(field)) parts.Add("a.Version >= 3");
        }

        return parts.Count > 0 ? string.Join(" and ", parts) : "true";
    }

    /// <summary>单位 v3 专有字段（v1 结构里没有）。</summary>
    private static bool IsV3OnlyArmyField(string field)
        => field.Equals("HpLevel", StringComparison.OrdinalIgnoreCase)
        || field.Equals("TransportShip", StringComparison.OrdinalIgnoreCase)
        || field.Equals("Hatred", StringComparison.OrdinalIgnoreCase)
        || field.Equals("AttackTarget", StringComparison.OrdinalIgnoreCase)
        || field.Equals("DeathDialogue", StringComparison.OrdinalIgnoreCase)
        || field.Equals("LevelMarkDisplay", StringComparison.OrdinalIgnoreCase)
        || field.Equals("AntiDefenseMode", StringComparison.OrdinalIgnoreCase);

    private static string BuildBuildingBatchLua(string field, string valueText, string op, string condition)
    {
        string valueExpr = op == "add" ? $"b.{field} + {valueText}" : valueText;
        return $"""
local ok, fail, firstErr = 0, 0, nil
map.ForEachBuilding(function(b)
    if {condition} then
        local r = map.SetBuildingField(b.Col, b.Row, '{field}', {valueExpr})
        if r.Success then ok = ok + 1 else fail = fail + 1; if not firstErr then firstErr = tostring(r.Message) end end
    end
end)
print(string.format('建筑批量修改：成功 %d，失败 %d', ok, fail))
if firstErr then print('首个失败原因：' .. firstErr) end
""";
    }

    private static string BuildArmyBatchLua(string field, string valueText, string op, string condition)
    {
        string valueExpr = op == "add" ? $"a.{field} + {valueText}" : valueText;
        return $"""
local ok, fail, firstErr = 0, 0, nil
map.ForEachArmy(function(a)
    if {condition} then
        local r = map.SetArmyField(a.Col, a.Row, '{field}', {valueExpr})
        if r.Success then ok = ok + 1 else fail = fail + 1; if not firstErr then firstErr = tostring(r.Message) end end
    end
end)
print(string.format('单位批量修改：成功 %d，失败 %d', ok, fail))
if firstErr then print('首个失败原因：' .. firstErr) end
""";
    }

    private static string BuildLegionBatchLua(string field, string valueText, string op, JsonElement args)
    {
        int from = Math.Max(0, GetInt(args, "index_from", 0));
        int to = GetInt(args, "index_to", -1);
        string toExpr = to >= 0 ? to.ToString() : "map.LegionCount - 1";

        // 定位键用带校验的专用方法（通用字段白名单刻意不含它们）
        string call;
        string valueExpr;
        if (field.Equals("CountryId", StringComparison.OrdinalIgnoreCase))
        {
            call = "map.SetLegionCountryIdAt(i, {0})";
            valueExpr = valueText;
        }
        else if (field.Equals("ActionId", StringComparison.OrdinalIgnoreCase))
        {
            call = "map.SetLegionActionIdAt(i, {0})";
            valueExpr = valueText;
        }
        else
        {
            call = $"map.SetLegionField(i, '{field}', {{0}})";
            valueExpr = op == "add" ? $"cur.{field} + {valueText}" : valueText;
        }

        return $"""
local ok, fail, firstErr = 0, 0, nil
for i = {from}, {toExpr} do
    local cur = map.GetLegionAt(i)
    if cur then
        local r = {string.Format(call, valueExpr)}
        if r.Success then ok = ok + 1 else fail = fail + 1; if not firstErr then firstErr = tostring(r.Message) end end
    end
end
print(string.format('军团批量修改：成功 %d，失败 %d', ok, fail))
if firstErr then print('首个失败原因：' .. firstErr) end
""";
    }

    /// <summary>字段名合法性：只允许字母/数字/下划线（会拼进 Lua 源码，必须挡住注入）。</summary>
    private static bool IsSimpleIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
        {
            if (!char.IsLetterOrDigit(c) && c != '_') return false;
        }
        return true;
    }

    // ============================================================= 记忆工具（Memory） =============================================================

    private static string GetMemoryDir()
    {
        // 与资源检索同一规则：优先使用 ConfigManager 找到的 Resource（exe 同级 → 逐级向上）；
        // 配置尚未初始化时退回 ProjectPaths 的同一套查找。
        string resourceRoot = ConfigManager.Instance.ResourcePath;
        if (string.IsNullOrEmpty(resourceRoot))
            resourceRoot = ProjectPaths.FindDirectory("Resource")
                           ?? System.IO.Path.Combine(AppContext.BaseDirectory, "Resource");

        string memoryDir = System.IO.Path.Combine(resourceRoot, "Memory");
        if (!Directory.Exists(memoryDir))
            Directory.CreateDirectory(memoryDir);
        return memoryDir;
    }

    private static string SanitizeMemoryTitle(string title)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (char c in title)
        {
            if (Array.IndexOf(invalid, c) >= 0) sb.Append('-');
            else sb.Append(c);
        }
        return sb.ToString().Trim().Trim('-');
    }

    private static string ToolSaveMemory(JsonElement args)
    {
        string title = GetString(args, "title");
        if (string.IsNullOrWhiteSpace(title)) return "失败：缺少 title 参数";
        string content = GetString(args, "content");
        if (string.IsNullOrWhiteSpace(content)) return "失败：缺少 content 参数";

        title = SanitizeMemoryTitle(title);
        string memoryDir = GetMemoryDir();
        string filePath = System.IO.Path.Combine(memoryDir, $"{title}.md");

        try
        {
            if (File.Exists(filePath))
            {
                string bak = filePath + ".bak";
                File.Copy(filePath, bak, overwrite: true);
            }

            string header = $"# {title}\n\n> 保存时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n\n---\n\n";
            File.WriteAllText(filePath, header + content);

            return $"[OK] 记忆已保存: {title}.md（{content.Length} 字符）" +
                   (File.Exists(filePath + ".bak") ? "\n已有旧版本已备份" : "");
        }
        catch (Exception ex) { return $"失败：保存记忆 {title} 出错: {ex.Message}"; }
    }

    private static string ToolReadMemory(JsonElement args)
    {
        string title = GetString(args, "title");
        if (string.IsNullOrWhiteSpace(title)) return "失败：缺少 title 参数";

        title = SanitizeMemoryTitle(title);
        string memoryDir = GetMemoryDir();
        string filePath = System.IO.Path.Combine(memoryDir, $"{title}.md");

        if (!File.Exists(filePath))
            return $"失败：记忆 '{title}' 不存在。用 list_memories 查看已有记忆。";

        try
        {
            string text = File.ReadAllText(filePath);
            return text;
        }
        catch (Exception ex) { return $"失败：读取记忆 {title} 出错: {ex.Message}"; }
    }

    private static string ToolListMemories()
    {
        string memoryDir = GetMemoryDir();

        var files = Directory.GetFiles(memoryDir, "*.md")
            .Select(f => new FileInfo(f))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
            return "（暂无记忆文件。可用 save_memory 保存重要信息。）";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"共 {files.Count} 条记忆:");

        foreach (var f in files)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(f.Name);
            string summary = "";
            try
            {
                using var reader = f.OpenText();
                string? line;
                int linesRead = 0;
                while ((line = reader.ReadLine()) != null && linesRead < 5)
                {
                    linesRead++;
                    string trimmed = line.TrimStart(' ', '>', '#', '-');
                    if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("保存时间"))
                    {
                        summary = trimmed.Length > 80 ? trimmed[..80] + "…" : trimmed;
                        break;
                    }
                }
            }
            catch { }

            sb.AppendLine($"  {name}  ({f.LastWriteTime:yyyy-MM-dd HH:mm}){(string.IsNullOrEmpty(summary) ? "" : $" — {summary}")}");
        }

        return sb.ToString().TrimEnd();
    }

    private static string ToolDeleteMemory(JsonElement args)
    {
        string title = GetString(args, "title");
        if (string.IsNullOrWhiteSpace(title)) return "失败：缺少 title 参数";

        title = SanitizeMemoryTitle(title);
        string memoryDir = GetMemoryDir();
        string filePath = System.IO.Path.Combine(memoryDir, $"{title}.md");

        if (!File.Exists(filePath))
            return $"失败：记忆 '{title}' 不存在。";

        try
        {
            File.Delete(filePath);
            string bak = filePath + ".bak";
            if (File.Exists(bak)) File.Delete(bak);
            return $"[OK] 记忆 '{title}' 已删除";
        }
        catch (Exception ex) { return $"失败：删除记忆 {title} 出错: {ex.Message}"; }
    }
}