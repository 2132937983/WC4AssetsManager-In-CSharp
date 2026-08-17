# 内存优化计划

## 背景

打开 17,280,000 个格子的 world.bin 地图文件时，程序内存使用量达到 ~4,403 MB，远超合理预期（~486 MB）。

## 当前内存分布分析

### 合理的内存占用（~312 MB）

| 数据项 | 类型 | 计算 | 大小 |
|--------|------|------|------|
| `_terrains` | `TerrainData[17280000]` (struct 16B) | 17,280,000 × 16 | ~263 MB |
| `_provinces` | `Province[17280000]` (struct 2B) | 17,280,000 × 2 | ~33 MB |
| `_coastDecorationArray` | `byte[mapW, mapH]` | 17,280,000 × 1 | ~16.5 MB |

### 渲染缓存（~144-174 MB，与地图大小无关）

| 来源 | 大小 |
|------|------|
| `_bgCacheSurfaceA` (2倍视口 SKSurface) | ~33 MB |
| `_bgCacheSurfaceB` (2倍视口 SKSurface) | ~33 MB |
| `_bgCacheImage` 快照 | ~33 MB |
| CoastHelper 图集 + 缓存 | ~10-20 MB |
| CoastMaskProcessor `_maskCache` | ~30-50 MB |
| LandTerrainsRender `_terrainSource` | ~5 MB |

---

## 优化项目

### P0 - 文件加载峰值内存

#### P0-1: File.ReadAllBytes() 一次性读取整个文件

- **文件**: `WC4MapEditor.Core\Parsers\World\WorldParser.cs:27`
- **问题**: 小于 1GB 的文件使用 `File.ReadAllBytes()` 一次性读入内存，~297 MB 的原始 byte 数组与结构体数组共存，峰值 ~593 MB
- **方案**: 将阈值降低或始终使用流式读取（已有 `LoadFromFileStream` 方法）
- **预估收益**: 峰值减少 ~297 MB
- **难度**: 低

```csharp
// 当前
if (fileSize < 1024L * 1024 * 1024)
{
    byte[] data = File.ReadAllBytes(filePath);
    return LoadFromBytes(data, filePath);
}

// 建议: 始终使用流式读取，或降低阈值到 100MB
if (fileSize < 100L * 1024 * 1024)
{
    byte[] data = File.ReadAllBytes(filePath);
    return LoadFromBytes(data, filePath);
}
```

#### P0-2: Terrains 属性每次访问创建 263 MB 集合

- **文件**: `WC4MapEditor.Core\Models\MapData.cs:38`
- **问题**: `public ReadOnlyObservableCollection<Terrain> Terrains => new(GetTerrainCollection());` 每次访问都遍历全部格子创建新 ObservableCollection，每次分配 ~263 MB
- **方案**:
  - 方案A: 移除此属性，渲染代码直接使用 `GetTerrain()`/`GetTerrainAt()` 访问结构体数组
  - 方案B: 缓存 ObservableCollection，仅在数据变更时重建
  - 方案C: 改为暴露 `ReadOnlySpan<TerrainData>` 或 `ReadOnlyMemory<TerrainData>`
- **预估收益**: 避免每次访问 +263 MB，消除定时炸弹
- **难度**: 低-中（需检查所有调用方）

```csharp
// 当前 (危险)
public ReadOnlyObservableCollection<Terrain> Terrains => new(GetTerrainCollection());

// 建议方案A: 直接暴露结构体数组
public TerrainData[] TerrainDataArray => _terrains;
public ReadOnlySpan<TerrainData> TerrainSpan => _terrains.AsSpan();

// 建议方案B: 惰性缓存
private ReadOnlyObservableCollection<Terrain>? _terrainsCache;
public ReadOnlyObservableCollection<Terrain> Terrains
{
    get
    {
        _terrainsCache ??= new(GetTerrainCollection());
        return _terrainsCache;
    }
}
```

#### P0-3: Provinces 属性同样的问题

- **文件**: `WC4MapEditor.Core\Models\MapData.cs:39`
- **问题**: 与 Terrains 相同，每次访问创建新 ObservableCollection
- **方案**: 同 P0-2
- **预估收益**: 避免每次访问 +33 MB
- **难度**: 低

---

### P1 - 数据结构优化

#### P1-1: _coastDecorationArray 改为一维数组

- **文件**: `WC4MapEditor.Rendering\Skia\BackGroundRender.cs:75`
- **问题**: `byte[,]` 二维数组在 CLR 中有额外开销，且 LOH 分配不连续
- **方案**: 改为 `byte[]` 一维数组，用 `row * mapWidth + col` 索引
- **预估收益**: 减少少量内存开销，提升访问速度（一维数组缓存友好）
- **难度**: 低

```csharp
// 当前
private byte[,]? _coastDecorationArray;
_coastDecorationArray = new byte[mapWidth, mapHeight];
_coastDecorationArray[col, row] = value;

// 建议
private byte[]? _coastDecorationArray;
private int _coastArrayWidth;
_coastDecorationArray = new byte[mapWidth * mapHeight];
_coastArrayWidth = mapWidth;
_coastDecorationArray[row * _coastArrayWidth + col] = value;
```

