namespace WC4MapEditor.Core.Mode;

/// <summary>
/// 编辑模式需要打开的渲染层（可组合）。
/// <para>
/// 每个模式通过 <see cref="IModeHandler.RenderLayers"/> 声明自己要看到哪些层，
/// GUI 在切换模式时把它翻译成渲染引擎的 Enable*Render 开关。
/// 地形与背景层始终开启，不在此列；选区层跟随 <see cref="IModeHandler.RequiresSelection"/>。
/// </para>
/// </summary>
[Flags]
public enum ModeRenderLayers : long
{
    None = 0,

    /// <summary>省区配色与边界</summary>
    Province = 1L << 0,

    /// <summary>省会标记</summary>
    ProvinceCapital = 1L << 1,

    /// <summary>首都国旗</summary>
    CapitalFlag = 1L << 2,

    /// <summary>建筑</summary>
    Building = 1L << 3,

    /// <summary>强制隐藏建筑名称（无视建筑名称的全局开关，地形模式使用）</summary>
    HideBuildingNames = 1L << 4,

    /// <summary>部队（同时会画出旧版援军层的左列样式）</summary>
    Army = 1L << 5,

    /// <summary>陷阱</summary>
    Trap = 1L << 6,

    /// <summary>国家领域着色（按归属值铺国家颜色）</summary>
    LegionDomain = 1L << 7,

    /// <summary>每格归属国旗</summary>
    BelongFlag = 1L << 8,

    /// <summary>援军（新版渲染：援军模式专用的居中网格）</summary>
    Reinforcement = 1L << 9,

    /// <summary>战略建设</summary>
    StrategicConstruction = 1L << 10,

    /// <summary>空袭 / 空中支援</summary>
    AirForce = 1L << 11,

    /// <summary>天气</summary>
    Weather = 1L << 12,

    /// <summary>方案（方针）目标与方案面板</summary>
    MapCase = 1L << 13,

    /// <summary>数据范围外（MapClip 裁剪区以外）格子的红叉标记</summary>
    ClippedTiles = 1L << 14,
}
