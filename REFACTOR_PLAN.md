# WC4MapEditor 重构计划：模型结构体化 + 模块化修改

> 会话交接文档。当前**编译失败**，需按本计划继续修复。
> 参考项目：`E:\VBProject\map`（VB.NET 版），其中 `Models\Province.vb` 是结构体范例，`Builder\*Modifier.vb` 是修改器范例。

---

## 一、已完成的工作（勿重做）

### 1. 资源管理系统（已验证可用）
| 文件 | 说明 |
|------|------|
| `WC4MapEditor.Core/Assets/AssetKind.cs` | 语义类别枚举（9 种 stage 子类型 + 8 种通用类型） |
| `WC4MapEditor.Core/Assets/AssetEntry.cs` | 文件元数据（路径/扩展名/大小/类别） |
| `WC4MapEditor.Core/Assets/AssetCache.cs` | 扫描 + 索引（RW 锁，按 kind/ext/dir 建索引） |
| `WC4MapEditor.Core/Assets/AssetManager.cs` | 高层门面，`GetDefaultAssetsPath()` 向上 4 层探测 `Resource\WC4DATA\assets` |

CLI 命令（`WC4MapEditor.Cli/Program.cs`）：`asset scan` / `asset summary` / `asset list` / `asset query`
实测：3435 个文件，Stage 295、Event 707、Frontier 213、Legend 60、GeneralStage 40、Warzone 25、InvadeCorps 10、Conquest 8。

### 2. GUI 资源浏览器
- `Views/AssetBrowserScene.xaml` + `.xaml.cs`（纯代码构建 UI，仿 BeginScene 模式）
- `Views/BeginScene.xaml.cs`：新增 `_assetButton`（"资源管理器"，位于"新地图"右侧）+ `AssetButton_Click`
- **遗留待验证**：右侧文件列表曾不显示，已加 `Grid.SetRowSpan(contentArea, 2)` 修复，用户尚未确认。

### 3. 解析器模块化（一个模式 = 多个模块）
```
WC4MapEditor.Core/Parsers/
├── BTL/          BTLParser.cs + 17 个 *Module.cs（Header/Legion/Terrain/Province/
│                 Belong/Building/Army/Trap/Case/Weather/Event/Reinforcement/
│                 AirForce/UnitPlacement/Capital/Strategy/AirSupport）
├── Stage/        StageParser.cs（懒加载）+ StageOffsets.cs
├── Conquest/     ConquestParser.cs（含 BelongOffset 逻辑）+ ConquestOffsets.cs
└── World/        WorldParser.cs（大文件流式）+ WorldOffsets.cs
```
旧的 4 个单体文件（`BTLFileParser.cs`/`StageParser.cs`/`ConquestParser.cs`/`WorldParser.cs`）**已删除**。
命名空间为 `WC4MapEditor.Parsers.BTL` 等（注意：**不是** `WC4MapEditor.Core.Parsers`）。

### 4. 模型结构体化（进行中，导致当前编译失败）
已改造：
- `Models/Terrain.cs` → `struct`（16 字节，`[StructLayout(Sequential, Pack=1)]`，`FromBytes`/`ToBytes(Span,int)`/`CreateDefault()`/`FromTerrainData(TerrainData)`/`ToTerrainData()`）
- `Models/Building.cs` → `struct`（32 字节，`FromBytes`/`ToBytes(Span,int)`/`CreateDefault(short)`）
- 新增 `Models/Modules/TerrainModule.cs`（`SetTerrainType`/`SetDecorationType`/`SetTextureOffset`/`SetRiverValue`/`SetTerrainTypeBatch`/`FloodFill`/`CopyTerrain`/`RandomizeDecoration`）
- 新增 `Models/Modules/BuildingModule.cs`（`SetBuildingType`/`SetAppearance`/`SetLevel`/`SetKeyPoint`/`SetOccupationEvent`/`SetAirDefense`/`SetFireProperties`/`CopyBuilding`/`SetLevelBatch`/`FindByType`/`CountByType`）
- `Models/MapData.cs`：`GetBuildingAt`/`GetArmyAt` 返回 `Building?`/`Army?`，`Add*`/`Remove*` 用 `.HasValue`/`.Value`，`ClearSelection()` 置空实现
- `Parsers/BTL/BTLTerrainModule.cs`、`BTLBuildingModule.cs`：改用 `FromBytes`