#### P1-2: PreCalculateAllCoastlines 全图遍历优化

- **文件**: `WC4MapEditor.Rendering\Skia\BackGroundRender.cs:793`
- **问题**: 遍历 17,280,000 个格子预计算海岸线，CPU 密集且创建临时 `byte[,]` 数组
- **方案**:
  - 方案A: 改为按需计算（lazy），仅计算视口内可见格子的海岸线
  - 方案B: 分块计算，先计算视口附近区域，后台逐步扩展
  - 方案C: 增量计算，仅在格子修改时重新计算受影响区域
- **预估收益**: 大幅减少启动时间和临时内存
- **难度**: 中

#### P1-3: GetTerrainAt() 返回 Terrain struct 的冗余转换

- **文件**: `WC4MapEditor.Core\Models\MapData.cs:316`
- **问题**: `GetTerrainAt()` 调用 `Terrain.FromTerrainData()` 做字段复制，渲染循环中大量调用
- **方案**: 渲染代码直接使用 `GetTerrain()` 返回 `TerrainData`，避免中间转换
- **预估收益**: 微小性能提升，无内存影响
- **难度**: 低（需修改渲染代码中的调用）

---

### P2 - 渲染优化

#### P2-1: RenderWithMasks 每个格子创建临时 SKSurface

- **文件**: `WC4MapEditor.Rendering\Helpers\CoastMaskProcessor.cs:129-142`
- **问题**: 渲染每个海岸格子时创建 `SKSurface.Create(info)`，高频分配
- **方案**: 预分配一个可复用的 SKSurface，每帧只创建一次
- **预估收益**: 减少 SkiaSharp 内部内存池压力
- **难度**: 中

```csharp
// 当前
private void RenderWithMasks(...)
{
    var info = new SKImageInfo(width, height);
    using var tempSurface = SKSurface.Create(info);  // 每个格子创建
    ...
}

// 建议: 预分配复用
private SKSurface? _reusableSurface;
private SKImageInfo _reusableSurfaceInfo;

private SKSurface GetOrCreateSurface(int width, int height)
{
    var info = new SKImageInfo(width, height);
    if (_reusableSurface == null || _reusableSurfaceInfo.Width < width || _reusableSurfaceInfo.Height < height)
    {
        _reusableSurface?.Dispose();
        _reusableSurface = SKSurface.Create(info);
        _reusableSurfaceInfo = info;
    }
    return _reusableSurface;
}
```

#### P2-2: SKSurface 缓存大小优化

- **文件**: `WC4MapEditor.Rendering\Skia\BackGroundRender.cs:407`
- **问题**: 两个 2 倍视口大小的 SKSurface + 快照，~99 MB
- **方案**: 可考虑缩小为 1.5 倍视口，或使用单个缓存 + 部分重绘
- **预估收益**: 减少 ~30-50 MB
- **难度**: 中（需调整 ping-pong 缓存逻辑）

---

### P3 - GC 和 LOH 优化

#### P3-1: LOH 碎片化

- **问题**: 大数组（>85KB）分配在 LOH 上，LOH 不自动压缩，导致虚拟内存膨胀
- **方案**:
  - 在 .NET 6+ 中设置 `GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce`
  - 在适当时机（如地图加载后）触发 GC.Collect(2, GCCollectionMode.Forced)
- **预估收益**: 减少虚拟内存碎片化
- **难度**: 低

#### P3-2: TerrainData 数组可考虑使用 Native 内存

- **问题**: 263 MB 的托管数组在 LOH 上，GC 扫描开销大
- **方案**: 使用 `NativeMemory` 或 `MemoryMarshal` 直接分配非托管内存，避免 LOH
- **预估收益**: 减少 GC 压力，避免 LOH 碎片化
- **难度**: 高（需重构数据访问层）

---

## 执行顺序

1. **P0-1**: File.ReadAllBytes 改为流式读取（最快见效）
2. **P0-2 + P0-3**: 移除/重构 Terrains/Provinces 属性（消除定时炸弹）
3. **P1-1**: _coastDecorationArray 改为一维数组
4. **P1-3**: 渲染代码直接使用 GetTerrain() 返回 TerrainData
5. **P1-2**: PreCalculateAllCoastlines 改为按需计算
6. **P2-1**: RenderWithMasks 预分配复用 SKSurface
7. **P2-2**: SKSurface 缓存大小优化
8. **P3-1**: LOH 压缩
9. **P3-2**: Native 内存（长期目标）

## 验证方法

每次优化后：
1. 打开 17,280,000 格子的 world.bin 文件
2. 记录任务管理器中的内存使用量
3. 对比优化前后的内存峰值和稳态值
4. 使用 `dotnet-trace` 或 `dotnet-dump` 分析托管堆详情