---

## 二、设计约定（后续所有模型统一遵守）

```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct XXX
{
    // 1) 字段用 public field（非 property），类型严格对应字节宽度
    //    byte / short / int / float，保留字段命名 ReservedN
    public short Coordinate;
    public byte  SomeFlag;

    // 2) 解析：ReadOnlySpan<byte> + offset
    public static XXX FromBytes(ReadOnlySpan<byte> data, int offset) { ... }
    //    布局完全匹配时优先用零拷贝：MemoryMarshal.Read<XXX>(data[offset..])

    // 3) 序列化：写入调用方缓冲区，不再 new byte[]
    public void ToBytes(Span<byte> data, int offset) { ... }

    // 4) 默认值
    public static XXX CreateDefault() => new XXX { ... };

    // 5) 只读辅助方法加 readonly 修饰
    public readonly string GetXxxName() => ...;
}
```

**禁止事项**：
- 结构体内**不要** `IsSelected` / `IsHighlighted` 等 UI 状态（改由渲染层/选择集维护）
- `FromBytes` **不返回 null**（返回 `default` 或调用方先判长度）
- 不保留旧的 `Parse()` 和无参 `ToBytes()`

**修改模块约定**（`Models/Modules/XxxModule.cs`）：
```csharp
public static class XxxModule
{
    public static void SetYyy(ref Xxx item, byte value) => item.Yyy = value;      // 单个：ref
    public static void SetYyyBatch(Span<Xxx> items, byte value) { ... }            // 批量：Span
    public static int FindByZzz(ReadOnlySpan<Xxx> items, byte z) { ... }           // 只读查询：ReadOnlySpan
}
```

---

## 三、待修复的编译错误（按此顺序）

### 步骤 1：修 `Parsers/BTL/BTLParser.cs`
| 行号 | 现状 | 改为 |
|------|------|------|
| ~58 | `Terrain.Parse(data, (int)offset, i)` + `if (terrain != null)` | `var terrain = Terrain.FromBytes(data, (int)offset);` 去掉 null 判断 |
| ~78 | `Building.Parse(data, (int)offset)` + null 判断 | `Building.FromBytes(data, (int)offset)` |
| ~161 | `mapData.Terrains[i].ToBytes()` 无参 | `mapData.Terrains[i].ToBytes(data.AsSpan(), (int)offset)` |
| ~178 | `mapData.Buildings[i].ToBytes()` 无参 | `mapData.Buildings[i].ToBytes(data.AsSpan(), (int)offset)` |

### 步骤 2：修 `Parsers/Conquest/ConquestParser.cs` (~467)
`resultData.AddRange(building.ToBytes())` → 改为先申请 `var buf = new byte[32]; building.ToBytes(buf, 0); resultData.AddRange(buf);`
（Province/Belong 已是此写法，照抄即可。同理后续所有结构体化的模型在 `SaveData` 中都要这样改。）

### 步骤 3：`Models/Army.cs` → struct（48 字节）
当前是 class，`Parse` 返回 null，含 `IsSelected`/`LegionId`。
- 字段按现有 `Parse` 的偏移表转为 public field
- `LegionId` 保留为字段（解析后由 Parser 赋值），`IsSelected` **删除**
- `Parse` → `FromBytes`，`ToBytes()` → `ToBytes(Span<byte>, int)`
- `IArmyRenderData` 接口：结构体实现接口会装箱，**改为**让渲染层直接读字段，或把接口删掉
- 新建 `Models/Modules/ArmyModule.cs`：`SetUnitType`/`SetLevel`/`SetHealth`/`SetGeneral`/`SetSkillLevels`/`SetPolicy`/`SetMorale`/`CopyArmy`/`FindByCoordinate`/`SetLegionIdBatch`

### 步骤 4：`Models/Legion.cs` → struct（300 字节）
含 ~80 个字段（经济/工业/科技/陆海空军科技树等）+ `float CountryHpRate`/`CountryTaxRate`。
- 同样删除 `IsSelected`；`IsPlayer`/`DisplayName` 加 `readonly`
- 300 字节字段极多，建议直接 `MemoryMarshal.Read<Legion>` 零拷贝（需确认字段顺序与偏移**完全**匹配，含所有 Reserved 补齐）
- 新建 `Models/Modules/LegionModule.cs`：`SetCountryId`/`SetEconomy`/`SetTechLevel`/`SetNuclearCount`/`SetArmyTech`/`SetNavyTech`/`SetAirTech`/`CopyLegion`

### 步骤 5：其余模型逐个转 struct + 建 Module
| 模型 | 字节 | Module |
|------|------|--------|
| `Army_3.cs` | 64 | 合并进 `ArmyModule`（用 `_3` 后缀方法）或单独 `Army3Module` |
| `Reinforcement.cs` | 80 | `ReinforcementModule` |
| `Reinforcement_3.cs` | 104 | 同上 |
| `Trap.cs` | 12 | `TrapModule` |
| `MapCase.cs` | 16 | `MapCaseModule` |
| `Weather.cs` | 16 | `WeatherModule` |
| `MapEvent.cs` | 44 | `MapEventModule` |
| `AirForce.cs` | 20 | `AirForceModule` |
| `AirSupport.cs` | 16 | `AirSupportModule` |
| `UnitPlacement.cs` | 8 | `UnitPlacementModule` |
| `Capital.cs` | 4 | `CapitalModule` |
| `StrategicConstruction.cs` | 16 | `StrategicConstructionModule` |

每转一个，同步修改：
1. 对应的 `Parsers/BTL/BTLXxxModule.cs`（`Xxx.Parse` → `Xxx.FromBytes`，去掉 null 判断）
2. `Parsers/Conquest/ConquestParser.cs` 的 `SaveData`（`ToBytes()` → 缓冲区写法）
3. `Models/MapData.cs` 中若有 `!= null` 判断改 `.HasValue`

### 步骤 6：清理与验证
- `Models/IArmyRenderData.cs`、`IReinforcementRenderData.cs`：结构体实现接口会装箱，考虑删除或改为 `in` 参数的静态适配器
- `WC4MapEditor.Rendering/Skia/*.cs`：若引用了 `army.IsSelected` 等已删属性需修
- `Views/AssetBrowserScene.xaml.cs` 中 `Parsers.BTL.BTLParser.LoadFromFile` 已正确，无需改

验证命令：
```powershell
# 关闭正在运行的 GUI（否则 DLL 被锁）
Stop-Process -Name "WC4MapEditor" -Force -ErrorAction SilentlyContinue
dotnet build "E:\CSharpProject\WC4MapEditor\WC4MapEditor.sln"

# 功能验证
dotnet run --project WC4MapEditor.Cli -- map load "Resource\WC4DATA\assets\stage\stage10101.btl"
dotnet run --project WC4MapEditor.Cli -- asset summary
```

---

## 四、内存收益预估

以 240×138 地图（33120 格）为例：

| 项目 | 改造前（class） | 改造后（struct 数组） |
|------|----------------|---------------------|
| Terrain | 33120 个对象 × (16 数据 + 24 对象头/指针) ≈ 1.3 MB + GC 压力 | 33120 × 16 = 530 KB 连续内存 |
| Building（约 2000） | 2000 × (32 + 24) ≈ 112 KB + 堆碎片 | 2000 × 32 = 64 KB |
| GC | 大量 Gen0/Gen1 回收 | 零分配（Span 操作） |

关键点：`MapDataOptimized` 已用 `TerrainData[]`/`Province[]` 连续数组，本次把 `Terrain`/`Building`/`Army`/`Legion` 等也变为值类型后，解析过程可做到**近乎零对象分配**。

---

## 五、注意事项

1. **命名空间**：Core 项目里 Parsers 用 `WC4MapEditor.Parsers.*`，Models 用 `WC4MapEditor.Models`，Assets 用 `WC4MapEditor.Core.Assets`（不一致但已存在，勿擅自统一以免大范围破坏）
2. **构建锁文件**：GUI 运行时会锁 `WC4MapEditor.Core.dll`，构建前先 `Stop-Process`
3. **`--no-incremental` 陷阱**：曾出现 `.g.cs` 丢失导致 CS2001，改用普通 `dotnet build` 即可
4. **System.CommandLine 版本**：`Option<T>` 用 `{ Description = ..., DefaultValueFactory = _ => x }`，`Argument<T>` 可选性用 `Arity = ArgumentArity.ZeroOrOne`
5. **ObservableCollection\<struct\>**：`MapData` 里 `ObservableCollection<Building>` 装的是值类型，`Remove` 依赖值相等语义，必要时为 struct 实现 `IEquatable<T>`
